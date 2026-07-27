# Alt-Proje 01: Temel + Domain — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sistemin saf-C# domain modelini, tüm arayüz sözleşmelerini ve DI/config iskeletini kurmak — hiç ML olmadan, tamamen test edilebilir.

**Architecture:** İki proje: `Core` (bağımlılıksız domain tipleri, saf fonksiyonlar) ve `Application` (Core'a bağlı arayüz sözleşmeleri + konfigürasyon + DI iskeleti). Bağımlılıklar hep içe doğru; `Core` hiçbir NuGet paketine bağlı değil. Test: xUnit ile `Core` domain mantığı %100 birim testli.

**Tech Stack:** .NET · C# · xUnit · (Core: sıfır NuGet)

## Global Constraints

- **Hedef framework:** `net9.0` (tüm projeler). Gerekirse ileride net10.0'a taşınabilir; şimdilik net9.0 sabit.
- **`Core` projesi sıfır NuGet bağımlılığı** taşır (yalnızca BCL).
- **Nullable reference types açık** (`<Nullable>enable</Nullable>`) tüm projelerde.
- **Duygu etiketleri dil-bağımsız enum**; UI metni bu katmanda yok.
- Domain tipleri **immutable** (record / readonly record struct).
- Valence aralığı **−1.0 … +1.0**, Arousal aralığı **0.0 … 1.0**, Nötr merkez **(V=0.0, A=0.5)**.
- Enum değerleri master tasarımdaki adlarla birebir: `Cosakulu, Mutlu, Sakin, Sikilmis, Uzgun, Gergin, Kizgin, Notr`.

---

### Task 1: Çözüm iskeleti + `ValenceArousal` değer tipi

**Files:**
- Create: `SentimentAnalysis.sln`
- Create: `src/SentimentAnalysis.Core/SentimentAnalysis.Core.csproj`
- Create: `src/SentimentAnalysis.Core/ValenceArousal.cs`
- Create: `tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj`
- Create: `tests/SentimentAnalysis.Core.Tests/ValenceArousalTests.cs`

**Interfaces:**
- Consumes: —
- Produces: `readonly record struct ValenceArousal(double Valence, double Arousal)` with `static ValenceArousal Clamp(double valence, double arousal)`, `static ValenceArousal Neutral { get; }`, `double DistanceTo(ValenceArousal other)`.

- [ ] **Step 1: Çözüm ve projeleri oluştur**

```bash
cd /Users/ilkaygokbudak/Development/Projects/sentiment-analysis
dotnet new sln -n SentimentAnalysis
dotnet new classlib -n SentimentAnalysis.Core -o src/SentimentAnalysis.Core -f net9.0
dotnet new xunit -n SentimentAnalysis.Core.Tests -o tests/SentimentAnalysis.Core.Tests -f net9.0
rm src/SentimentAnalysis.Core/Class1.cs tests/SentimentAnalysis.Core.Tests/UnitTest1.cs
dotnet sln add src/SentimentAnalysis.Core tests/SentimentAnalysis.Core.Tests
dotnet add tests/SentimentAnalysis.Core.Tests reference src/SentimentAnalysis.Core
```

Ardından `src/SentimentAnalysis.Core/SentimentAnalysis.Core.csproj` içeriğini şununla değiştir:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Başarısız testi yaz**

`tests/SentimentAnalysis.Core.Tests/ValenceArousalTests.cs`:

```csharp
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Core.Tests;

public class ValenceArousalTests
{
    [Fact]
    public void Clamp_KeepsValuesInRange()
    {
        var va = ValenceArousal.Clamp(valence: 2.5, arousal: -0.4);
        Assert.Equal(1.0, va.Valence);
        Assert.Equal(0.0, va.Arousal);
    }

    [Fact]
    public void Clamp_LeavesInRangeValuesUntouched()
    {
        var va = ValenceArousal.Clamp(valence: 0.3, arousal: 0.7);
        Assert.Equal(0.3, va.Valence, precision: 10);
        Assert.Equal(0.7, va.Arousal, precision: 10);
    }

    [Fact]
    public void Neutral_IsCenter()
    {
        Assert.Equal(0.0, ValenceArousal.Neutral.Valence);
        Assert.Equal(0.5, ValenceArousal.Neutral.Arousal);
    }

    [Fact]
    public void DistanceTo_IsEuclidean()
    {
        var a = new ValenceArousal(0.0, 0.0);
        var b = new ValenceArousal(3.0, 4.0);
        Assert.Equal(5.0, a.DistanceTo(b), precision: 10);
    }
}
```

- [ ] **Step 3: Testin başarısız olduğunu doğrula**

Run: `dotnet test tests/SentimentAnalysis.Core.Tests`
Expected: FAIL — `ValenceArousal` tipi bulunamıyor (derleme hatası).

- [ ] **Step 4: Minimal implementasyonu yaz**

`src/SentimentAnalysis.Core/ValenceArousal.cs`:

```csharp
namespace SentimentAnalysis.Core;

/// <summary>
/// Duygunun boyutsal çekirdek temsili. Valence olumsuz↔olumlu (−1..+1),
/// Arousal sakin↔uyarılmış (0..1). Sistemin iç para birimidir.
/// </summary>
public readonly record struct ValenceArousal(double Valence, double Arousal)
{
    /// <summary>Nötr merkez: valence 0, arousal 0.5.</summary>
    public static ValenceArousal Neutral => new(0.0, 0.5);

    /// <summary>Değerleri geçerli aralığa kırparak güvenli bir örnek üretir.</summary>
    public static ValenceArousal Clamp(double valence, double arousal) =>
        new(Math.Clamp(valence, -1.0, 1.0), Math.Clamp(arousal, 0.0, 1.0));

    /// <summary>İki VA noktası arasındaki Öklid uzaklığı (duygu haritalamasında kullanılır).</summary>
    public double DistanceTo(ValenceArousal other)
    {
        var dv = Valence - other.Valence;
        var da = Arousal - other.Arousal;
        return Math.Sqrt(dv * dv + da * da);
    }
}
```

- [ ] **Step 5: Testin geçtiğini doğrula**

Run: `dotnet test tests/SentimentAnalysis.Core.Tests`
Expected: PASS (4 test).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(core): çözüm iskeleti + ValenceArousal değer tipi"
```

---

### Task 2: `Emotion` enum + `EmotionWheel` haritalama

**Files:**
- Create: `src/SentimentAnalysis.Core/Emotion.cs`
- Create: `src/SentimentAnalysis.Core/EmotionWheel.cs`
- Create: `tests/SentimentAnalysis.Core.Tests/EmotionWheelTests.cs`

**Interfaces:**
- Consumes: `ValenceArousal`
- Produces: `enum Emotion { Cosakulu, Mutlu, Sakin, Sikilmis, Uzgun, Gergin, Kizgin, Notr }`; `static class EmotionWheel` with `Emotion Map(ValenceArousal va)` and `(Emotion Primary, Emotion Secondary) MapWithSecondary(ValenceArousal va)`.

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Core.Tests/EmotionWheelTests.cs`:

```csharp
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Core.Tests;

public class EmotionWheelTests
{
    [Theory]
    [InlineData(0.6, 0.9, Emotion.Cosakulu)]   // yüksek valence + yüksek arousal
    [InlineData(0.8, 0.5, Emotion.Mutlu)]      // yüksek valence + orta arousal
    [InlineData(0.6, 0.15, Emotion.Sakin)]     // olumlu + düşük enerji
    [InlineData(-0.4, 0.1, Emotion.Sikilmis)]  // olumsuz + çok düşük enerji
    [InlineData(-0.6, 0.3, Emotion.Uzgun)]     // olumsuz + düşük-orta enerji
    [InlineData(-0.5, 0.8, Emotion.Gergin)]    // olumsuz + yüksek enerji
    [InlineData(-0.8, 0.9, Emotion.Kizgin)]    // çok olumsuz + yüksek enerji
    [InlineData(0.0, 0.5, Emotion.Notr)]       // merkez
    public void Map_ReturnsNearestRegion(double v, double a, Emotion expected)
    {
        Assert.Equal(expected, EmotionWheel.Map(new ValenceArousal(v, a)));
    }

    [Fact]
    public void MapWithSecondary_PrimaryAndSecondaryDiffer()
    {
        var (primary, secondary) = EmotionWheel.MapWithSecondary(new ValenceArousal(0.7, 0.7));
        Assert.NotEqual(primary, secondary);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test tests/SentimentAnalysis.Core.Tests`
Expected: FAIL — `Emotion` / `EmotionWheel` bulunamıyor.

- [ ] **Step 3: `Emotion` enum'ını yaz**

`src/SentimentAnalysis.Core/Emotion.cs`:

```csharp
namespace SentimentAnalysis.Core;

/// <summary>
/// Valence-Arousal uzayından haritalanan dil-bağımsız 8 duygu (circumplex).
/// UI metni bu katmanda YOK; enum → yerelleştirme UI katmanında yapılır.
/// </summary>
public enum Emotion
{
    Cosakulu,  // V+ A+
    Mutlu,     // V+ orta-A
    Sakin,     // V+ A-
    Sikilmis,  // V- A-
    Uzgun,     // V- düşük-A
    Gergin,    // V- yüksek-A
    Kizgin,    // V-- A+
    Notr       // merkez
}
```

- [ ] **Step 4: `EmotionWheel` haritalamayı yaz**

`src/SentimentAnalysis.Core/EmotionWheel.cs`:

```csharp
namespace SentimentAnalysis.Core;

/// <summary>
/// Sürekli Valence-Arousal noktasını en yakın ayrık duyguya haritalar.
/// Her duygunun VA uzayında bir merkez noktası vardır; haritalama en yakın
/// merkeze (Öklid) göre yapılır. Saf, deterministik.
/// </summary>
public static class EmotionWheel
{
    private static readonly (Emotion Emotion, ValenceArousal Center)[] Regions =
    [
        (Emotion.Cosakulu, new ValenceArousal(0.6, 0.9)),
        (Emotion.Mutlu,    new ValenceArousal(0.8, 0.5)),
        (Emotion.Sakin,    new ValenceArousal(0.6, 0.15)),
        (Emotion.Sikilmis, new ValenceArousal(-0.4, 0.1)),
        (Emotion.Uzgun,    new ValenceArousal(-0.6, 0.3)),
        (Emotion.Gergin,   new ValenceArousal(-0.5, 0.8)),
        (Emotion.Kizgin,   new ValenceArousal(-0.8, 0.9)),
        (Emotion.Notr,     new ValenceArousal(0.0, 0.5)),
    ];

    /// <summary>Verilen VA noktasına en yakın duyguyu döndürür.</summary>
    public static Emotion Map(ValenceArousal va) => MapWithSecondary(va).Primary;

    /// <summary>En yakın (primary) ve ikinci en yakın (secondary) duyguyu döndürür.</summary>
    public static (Emotion Primary, Emotion Secondary) MapWithSecondary(ValenceArousal va)
    {
        var ranked = Regions
            .OrderBy(r => va.DistanceTo(r.Center))
            .Select(r => r.Emotion)
            .ToArray();
        return (ranked[0], ranked[1]);
    }
}
```

- [ ] **Step 5: Testin geçtiğini doğrula**

Run: `dotnet test tests/SentimentAnalysis.Core.Tests`
Expected: PASS (tüm testler).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(core): 8-duygu circumplex enum + EmotionWheel haritalama"
```

---

### Task 3: Domain DTO'ları (kayıt tipleri)

**Files:**
- Create: `src/SentimentAnalysis.Core/Signals.cs`
- Create: `src/SentimentAnalysis.Core/Results.cs`
- Create: `tests/SentimentAnalysis.Core.Tests/DomainRecordsTests.cs`

**Interfaces:**
- Consumes: `ValenceArousal`, `Emotion`
- Produces: `enum Confidence`, `enum IncongruencePattern`, `enum DetectedLanguage`; records `AudioChunk`, `Transcript`, `TextEmotion`, `ProsodyEmotion`, `QualitySignals`, `FusedResult`, `ChunkResult`, `MeetingSession`.

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Core.Tests/DomainRecordsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Core.Tests;

public class DomainRecordsTests
{
    [Fact]
    public void FusedResult_CarriesBothSignalsAndCongruence()
    {
        var result = new FusedResult(
            Combined: new ValenceArousal(-0.5, 0.7),
            PrimaryEmotion: Emotion.Gergin,
            SecondaryEmotion: Emotion.Kizgin,
            Confidence: Confidence.Orta,
            Agreement: false,
            Incongruent: true,
            Pattern: IncongruencePattern.Maskeleme,
            Congruence: 0.2,
            TextValence: 0.6,
            AudioValence: -0.6);

        Assert.True(result.Incongruent);
        Assert.Equal(IncongruencePattern.Maskeleme, result.Pattern);
        Assert.Equal(Emotion.Gergin, result.PrimaryEmotion);
    }

    [Fact]
    public void MeetingSession_AggregatesChunks()
    {
        var chunk = new ChunkResult(
            TimeStart: TimeSpan.Zero,
            TimeEnd: TimeSpan.FromSeconds(15),
            Fused: new FusedResult(
                new ValenceArousal(0.8, 0.5), Emotion.Mutlu, Emotion.Sakin,
                Confidence.Yuksek, true, false, IncongruencePattern.Yok, 0.9, 0.8, 0.7),
            Transcript: "merhaba");

        var session = new MeetingSession(
            Id: Guid.NewGuid(),
            StartedAt: DateTimeOffset.UtcNow,
            Language: DetectedLanguage.Turkish,
            Chunks: new List<ChunkResult> { chunk });

        Assert.Single(session.Chunks);
        Assert.Equal(DetectedLanguage.Turkish, session.Language);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test tests/SentimentAnalysis.Core.Tests`
Expected: FAIL — tipler bulunamıyor.

- [ ] **Step 3: Enum ve sinyal tiplerini yaz**

`src/SentimentAnalysis.Core/Signals.cs`:

```csharp
namespace SentimentAnalysis.Core;

/// <summary>Nihai sonucun güven düzeyi.</summary>
public enum Confidence { Yuksek, Orta, Dusuk }

/// <summary>Söz-ton uyumsuzluğunun örüntüsü.</summary>
public enum IncongruencePattern { Yok, Maskeleme, Saka, OlasiIroni }

/// <summary>Algılanan konuşma dili (çok-dilli-hazır).</summary>
public enum DetectedLanguage { Turkish, English, Other }

/// <summary>Ham ses parçası: 16 kHz mono float PCM.</summary>
public sealed record AudioChunk(float[] Samples, int SampleRate, TimeSpan Start, TimeSpan End);

/// <summary>STT çıktısı.</summary>
public sealed record Transcript(string Text, DetectedLanguage Language, double AvgLogProb);

/// <summary>Metin modelinin ham çıktısı.</summary>
public sealed record TextEmotion(ValenceArousal Va, double ModelConfidence);

/// <summary>Prozodi modelinin ham çıktısı.</summary>
public sealed record ProsodyEmotion(ValenceArousal Va, double ModelConfidence);

/// <summary>Füzyon ağırlığını besleyen girdi kalite sinyalleri.</summary>
public sealed record QualitySignals(
    double AudioSnr,
    double SpeechRatio,
    TimeSpan Duration,
    int TranscriptWordCount,
    double TranscriptAvgLogProb);
```

- [ ] **Step 4: Sonuç tiplerini yaz**

`src/SentimentAnalysis.Core/Results.cs`:

```csharp
using System.Collections.Generic;

namespace SentimentAnalysis.Core;

/// <summary>Füzyon sonucu (chunk düzeyi). İki ham valence de saydamlık için taşınır.</summary>
public sealed record FusedResult(
    ValenceArousal Combined,
    Emotion PrimaryEmotion,
    Emotion SecondaryEmotion,
    Confidence Confidence,
    bool Agreement,
    bool Incongruent,
    IncongruencePattern Pattern,
    double Congruence,
    double TextValence,
    double AudioValence);

/// <summary>Bir chunk'ın tam sonucu (zaman çizelgesi öğesi).</summary>
public sealed record ChunkResult(
    TimeSpan TimeStart,
    TimeSpan TimeEnd,
    FusedResult Fused,
    string Transcript);

/// <summary>Bir toplantı/oturum: chunk listesi + üst-veriler. Toplantı-düzeyi
/// agregasyon (baskın duygu, trend, oynaklık, tepe anlar) Füzyon motorunda
/// (Alt-proje 02) hesaplanır; burada ham veri tutulur.</summary>
public sealed record MeetingSession(
    Guid Id,
    DateTimeOffset StartedAt,
    DetectedLanguage Language,
    IReadOnlyList<ChunkResult> Chunks);
```

- [ ] **Step 5: Testin geçtiğini doğrula**

Run: `dotnet test tests/SentimentAnalysis.Core.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(core): domain DTO'ları ve sinyal/sonuç tipleri"
```

---

### Task 4: `Application` arayüzleri + `AppConfig` + DI iskeleti

**Files:**
- Create: `src/SentimentAnalysis.Application/SentimentAnalysis.Application.csproj`
- Create: `src/SentimentAnalysis.Application/Abstractions/Interfaces.cs`
- Create: `src/SentimentAnalysis.Application/Configuration/AppConfig.cs`
- Create: `src/SentimentAnalysis.Application/DependencyInjection.cs`
- Create: `tests/SentimentAnalysis.Application.Tests/SentimentAnalysis.Application.Tests.csproj`
- Create: `tests/SentimentAnalysis.Application.Tests/AppConfigTests.cs`

**Interfaces:**
- Consumes: tüm `Core` tipleri
- Produces: `ISpeechToText`, `ITextEmotionModel`, `IProsodyEmotionModel`, `IEmotionFusion`, `IAudioCaptureSource`, `IVadGate`, `ISessionStore`, `AppConfig`, `FusionConfig`, `StorageConfig`.

- [ ] **Step 1: Application projesini oluştur**

```bash
cd /Users/ilkaygokbudak/Development/Projects/sentiment-analysis
dotnet new classlib -n SentimentAnalysis.Application -o src/SentimentAnalysis.Application -f net9.0
rm src/SentimentAnalysis.Application/Class1.cs
dotnet sln add src/SentimentAnalysis.Application
dotnet add src/SentimentAnalysis.Application reference src/SentimentAnalysis.Core
dotnet add src/SentimentAnalysis.Application package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet new xunit -n SentimentAnalysis.Application.Tests -o tests/SentimentAnalysis.Application.Tests -f net9.0
rm tests/SentimentAnalysis.Application.Tests/UnitTest1.cs
dotnet sln add tests/SentimentAnalysis.Application.Tests
dotnet add tests/SentimentAnalysis.Application.Tests reference src/SentimentAnalysis.Application
```

`src/SentimentAnalysis.Application/SentimentAnalysis.Application.csproj` PropertyGroup'una ekle:

```xml
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
```

- [ ] **Step 2: Başarısız testi yaz**

`tests/SentimentAnalysis.Application.Tests/AppConfigTests.cs`:

```csharp
using SentimentAnalysis.Application.Configuration;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class AppConfigTests
{
    [Fact]
    public void Default_HasSaneFusionAndStorageDefaults()
    {
        var config = AppConfig.Default;

        // Arousal ağırlıkla sesten, valence ağırlıkla metinden (master §5).
        Assert.True(config.Fusion.AudioArousalWeight > config.Fusion.TextArousalWeight);
        Assert.True(config.Fusion.TextValenceWeight > config.Fusion.AudioValenceWeight);

        // Ses saklama varsayılanı 7 gün (master §D13).
        Assert.Equal(7, config.Storage.AudioRetentionDays);
    }
}
```

- [ ] **Step 3: Testin başarısız olduğunu doğrula**

Run: `dotnet test tests/SentimentAnalysis.Application.Tests`
Expected: FAIL — `AppConfig` bulunamıyor.

- [ ] **Step 4: `AppConfig`'i yaz**

`src/SentimentAnalysis.Application/Configuration/AppConfig.cs`:

```csharp
namespace SentimentAnalysis.Application.Configuration;

/// <summary>Füzyon başlangıç ağırlıkları ve eşikleri. Nihai değerler
/// Alt-proje 09 (eval) ile kalibre edilip buraya yansıtılır — keyfi sabit değil.</summary>
public sealed record FusionConfig(
    double AudioArousalWeight = 0.75,
    double TextArousalWeight  = 0.25,
    double TextValenceWeight  = 0.70,
    double AudioValenceWeight = 0.30,
    double IncongruenceThreshold = 0.8,   // |valence_text − valence_audio| eşiği
    double LowConfidenceThreshold = 0.35);

/// <summary>Depolama/gizlilik ayarları (master §D13).</summary>
public sealed record StorageConfig(
    int AudioRetentionDays = 7,
    bool KeepAudio = true,
    string OpusBitrate = "24k");

/// <summary>STT profil ayarları (master §D6).</summary>
public sealed record SttConfig(
    string LiveModel = "large-v3-turbo",
    string? DeepModel = "large-v3");

/// <summary>Uygulama genel konfigürasyonu (stajyerin config.py karşılığı).</summary>
public sealed record AppConfig(
    FusionConfig Fusion,
    StorageConfig Storage,
    SttConfig Stt)
{
    public static AppConfig Default => new(
        Fusion: new FusionConfig(),
        Storage: new StorageConfig(),
        Stt: new SttConfig());
}
```

- [ ] **Step 5: Testin geçtiğini doğrula**

Run: `dotnet test tests/SentimentAnalysis.Application.Tests`
Expected: PASS.

- [ ] **Step 6: Arayüz sözleşmelerini yaz**

`src/SentimentAnalysis.Application/Abstractions/Interfaces.cs`:

```csharp
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Abstractions;

/// <summary>Konuşma → metin (Whisper.net; Alt-proje 03).</summary>
public interface ISpeechToText
{
    Task<Transcript> TranscribeAsync(AudioChunk chunk, CancellationToken ct);
}

/// <summary>Metnin duygusu → valence-arousal (ONNX; Alt-proje 04).
/// Dil-başına takılabilir (çok-dilli-hazır).</summary>
public interface ITextEmotionModel
{
    Task<TextEmotion> AnalyzeAsync(Transcript transcript, CancellationToken ct);
}

/// <summary>Sesin prozodisi → valence-arousal (VAD-regresyon ONNX; Alt-proje 04).</summary>
public interface IProsodyEmotionModel
{
    Task<ProsodyEmotion> AnalyzeAsync(AudioChunk chunk, CancellationToken ct);
}

/// <summary>İki modeli birleştiren saf, deterministik füzyon (Alt-proje 02).</summary>
public interface IEmotionFusion
{
    FusedResult Fuse(TextEmotion text, ProsodyEmotion prosody, QualitySignals quality);
}

/// <summary>Canlı sistem-sesi / dosya yakalama (Alt-proje 05).</summary>
public interface IAudioCaptureSource
{
    IAsyncEnumerable<AudioChunk> CaptureAsync(CaptureOptions options, CancellationToken ct);
}

/// <summary>Yakalama seçenekleri.</summary>
public sealed record CaptureOptions(TimeSpan ChunkLength, TimeSpan Overlap);

/// <summary>Sessiz chunk'ları eleyen VAD kapısı (Silero; Alt-proje 05).</summary>
public interface IVadGate
{
    bool HasSpeech(AudioChunk chunk);
}

/// <summary>Oturum kalıcılığı ve saklama politikası (Alt-proje 07).</summary>
public interface ISessionStore
{
    Task SaveChunkAsync(Guid sessionId, ChunkResult result, CancellationToken ct);
    Task<MeetingSession?> GetSessionAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken ct);
    Task PurgeExpiredAudioAsync(CancellationToken ct);
}
```

- [ ] **Step 7: DI iskeletini yaz**

`src/SentimentAnalysis.Application/DependencyInjection.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Application;

/// <summary>Application katmanı DI kayıt noktası. Infrastructure implementasyonları
/// (Alt-projeler 02–07) kendi uzantı metotlarıyla eklenir.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationCore(
        this IServiceCollection services, AppConfig? config = null)
    {
        services.AddSingleton(config ?? AppConfig.Default);
        // Somut servisler (ISpeechToText, IEmotionFusion, ...) Infrastructure
        // katmanında ayrı AddXxx() uzantılarıyla kaydedilecek.
        return services;
    }
}
```

- [ ] **Step 8: Tüm çözümün derlendiğini ve testlerin geçtiğini doğrula**

Run: `dotnet build && dotnet test`
Expected: Build succeeded; tüm testler PASS.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(application): arayüz sözleşmeleri, AppConfig ve DI iskeleti"
```

---

## Self-Review (plan yazarı tarafından)

- **Spec coverage:** Alt-proje 01 spec'inin her maddesi bir task'a bağlı — ValenceArousal/Emotion/EmotionWheel (Task 1–2), DTO'lar ve `DetectedLanguage` (Task 3), tüm arayüzler + `AppConfig` + DI (Task 4). Boşluk yok.
- **Placeholder scan:** TODO/TBD yok; her kod adımı tam içerik taşıyor.
- **Type consistency:** `ValenceArousal`, `Emotion`, `FusedResult`, `ChunkResult`, `MeetingSession`, `Transcript`, `AudioChunk` adları ve alanları task'lar arası tutarlı; testlerdeki kullanımla implementasyon eşleşiyor. `AppConfig.Fusion` ağırlık isimleri test ve implementasyonda birebir.
- Not: Toplantı-düzeyi agregasyon (baskın duygu/trend/oynaklık) bilinçli olarak **Alt-proje 02 (Füzyon)** planına bırakıldı; burada yalnızca veri tipi (`MeetingSession.Chunks`) var — master §5.2 ile tutarlı.
