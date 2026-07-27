# Alt-Proje 06: Pipeline Orkestrasyonu — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Yakalama → VAD → (STT ∥ prozodi → metin-duygu) → füzyon → zaman çizelgesi + kalıcılık akışını, bounded `System.Threading.Channels` ile backpressure/overflow yönetimi altında orkestre etmek; toplantı sonu `MeetingSummary` üretmek.

**Architecture:** Tümü `SentimentAnalysis.Application` katmanında (yeni Infrastructure yok; mevcut 02–05 bileşenlerini arayüz üzerinden orkestre eder). Saf `QualitySignalsBuilder` → `QualitySignals`; `ChunkProcessor` (per-chunk DAG, tüm bağımlılıklar arayüz → fake'lerle test); `EmotionPipeline` (bounded kanal producer/consumer, tek consumer sıralı, DropOldest overflow, timeline + store yazımı, sonda agregasyon); `InMemorySessionStore : ISessionStore` (bellek; gerçek SQLite → 07); `PipelineConfig` (AppConfig'e additive). Modelsiz tam test.

**Tech Stack:** .NET 9 · C# · xUnit 2.9.2 · `System.Threading.Channels` (BCL)

## Global Constraints

- **Hedef framework:** `net9.0`. Nullable + ImplicitUsings açık.
- **Katman:** yalnızca `SentimentAnalysis.Application` (→ Core). Yeni proje YOK.
- **`git YOK`**: her task sonu `dotnet build && dotnet test`, commit değil.
- **Yerleşik tipler DEĞİŞMEZ:** `AudioChunk`, `Transcript(Text, DetectedLanguage Language, double AvgLogProb)`, `TextEmotion`, `ProsodyEmotion`, `QualitySignals(double AudioSnr, double SpeechRatio, TimeSpan Duration, int TranscriptWordCount, double TranscriptAvgLogProb)`, `FusedResult`, `ChunkResult(TimeSpan TimeStart, TimeSpan TimeEnd, FusedResult Fused, string Transcript)`, `MeetingSession(Guid Id, DateTimeOffset StartedAt, DetectedLanguage Language, IReadOnlyList<ChunkResult> Chunks)`, `MeetingSummary`.
- **Yerleşik arayüzler:** `IAudioCaptureSource`, `IVadGate`, `ISpeechToText`, `IProsodyEmotionModel`, `ITextEmotionModel`, `IEmotionFusion`, `IMeetingAggregator`, `ISessionStore`. Mevcut DI: `AddApplicationCore`, `AddFusion` (EmotionFusion + MeetingAggregator singleton).
- **Per-chunk DAG:** VAD sessiz → chunk atlanır (timeline'a/store'a düşmez). Konuşma varsa: prozodi ∥ STT paralel (`Task.WhenAll`), sonra metin-duygu (transcript'e bağlı), sonra `QualitySignalsBuilder`, sonra füzyon → `ChunkResult`.
- **Sıralama:** tek consumer → `ChunkResult`'lar chunk sırasına göre timeline'a + store'a düşer (kabul kriteri).
- **Overflow:** bounded kanal `FullMode=DropOldest` + `itemDropped` callback ile drop sayımı (`DroppedCount`). Canlı akıcılık için producer bloklanmaz (en eski düşer). Bellek kanal kapasitesiyle sınırlı.
- **Hata izolasyonu:** bir chunk işlenirken bileşen hatası (OperationCanceledException hariç) → logla/say + o chunk'ı atla; pipeline çökmez. `CancellationToken` akar.
- **Metadata:** `MeetingSession.StartedAt`/`Language` `ISessionStore.SaveChunkAsync`'ten gelmez → `RunAsync` çağrandan alır (test determinizmi için `startedAt` enjekte, saat tahmini yok).
- **AppConfig additive:** yeni `PipelineConfig` defaultlu; `AppConfig.Default` + `with` kırılmaz.
- **Kapsam dışı (06b/07):** cümle-hizalama, degradasyon heuristiği, derin geçiş, ArrayPool; gerçek `ISessionStore` (SQLite/retention) → 07.

### `System.Threading.Channels` — bounded + drop callback (Task 4)

```csharp
using System.Threading.Channels;
var channel = Channel.CreateBounded<AudioChunk>(
    new BoundedChannelOptions(capacity)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = true,
    },
    itemDropped: _ => Interlocked.Increment(ref _droppedCount)); // .NET 7+ overload
// producer: await channel.Writer.WriteAsync(chunk, ct) — DropOldest'te bloklanmaz
// consumer: await foreach (var c in channel.Reader.ReadAllAsync(ct)) { ... }
```

---

### Task 1: `QualitySignalsBuilder` (saf)

**Files:**
- Create: `src/SentimentAnalysis.Application/Pipeline/QualitySignalsBuilder.cs`
- Create: `tests/SentimentAnalysis.Application.Tests/Pipeline/QualitySignalsBuilderTests.cs`

**Interfaces:**
- Consumes: `AudioChunk`, `Transcript`, `QualitySignals` (Core)
- Produces: `static class QualitySignalsBuilder { QualitySignals Build(AudioChunk chunk, Transcript transcript); }`. SNR/konuşma-oranı basit ses-enerji tabanlı (kalibrasyon 09); süre = chunk End−Start; kelime = transcript boşluk-bölme; avg-logprob = transcript.AvgLogProb.

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Application.Tests/Pipeline/QualitySignalsBuilderTests.cs`:

```csharp
using System;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Pipeline;

public class QualitySignalsBuilderTests
{
    private static AudioChunk Chunk(float[] s, double startSec = 0, double endSec = 1) =>
        new(s, 16000, TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec));

    private static Transcript Tr(string text, double logProb = -0.3) =>
        new(text, DetectedLanguage.Turkish, logProb);

    [Fact]
    public void Build_CopiesDurationWordCountAndLogProb()
    {
        var q = QualitySignalsBuilder.Build(Chunk(new float[16000], 1, 3.5), Tr("bugün hava çok güzel", -0.42));
        Assert.Equal(TimeSpan.FromSeconds(2.5), q.Duration);
        Assert.Equal(4, q.TranscriptWordCount);
        Assert.Equal(-0.42, q.TranscriptAvgLogProb, precision: 5);
    }

    [Fact]
    public void Build_EmptyTranscript_ZeroWords()
    {
        var q = QualitySignalsBuilder.Build(Chunk(new float[1600]), Tr("   "));
        Assert.Equal(0, q.TranscriptWordCount);
    }

    [Fact]
    public void Build_Silence_LowSnrAndSpeechRatio()
    {
        var q = QualitySignalsBuilder.Build(Chunk(new float[16000]), Tr("x"));
        Assert.Equal(0.0, q.AudioSnr, precision: 5);
        Assert.Equal(0.0, q.SpeechRatio, precision: 5);
    }

    [Fact]
    public void Build_HalfLoud_PositiveSnr_MidSpeechRatio()
    {
        // İlk yarı yüksek (0.5 genlik kare-dalga), ikinci yarı sessiz.
        var s = new float[16000];
        for (int i = 0; i < 8000; i++) s[i] = (i % 2 == 0) ? 0.5f : -0.5f;
        var q = QualitySignalsBuilder.Build(Chunk(s), Tr("konuşma"));
        Assert.True(q.AudioSnr > 0, $"SNR>0 beklenir, {q.AudioSnr}");
        Assert.InRange(q.SpeechRatio, 0.3, 0.7); // ~yarısı konuşma
    }

    [Fact]
    public void Build_EmptySamples_SafeZeroSignals()
    {
        var q = QualitySignalsBuilder.Build(Chunk(Array.Empty<float>(), 0, 0), Tr("x"));
        Assert.Equal(0.0, q.AudioSnr, precision: 5);
        Assert.Equal(0.0, q.SpeechRatio, precision: 5);
        Assert.Equal(TimeSpan.Zero, q.Duration);
    }
}
```

- [ ] **Step 2: Başarısız olduğunu doğrula** — Run: `dotnet test tests/SentimentAnalysis.Application.Tests`. Expected: FAIL (`QualitySignalsBuilder` yok).

- [ ] **Step 3: `QualitySignalsBuilder`'ı yaz**

`src/SentimentAnalysis.Application/Pipeline/QualitySignalsBuilder.cs`:

```csharp
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Pipeline;

