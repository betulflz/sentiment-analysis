using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Pipeline;

public class EmotionPipelineTests
{
    // ---- fakes ----
    private sealed class FakeSource(int count, Func<int, AudioChunk> make) : IAudioCaptureSource
    {
        public async IAsyncEnumerable<AudioChunk> CaptureAsync(
            CaptureOptions options, [EnumeratorCancellation] CancellationToken ct)
        {
            for (int i = 0; i < count; i++) { ct.ThrowIfCancellationRequested(); yield return make(i); await Task.Yield(); }
        }
    }

    /// <summary>Yakalama kaynağının kendisi hata fırlattığında (ör. gerçek
    /// MicrophonePermissionException) hattın bunu yutup boş-ama-başarılı bir özete
    /// düşmediğini kanıtlamak için kullanılır.</summary>
    private sealed class ThrowingSource(Exception toThrow) : IAudioCaptureSource
    {
        public async IAsyncEnumerable<AudioChunk> CaptureAsync(
            CaptureOptions options, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            throw toThrow;
#pragma warning disable CS0162 // erişilemez kod: yalnızca derleyicinin metodu iterator olarak tanıması için
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class GatedProcessor : IChunkProcessor
    {
        private readonly Func<AudioChunk, ChunkResult?> _map;
        private readonly TaskCompletionSource? _gate;
        public int Processed;
        public GatedProcessor(Func<AudioChunk, ChunkResult?> map, TaskCompletionSource? gate = null) { _map = map; _gate = gate; }
        public async Task<ChunkResult?> ProcessAsync(AudioChunk chunk, CancellationToken ct)
        {
            if (_gate is not null) await _gate.Task.ConfigureAwait(false);
            Interlocked.Increment(ref Processed);
            return _map(chunk);
        }
    }

    private sealed class ThrowingProcessor(int throwAt, Func<AudioChunk, ChunkResult?> map) : IChunkProcessor
    {
        public int Seen;
        public Task<ChunkResult?> ProcessAsync(AudioChunk chunk, CancellationToken ct)
        {
            int n = Interlocked.Increment(ref Seen);
            if (n == throwAt) throw new InvalidOperationException("boom");
            return Task.FromResult(map(chunk));
        }
    }

    /// <summary>N'inci chunk işlendiğinde verilen <see cref="CancellationTokenSource"/>'u iptal eder —
    /// canlı mikrofonda kullanıcının "Durdur"a basmasını simüle eder (birkaç chunk toplanmış olur).</summary>
    private sealed class CancelAfterCountProcessor(int cancelAfter, CancellationTokenSource cts, Func<AudioChunk, ChunkResult?> map) : IChunkProcessor
    {
        private int _count;
        public Task<ChunkResult?> ProcessAsync(AudioChunk chunk, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _count) == cancelAfter) cts.Cancel();
            return Task.FromResult(map(chunk));
        }
    }

    private sealed class RecordingStore : ISessionStore
    {
        public readonly List<(Guid, ChunkResult)> Saved = new();
        public Task SaveChunkAsync(Guid id, ChunkResult r, CancellationToken ct) { lock (Saved) Saved.Add((id, r)); return Task.CompletedTask; }
        public Task<MeetingSession?> GetSessionAsync(Guid id, CancellationToken ct) => Task.FromResult<MeetingSession?>(null);
        public Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MeetingSession>>(Array.Empty<MeetingSession>());
        public Task PurgeExpiredAudioAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeAggregator : IMeetingAggregator
    {
        public MeetingSession? Seen;
        public MeetingSummary Aggregate(MeetingSession s)
        {
            Seen = s;
            return new MeetingSummary(Emotion.Notr, new Dictionary<Emotion, double>(), 0, 0,
                Array.Empty<ChunkResult>(), ValenceArousal.Neutral, 1.0, "özet");
        }
    }

    private static AudioChunk ChunkAt(int i) =>
        new(new float[16000], 16000, TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(i + 1));

    private static ChunkResult ResultFor(AudioChunk c) =>
        new(c.Start, c.End, new FusedResult(ValenceArousal.Neutral, Emotion.Notr, Emotion.Notr,
            Confidence.Orta, true, false, IncongruencePattern.Yok, 1.0,
            TextVa: ValenceArousal.Neutral, ProsodyVa: ValenceArousal.Neutral,
            TextConfidence: 0, ProsodyConfidence: 0), $"c{c.Start.TotalSeconds}");

    private static AppConfig Cfg(int cap) =>
        AppConfig.Default with { Pipeline = AppConfig.Default.Pipeline with { ChannelCapacity = cap } };

