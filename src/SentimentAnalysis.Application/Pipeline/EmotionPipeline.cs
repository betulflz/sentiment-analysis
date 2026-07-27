using System.Threading.Channels;
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

    public EmotionPipeline(IChunkProcessor processor, ISessionStore store, IMeetingAggregator aggregator, AppConfig config)
    {
        _processor = processor;
        _store = store;
        _aggregator = aggregator;
        _capacity = Math.Max(1, config.Pipeline.ChannelCapacity);
    }

    public IReadOnlyList<ChunkResult> Timeline => _timeline;
    public int DroppedCount => Volatile.Read(ref _droppedCount);

    public async Task<MeetingSummary> RunAsync(
        IAudioCaptureSource source, CaptureOptions options, Guid sessionId,
        DateTimeOffset startedAt, DetectedLanguage language,
        IProgress<ChunkResult>? progress, CancellationToken ct)
    {
        _timeline.Clear();
        _droppedCount = 0;

        var channel = Channel.CreateBounded<AudioChunk>(
            new BoundedChannelOptions(_capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            },
            itemDropped: _ => Interlocked.Increment(ref _droppedCount));

        var producer = Task.Run(async () =>
        {
            try
            {
                await foreach (var chunk in source.CaptureAsync(options, ct).ConfigureAwait(false))
                    await channel.Writer.WriteAsync(chunk, ct).ConfigureAwait(false);
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, ct);

        try
        {
            await foreach (var chunk in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                ChunkResult? result;
                try
                {
                    result = await _processor.ProcessAsync(chunk, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Bir chunk'ın hatası hattı çökertmez (spec: degradasyon, çökme yok).
                    continue;
                }

                if (result is null) continue; // VAD sessiz
                _timeline.Add(result);
                progress?.Report(result);
                await _store.SaveChunkAsync(sessionId, result, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            // Producer'ı her durumda gözle (iptal/hata dahil) — kayıp exception ve
            // serbest bırakılmamış kaynak olmasın. İptal zaten yukarı akar.
            try { await producer.ConfigureAwait(false); } catch { /* iptal/producer hatası yutulur; asıl akış yukarı gider */ }
        }

        var session = new MeetingSession(sessionId, startedAt, language, _timeline.ToList());
        return _aggregator.Aggregate(session);
    }
}