/// <summary>Bir chunk'ın ses + transkript kalite sinyallerini üretir (füzyonun kalite-ağırlıklandırması
/// için). SNR ve konuşma-oranı basit çerçeve-enerji sezgiselleridir (kalibrasyon → Alt-Proje 09).
/// Saf, deterministik. (04a'nın 06'ya bıraktığı iş.)</summary>
public static class QualitySignalsBuilder
{
    private const int FrameSize = 320;   // 20 ms @16k
    private const double Eps = 1e-9;
    private const double SpeechFactor = 0.1; // tepe-enerjinin %10'u üstü = konuşma çerçevesi

    public static QualitySignals Build(AudioChunk chunk, Transcript transcript)
    {
        var (snr, speechRatio) = AnalyzeAudio(chunk.Samples);
        int words = CountWords(transcript.Text);
        return new QualitySignals(
            AudioSnr: snr,
            SpeechRatio: speechRatio,
            Duration: chunk.End - chunk.Start,
            TranscriptWordCount: words,
            TranscriptAvgLogProb: transcript.AvgLogProb);
    }

    private static (double snr, double speechRatio) AnalyzeAudio(float[] samples)
    {
        if (samples.Length == 0) return (0.0, 0.0);

        // Çerçeve-başı RMS.
        int frameCount = Math.Max(1, samples.Length / FrameSize);
        var rms = new double[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            int start = f * FrameSize;
            int end = Math.Min(start + FrameSize, samples.Length);
            double sq = 0;
            for (int i = start; i < end; i++) sq += (double)samples[i] * samples[i];
            rms[f] = Math.Sqrt(sq / Math.Max(1, end - start));
        }

        double peak = 0;
        for (int f = 0; f < frameCount; f++) if (rms[f] > peak) peak = rms[f];
        if (peak < Eps) return (0.0, 0.0); // tümü sessiz

        // Konuşma oranı: tepe-enerjinin SpeechFactor'ü üstündeki çerçeveler.
        double threshold = peak * SpeechFactor;
        int speechFrames = 0;
        for (int f = 0; f < frameCount; f++) if (rms[f] > threshold) speechFrames++;
        double speechRatio = (double)speechFrames / frameCount;

        // SNR(dB): genel sinyal RMS'i vs gürültü tabanı (en düşük %10 çerçeve RMS ortalaması).
        var sorted = (double[])rms.Clone();
        Array.Sort(sorted);
        int noiseCount = Math.Max(1, frameCount / 10);
        double noise = 0;
        for (int i = 0; i < noiseCount; i++) noise += sorted[i];
        noise = noise / noiseCount + Eps;

        double signal = 0;
        for (int f = 0; f < frameCount; f++) signal += rms[f];
        signal /= frameCount;

        double snr = 20.0 * Math.Log10((signal + Eps) / noise);
        if (snr < 0) snr = 0; // negatif SNR'ı 0'a kırp (güvenilirlik tabanı)
        return (snr, speechRatio);
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
```

- [ ] **Step 4: Testin geçtiğini doğrula** — Run: `dotnet test tests/SentimentAnalysis.Application.Tests`. Expected: PASS.

---

### Task 2: `InMemorySessionStore : ISessionStore`

**Files:**
- Create: `src/SentimentAnalysis.Application/Pipeline/InMemorySessionStore.cs`
- Create: `tests/SentimentAnalysis.Application.Tests/Pipeline/InMemorySessionStoreTests.cs`

**Interfaces:**
- Consumes: `ISessionStore`, `ChunkResult`, `MeetingSession` (Application/Core)
- Produces: `sealed class InMemorySessionStore : ISessionStore`. Chunk'ları sessionId başına thread-safe biriktirir; `GetSessionAsync` biriken chunk'larla `MeetingSession` döner (StartedAt = ilk kayıt sırası enjekte saatten; Language = `DetectedLanguage.Other` — metadata sınırı, gerçek store 07). `PurgeExpiredAudioAsync` no-op.

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Application.Tests/Pipeline/InMemorySessionStoreTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Pipeline;

public class InMemorySessionStoreTests
{
    private static ChunkResult Result(double start) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(start + 1),
            new FusedResult(ValenceArousal.Neutral, Emotion.Notr, Emotion.Notr,
                Confidence.Orta, true, false, IncongruencePattern.Yok, 1.0, 0.0, 0.0),
            $"chunk-{start}");

    [Fact]
    public async Task SaveAndGet_ReturnsChunksInOrder()
    {
        var store = new InMemorySessionStore();
        var id = Guid.NewGuid();
        await store.SaveChunkAsync(id, Result(0), CancellationToken.None);
        await store.SaveChunkAsync(id, Result(1), CancellationToken.None);

        var session = await store.GetSessionAsync(id, CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal(id, session!.Id);
        Assert.Equal(2, session.Chunks.Count);
        Assert.Equal("chunk-0", session.Chunks[0].Transcript);
        Assert.Equal("chunk-1", session.Chunks[1].Transcript);
    }

    [Fact]
    public async Task GetSession_UnknownId_ReturnsNull()
    {
        var store = new InMemorySessionStore();
        Assert.Null(await store.GetSessionAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ListSessions_ReturnsAllSaved()
    {
        var store = new InMemorySessionStore();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        await store.SaveChunkAsync(a, Result(0), CancellationToken.None);
        await store.SaveChunkAsync(b, Result(0), CancellationToken.None);

        var list = await store.ListSessionsAsync(CancellationToken.None);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, s => s.Id == a);
        Assert.Contains(list, s => s.Id == b);
    }

    [Fact]
    public async Task PurgeExpiredAudio_NoThrow()
    {
        var store = new InMemorySessionStore();
        await store.PurgeExpiredAudioAsync(CancellationToken.None); // no-op (gerçek retention 07)
    }
}
```

- [ ] **Step 2: Başarısız olduğunu doğrula** — FAIL (`InMemorySessionStore` yok). (`Emotion.Notr` / `ValenceArousal.Neutral` adlarını mevcut Core'dan teyit et; farklıysa uyarlanır — implementer `grep -rn "enum Emotion" src/SentimentAnalysis.Core` ile doğrular.)

- [ ] **Step 3: `InMemorySessionStore`'ı yaz**

`src/SentimentAnalysis.Application/Pipeline/InMemorySessionStore.cs`:

```csharp
using System.Collections.Concurrent;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Pipeline;

/// <summary>Oturum chunk'larını BELLEKTE biriktiren <see cref="ISessionStore"/> (Alt-Proje 06;
/// gerçek SQLite/dosya + retention → Alt-Proje 07). Thread-safe. Metadata sınırı: StartedAt ilk
/// kayıt anıdır, Language <see cref="DetectedLanguage.Other"/> (SaveChunkAsync bunları taşımaz;
/// toplantı-düzeyi metadata pipeline tarafından ayrıca yönetilir).</summary>
public sealed class InMemorySessionStore : ISessionStore
{
    private sealed class Entry
    {
        public DateTimeOffset StartedAt { get; init; }
        public List<ChunkResult> Chunks { get; } = new();
    }

    private readonly ConcurrentDictionary<Guid, Entry> _sessions = new();
    private readonly Func<DateTimeOffset> _clock;

    public InMemorySessionStore() : this(() => DateTimeOffset.UtcNow) { }
    public InMemorySessionStore(Func<DateTimeOffset> clock) => _clock = clock;

    public Task SaveChunkAsync(Guid sessionId, ChunkResult result, CancellationToken ct)
    {
        var entry = _sessions.GetOrAdd(sessionId, _ => new Entry { StartedAt = _clock() });
        lock (entry.Chunks) entry.Chunks.Add(result);
        return Task.CompletedTask;
    }

    public Task<MeetingSession?> GetSessionAsync(Guid id, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(id, out var entry)) return Task.FromResult<MeetingSession?>(null);
        MeetingSession session;
        lock (entry.Chunks)
            session = new MeetingSession(id, entry.StartedAt, DetectedLanguage.Other, entry.Chunks.ToList());
        return Task.FromResult<MeetingSession?>(session);
    }

    public Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken ct)
    {
        var list = new List<MeetingSession>();
        foreach (var kv in _sessions)
        {
            lock (kv.Value.Chunks)
                list.Add(new MeetingSession(kv.Key, kv.Value.StartedAt, DetectedLanguage.Other, kv.Value.Chunks.ToList()));
        }
        return Task.FromResult<IReadOnlyList<MeetingSession>>(list);
    }

    public Task PurgeExpiredAudioAsync(CancellationToken ct) => Task.CompletedTask;
}
```

- [ ] **Step 4: Testin geçtiğini doğrula** — Run: `dotnet test tests/SentimentAnalysis.Application.Tests`. Expected: PASS.

---

### Task 3: `ChunkProcessor` + `IChunkProcessor` (per-chunk DAG)

**Files:**
- Create: `src/SentimentAnalysis.Application/Pipeline/IChunkProcessor.cs`
- Create: `src/SentimentAnalysis.Application/Pipeline/ChunkProcessor.cs`
- Create: `tests/SentimentAnalysis.Application.Tests/Pipeline/ChunkProcessorTests.cs`

**Interfaces:**
- Consumes: `IVadGate`, `ISpeechToText`, `IProsodyEmotionModel`, `ITextEmotionModel`, `IEmotionFusion`, `QualitySignalsBuilder`; `AudioChunk`, `ChunkResult` (Core)
- Produces: `interface IChunkProcessor { Task<ChunkResult?> ProcessAsync(AudioChunk chunk, CancellationToken ct); }`; `sealed class ChunkProcessor : IChunkProcessor` (ctor tüm 5 arayüz). VAD sessiz → `null`; aksi halde prozodi ∥ STT paralel → metin → quality → füzyon → `ChunkResult`.

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Application.Tests/Pipeline/ChunkProcessorTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Pipeline;

public class ChunkProcessorTests
{
    private sealed class FakeVad(bool speech) : IVadGate { public bool HasSpeech(AudioChunk c) => speech; }
    private sealed class FakeStt(Transcript t) : ISpeechToText
    { public int Calls; public Task<Transcript> TranscribeAsync(AudioChunk c, CancellationToken ct) { Interlocked.Increment(ref Calls); return Task.FromResult(t); } }
    private sealed class FakeProsody(ProsodyEmotion p) : IProsodyEmotionModel
    { public int Calls; public Task<ProsodyEmotion> AnalyzeAsync(AudioChunk c, CancellationToken ct) { Interlocked.Increment(ref Calls); return Task.FromResult(p); } }
    private sealed class FakeText(TextEmotion e) : ITextEmotionModel
    { public Transcript? Seen; public Task<TextEmotion> AnalyzeAsync(Transcript t, CancellationToken ct) { Seen = t; return Task.FromResult(e); } }
    private sealed class FakeFusion(FusedResult r) : IEmotionFusion
    { public QualitySignals? Q; public FusedResult Fuse(TextEmotion t, ProsodyEmotion p, QualitySignals q) { Q = q; return r; } }

    private static AudioChunk Chunk() => new(new float[16000], 16000, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
    private static readonly Transcript Tx = new("merhaba dünya", DetectedLanguage.Turkish, -0.2);
    private static readonly ProsodyEmotion Pr = new(new ValenceArousal(0.1, 0.6), 0.5);
    private static readonly TextEmotion Te = new(new ValenceArousal(0.3, 0.4), 0.7);
    private static readonly FusedResult Fr = new(new ValenceArousal(0.2, 0.5), Emotion.Notr, Emotion.Notr,
        Confidence.Orta, true, false, IncongruencePattern.Yok, 1.0, 0.3, 0.1);

    [Fact]
    public async Task ProcessAsync_Silence_ReturnsNull_NoModelCalls()
    {
        var stt = new FakeStt(Tx); var pros = new FakeProsody(Pr);
        var sut = new ChunkProcessor(new FakeVad(false), stt, pros, new FakeText(Te), new FakeFusion(Fr));
        var r = await sut.ProcessAsync(Chunk(), CancellationToken.None);
        Assert.Null(r);
        Assert.Equal(0, stt.Calls);
        Assert.Equal(0, pros.Calls);
    }

    [Fact]
    public async Task ProcessAsync_Speech_RunsSttAndProsody_BuildsChunkResult()
    {
        var stt = new FakeStt(Tx); var pros = new FakeProsody(Pr);
        var text = new FakeText(Te); var fusion = new FakeFusion(Fr);
        var sut = new ChunkProcessor(new FakeVad(true), stt, pros, text, fusion);

        var r = await sut.ProcessAsync(Chunk(), CancellationToken.None);

        Assert.NotNull(r);
        Assert.Equal(TimeSpan.FromSeconds(2), r!.TimeStart);
        Assert.Equal(TimeSpan.FromSeconds(4), r.TimeEnd);
        Assert.Equal("merhaba dünya", r.Transcript);
        Assert.Same(Fr, r.Fused);
        Assert.Equal(1, stt.Calls);
        Assert.Equal(1, pros.Calls);
        Assert.Same(Tx, text.Seen);                 // metin-duygu STT transcript'ini aldı
        Assert.Equal(2, fusion.Q!.TranscriptWordCount); // quality doğru kuruldu
        Assert.Equal(TimeSpan.FromSeconds(2), fusion.Q.Duration);
    }
}
```

- [ ] **Step 2: Başarısız olduğunu doğrula** — FAIL (`ChunkProcessor`/`IChunkProcessor` yok). (`Emotion.Notr` adını Core'dan teyit et.)

- [ ] **Step 3: `IChunkProcessor` + `ChunkProcessor`'ı yaz**

`src/SentimentAnalysis.Application/Pipeline/IChunkProcessor.cs`:

```csharp
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Pipeline;

/// <summary>Tek bir chunk'ı duygu boru hattından geçirir. VAD sessiz derse <c>null</c> döner
/// (chunk timeline'a düşmez).</summary>
public interface IChunkProcessor
{
    Task<ChunkResult?> ProcessAsync(AudioChunk chunk, CancellationToken ct);
}
```

`src/SentimentAnalysis.Application/Pipeline/ChunkProcessor.cs`:

```csharp
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Pipeline;

/// <summary>Per-chunk DAG: VAD kapısı → (prozodi ∥ STT paralel) → metin-duygu (transcript'e bağlı) →
/// <see cref="QualitySignalsBuilder"/> → füzyon → <see cref="ChunkResult"/>. Durumsuz.</summary>
public sealed class ChunkProcessor : IChunkProcessor
{
    private readonly IVadGate _vad;
    private readonly ISpeechToText _stt;
    private readonly IProsodyEmotionModel _prosody;
    private readonly ITextEmotionModel _text;
    private readonly IEmotionFusion _fusion;

    public ChunkProcessor(
        IVadGate vad, ISpeechToText stt, IProsodyEmotionModel prosody,
        ITextEmotionModel text, IEmotionFusion fusion)
    {
        _vad = vad; _stt = stt; _prosody = prosody; _text = text; _fusion = fusion;
    }

    public async Task<ChunkResult?> ProcessAsync(AudioChunk chunk, CancellationToken ct)
    {
        if (!_vad.HasSpeech(chunk)) return null;

        // Prozodi ∥ STT (ikisi de aynı ham chunk'ı alır).
        var prosodyTask = _prosody.AnalyzeAsync(chunk, ct);
        var sttTask = _stt.TranscribeAsync(chunk, ct);
        await Task.WhenAll(prosodyTask, sttTask).ConfigureAwait(false);

        var prosody = prosodyTask.Result;
        var transcript = sttTask.Result;

        var text = await _text.AnalyzeAsync(transcript, ct).ConfigureAwait(false); // transcript'e bağlı
        var quality = QualitySignalsBuilder.Build(chunk, transcript);
        var fused = _fusion.Fuse(text, prosody, quality);

        return new ChunkResult(chunk.Start, chunk.End, fused, transcript.Text);
    }
}
```

- [ ] **Step 4: Testin geçtiğini doğrula** — Run: `dotnet test tests/SentimentAnalysis.Application.Tests`. Expected: PASS.

---

### Task 4: `EmotionPipeline` + `PipelineConfig` + `AddPipeline()` DI

**Files:**
- Create: `src/SentimentAnalysis.Application/Pipeline/EmotionPipeline.cs`
- Create: `src/SentimentAnalysis.Application/Pipeline/PipelineServiceCollectionExtensions.cs`
- Modify: `src/SentimentAnalysis.Application/Configuration/AppConfig.cs` (`PipelineConfig` + `AppConfig`'e alan)
- Create: `tests/SentimentAnalysis.Application.Tests/Pipeline/EmotionPipelineTests.cs`

**Interfaces:**
- Consumes: `IChunkProcessor`, `ISessionStore`, `IMeetingAggregator`, `IAudioCaptureSource`, `CaptureOptions`, `AppConfig`; `ChunkResult`, `MeetingSession`, `MeetingSummary` (Core)
- Produces: `sealed record PipelineConfig(int ChannelCapacity = 32)`; `AppConfig`'e `PipelineConfig Pipeline` alanı (additive); `sealed class EmotionPipeline { EmotionPipeline(IChunkProcessor, ISessionStore, IMeetingAggregator, AppConfig); int DroppedCount { get; }; IReadOnlyList<ChunkResult> Timeline { get; }; Task<MeetingSummary> RunAsync(IAudioCaptureSource source, CaptureOptions options, Guid sessionId, DateTimeOffset startedAt, DetectedLanguage language, IProgress<ChunkResult>? progress, CancellationToken ct); }`; `static class PipelineServiceCollectionExtensions { IServiceCollection AddPipeline(this IServiceCollection); }`.

- [ ] **Step 1: `PipelineConfig`'i ekle ve `AppConfig`'e bağla (additive)**

`src/SentimentAnalysis.Application/Configuration/AppConfig.cs` içine, `VadConfig`'ten sonra ekle:

```csharp
/// <summary>Pipeline orkestrasyon ayarları (spec 06).</summary>
public sealed record PipelineConfig(
    int ChannelCapacity = 32);   // bounded kanal kapasitesi; taşarsa en eski chunk düşer
```

Ve `AppConfig` kaydını + `Default`'unu genişlet (additive, sona ekle):

```csharp
public sealed record AppConfig(
    FusionConfig Fusion,
    StorageConfig Storage,
    SttConfig Stt,
    EmotionConfig Emotion,
    VadConfig Vad,
    PipelineConfig Pipeline)
{
    public static AppConfig Default => new(
        Fusion: new FusionConfig(),
        Storage: new StorageConfig(),
        Stt: new SttConfig(),
        Emotion: new EmotionConfig(),
        Vad: new VadConfig(),
        Pipeline: new PipelineConfig());
}
```

> Not: Değişiklikten önce `grep -rn "new AppConfig(" src tests` ile pozisyonel çağrı olmadığını teyit et (yalnızca `AppConfig.Default` + `with` beklenir — 04a/04b/05'te kırılmadı).

- [ ] **Step 2: Başarısız testi yaz**

`tests/SentimentAnalysis.Application.Tests/Pipeline/EmotionPipelineTests.cs`:

```csharp
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
            Confidence.Orta, true, false, IncongruencePattern.Yok, 1.0, 0, 0), $"c{c.Start.TotalSeconds}");

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
}
```

- [ ] **Step 3: Başarısız olduğunu doğrula** — Run: `dotnet test tests/SentimentAnalysis.Application.Tests`. Expected: FAIL (`EmotionPipeline`/`PipelineConfig` yok).

- [ ] **Step 4: `EmotionPipeline`'ı yaz**

`src/SentimentAnalysis.Application/Pipeline/EmotionPipeline.cs`:

```csharp
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

