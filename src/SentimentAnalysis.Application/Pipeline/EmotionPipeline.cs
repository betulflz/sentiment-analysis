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
    private readonly IDiagnosticLog _log;
    private readonly int _capacity;
    private readonly TimeSpan _drainTimeBudget;

    private readonly List<ChunkResult> _timeline = new();
    private int _droppedCount;
    private int _drainSkippedCount;
    private readonly PipelineJournal _journal = new();
    private sealed record WorkItem(int Index, AudioChunk Chunk);

    public EmotionPipeline(
        IChunkProcessor processor, ISessionStore store, IMeetingAggregator aggregator,
        AppConfig config, IDiagnosticLog? log = null)
    {
        _processor = processor;
        _store = store;
        _aggregator = aggregator;
        _log = log ?? NullDiagnosticLog.Instance;   // üretim varsayılanı: sessiz
        _capacity = Math.Max(1, config.Pipeline.ChannelCapacity);
        _drainTimeBudget = TimeSpan.FromSeconds(Math.Max(1.0, config.Pipeline.DrainTimeBudgetSeconds));
    }

    /// <summary>Teşhis mesajını yalnızca günlük açıkken kurar ve yazar. Teşhis, analizi
    /// çökertemez: günlük uygulaması fırlatırsa hata yutulur.</summary>
    private void Diagnose(Func<string> message)
    {
        if (!_log.IsEnabled) return;
        try { _log.Write(message()); } catch { /* teşhis, teşhis ettiği şeyi bozamaz */ }
    }

    public IReadOnlyList<ChunkResult> Timeline => _timeline;
    public int DroppedCount => Volatile.Read(ref _droppedCount);
    public int DrainSkippedCount => Volatile.Read(ref _drainSkippedCount);
    public AnalysisDiagnostics Diagnostics => _journal.Snapshot();

    /// <summary>Modelleri asenkron olarak warm-up eder ve ilk çıkarım gecikmesini önler.
    /// Yüklenemeyen modelin hatasını açıklayıcı bir şekilde fırlatır.</summary>
    public Task WarmUpAsync(CancellationToken ct) => _processor.WarmUpAsync(ct);

    /// <param name="roles">İsteğe bağlı (12): canlı "Yöneten / Karşı taraf" etiketleyicisi; koşu başında sıfırlanır.</param>
    /// <param name="enrich">İsteğe bağlı (11d): işlenen chunk timeline/progress/depoya yazılmadan önce
    /// dönüştürülür — ör. aynı aralığın yüz özetiyle üç kaynaklı füzyon. Fırlatırsa chunk olduğu gibi kalır.</param>
    public async Task<MeetingSummary> RunAsync(
        IAudioCaptureSource source, CaptureOptions options, Guid sessionId,
        DateTimeOffset startedAt, DetectedLanguage language,
        IProgress<ChunkResult>? progress, CancellationToken ct,
        Func<ChunkResult, ChunkResult>? enrich = null, IRoleLabeler? roles = null,
        PipelineExecutionMode mode = PipelineExecutionMode.Realtime)
    {
        _timeline.Clear();
        roles?.Reset();
        _droppedCount = 0;
        _drainSkippedCount = 0;
        _journal.Reset();
        using var producerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var writerCts = new CancellationTokenSource();

        var channel = Channel.CreateBounded<WorkItem>(
            new BoundedChannelOptions(_capacity)
            {
                FullMode = mode == PipelineExecutionMode.Offline ? BoundedChannelFullMode.Wait : BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            },
            itemDropped: item =>
            {
                Interlocked.Increment(ref _droppedCount);
                _journal.Complete(item.Index, ChunkDisposition.QueueDropped);
            });

        // Üretici: Mikrofon yakalamayı durdurma (ct) token'ı ile yönetir.
        var producer = Task.Run(async () =>
        {
            Exception? producerError = null;
            try
            {
                await foreach (var chunk in source.CaptureAsync(options, producerCts.Token).ConfigureAwait(false))
                {
                    int index = _journal.Capture(chunk);
                    // A capture source may yield its final partial chunk AFTER user stop.
                    // Keep writing until the consumer ends or its drain budget expires.
                    await channel.Writer.WriteAsync(new(index, chunk), writerCts.Token).ConfigureAwait(false);
                }
                _journal.SourceEnded(producerCts.IsCancellationRequested ? "stopped" : "completed");
            }
            catch (OperationCanceledException)
            {
                _journal.SourceEnded("stopped");
                // Durdurma tetiklendi
            }
            catch (Exception ex)
            {
                producerError = ex;
                _journal.SourceEnded("failed", ex);
            }
            finally
            {
                channel.Writer.Complete(producerError);
            }
        });

        // Tüketici için bağımsız token: bütçe "Durdur" (ct) ANINDA başlar, koşunun başında değil —
        // aksi halde bütçeden uzun her canlı görüşmenin geri kalanı atılırdı.
        using var processingCts = new CancellationTokenSource();
        using var startDrainBudget = ct.Register(() => processingCts.CancelAfter(_drainTimeBudget));

        try
        {
            // ct iptal olsa bile kanal writer tamamlanana ( Complete ) kadar tüm chunk'ları tüketiriz
            await foreach (var item in channel.Reader.ReadAllAsync(processingCts.Token).ConfigureAwait(false))
            {
                await ProcessOneAsync(item, sessionId, progress, enrich, roles, processingCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Zaman bütçesi doldu: elimizdeki kalan işlenemeyenler atlanmış sayılır
            var pendingChunks = new List<WorkItem>();
            while (channel.Reader.TryRead(out var pending))
            {
                pendingChunks.Add(pending);
                _journal.Complete(pending.Index, ChunkDisposition.DrainSkipped);
            }

            if (pendingChunks.Count > 0)
                Interlocked.Add(ref _drainSkippedCount, pendingChunks.Count);
        }
        finally
        {
            writerCts.Cancel();
            producerCts.Cancel(); // unblock offline writers when processing/storage failed
            try { await producer.ConfigureAwait(false); } catch { }
            _journal.FinishPending();
        }

        var outcome = _timeline.Count > 0 ? AnalysisOutcome.Success : AnalysisOutcome.NoResult;
        return BuildSummary(sessionId, startedAt, language, outcome);
    }

    /// <summary>Tek bir chunk'ı işleyip (VAD sessizse atlanır) zaman çizelgesine/depoya yazar.
    /// Bir chunk'ın işleme hatası hattı çökertmez (spec: degradasyon, çökme yok) — yalnızca
    /// iptal (<see cref="OperationCanceledException"/>) yukarı akar, çünkü o bir chunk hatası
    /// değil, koşunun kendisinin durumudur.</summary>
    private async Task ProcessOneAsync(
        WorkItem item, Guid sessionId, IProgress<ChunkResult>? progress,
        Func<ChunkResult, ChunkResult>? enrich, IRoleLabeler? roles, CancellationToken ct)
    {
        var chunk = item.Chunk;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ChunkResult? result = null;
        try
        {
            // RMS/tepe hesabı chunk başına ~160 bin örnek gezer — yalnızca teşhis açıkken.
            Diagnose(() =>
            {
                double rms = Math.Sqrt(chunk.Samples.Select(s => (double)s * s).Average());
                float peak = chunk.Samples.Length > 0 ? chunk.Samples.Max(Math.Abs) : 0f;
                return $"[PIPELINE] Chunk işleme başladı: Start={chunk.Start.TotalSeconds}s, " +
                       $"End={chunk.End.TotalSeconds}s, Samples={chunk.Samples.Length}, " +
                       $"RMS={rms:F4}, Peak={peak:F4}, Canceled={ct.IsCancellationRequested}";
            });

            result = await _processor.ProcessAsync(chunk, roles, ct).ConfigureAwait(false);
            sw.Stop();
            _journal.Complete(item.Index, result is null ? ChunkDisposition.NoSpeech : ChunkDisposition.Speech, sw.Elapsed.TotalSeconds);

            var outcome = result is not null ? "Konuşma var" : "Sessizlik";
            Diagnose(() => $"[PIPELINE] Chunk işleme bitti: Süre={sw.ElapsedMilliseconds}ms, VAD={outcome}");
        }
        catch (OperationCanceledException ex)
        {
            _journal.Complete(item.Index, ChunkDisposition.Cancelled, sw.Elapsed.TotalSeconds, ex.GetType().Name);
            Diagnose(() => $"[PIPELINE] Chunk işlemi iptal edildi: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            _journal.Complete(item.Index, ChunkDisposition.ProcessingFailed, sw.Elapsed.TotalSeconds, ex.GetType().Name);
            Diagnose(() => $"[PIPELINE] Chunk işleminde beklenmedik hata yutuldu: " +
                           $"{ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}");
            return;
        }

        if (result is null) return; // VAD sessiz
        if (enrich is not null)
        {
            try { result = enrich(result); }
            catch (Exception ex)
            {
                _journal.EnrichmentFailed(item.Index, ex);
                Diagnose(() => $"[PIPELINE] Chunk zenginleştirme hatası yutuldu: {ex.GetType().Name} - {ex.Message}");
            }
        }
        _timeline.Add(result);
        progress?.Report(result);
        try { await _store.SaveChunkAsync(sessionId, result, ct).ConfigureAwait(false); }
        catch (Exception ex) { _journal.StorageFailed(ex); throw; }
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
        return summary with { Outcome = outcome, Diagnostics = Diagnostics };
    }
}