    private static readonly CaptureOptions Opts = new(TimeSpan.FromSeconds(1), TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_ProcessesAllChunksInOrder_WritesTimelineAndStore()
    {
        var store = new RecordingStore(); var agg = new FakeAggregator();
        var proc = new GatedProcessor(ResultFor);
        var pipeline = new EmotionPipeline(proc, store, agg, Cfg(32));
        var id = Guid.NewGuid(); var started = DateTimeOffset.UnixEpoch;

        var summary = await pipeline.RunAsync(new FakeSource(5, ChunkAt), Opts, id, started,
            DetectedLanguage.Turkish, progress: null, CancellationToken.None);

        Assert.Equal(5, pipeline.Timeline.Count);
        Assert.Equal(Enumerable.Range(0, 5).Select(i => (double)i).ToArray(),
            pipeline.Timeline.Select(c => c.TimeStart.TotalSeconds).ToArray()); // sıralı
        Assert.Equal(5, store.Saved.Count);
        Assert.All(store.Saved, s => Assert.Equal(id, s.Item1));
        Assert.NotNull(agg.Seen);
        Assert.Equal(id, agg.Seen!.Id);
        Assert.Equal(started, agg.Seen.StartedAt);
        Assert.Equal(DetectedLanguage.Turkish, agg.Seen.Language);
        Assert.Equal("özet", summary.TurkishSummary);
    }

    [Fact]
    public async Task RunAsync_SilenceChunks_SkippedFromTimeline()
    {
        var store = new RecordingStore(); var agg = new FakeAggregator();
        // tek sayılı chunk'lar sessiz (null)
        var proc = new GatedProcessor(c => ((int)c.Start.TotalSeconds % 2 == 0) ? ResultFor(c) : null);
        var pipeline = new EmotionPipeline(proc, store, agg, Cfg(32));

        await pipeline.RunAsync(new FakeSource(6, ChunkAt), Opts, Guid.NewGuid(), DateTimeOffset.UnixEpoch,
            DetectedLanguage.Turkish, null, CancellationToken.None);

        Assert.Equal(3, pipeline.Timeline.Count); // 0,2,4
        Assert.All(pipeline.Timeline, c => Assert.Equal(0, (int)c.TimeStart.TotalSeconds % 2));
    }

    [Fact]
    public async Task RunAsync_ProcessorThrows_ChunkSkipped_PipelineContinues()
    {
        var store = new RecordingStore(); var agg = new FakeAggregator();
        var proc = new ThrowingProcessor(throwAt: 2, ResultFor); // 2. chunk patlar
        var pipeline = new EmotionPipeline(proc, store, agg, Cfg(32));

        await pipeline.RunAsync(new FakeSource(4, ChunkAt), Opts, Guid.NewGuid(), DateTimeOffset.UnixEpoch,
            DetectedLanguage.Turkish, null, CancellationToken.None);

        Assert.Equal(3, pipeline.Timeline.Count); // 4 üretildi, 1 hata → 3 sonuç
    }

    [Fact]
    public async Task RunAsync_Overflow_DropsOldest_BoundedMemory()
    {
        var store = new RecordingStore(); var agg = new FakeAggregator();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proc = new GatedProcessor(ResultFor, gate);       // consumer tüm chunk'larda bloklu
        var pipeline = new EmotionPipeline(proc, store, agg, Cfg(2)); // küçük kapasite

        var run = pipeline.RunAsync(new FakeSource(20, ChunkAt), Opts, Guid.NewGuid(), DateTimeOffset.UnixEpoch,
            DetectedLanguage.Turkish, null, CancellationToken.None);

        // Producer 20 chunk yazarken consumer bloklu → en eskiler düşer.
        Assert.True(SpinWait.SpinUntil(() => pipeline.DroppedCount > 0, TimeSpan.FromSeconds(5)),
            "overflow'da drop beklenir");
        gate.SetResult();
        await run;

        Assert.True(pipeline.DroppedCount > 0, $"drop sayısı>0, {pipeline.DroppedCount}");
        Assert.True(pipeline.Timeline.Count <= 3, $"bounded: kapasite+1'i aşmamalı, {pipeline.Timeline.Count}");
        // Hayatta kalanlar en YENİ chunk'lar (en yüksek indeks 19 dahil).
        Assert.Contains(pipeline.Timeline, c => (int)c.TimeStart.TotalSeconds == 19);
    }

    [Fact]
    public async Task RunAsync_CaptureSourceThrows_PropagatesError_NotSwallowedIntoEmptySummary()
    {
        var store = new RecordingStore(); var agg = new FakeAggregator();
        var proc = new GatedProcessor(ResultFor);
        var pipeline = new EmotionPipeline(proc, store, agg, Cfg(32));
        var boom = new InvalidOperationException("mikrofon izni reddi simülasyonu");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.RunAsync(new ThrowingSource(boom), Opts, Guid.NewGuid(), DateTimeOffset.UnixEpoch,
                DetectedLanguage.Turkish, null, CancellationToken.None));

        // Hata sessizce yutulup boş-ama-başarılı bir özete düşülmediğini doğrula.
        Assert.Empty(pipeline.Timeline);
        Assert.Null(agg.Seen);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task RunAsync_Cancelled_ThrowsOperationCanceled_DoesNotHang()
    {
        var store = new RecordingStore(); var agg = new FakeAggregator();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proc = new GatedProcessor(ResultFor, gate); // consumer bloklu kalır → iptali bekleriz
        var pipeline = new EmotionPipeline(proc, store, agg, Cfg(32));
        using var cts = new CancellationTokenSource();

        var run = pipeline.RunAsync(new FakeSource(50, ChunkAt), Opts, Guid.NewGuid(), DateTimeOffset.UnixEpoch,
            DetectedLanguage.Turkish, null, cts.Token);

        // Consumer ilk chunk'ta gate'te bloklu kalır (ct'ye bağlı değil) — gate'i de
        // iptalle serbest bırakmazsak test asla dönmez. cts.Cancel() + gate'in canceled
        // olması birlikte OperationCanceledException'ın consumer'dan yukarı akmasını sağlar.
        cts.Cancel();
        gate.TrySetCanceled(cts.Token);

        // ThrowsAny: iptal bazen alt-sınıf TaskCanceledException olarak gelir (ThrowsAsync tam-tip ister → flaky).
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    /// <summary>Canlı mikrofonda "Durdur" tam olarak bu şekli alır: RunAsync iptal ile çıkar (yukarıdaki
    /// test bunu doğruluyor — motorun sözleşmesi bu), ama o ana kadar toplanan zaman çizelgesi kaybolmaz.
    /// Çağıran taraf (PipelineAnalysisRunner) BuildSummary ile bunu bir özete çevirebilir — "Durdur"
    /// sessizce boş bir sonuca düşmez.</summary>
    [Fact]
    public async Task RunAsync_CancelledMidRun_BuildSummary_ReflectsChunksCollectedSoFar()
    {
        var store = new RecordingStore(); var agg = new FakeAggregator();
        using var cts = new CancellationTokenSource();
        var proc = new CancelAfterCountProcessor(cancelAfter: 3, cts, ResultFor);
        var pipeline = new EmotionPipeline(proc, store, agg, Cfg(32));
        var id = Guid.NewGuid(); var started = DateTimeOffset.UnixEpoch;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pipeline.RunAsync(new FakeSource(20, ChunkAt), Opts, id, started, DetectedLanguage.Turkish, null, cts.Token));

        // İptalden önce işlenen chunk'lar zaman çizelgesinde kalır — motor onları atmaz.
        Assert.True(pipeline.Timeline.Count >= 1, "iptal öncesi en az bir chunk işlenmiş olmalı");

        var summary = pipeline.BuildSummary(id, started, DetectedLanguage.Turkish);

        Assert.NotNull(agg.Seen);
        Assert.Equal(pipeline.Timeline.Count, agg.Seen!.Chunks.Count);
        Assert.True(agg.Seen.Chunks.Count > 0);
        Assert.Equal("özet", summary.TurkishSummary);
    }

    /// <summary>"Durdur" ilk chunk işlenmeden basılırsa (boş oturum) BuildSummary çökmemeli —
    /// MeetingAggregator boş listede geçerli, boş-ama-başarılı bir özet döner.</summary>
    [Fact]
    public void BuildSummary_EmptyTimeline_ReturnsValidEmptySummary_DoesNotThrow()
    {
        var store = new RecordingStore(); var agg = new FakeAggregator();
        var proc = new GatedProcessor(ResultFor);
        var pipeline = new EmotionPipeline(proc, store, agg, Cfg(32));
        var id = Guid.NewGuid(); var started = DateTimeOffset.UnixEpoch;

        var summary = pipeline.BuildSummary(id, started, DetectedLanguage.Turkish);

        Assert.Empty(pipeline.Timeline);
        Assert.NotNull(agg.Seen);
        Assert.Empty(agg.Seen!.Chunks);
        Assert.Equal("özet", summary.TurkishSummary);
    }
}