        await producer.ConfigureAwait(false);

        var session = new MeetingSession(sessionId, startedAt, language, _timeline.ToList());
        return _aggregator.Aggregate(session);
    }
}
```

- [ ] **Step 5: `AddPipeline()`'ı yaz**

`src/SentimentAnalysis.Application/Pipeline/PipelineServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Application.Pipeline;

/// <summary>Pipeline orkestrasyon DI kaydı. `AddApplicationCore()` + model/füzyon uzantılarıyla
/// (AddStt/AddProsodyEmotion/AddTextEmotion/AddSileroVad/AddFusion) birlikte kullanılır.
/// `ISessionStore` bu turda bellek-içi (gerçek store 07'de `TryAdd`'i ezmeden değiştirilebilir).</summary>
public static class PipelineServiceCollectionExtensions
{
    public static IServiceCollection AddPipeline(this IServiceCollection services)
    {
        services.TryAddSingleton<ISessionStore, InMemorySessionStore>();
        services.AddSingleton<IChunkProcessor, ChunkProcessor>();
        services.AddSingleton<EmotionPipeline>();
        return services;
    }
}
```

- [ ] **Step 6: Testin geçtiğini + tüm çözümün derlendiğini doğrula** — Run: `dotnet build && dotnet test`. Expected: Build **0 uyarı**; tüm `EmotionPipeline` testleri PASS; mevcut testler (01–05) kırılmaz. Toplam ~131 + yeni pipeline testleri; env-gated skip 6'da kalır.

- [ ] **Step 7: `AddPipeline` DI smoke testi (opsiyonel ama önerilir)**

`tests/SentimentAnalysis.Application.Tests/Pipeline/PipelineDiTests.cs` ekle — DI'ın `EmotionPipeline`'ı çözebildiğini doğrula. `EmotionPipeline` model arayüzlerine (ISpeechToText vb.) DEĞİL, yalnızca `IChunkProcessor`/`ISessionStore`/`IMeetingAggregator`/`AppConfig`'e bağlı; `IChunkProcessor` ise model arayüzlerine bağlı. Bu yüzden testte fake model kayıtları + `AddFusion` + `AddPipeline` gerekir:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Pipeline;

public class PipelineDiTests
{
    private sealed class DummyStt : ISpeechToText { public Task<Transcript> TranscribeAsync(AudioChunk c, CancellationToken ct) => Task.FromResult(new Transcript("", DetectedLanguage.Other, 0)); }
    private sealed class DummyProsody : IProsodyEmotionModel { public Task<ProsodyEmotion> AnalyzeAsync(AudioChunk c, CancellationToken ct) => Task.FromResult(new ProsodyEmotion(ValenceArousal.Neutral, 0)); }
    private sealed class DummyText : ITextEmotionModel { public Task<TextEmotion> AnalyzeAsync(Transcript t, CancellationToken ct) => Task.FromResult(new TextEmotion(ValenceArousal.Neutral, 0)); }
    private sealed class DummyVad : IVadGate { public bool HasSpeech(AudioChunk c) => true; }

    [Fact]
    public void AddPipeline_ResolvesEmotionPipelineAndStore()
    {
        var provider = new ServiceCollection()
            .AddApplicationCore()
            .AddFusion()
            .AddSingleton<ISpeechToText, DummyStt>()
            .AddSingleton<IProsodyEmotionModel, DummyProsody>()
            .AddSingleton<ITextEmotionModel, DummyText>()
            .AddSingleton<IVadGate, DummyVad>()
            .AddPipeline()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<EmotionPipeline>());
        Assert.NotNull(provider.GetRequiredService<ISessionStore>());
        Assert.NotNull(provider.GetRequiredService<IChunkProcessor>());
    }
}
```

