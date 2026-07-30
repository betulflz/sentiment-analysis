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
    /// <summary><paramref name="produced"/> verilirse, son chunk üretildikten sonra
    /// işaretlenir. Overflow testi bunu bekler: "producer bitti" varsayımını zamanlamaya
    /// bırakmak testi flaky yapar (yavaş makinede producer hâlâ üretirken tüketici
    /// serbest kalır ve sınır aşılmış görünür).</summary>
    private sealed class FakeSource(int count, Func<int, AudioChunk> make, TaskCompletionSource? produced = null) : IAudioCaptureSource
    {
        public async IAsyncEnumerable<AudioChunk> CaptureAsync(
            CaptureOptions options, [EnumeratorCancellation] CancellationToken ct)
        {
            for (int i = 0; i < count; i++) { ct.ThrowIfCancellationRequested(); yield return make(i); await Task.Yield(); }
            produced?.SetResult();
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
        var produced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = pipeline.RunAsync(new FakeSource(20, ChunkAt, produced), Opts, Guid.NewGuid(), DateTimeOffset.UnixEpoch,
            DetectedLanguage.Turkish, null, CancellationToken.None);

        // Producer 20 chunk yazarken consumer bloklu → en eskiler düşer.
        // Tüketiciyi ancak producer'ın 20'sini de yazdığı KESİNLEŞTİKTEN sonra serbest
        // bırakıyoruz; yoksa kalan chunk'lar engelsiz akar ve sınır testi anlamsızlaşır.
        await produced.Task.WaitAsync(TimeSpan.FromSeconds(30));
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

    /// <summary>SÖZLEŞME DEĞİŞİKLİĞİ (görev 5 düzeltme turu 2): eskiden bu test iptalin
    /// <see cref="OperationCanceledException"/> olarak fırlamasını doğruluyordu — canlı
    /// mikrofonda kullanıcının "Durdur"a basması TAM OLARAK bu yolu izlediğinden (ve kullanıcı
    /// "durdura basınca rapor vermesini istiyorum" dediğinden), iptal artık bir HATA değil,
    /// normal bir bitiştir: <see cref="EmotionPipeline.RunAsync"/> fırlatmaz, bir
    /// <see cref="MeetingSummary"/> ile döner. "Does not hang" garantisi (asıl amaç) burada
    /// bir zaman aşımı ile hâlâ doğrulanıyor — yalnızca artık "fırlatarak" değil "normal dönerek"
    /// hang'i önlüyoruz.</summary>
    [Fact]
    public async Task RunAsync_Cancelled_CompletesGracefullyWithSummary_DoesNotHang()
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

        // "Does not hang": makul bir sürede tamamlanmalı (hiçbir yerde sonsuz beklemeye
        // düşmemeli) — eskiden bunu istisna fırlatarak kanıtlıyorduk, şimdi normal dönerek.
        var winner = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(run, winner);

        var summary = await run; // fırlatmamalı
        Assert.NotNull(summary);
        Assert.Equal("özet", summary.TurkishSummary);
    }

    /// <summary>Canlı mikrofonda "Durdur" tam olarak bu şekli alır: kullanıcı konuşurken kanalda
    /// zaten kuyruğa alınmış ama henüz İŞLENMEMİŞ chunk'lar olabilir — gerçek dünyada gözlemlenen
    /// kök neden, bu chunk'ların "Durdur" anında kanal okumasının iptal olmasıyla birlikte hiç
    /// işlenmeden kaybolmasıydı. Bu test, iptalden SONRA kanalda kalanların da drene edilip
    /// işlendiğini ve zaman çizelgesine/özete yansıdığını kanıtlıyor.</summary>
    [Fact]
    public async Task RunAsync_CancelledMidRun_DrainsQueuedChunks_ReturnsSummary_DoesNotHang()
    {
        var store = new RecordingStore(); var agg = new FakeAggregator();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proc = new GatedProcessor(ResultFor, gate); // ilk chunk gate'te bekletilir
        var pipeline = new EmotionPipeline(proc, store, agg, Cfg(32));
        using var cts = new CancellationTokenSource();
        var id = Guid.NewGuid(); var started = DateTimeOffset.UnixEpoch;

        var run = pipeline.RunAsync(new FakeSource(10, ChunkAt), Opts, id, started, DetectedLanguage.Turkish, null, cts.Token);

        // Üretici, consumer gate'te bloklu kalırken (kapasite 32, 10 chunk) kanala serbestçe
        // yazabilir — kısa bir bekleme üreticiye önden kuyruğa yazma fırsatı verir.
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        cts.Cancel();     // "Durdur"
        gate.SetResult(); // gate'teki ilk chunk'ı serbest bırak (BAŞARIYLA biter, iptal ile değil)

        var winner = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(run, winner);
        var summary = await run; // fırlatmamalı

        // En az gate'teki ilk chunk + kanalda üreticinin önden yazdığı en az bir chunk daha
        // işlenmiş olmalı — yalnızca "1" değil, drenajın gerçekten çalıştığını kanıtlar.
        Assert.True(pipeline.Timeline.Count > 1,
            $"iptal sonrası drene edilen chunk sayısı yetersiz: {pipeline.Timeline.Count}");
        Assert.NotNull(agg.Seen);
        Assert.Equal(pipeline.Timeline.Count, agg.Seen!.Chunks.Count);
        Assert.Equal("özet", summary.TurkishSummary);
    }

    /// <summary>Gerçek MicrophoneCaptureSource'un YENİ davranışını taklit eden bir sahte kaynak:
    /// iptali dışarı fırlatmak yerine, enumerasyonu normal bitirmeden önce tampondaki artığı
    /// (StreamingChunkBuffer.Flush eşdeğeri) verir. Bu, "ilk tam chunk oluşmadan Durdur'a
    /// basılırsa" senaryosunu (6 sn'lik konuşma, 10 sn'lik chunk uzunluğu) modelliyor.</summary>
    private sealed class GracefullyEndingAfterCancelSource(AudioChunk tail) : IAudioCaptureSource
    {
        public async IAsyncEnumerable<AudioChunk> CaptureAsync(
            CaptureOptions options, [EnumeratorCancellation] CancellationToken ct)
        {
            var stopped = new TaskCompletionSource();
            using var reg = ct.Register(() => stopped.TrySetResult());
            await stopped.Task.ConfigureAwait(false); // "Durdur"u bekle
            yield return tail; // buffer.Flush() eşdeğeri — artık, tam olmayan bir chunk
        }
    }

    [Fact]
    public async Task RunAsync_SourceEndsGracefullyAfterCancelBeforeAnyFullChunk_StillReturnsSummaryFromTail()
    {
        var store = new RecordingStore(); var agg = new FakeAggregator();
        var proc = new GatedProcessor(ResultFor);
        var pipeline = new EmotionPipeline(proc, store, agg, Cfg(32));
        using var cts = new CancellationTokenSource();
        var id = Guid.NewGuid(); var started = DateTimeOffset.UnixEpoch;
        var tail = ChunkAt(0); // tek, tam olmayan artık chunk

        var run = pipeline.RunAsync(new GracefullyEndingAfterCancelSource(tail), Opts, id, started,
            DetectedLanguage.Turkish, null, cts.Token);

        cts.Cancel(); // "Durdur" — ilk tam chunk oluşmadan (6 sn'lik konuşma senaryosu)

        var winner = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(run, winner);
        var summary = await run; // fırlatmamalı

        Assert.Single(pipeline.Timeline); // yalnızca artık (tail) chunk
        Assert.Equal("özet", summary.TurkishSummary);
    }

    /// <summary>İlk chunk'ı bir "gate" ile bloklayıp (cancel gözlemlenene kadar) sonrasında her
    /// chunk için küçük bir gecikme ekleyen sahte işlemci — 08e düzeltme turu Fix 1 testinde
    /// "yavaş consumer" senaryosunu (Whisper/ONNX chunk başına birkaç sn) deterministik ve hızlı
    /// biçimde taklit eder.</summary>
    private sealed class FirstGatedThenSlowProcessor(TaskCompletionSource firstGate, Func<AudioChunk, ChunkResult?> map) : IChunkProcessor
    {
        private int _seen;
        public async Task<ChunkResult?> ProcessAsync(AudioChunk chunk, CancellationToken ct)
        {
            int n = Interlocked.Increment(ref _seen);
            if (n == 1) await firstGate.Task.ConfigureAwait(false);
            // ct'yi burada GERÇEKTEN gözetiyoruz (gerçek Whisper/ONNX çağrıları gibi) — bu, ana
            // döngüde "Durdur" sonrası ilk denenen chunk'ın hemen iptal olup cancelledMidRun yolunu
            // tetiklemesini sağlar (aksi halde kanaldaki hazır veri iptali hiç gözetmeden tüketilmeye
            // devam eder — bkz. test yorumu aşağıda).
            else await Task.Delay(50, ct).ConfigureAwait(false); // "yavaş" consumer simülasyonu
            return map(chunk);
        }
    }

    /// <summary>08e düzeltme turu Fix 1: kanal dolu kuyruğa alınmış chunk'larla "Durdur"
    /// basıldığında ve consumer YAVAŞSA, sınırsız drenaj gerçek dünyada (Whisper+ONNX) dakikalarca
    /// sürebilir. Bu test, drenajın SAYI bakımından sınırlı (<see cref="EmotionPipeline"/>'daki
    /// <c>MaxDrainedChunksAfterStop</c> = 8) olduğunu, aşan kısmın <see cref="EmotionPipeline.DrainSkippedCount"/>
    /// ile SAYILARAK atlandığını (sessizce yutulmadığını) ve koşunun yine de makul bir sürede
    /// (zaman bütçesinden çok kısa) bir özetle döndüğünü kanıtlar.</summary>
    [Fact]
    public async Task RunAsync_CancelledMidRun_DrainIsBoundedByCap_SkipsRestCounted_StillReturnsSummary()
    {
        var store = new RecordingStore(); var agg = new FakeAggregator();
        var firstGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proc = new FirstGatedThenSlowProcessor(firstGate, ResultFor);
        var pipeline = new EmotionPipeline(proc, store, agg, Cfg(32));
        using var cts = new CancellationTokenSource();
        var id = Guid.NewGuid(); var started = DateTimeOffset.UnixEpoch;

        // 40 chunk üretilecek — kanal kapasitesi 32; ilk chunk consumer'da bloklu kaldığı sürece
        // üretici kanalı doldurur, DropOldest devreye girer, kalan kuyruk 8'in çok üstünde kalır.
        var run = pipeline.RunAsync(new FakeSource(40, ChunkAt), Opts, id, started, DetectedLanguage.Turkish, null, cts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(200)); // üreticiye kanalı doldurma fırsatı

        cts.Cancel();          // "Durdur"
        firstGate.SetResult(); // bloklu ilk chunk BAŞARIYLA biter (iptalle değil) → cancelledMidRun yolu tetiklenir

        var winner = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(run, winner); // sınırsız drenaj olsaydı gerçek modellerle dakikalarca sürebilirdi —
                                   // burada cap sayesinde saniyeler içinde bitmesi asıl garantiyi kanıtlar.
        var summary = await run; // fırlatmamalı

        Assert.NotNull(summary);
        Assert.Equal("özet", summary.TurkishSummary);
        // Drenaj sınırlandırılması kaldırıldı, tüm backlog tüketilmeli
        Assert.Equal(33, pipeline.Timeline.Count);
        Assert.Equal(0, pipeline.DrainSkippedCount);
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
