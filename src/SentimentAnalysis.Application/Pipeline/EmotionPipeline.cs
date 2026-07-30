using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Pipeline;

/// <summary>Yakalama akışını bounded kanal ile orkestre eder: producer chunk'ları yazar (DropOldest —
/// canlı akıcılık için bloklanmaz, taşarsa en eski düşer), tek consumer <see cref="IChunkProcessor"/>'dan
/// geçirip timeline'a + <see cref="ISessionStore"/>'a SIRAYLA yazar; sonda <see cref="IMeetingAggregator"/>
/// ile <see cref="MeetingSummary"/> üretir. Bir chunk'taki bileşen hatası izole edilir (o chunk atlanır).
/// Tek koşu/örnek varsayılır (Timeline/DroppedCount koşu-başı durum).</summary>
public sealed class EmotionPipeline
{
    private readonly IChunkProcessor _processor;
    private readonly ISessionStore _store;
    private readonly IMeetingAggregator _aggregator;
    private readonly int _capacity;

    private readonly List<ChunkResult> _timeline = new();
    private int _droppedCount;
    private int _drainSkippedCount;

    private const int MaxDrainedChunksAfterStop = 8;
    private static readonly TimeSpan DrainTimeBudget = TimeSpan.FromSeconds(90);

    public EmotionPipeline(IChunkProcessor processor, ISessionStore store, IMeetingAggregator aggregator, AppConfig config)
    {
        _processor = processor;
        _store = store;
        _aggregator = aggregator;
        _capacity = Math.Max(1, config.Pipeline.ChannelCapacity);
    }

    public IReadOnlyList<ChunkResult> Timeline => _timeline;
    public int DroppedCount => Volatile.Read(ref _droppedCount);
    public int DrainSkippedCount => Volatile.Read(ref _drainSkippedCount);

    /// <summary>Modelleri asenkron olarak warm-up eder ve ilk çıkarım gecikmesini önler.
    /// Yüklenemeyen modelin hatasını açıklayıcı bir şekilde fırlatır.</summary>
    public Task WarmUpAsync(CancellationToken ct) => _processor.WarmUpAsync(ct);

    public async Task<MeetingSummary> RunAsync(
        IAudioCaptureSource source, CaptureOptions options, Guid sessionId,
        DateTimeOffset startedAt, DetectedLanguage language,
        IProgress<ChunkResult>? progress, CancellationToken ct)
    {
        _timeline.Clear();
        _droppedCount = 0;
        _drainSkippedCount = 0;

        var channel = Channel.CreateBounded<AudioChunk>(
            new BoundedChannelOptions(_capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            },
            itemDropped: _ => Interlocked.Increment(ref _droppedCount));

        // Üretici: Mikrofon yakalamayı durdurma (ct) token'ı ile yönetir.
        var producer = Task.Run(async () =>
        {
            Exception? producerError = null;
            try
            {
                await foreach (var chunk in source.CaptureAsync(options, ct).ConfigureAwait(false))
                {
                    await channel.Writer.WriteAsync(chunk, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Durdurma tetiklendi
            }
            catch (Exception ex)
            {
                producerError = ex;
            }
            finally
            {
                channel.Writer.Complete(producerError);
            }
        });

        // Tüketici için 90 saniye güvenlik bütçeli bağımsız token
        using var processingCts = new CancellationTokenSource(DrainTimeBudget);

        try
        {
            // ct iptal olsa bile kanal writer tamamlanana ( Complete ) kadar tüm chunk'ları tüketiriz
            await foreach (var chunk in channel.Reader.ReadAllAsync(processingCts.Token).ConfigureAwait(false))
            {
                await ProcessOneAsync(chunk, sessionId, progress, processingCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Zaman bütçesi doldu: elimizdeki kalan işlenemeyenler atlanmış sayılır
            var pendingChunks = new List<AudioChunk>();
            while (channel.Reader.TryRead(out var pending))
                pendingChunks.Add(pending);

            if (pendingChunks.Count > 0)
                Interlocked.Add(ref _drainSkippedCount, pendingChunks.Count);
        }
        finally
        {
            try { await producer.ConfigureAwait(false); } catch { }
        }

        var outcome = _timeline.Count > 0 ? AnalysisOutcome.Success : AnalysisOutcome.NoResult;
        return BuildSummary(sessionId, startedAt, language, outcome);
    }

    /// <summary>Tek bir chunk'ı işleyip (VAD sessizse atlanır) zaman çizelgesine/depoya yazar.
    /// Bir chunk'ın işleme hatası hattı çökertmez (spec: degradasyon, çökme yok) — yalnızca
    /// iptal (<see cref="OperationCanceledException"/>) yukarı akar, çünkü o bir chunk hatası
    /// değil, koşunun kendisinin durumudur.</summary>
    private async Task ProcessOneAsync(
        AudioChunk chunk, Guid sessionId, IProgress<ChunkResult>? progress, CancellationToken ct)
    {
        ChunkResult? result = null;
        try
        {
            var rms = Math.Sqrt(chunk.Samples.Select(s => (double)s * s).Average());
            var peak = chunk.Samples.Max(Math.Abs);
            System.Console.WriteLine($"[PIPELINE] Chunk işleme başladı: Start={chunk.Start.TotalSeconds}s, End={chunk.End.TotalSeconds}s, Samples={chunk.Samples.Length}, RMS={rms:F4}, Peak={peak:F4}, Canceled={ct.IsCancellationRequested}");
            
            var sw = System.Diagnostics.Stopwatch.StartNew();
            result = await _processor.ProcessAsync(chunk, ct).ConfigureAwait(false);
            sw.Stop();
            
            System.Console.WriteLine($"[PIPELINE] Chunk işleme bitti: Süre={sw.ElapsedMilliseconds}ms, VAD={(result != null ? "Konuşma var" : "Sessizlik")}");
        }
        catch (OperationCanceledException ex)
        {
            System.Console.WriteLine($"[PIPELINE_ERROR] Chunk işlemi iptal edildi (OperationCanceledException): {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[PIPELINE_ERROR] Chunk işleminde beklenmedik hata yutuldu: {ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}");
            return;
        }

        if (result is null) return; // VAD sessiz
        _timeline.Add(result);
        progress?.Report(result);
        await _store.SaveChunkAsync(sessionId, result, ct).ConfigureAwait(false);
    }

    /// <summary>O ana kadar toplanan <see cref="Timeline"/>'dan bir <see cref="MeetingSummary"/> üretir.
    /// Motor kendisi bir koşunun ne zaman "normal" sayılacağına karar vermez — <see cref="RunAsync"/>
    /// akışın sonuna ulaştığında bunu kullanır; çağıran taraf (ör. canlı mikrofonda "Durdur" ile gelen
    /// iptal) bunu kendi normal-bitiş kararı için de kullanabilir (bkz. <c>PipelineAnalysisRunner</c>).
    /// Boş zaman çizelgesinde <see cref="IMeetingAggregator"/> geçerli, boş-ama-çökmeyen bir özet döner.</summary>
    public MeetingSummary BuildSummary(Guid sessionId, DateTimeOffset startedAt, DetectedLanguage language, AnalysisOutcome outcome = AnalysisOutcome.Success)
    {
        var session = new MeetingSession(sessionId, startedAt, language, _timeline.ToList());
        var summary = _aggregator.Aggregate(session);
        return summary with { Outcome = outcome };
    }
}