- [ ] **Step 8: Tam çözüm doğrulaması** — Run: `dotnet build && dotnet test`. Expected: Build 0 uyarı; **tüm** testler PASS (01–05 + 06 pipeline); env-gated skip 6'da kalır.

---

## Self-Review (plan yazarı tarafından)

**1. Spec coverage (06, bu tur):**
- Bounded producer/consumer (Channels, DropOldest) → Task 4 `EmotionPipeline`. ✔
- Per-chunk DAG (VAD → prozodi ∥ STT → metin → quality → füzyon) → Task 3 `ChunkProcessor`. ✔
- STT ∥ prozodi paralel; metin STT'ye bağlı → Task 3 `Task.WhenAll` + sonra metin. ✔
- QualitySignals üretimi (04a'dan devir) → Task 1 `QualitySignalsBuilder`. ✔
- Backpressure + overflow drop-oldest → Task 4 bounded kanal + `DroppedCount`. ✔
- Sıralı + zaman-damgalı sonuçlar → tek consumer; test `RunAsync_ProcessesAllChunksInOrder`. ✔
- Timeline + kalıcılık → `Timeline` + `ISessionStore` (Task 2 in-memory). ✔
- Toplantı sonu agregasyon → Task 4 `IMeetingAggregator.Aggregate` → `MeetingSummary`. ✔
- Hata izolasyonu (çökme yok) → Task 4 per-chunk try/catch; test `ProcessorThrows`. ✔
- Bellek sabit (bounded) → kanal kapasitesi; test `Overflow_DropsOldest_BoundedMemory`. ✔

**2. Kapsam dışı (bilinçli, spec ile uyumlu):** cümle-hizalama, turbo→chunk-birleştirme degradasyonu, yapay-yavaşlatma senaryoları, toplantı-sonu derin geçiş, ArrayPool → 06b; gerçek `ISessionStore` (SQLite/retention) → 07.

**3. Placeholder scan:** TODO/TBD yok. SNR/konuşma-oranı "basit sezgisel, kalibrasyon 09" olarak işaretli (sahte-kesinlik değil). InMemoryStore metadata sınırı (StartedAt/Language) açıkça dokümante. Tüm kod blokları tam.

**4. Type consistency:**
- `QualitySignalsBuilder.Build(AudioChunk, Transcript) → QualitySignals` → Task 3 tüketir. ✔
- `IChunkProcessor.ProcessAsync(AudioChunk, ct) → Task<ChunkResult?>` → Task 4 `EmotionPipeline` tüketir. ✔
- `EmotionPipeline(IChunkProcessor, ISessionStore, IMeetingAggregator, AppConfig)` + `RunAsync(...)` imzası → test + DI tutarlı. ✔
- `PipelineConfig(ChannelCapacity=32)` → `AppConfig.Pipeline` (additive). ✔
- `InMemorySessionStore : ISessionStore` tüm 4 metot. ✔
- `ChunkResult`/`MeetingSession`/`MeetingSummary`/`FusedResult` alanları mevcut Core imzalarıyla birebir (testlerdeki kurucular gerçek alanları kullanır). **DİKKAT (implementer):** `Emotion` enum üye adı (testlerde `Emotion.Notr`) + `ValenceArousal.Neutral`/`Confidence.Orta`/`IncongruencePattern.Yok` adlarını `grep -rn "enum Emotion\|enum Confidence\|struct ValenceArousal" src/SentimentAnalysis.Core` ile teyit et; farklıysa test kurucularını gerçek adlara uyarla (mantık değişmez).
- `AppConfig` genişletmesi: kurucuya zorunlu `PipelineConfig` eklenir — Task 4 Step 1 notu tek kullanımın `AppConfig.Default`/`with` olduğunu belirtir (04a/04b/05'te kırılmadı; implementer `grep` ile teyit).

**5. Mimari:** Yalnızca Application. Bağımlılıklar arayüz (fake'lerle tam test, modelsiz yeşil). `EmotionPipeline` model arayüzlerini doğrudan bilmez (yalnızca `IChunkProcessor` üzerinden) → temiz sınır.

**6. Eşzamanlılık/kaynak:** producer `Task.Run` içinde, `finally` ile `Writer.Complete()`; tek reader/writer (SingleReader/SingleWriter true); DropOldest'te WriteAsync bloklanmaz (drop callback ile sayılır). `DroppedCount` `Volatile`/`Interlocked`. Per-chunk hata `OperationCanceledException` hariç yutulur (iptal yukarı akar). `RunAsync` koşu-başı durumu sıfırlar (tek koşu varsayımı dokümante).

**Riskler / dikkat:**
- **Drop testi determinizmi:** `SpinWait.SpinUntil(..., 5sn)` ile drop beklenir; kapasite 2 + 20 chunk + bloklu consumer'da drop hızlı gerçekleşir (producer WriteAsync DropOldest'te bloklanmaz). 5 sn tolerans bol. Yine de zamanlama-hassas; CI'da nadir gecikirse tolerans artırılabilir (flaky ise adjudicate).
- **`Channel.CreateBounded` itemDropped overload'ı** .NET 7+; net9'da mevcut (BCL). İlk build doğrular.
- **Metadata sınırı:** InMemoryStore StartedAt/Language tam değil (SaveChunkAsync taşımaz) — pipeline kendi `MeetingSession`'ını doğru metadata ile kurar (agregasyon bunu kullanır); store yalnızca chunk kalıcılığı. Gerçek store 07.
- **Git kuralı:** commit yok; her task sonu `dotnet build && dotnet test`.

---

## Post-Review Düzeltmeleri (uygulama sırasında)

- **Task 4 (Important — iptal yaşam döngüsü):** `EmotionPipeline.RunAsync`'te producer Task yalnızca happy-path'te await ediliyordu; consumer iptalde (OperationCanceledException) loop'tan çıkınca producer await edilmeden dönülüyordu (kayıp exception / serbest bırakılmamış kaynak riski). Düzeltme: consumer loop try/finally'ye alındı, `finally` producer'ı her durumda await eder (producer'ın kendi exception'ı yutulur; consumer'ın OCE'si yukarı akmaya devam eder). Coupled iptal testi (`RunAsync_Cancelled_ThrowsOperationCanceled_DoesNotHang`, deterministik, bounded) eklendi. Re-review: ADDRESSED.
- **Channels API:** `Channel.CreateBounded` `itemDropped` callback overload'ı (.NET 7+) net9'da sorunsuz derlendi (drop sayımı için).

**Nihai durum:** 148 test geçti + 6 env-gated Skip (değişmedi — 06 modelsiz), 0 başarısız, 0 uyarı. Drop testi + iptal testi iki koşuda kararlı (flaky değil).

**Final whole-branch review: ATLANDI** (kullanıcı hız tercihi — Task 4 tam review'dan geçti, diğerleri saf/fake-testli yeşil).

**Biriken deferred Minor'lar (Task 4):**
- M-a: `_droppedCount` reset plain write (Interlocked/Volatile disiplini ile tutarsız; tek-koşu varsayımında cosmetic).
- M-b: `Task.Run(...,ct)` önceden-iptalde `Writer.Complete()` atlanır (deadlock yok — consumer aynı ct'yi gözler; yorum önerildi).

**Kapsam dışı (bilinçli, 06b/07):** cümle-hizalama, turbo→chunk-birleştirme degradasyonu, yapay-yavaşlatma, toplantı-sonu derin geçiş, ArrayPool → 06b; gerçek `ISessionStore` (SQLite/retention) → 07.
