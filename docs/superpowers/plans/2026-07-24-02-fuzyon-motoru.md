# Alt-Proje 02: Füzyon Motoru — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** İki modelin (metin + prozodi) çıktısını eksen-başına güvenilirlik-ağırlıklı birleştiren, söz-ton uyumsuzluğunu birinci sınıf sinyal olarak işleyen, ayrık duyguya haritalayan ve toplantı-düzeyi agregasyon üreten deterministik füzyon motorunu kurmak.

**Architecture:** Saf C#, sıfır ML/I-O. Füzyon motoru `Application` katmanında yaşar (spec 02: "Katman: Application (saf C#)"). Küçük, tek-sorumluluklu saf yardımcı sınıflar (`QualityWeighting`, `IncongruenceAnalysis`), bunları birleştiren `EmotionFusion : IEmotionFusion`, ve ayrı `MeetingAggregator : IMeetingAggregator`. Toplantı özeti tipi (`MeetingSummary`) domain olduğu için `Core`'da; doğal-dil Türkçe cümle Application'da üretilir (Core dil-nötr kalır).

**Tech Stack:** .NET 9 · C# · xUnit · Microsoft.Extensions.DependencyInjection.Abstractions (zaten Application'da) · sıfır yeni NuGet

## Global Constraints

- **Hedef framework:** `net9.0` (tüm projeler). Sabit.
- **`Core` sıfır NuGet bağımlılığı** taşır (yalnızca BCL). `MeetingSummary` yalnızca BCL tipleri kullanır.
- **Nullable reference types açık** tüm projelerde.
- **Füzyon = saf, deterministik:** aynı girdi → aynı çıktı; hiçbir I/O, rastgelelik veya zaman bağımlılığı yok (spec 02 Kabul Kriteri).
- Valence aralığı **−1.0 … +1.0**, Arousal aralığı **0.0 … 1.0**, Nötr merkez **(V=0.0, A=0.5)**.
- **`git YOK`** (proje sahibi kararı): her task'ın son adımı `git commit` **değil**, `dotnet build && dotnet test` ile doğrulamadır.
- **Duygu enum'ı 8 değerde sabit** (D8): "belirsiz" için 9. değer eklenmez; `Confidence.Dusuk` ile temsil edilir.
- Füzyon eşik/ağırlıkları **`AppConfig.Fusion`'dan** okunur; nihai kalibrasyon **Alt-proje 09 (eval)** işidir — bu plandaki sabitler makul başlangıç değerleridir, keyfi son karar değil.

### Mevcut tipler (Alt-proje 01'den — değiştirilmez, tüketilir)

```csharp
// SentimentAnalysis.Core
public readonly record struct ValenceArousal(double Valence, double Arousal) {
    static ValenceArousal Neutral { get; }              // (0.0, 0.5)
    static ValenceArousal Clamp(double valence, double arousal);
    double DistanceTo(ValenceArousal other);
}
public enum Emotion { Cosakulu, Mutlu, Sakin, Sikilmis, Uzgun, Gergin, Kizgin, Notr }
public static class EmotionWheel {
    static Emotion Map(ValenceArousal va);
    static (Emotion Primary, Emotion Secondary) MapWithSecondary(ValenceArousal va);
}
public enum Confidence { Yuksek, Orta, Dusuk }
public enum IncongruencePattern { Yok, Maskeleme, Saka, OlasiIroni }
public enum DetectedLanguage { Turkish, English, Other }
public sealed record TextEmotion(ValenceArousal Va, double ModelConfidence);
public sealed record ProsodyEmotion(ValenceArousal Va, double ModelConfidence);
public sealed record QualitySignals(double AudioSnr, double SpeechRatio, TimeSpan Duration, int TranscriptWordCount, double TranscriptAvgLogProb);
public sealed record FusedResult(ValenceArousal Combined, Emotion PrimaryEmotion, Emotion SecondaryEmotion,
    Confidence Confidence, bool Agreement, bool Incongruent, IncongruencePattern Pattern,
    double Congruence, double TextValence, double AudioValence);
public sealed record ChunkResult(TimeSpan TimeStart, TimeSpan TimeEnd, FusedResult Fused, string Transcript);
public sealed record MeetingSession(Guid Id, DateTimeOffset StartedAt, DetectedLanguage Language, IReadOnlyList<ChunkResult> Chunks);

// SentimentAnalysis.Application.Abstractions
public interface IEmotionFusion { FusedResult Fuse(TextEmotion text, ProsodyEmotion prosody, QualitySignals quality); }
```

---

### Task 1: `FusionConfig` eşiklerini genişlet + `QualityWeighting` (güvenilirlik faktörleri)

Füzyon ağırlıklarını dinamik modüle eden saf güvenilirlik fonksiyonları ve bunların (ve sonraki task'ların) ihtiyaç duyduğu config eşikleri.

**Files:**
- Modify: `src/SentimentAnalysis.Application/Configuration/AppConfig.cs` (yalnızca `FusionConfig` kaydına alan ekle)
- Create: `src/SentimentAnalysis.Application/Fusion/QualityWeighting.cs`
- Create: `tests/SentimentAnalysis.Application.Tests/QualityWeightingTests.cs`

**Interfaces:**
- Consumes: `QualitySignals` (Core), `FusionConfig` (Application.Configuration)
- Produces: `static class QualityWeighting` with `double AudioReliability(QualitySignals q, FusionConfig cfg)` ve `double TextReliability(QualitySignals q, FusionConfig cfg)`, her ikisi de `0.0..1.0`.
- Produces (config, additive — mevcut çağrılar bozulmaz): `FusionConfig` yeni alanlar: `HighConfidenceThreshold`, `IncongruentValenceProsodyWeight`, `SnrLowDb`, `SnrHighDb`, `MinReliableSeconds`, `MinReliableWords`, `LogProbFloor`, `IronyValenceFloor`, `IronyArousalCeiling`, `IronyAudioValenceCeiling`, `PeakMomentCount`.

- [ ] **Step 1: `FusionConfig`'i genişlet**

`src/SentimentAnalysis.Application/Configuration/AppConfig.cs` içindeki `FusionConfig` kaydını şununla değiştir (yalnızca bu kayıt; `StorageConfig`, `SttConfig`, `AppConfig` aynen kalır):

```csharp
/// <summary>Füzyon başlangıç ağırlıkları ve eşikleri. Nihai değerler
/// Alt-proje 09 (eval) ile kalibre edilip buraya yansıtılır — keyfi sabit değil.</summary>
public sealed record FusionConfig(
    double AudioArousalWeight = 0.75,
    double TextArousalWeight  = 0.25,
    double TextValenceWeight  = 0.70,
    double AudioValenceWeight = 0.30,
    double IncongruenceThreshold = 0.8,       // |valence_text − valence_audio| eşiği
    double LowConfidenceThreshold = 0.35,     // altı "düşük güven"
    double HighConfidenceThreshold = 0.65,    // üstü "yüksek güven"
    double IncongruentValenceProsodyWeight = 0.75, // uyumsuzda valence prozodiye yaslanma payı
    double SnrLowDb = 0.0,                     // bu SNR'de ses güvenilirliği ~0
    double SnrHighDb = 15.0,                   // bu SNR'de ses güvenilirliği ~1
    double MinReliableSeconds = 3.0,           // altında süre kısa → ses güvenilirliği düşer
    double MinReliableWords = 3.0,             // altında transkript kısa → metin güvenilirliği düşer
    double LogProbFloor = -1.0,                // Whisper avg-logprob tabanı (bu değerde güven ~0)
    double IronyValenceFloor = 0.3,            // ironi için metin en az bu kadar olumlu
    double IronyArousalCeiling = 0.35,         // ironi için ses "düz" (düşük arousal)
    double IronyAudioValenceCeiling = 0.25,    // ironi için ses net olumlu değil
    int PeakMomentCount = 3);                  // toplantı özetinde tepe an sayısı
```

- [ ] **Step 2: Başarısız testi yaz**

`tests/SentimentAnalysis.Application.Tests/QualityWeightingTests.cs`:

```csharp
using System;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class QualityWeightingTests
{
    private static readonly FusionConfig Cfg = new();

    private static QualitySignals Signals(
        double snr = 20, double speech = 1.0, double seconds = 10,
        int words = 20, double logprob = 0.0) =>
        new(snr, speech, TimeSpan.FromSeconds(seconds), words, logprob);

    [Fact]
    public void AudioReliability_IsHigh_ForCleanLongSpeech()
    {
        var r = QualityWeighting.AudioReliability(Signals(snr: 20, speech: 1.0, seconds: 10), Cfg);
        Assert.True(r > 0.9, $"beklenen yüksek, gelen {r}");
    }

    [Fact]
    public void AudioReliability_IsZero_ForZeroDuration()
    {
        var r = QualityWeighting.AudioReliability(Signals(seconds: 0), Cfg);
        Assert.Equal(0.0, r, precision: 10);
    }

    [Fact]
    public void AudioReliability_DropsWithSilence()
    {
        var loud = QualityWeighting.AudioReliability(Signals(speech: 1.0), Cfg);
        var quiet = QualityWeighting.AudioReliability(Signals(speech: 0.1), Cfg);
        Assert.True(quiet < loud);
    }

    [Fact]
    public void TextReliability_IsHigh_ForLongConfidentTranscript()
    {
        var r = QualityWeighting.TextReliability(Signals(words: 20, logprob: 0.0), Cfg);
        Assert.True(r > 0.9, $"beklenen yüksek, gelen {r}");
    }

    [Fact]
    public void TextReliability_IsZero_ForEmptyTranscript()
    {
        var r = QualityWeighting.TextReliability(Signals(words: 0, logprob: -1.0), Cfg);
        Assert.Equal(0.0, r, precision: 10);
    }

    [Fact]
    public void Reliabilities_StayInUnitRange()
    {
        var extreme = QualityWeighting.AudioReliability(Signals(snr: 999, speech: 5, seconds: 999), Cfg);
        Assert.InRange(extreme, 0.0, 1.0);
    }
}
```

- [ ] **Step 3: Testin başarısız olduğunu doğrula**

Run: `dotnet test tests/SentimentAnalysis.Application.Tests`
Expected: FAIL — `QualityWeighting` bulunamıyor (derleme hatası).

- [ ] **Step 4: `QualityWeighting`'i yaz**

`src/SentimentAnalysis.Application/Fusion/QualityWeighting.cs`:

```csharp
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Fusion;

/// <summary>
/// Girdi kalite sinyallerini, füzyon ağırlıklarını modüle eden 0..1 güvenilirlik
/// faktörlerine çevirir. Saf ve deterministik. Her faktör, zayıf herhangi bir
/// boyutun sonucu aşağı çekmesi için bileşenlerin ÇARPIMIdır (master §5).
/// </summary>
public static class QualityWeighting
{
    /// <summary>Ses güvenilirliği: SNR, konuşma oranı ve süre birlikte.</summary>
    public static double AudioReliability(QualitySignals q, FusionConfig cfg)
    {
        double snrF = Clamp01((q.AudioSnr - cfg.SnrLowDb) / (cfg.SnrHighDb - cfg.SnrLowDb));
        double speechF = Clamp01(q.SpeechRatio);
        double durF = Clamp01(q.Duration.TotalSeconds / cfg.MinReliableSeconds);
        return Clamp01(snrF * speechF * durF);
    }

    /// <summary>Metin güvenilirliği: kelime sayısı ve Whisper avg-logprob birlikte.</summary>
    public static double TextReliability(QualitySignals q, FusionConfig cfg)
    {
        double wordF = Clamp01(q.TranscriptWordCount / cfg.MinReliableWords);
        double logF = Clamp01((q.TranscriptAvgLogProb - cfg.LogProbFloor) / (0.0 - cfg.LogProbFloor));
        return Clamp01(wordF * logF);
    }

    private static double Clamp01(double x) => Math.Clamp(x, 0.0, 1.0);
}
```

- [ ] **Step 5: Testin geçtiğini ve mevcut testlerin kırılmadığını doğrula**

Run: `dotnet test`
Expected: PASS — yeni 6 test geçer; mevcut `AppConfigTests` (additive config değişikliği) hâlâ geçer; toplam artar.

---

### Task 2: `IncongruenceAnalysis` — congruence + uyumsuzluk tespiti + örüntü sınıflandırma

Sistemin kalbi (master §5.1). Saf işaret/eşik mantığı; model yok.

**Files:**
- Create: `src/SentimentAnalysis.Application/Fusion/IncongruenceAnalysis.cs`
- Create: `tests/SentimentAnalysis.Application.Tests/IncongruenceAnalysisTests.cs`

**Interfaces:**
- Consumes: `ValenceArousal`, `IncongruencePattern` (Core), `FusionConfig`
- Produces: `static class IncongruenceAnalysis` with:
  - `double Congruence(double textValence, double audioValence)` → `0..1`
  - `bool IsIncongruent(double textValence, double audioValence, FusionConfig cfg)`
  - `IncongruencePattern Classify(ValenceArousal text, ValenceArousal prosody, bool incongruent, FusionConfig cfg)`

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Application.Tests/IncongruenceAnalysisTests.cs`:

```csharp
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class IncongruenceAnalysisTests
{
    private static readonly FusionConfig Cfg = new();

    [Fact]
    public void Congruence_IsOne_WhenValencesMatch()
    {
        Assert.Equal(1.0, IncongruenceAnalysis.Congruence(0.5, 0.5), precision: 10);
    }

    [Fact]
    public void Congruence_IsZero_WhenValencesOpposedExtremes()
    {
        Assert.Equal(0.0, IncongruenceAnalysis.Congruence(1.0, -1.0), precision: 10);
    }

    [Fact]
    public void IsIncongruent_TrueWhenGapExceedsThreshold()
    {
        Assert.True(IncongruenceAnalysis.IsIncongruent(0.6, -0.6, Cfg));   // fark 1.2 ≥ 0.8
        Assert.False(IncongruenceAnalysis.IsIncongruent(0.6, 0.2, Cfg));   // fark 0.4 < 0.8
    }

    // master §5.1 tablosu — üç örüntü:

    [Fact]
    public void Classify_Maskeleme_PositiveWordsNegativeTone()
    {
        // Ağlamaklı "çok mutluyum": metin +, ses −, arousal orta-yüksek
        var p = IncongruenceAnalysis.Classify(
            text: new ValenceArousal(0.6, 0.5), prosody: new ValenceArousal(-0.6, 0.6),
            incongruent: true, Cfg);
        Assert.Equal(IncongruencePattern.Maskeleme, p);
    }

    [Fact]
    public void Classify_Saka_NegativeWordsCheerfulTone()
    {
        // Gülerek "çok üzgünüm": metin −, ses +, arousal yüksek
        var p = IncongruenceAnalysis.Classify(
            text: new ValenceArousal(-0.6, 0.5), prosody: new ValenceArousal(0.6, 0.85),
            incongruent: true, Cfg);
        Assert.Equal(IncongruencePattern.Saka, p);
    }

    [Fact]
    public void Classify_OlasiIroni_PositiveWordsFlatDelivery()
    {
        // Düz tonla "harika, bayıldım": metin +, ses nötr/düz, arousal düşük
        var p = IncongruenceAnalysis.Classify(
            text: new ValenceArousal(0.6, 0.4), prosody: new ValenceArousal(0.0, 0.2),
            incongruent: false, Cfg);
        Assert.Equal(IncongruencePattern.OlasiIroni, p);
    }

    [Fact]
    public void Classify_Yok_WhenCongruentAndNotIronic()
    {
        var p = IncongruenceAnalysis.Classify(
            text: new ValenceArousal(0.7, 0.5), prosody: new ValenceArousal(0.6, 0.55),
            incongruent: false, Cfg);
        Assert.Equal(IncongruencePattern.Yok, p);
    }

    [Fact]
    public void Classify_Yok_ForIncongruentButUnnamedDivergence()
    {
        // Net olumsuz söz + nötr ton: eşiği aşar (uyumsuz) ama Maskeleme/Şaka/ironi değil.
        // OlasiIroni yalnızca "olumlu söz + düz ton" içindir; buraya düşmemeli.
        var p = IncongruenceAnalysis.Classify(
            text: new ValenceArousal(-0.9, 0.5), prosody: new ValenceArousal(0.0, 0.5),
            incongruent: true, Cfg);
        Assert.Equal(IncongruencePattern.Yok, p);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test tests/SentimentAnalysis.Application.Tests`
Expected: FAIL — `IncongruenceAnalysis` bulunamıyor.

- [ ] **Step 3: `IncongruenceAnalysis`'i yaz**

`src/SentimentAnalysis.Application/Fusion/IncongruenceAnalysis.cs`:

```csharp
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Fusion;

/// <summary>
/// Söz (metin valence) ile ton (ses valence) arasındaki örtüşme/çelişkiyi ölçer.
/// Uyumsuzluk gürültü değil, birinci sınıf sinyaldir (master §5.1, §12.3).
/// Saf ve deterministik.
/// </summary>
public static class IncongruenceAnalysis
{
    /// <summary>Söylenen ile hissedilenin örtüşmesi (0..1). 1 = tam uyum,
    /// 0 = zıt uçlar. Valence aralığı 2 birim olduğu için farkı 2'ye böleriz.</summary>
    public static double Congruence(double textValence, double audioValence) =>
        Math.Clamp(1.0 - Math.Abs(textValence - audioValence) / 2.0, 0.0, 1.0);

    /// <summary>Metin ve ses valence'ı eşiği aşacak kadar ıraksıyor mu?</summary>
    public static bool IsIncongruent(double textValence, double audioValence, FusionConfig cfg) =>
        Math.Abs(textValence - audioValence) >= cfg.IncongruenceThreshold;

    /// <summary>Uyumsuzluk örüntüsünü sınıflandırır (master §5.1 tablosu).</summary>
    public static IncongruencePattern Classify(
        ValenceArousal text, ValenceArousal prosody, bool incongruent, FusionConfig cfg)
    {
        if (!incongruent)
        {
            // Eşiği aşmayan ama olası ironi: olumlu söz + düz/düşük-enerjili, net-olumsuz-olmayan ton.
            bool positiveWords = text.Valence >= cfg.IronyValenceFloor;
            bool flatDelivery = prosody.Arousal <= cfg.IronyArousalCeiling;
            bool toneNotClearlyPositive = prosody.Valence <= cfg.IronyAudioValenceCeiling;
            if (positiveWords && flatDelivery && toneNotClearlyPositive)
                return IncongruencePattern.OlasiIroni;
            return IncongruencePattern.Yok;
        }

        if (text.Valence > 0 && prosody.Valence < 0) return IncongruencePattern.Maskeleme; // olumlu söz / olumsuz ton
        if (text.Valence < 0 && prosody.Valence > 0) return IncongruencePattern.Saka;      // olumsuz söz / neşeli ton
        // Adı konmamış diğer ıraksama (ör. olumsuz söz + nötr ton): OlasiIroni'yi kirletme.
        // Uyumsuzluk zaten `Incongruent = true` bayrağıyla raporlanır; Pattern isim iddia etmez.
        return IncongruencePattern.Yok;
    }
}
```

- [ ] **Step 4: Testin geçtiğini doğrula**

Run: `dotnet test tests/SentimentAnalysis.Application.Tests`
Expected: PASS — 8 yeni test.

---

### Task 3: `EmotionFusion : IEmotionFusion` — chunk-düzeyi füzyon

Task 1 ve 2'yi birleştirip nihai `FusedResult` üretir: eksen-başına ağırlıklı birleştirme, uyumsuzda valence-prozodi yaslanması, belirsiz tespiti, ayrık haritalama.

**Files:**
- Create: `src/SentimentAnalysis.Application/Fusion/EmotionFusion.cs`
- Create: `tests/SentimentAnalysis.Application.Tests/EmotionFusionTests.cs`

**Interfaces:**
- Consumes: `IEmotionFusion` (Application.Abstractions), `TextEmotion`, `ProsodyEmotion`, `QualitySignals`, `FusedResult`, `ValenceArousal`, `EmotionWheel`, `Confidence`, `IncongruencePattern` (Core); `AppConfig`, `FusionConfig`; `QualityWeighting`, `IncongruenceAnalysis`
- Produces: `sealed class EmotionFusion : IEmotionFusion` — ctor `EmotionFusion(AppConfig config)`; `FusedResult Fuse(TextEmotion text, ProsodyEmotion prosody, QualitySignals quality)`.

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Application.Tests/EmotionFusionTests.cs`:

```csharp
using System;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class EmotionFusionTests
{
    private static readonly EmotionFusion Fusion = new(AppConfig.Default);

    // Yüksek kaliteli girdi (güvenilirlik ~1) → ağırlıklar tam etkili.
    private static QualitySignals Clean(int words = 20) =>
        new(AudioSnr: 20, SpeechRatio: 1.0, Duration: TimeSpan.FromSeconds(10),
            TranscriptWordCount: words, TranscriptAvgLogProb: 0.0);

    [Fact]
    public void Arousal_LeansOnAudio()
    {
        // ses yüksek arousal (0.9), metin düşük (0.1) → sonuç sese yakın (>0.5)
        var text = new TextEmotion(new ValenceArousal(0.0, 0.1), ModelConfidence: 0.8);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.0, 0.9), ModelConfidence: 0.8);
        var r = Fusion.Fuse(text, prosody, Clean());
        Assert.True(r.Combined.Arousal > 0.5, $"arousal sese yaslanmalı, gelen {r.Combined.Arousal}");
    }

    [Fact]
    public void Valence_LeansOnText_WhenCongruent()
    {
        // metin olumlu (0.8), ses hafif olumlu (0.2), uyumlu → sonuç metne yakın (>0.5)
        var text = new TextEmotion(new ValenceArousal(0.8, 0.5), ModelConfidence: 0.8);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.2, 0.5), ModelConfidence: 0.8);
        var r = Fusion.Fuse(text, prosody, Clean());
        Assert.False(r.Incongruent);
        Assert.True(r.Combined.Valence > 0.5, $"valence metne yaslanmalı, gelen {r.Combined.Valence}");
    }

    [Fact]
    public void Incongruent_Masking_ValenceGoesNegative()
    {
        // Ağlamaklı "çok mutluyum": metin +0.6, ses −0.6 → uyumsuz, valence prozodiye yaslanır (negatif)
        var text = new TextEmotion(new ValenceArousal(0.6, 0.5), ModelConfidence: 0.8);
        var prosody = new ProsodyEmotion(new ValenceArousal(-0.6, 0.6), ModelConfidence: 0.8);
        var r = Fusion.Fuse(text, prosody, Clean());
        Assert.True(r.Incongruent);
        Assert.Equal(IncongruencePattern.Maskeleme, r.Pattern);
        Assert.True(r.Combined.Valence < 0, $"maskeleme: valence negatif olmalı, gelen {r.Combined.Valence}");
        Assert.Equal(0.6, r.TextValence, precision: 10);
        Assert.Equal(-0.6, r.AudioValence, precision: 10);
    }

    [Fact]
    public void Incongruent_ZeroAudioReliability_ValenceFallsBackToText()
    {
        // Uyumsuz ama ses çöp (rAudio≈0), metin güvenilir: valence güvenilmez prozodiye
        // değil metne yaslanmalı (master §5). Metin +0.6, ses −0.6.
        var q = new QualitySignals(AudioSnr: 0, SpeechRatio: 0, Duration: TimeSpan.Zero,
            TranscriptWordCount: 20, TranscriptAvgLogProb: 0.0); // rAudio=0, rText=1
        var text = new TextEmotion(new ValenceArousal(0.6, 0.5), ModelConfidence: 0.8);
        var prosody = new ProsodyEmotion(new ValenceArousal(-0.6, 0.6), ModelConfidence: 0.8);
        var r = Fusion.Fuse(text, prosody, q);
        Assert.True(r.Incongruent);
        Assert.True(r.Combined.Valence > 0,
            $"ses güvenilmezken valence metne (pozitif) yaslanmalı, gelen {r.Combined.Valence}");
    }

    [Fact]
    public void Belirsiz_OppositeValenceAndBothLowConfidence_YieldsDusuk()
    {
        // zıt valence (+0.2 / −0.2) + iki model de düşük güven → belirsiz = Confidence.Dusuk
        var text = new TextEmotion(new ValenceArousal(0.2, 0.5), ModelConfidence: 0.2);
        var prosody = new ProsodyEmotion(new ValenceArousal(-0.2, 0.5), ModelConfidence: 0.2);
        var r = Fusion.Fuse(text, prosody, Clean());
        Assert.Equal(Confidence.Dusuk, r.Confidence);
    }

    [Fact]
    public void Agreement_HighConfidence_WhenBothPointSameEmotion()
    {
        // iki model de aynı duyguya (mutlu bölgesi) ve yüksek güven → agreement + Yuksek
        var text = new TextEmotion(new ValenceArousal(0.8, 0.5), ModelConfidence: 0.9);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.8, 0.5), ModelConfidence: 0.9);
        var r = Fusion.Fuse(text, prosody, Clean());
        Assert.True(r.Agreement);
        Assert.Equal(Confidence.Yuksek, r.Confidence);
        Assert.True(r.Congruence > 0.9);
    }

    [Fact]
    public void PrimaryAndSecondaryEmotionsPopulated()
    {
        var text = new TextEmotion(new ValenceArousal(0.7, 0.7), ModelConfidence: 0.8);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.7, 0.7), ModelConfidence: 0.8);
        var r = Fusion.Fuse(text, prosody, Clean());
        Assert.NotEqual(r.PrimaryEmotion, r.SecondaryEmotion);
    }

    [Fact]
    public void ZeroReliabilityBothSides_FallsBackToNeutralArousal()
    {
        // süre 0 → ses güvenilirliği 0; kelime 0 + logprob taban → metin güvenilirliği 0
        var q = new QualitySignals(AudioSnr: 0, SpeechRatio: 0, Duration: TimeSpan.Zero,
            TranscriptWordCount: 0, TranscriptAvgLogProb: -1.0);
        var text = new TextEmotion(new ValenceArousal(0.0, 0.9), ModelConfidence: 0.5);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.0, 0.9), ModelConfidence: 0.5);
        var r = Fusion.Fuse(text, prosody, q);
        Assert.Equal(0.5, r.Combined.Arousal, precision: 10); // Neutral.Arousal fallback
    }

    [Fact]
    public void Deterministic_SameInputSameOutput()
    {
        var text = new TextEmotion(new ValenceArousal(0.3, 0.6), ModelConfidence: 0.7);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.1, 0.7), ModelConfidence: 0.6);
        var a = Fusion.Fuse(text, prosody, Clean());
        var b = Fusion.Fuse(text, prosody, Clean());
        Assert.Equal(a, b); // record değer eşitliği
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test tests/SentimentAnalysis.Application.Tests`
Expected: FAIL — `EmotionFusion` bulunamıyor.

- [ ] **Step 3: `EmotionFusion`'i yaz**

`src/SentimentAnalysis.Application/Fusion/EmotionFusion.cs`:

```csharp
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Fusion;

/// <summary>
/// Eksen-başına güvenilirlik-ağırlıklı füzyon (master §5): arousal ağırlıkla
/// sesten, valence ağırlıkla metinden; ağırlıklar kalite ile modüle edilir.
/// Uyumsuzlukta valence prozodiye yaslanır (affect leakage). Saf, deterministik.
/// </summary>
public sealed class EmotionFusion : IEmotionFusion
{
    private readonly FusionConfig _cfg;

    public EmotionFusion(AppConfig config) => _cfg = config.Fusion;

    public FusedResult Fuse(TextEmotion text, ProsodyEmotion prosody, QualitySignals quality)
    {
        double rAudio = QualityWeighting.AudioReliability(quality, _cfg);
        double rText = QualityWeighting.TextReliability(quality, _cfg);

        double vt = text.Va.Valence;
        double va = prosody.Va.Valence;

        // --- Arousal: ses baskın ---
        double arousalFinal = WeightedAverage(
            prosody.Va.Arousal, _cfg.AudioArousalWeight * rAudio,
            text.Va.Arousal, _cfg.TextArousalWeight * rText,
            fallback: ValenceArousal.Neutral.Arousal);

        // --- Uyumsuzluk ---
        bool incongruent = IncongruenceAnalysis.IsIncongruent(vt, va, _cfg);
        var pattern = IncongruenceAnalysis.Classify(text.Va, prosody.Va, incongruent, _cfg);
        double congruence = IncongruenceAnalysis.Congruence(vt, va);

        // --- Valence: uyumluda metin baskın; uyumsuzda prozodiye yaslan ---
        // Uyumsuz dalda da ağırlıklar kaliteyle modüle edilir (master §5: gürültülü/kısa/sessiz
        // ses → ağırlığı düşer). rAudio=rText=1 iken bu tam olarak 0.75/0.25'e indirgenir;
        // ses güvenilmezken (rAudio→0) valence metne kayar.
        double valenceFinal = incongruent
            ? WeightedAverage(
                va, _cfg.IncongruentValenceProsodyWeight * rAudio,
                vt, (1.0 - _cfg.IncongruentValenceProsodyWeight) * rText,
                fallback: 0.0)
            : WeightedAverage(
                vt, _cfg.TextValenceWeight * rText,
                va, _cfg.AudioValenceWeight * rAudio,
                fallback: 0.0);

        var combined = ValenceArousal.Clamp(valenceFinal, arousalFinal);
        var (primary, secondary) = EmotionWheel.MapWithSecondary(combined);

        bool agreement = EmotionWheel.Map(text.Va) == EmotionWheel.Map(prosody.Va);
        var confidence = ResolveConfidence(text, prosody, agreement, incongruent, vt, va);

        return new FusedResult(
            Combined: combined,
            PrimaryEmotion: primary,
            SecondaryEmotion: secondary,
            Confidence: confidence,
            Agreement: agreement,
            Incongruent: incongruent,
            Pattern: pattern,
            Congruence: congruence,
            TextValence: vt,
            AudioValence: va);
    }

    /// <summary>Ağırlıklı ortalama; toplam ağırlık ~0 ise fallback döner (sıfıra bölmeyi önler).</summary>
    private static double WeightedAverage(double x, double wx, double y, double wy, double fallback)
    {
        double sum = wx + wy;
        return sum <= 1e-9 ? fallback : (wx * x + wy * y) / sum;
    }

    /// <summary>Güven düzeyi. Zıt valence + iki model de düşük güven → belirsiz (Dusuk);
    /// aksi halde uyum + yüksek ortalama model güveni → Yuksek; çok düşükse Dusuk; yoksa Orta.</summary>
    private Confidence ResolveConfidence(
        TextEmotion text, ProsodyEmotion prosody, bool agreement, bool incongruent,
        double textValence, double audioValence)
    {
        bool bothLow = text.ModelConfidence < _cfg.LowConfidenceThreshold
                    && prosody.ModelConfidence < _cfg.LowConfidenceThreshold;
        bool oppositeValence = textValence * audioValence < 0.0;
        if (oppositeValence && bothLow) return Confidence.Dusuk; // belirsiz (master §5.1)

        double avgConf = (text.ModelConfidence + prosody.ModelConfidence) / 2.0;
        if (agreement && !incongruent && avgConf >= _cfg.HighConfidenceThreshold)
            return Confidence.Yuksek;
        if (avgConf < _cfg.LowConfidenceThreshold)
            return Confidence.Dusuk;
        return Confidence.Orta;
    }
}
```

- [ ] **Step 4: Testin geçtiğini doğrula**

Run: `dotnet test tests/SentimentAnalysis.Application.Tests`
Expected: PASS — 8 yeni test.

---

### Task 4: `MeetingSummary` (Core) + `IMeetingAggregator` (Application.Abstractions)

Toplantı-düzeyi çıktı sözleşmesi. `MeetingSummary` domain tipidir → `Core`'da (sıfır bağımlılık). Agregasyon sözleşmesi Application arayüzlerine eklenir.

**Files:**
- Create: `src/SentimentAnalysis.Core/MeetingSummary.cs`
- Modify: `src/SentimentAnalysis.Application/Abstractions/Interfaces.cs` (yalnızca `IMeetingAggregator` ekle)
- Create: `tests/SentimentAnalysis.Core.Tests/MeetingSummaryTests.cs`

**Interfaces:**
- Consumes: `Emotion`, `ValenceArousal`, `ChunkResult`, `MeetingSession` (Core)
- Produces: `sealed record MeetingSummary(Emotion DominantEmotion, IReadOnlyDictionary<Emotion,double> TimeShareByEmotion, double ValenceTrend, double EmotionalVolatility, IReadOnlyList<ChunkResult> PeakMoments, ValenceArousal AverageVa, double AverageCongruence, string TurkishSummary)`; `interface IMeetingAggregator { MeetingSummary Aggregate(MeetingSession session); }`.

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Core.Tests/MeetingSummaryTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Core.Tests;

public class MeetingSummaryTests
{
    [Fact]
    public void MeetingSummary_CarriesAggregateMetrics()
    {
        var summary = new MeetingSummary(
            DominantEmotion: Emotion.Mutlu,
            TimeShareByEmotion: new Dictionary<Emotion, double> { [Emotion.Mutlu] = 1.0 },
            ValenceTrend: 0.1,
            EmotionalVolatility: 0.05,
            PeakMoments: new List<ChunkResult>(),
            AverageVa: new ValenceArousal(0.6, 0.5),
            AverageCongruence: 0.9,
            TurkishSummary: "Toplantı genel olarak olumluydu.");

        Assert.Equal(Emotion.Mutlu, summary.DominantEmotion);
        Assert.Equal(1.0, summary.TimeShareByEmotion[Emotion.Mutlu]);
        Assert.Equal(0.9, summary.AverageCongruence);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test tests/SentimentAnalysis.Core.Tests`
Expected: FAIL — `MeetingSummary` bulunamıyor.

- [ ] **Step 3: `MeetingSummary`'i yaz**

`src/SentimentAnalysis.Core/MeetingSummary.cs`:

```csharp
using System.Collections.Generic;

namespace SentimentAnalysis.Core;

/// <summary>
/// Toplantı-düzeyi agregasyon çıktısı (master §6). Chunk zaman çizelgesinden
/// hesaplanır. Türkçe özet cümlesi Application katmanında üretilir; burada yalnızca
/// taşınır (Core dil-nötr kalır, yalnızca hazır string tutar).
/// </summary>
public sealed record MeetingSummary(
    Emotion DominantEmotion,
    IReadOnlyDictionary<Emotion, double> TimeShareByEmotion, // duygu → 0..1 süre oranı
    double ValenceTrend,          // regresyon eğimi: + ısındı, − soğudu
    double EmotionalVolatility,   // valence standart sapması (oynaklık)
    IReadOnlyList<ChunkResult> PeakMoments, // en kritik anlar (yüksek arousal / düşük valence)
    ValenceArousal AverageVa,
    double AverageCongruence,     // ortalama söz-ton örtüşmesi (0..1)
    string TurkishSummary);
```

- [ ] **Step 4: `IMeetingAggregator`'ı ekle**

`src/SentimentAnalysis.Application/Abstractions/Interfaces.cs` dosyasının **sonuna** (son `}` kapanışından önce, `ISessionStore` bloğunun ardından) ekle:

```csharp

/// <summary>Toplantı-düzeyi agregasyon (baskın duygu, trend, oynaklık, tepe
/// anlar, ortalama congruence + Türkçe özet). Saf, deterministik (Alt-proje 02).</summary>
public interface IMeetingAggregator
{
    MeetingSummary Aggregate(MeetingSession session);
}
```

- [ ] **Step 5: Testin geçtiğini ve tüm çözümün derlendiğini doğrula**

Run: `dotnet build && dotnet test`
Expected: Build succeeded; yeni Core testi PASS; mevcut testler PASS.

---

### Task 5: `MeetingAggregator` — sayısal metrikler

`IMeetingAggregator`'ı uygular; Türkçe cümle **hariç** tüm sayısal metrikler. Cümle Task 6'da eklenir; bu task'ta geçici olarak boş string döner ama tüm metrikler doğrulanır.

**Files:**
- Create: `src/SentimentAnalysis.Application/Fusion/MeetingAggregator.cs`
- Create: `tests/SentimentAnalysis.Application.Tests/MeetingAggregatorTests.cs`

**Interfaces:**
- Consumes: `IMeetingAggregator`, `MeetingSummary`, `MeetingSession`, `ChunkResult`, `FusedResult`, `Emotion`, `ValenceArousal` (Core); `AppConfig`, `FusionConfig`
- Produces: `sealed class MeetingAggregator : IMeetingAggregator` — ctor `MeetingAggregator(AppConfig config)`; `MeetingSummary Aggregate(MeetingSession session)`.

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Application.Tests/MeetingAggregatorTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class MeetingAggregatorTests
{
    private static readonly MeetingAggregator Agg = new(AppConfig.Default);

    private static FusedResult Fused(double v, double a, Emotion primary, double congruence = 0.9) =>
        new(new ValenceArousal(v, a), primary, Emotion.Notr,
            Confidence.Orta, true, false, IncongruencePattern.Yok, congruence, v, v);

    private static ChunkResult Chunk(int startSec, int endSec, double v, double a, Emotion e, double congruence = 0.9) =>
        new(TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec), Fused(v, a, e, congruence), "x");

    private static MeetingSession Session(params ChunkResult[] chunks) =>
        new(Guid.Empty, DateTimeOffset.UnixEpoch, DetectedLanguage.Turkish, chunks);

    [Fact]
    public void EmptySession_ReturnsNeutralSummary()
    {
        var s = Agg.Aggregate(Session());
        Assert.Equal(Emotion.Notr, s.DominantEmotion);
        Assert.Empty(s.PeakMoments);
        Assert.Equal(0.0, s.ValenceTrend);
        Assert.Equal(0.0, s.EmotionalVolatility);
    }

    [Fact]
    public void DominantEmotion_IsLongestByDuration()
    {
        // Mutlu 20sn, Uzgun 5sn → baskın Mutlu
        var s = Agg.Aggregate(Session(
            Chunk(0, 20, 0.8, 0.5, Emotion.Mutlu),
            Chunk(20, 25, -0.6, 0.3, Emotion.Uzgun)));
        Assert.Equal(Emotion.Mutlu, s.DominantEmotion);
        Assert.True(s.TimeShareByEmotion[Emotion.Mutlu] > s.TimeShareByEmotion[Emotion.Uzgun]);
    }

    [Fact]
    public void TimeShare_SumsToOne()
    {
        var s = Agg.Aggregate(Session(
            Chunk(0, 10, 0.8, 0.5, Emotion.Mutlu),
            Chunk(10, 20, -0.6, 0.3, Emotion.Uzgun)));
        double total = 0;
        foreach (var kv in s.TimeShareByEmotion) total += kv.Value;
        Assert.Equal(1.0, total, precision: 10);
    }

    [Fact]
    public void ValenceTrend_PositiveWhenWarming()
    {
        // valence artıyor: −0.5 → 0.0 → 0.5
        var s = Agg.Aggregate(Session(
            Chunk(0, 10, -0.5, 0.5, Emotion.Uzgun),
            Chunk(10, 20, 0.0, 0.5, Emotion.Notr),
            Chunk(20, 30, 0.5, 0.5, Emotion.Mutlu)));
        Assert.True(s.ValenceTrend > 0, $"ısınan toplantı, gelen {s.ValenceTrend}");
    }

    [Fact]
    public void Volatility_HigherForSwings()
    {
        var stable = Agg.Aggregate(Session(
            Chunk(0, 10, 0.5, 0.5, Emotion.Mutlu), Chunk(10, 20, 0.5, 0.5, Emotion.Mutlu)));
        var swingy = Agg.Aggregate(Session(
            Chunk(0, 10, -0.9, 0.5, Emotion.Kizgin), Chunk(10, 20, 0.9, 0.5, Emotion.Cosakulu)));
        Assert.True(swingy.EmotionalVolatility > stable.EmotionalVolatility);
    }

    [Fact]
    public void PeakMoments_AreMostCritical_AndCapped()
    {
        // PeakMomentCount = 3; en kritik = yüksek arousal + düşük valence
        var s = Agg.Aggregate(Session(
            Chunk(0, 10, 0.8, 0.2, Emotion.Sakin),    // sakin
            Chunk(10, 20, -0.9, 0.95, Emotion.Kizgin), // en kritik
            Chunk(20, 30, 0.5, 0.5, Emotion.Mutlu),
            Chunk(30, 40, -0.5, 0.8, Emotion.Gergin),
            Chunk(40, 50, 0.7, 0.4, Emotion.Sakin)));
        Assert.Equal(3, s.PeakMoments.Count);
        Assert.Equal(-0.9, s.PeakMoments[0].Fused.Combined.Valence, precision: 10); // en kritik (Kizgin, arousal−valence=1.85)
        Assert.Equal(-0.5, s.PeakMoments[1].Fused.Combined.Valence, precision: 10); // 2. kritik (Gergin, 1.3)
        Assert.Equal(0.5, s.PeakMoments[2].Fused.Combined.Valence, precision: 10);  // 3. kritik (Mutlu, 0.0)
    }

    [Fact]
    public void DominantEmotion_TieBreaksByEnumOrderDeterministically()
    {
        // Mutlu ve Uzgun tam eşit süre (10sn). Deterministik tie-break: enum sırası küçük olan
        // (Mutlu=1 < Uzgun=4) kazanır. Aynı girdi hep aynı sonucu vermeli.
        var session = Session(
            Chunk(0, 10, 0.8, 0.5, Emotion.Mutlu),
            Chunk(10, 20, -0.6, 0.3, Emotion.Uzgun));
        Assert.Equal(Emotion.Mutlu, Agg.Aggregate(session).DominantEmotion);
        Assert.Equal(Emotion.Mutlu, Agg.Aggregate(session).DominantEmotion);
    }

    [Fact]
    public void AverageCongruence_IsMean()
    {
        var s = Agg.Aggregate(Session(
            Chunk(0, 10, 0.5, 0.5, Emotion.Mutlu, congruence: 0.8),
            Chunk(10, 20, 0.5, 0.5, Emotion.Mutlu, congruence: 0.4)));
        Assert.Equal(0.6, s.AverageCongruence, precision: 10);
    }

    [Fact]
    public void AverageVa_IsMean()
    {
        var s = Agg.Aggregate(Session(
            Chunk(0, 10, 0.4, 0.6, Emotion.Mutlu),
            Chunk(10, 20, 0.6, 0.4, Emotion.Mutlu)));
        Assert.Equal(0.5, s.AverageVa.Valence, precision: 10);
        Assert.Equal(0.5, s.AverageVa.Arousal, precision: 10);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test tests/SentimentAnalysis.Application.Tests`
Expected: FAIL — `MeetingAggregator` bulunamıyor.

- [ ] **Step 3: `MeetingAggregator`'ı yaz (Türkçe cümle geçici boş)**

`src/SentimentAnalysis.Application/Fusion/MeetingAggregator.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Fusion;

/// <summary>
/// Chunk zaman çizelgesini toplantı-düzeyi metriklere indirger (master §6):
/// baskın duygu, süre oranları, valence trendi, oynaklık, tepe anlar, ortalama
/// congruence. Saf ve deterministik.
/// </summary>
public sealed class MeetingAggregator : IMeetingAggregator
{
    private readonly FusionConfig _cfg;

    public MeetingAggregator(AppConfig config) => _cfg = config.Fusion;

    public MeetingSummary Aggregate(MeetingSession session)
    {
        var chunks = session.Chunks;
        if (chunks.Count == 0)
        {
            return new MeetingSummary(
                DominantEmotion: Emotion.Notr,
                TimeShareByEmotion: new Dictionary<Emotion, double>(),
                ValenceTrend: 0.0,
                EmotionalVolatility: 0.0,
                PeakMoments: new List<ChunkResult>(),
                AverageVa: ValenceArousal.Neutral,
                AverageCongruence: 1.0,
                TurkishSummary: string.Empty);
        }

        var timeShare = ComputeTimeShare(chunks, out var dominant);
        var valences = chunks.Select(c => c.Fused.Combined.Valence).ToArray();
        var times = chunks.Select(c => (c.TimeStart + (c.TimeEnd - c.TimeStart) / 2).TotalSeconds).ToArray();
        double trend = Slope(times, valences);
        double volatility = PopulationStdDev(valences);
        var peaks = chunks
            .OrderByDescending(c => Criticality(c.Fused.Combined))
            .Take(_cfg.PeakMomentCount)
            .ToList();
        var averageVa = new ValenceArousal(
            chunks.Average(c => c.Fused.Combined.Valence),
            chunks.Average(c => c.Fused.Combined.Arousal));
        double avgCongruence = chunks.Average(c => c.Fused.Congruence);

        return new MeetingSummary(
            DominantEmotion: dominant,
            TimeShareByEmotion: timeShare,
            ValenceTrend: trend,
            EmotionalVolatility: volatility,
            PeakMoments: peaks,
            AverageVa: averageVa,
            AverageCongruence: avgCongruence,
            TurkishSummary: string.Empty); // Task 6'da doldurulur
    }

    /// <summary>Kritiklik: yüksek arousal + düşük valence = en kritik an.</summary>
    private static double Criticality(ValenceArousal va) => va.Arousal - va.Valence;

    private static IReadOnlyDictionary<Emotion, double> ComputeTimeShare(
        IReadOnlyList<ChunkResult> chunks, out Emotion dominant)
    {
        var weights = new Dictionary<Emotion, double>();
        double total = 0;
        foreach (var c in chunks)
        {
            double w = Weight(c);
            total += w;
            weights[c.Fused.PrimaryEmotion] = weights.GetValueOrDefault(c.Fused.PrimaryEmotion) + w;
        }

        var share = new Dictionary<Emotion, double>();
        foreach (var kv in weights)
            share[kv.Key] = kv.Value / total;

        // Baskın: en yüksek pay; eşitlikte enum sırasına göre deterministik.
        dominant = share
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => (int)kv.Key)
            .First().Key;
        return share;
    }

    /// <summary>Chunk ağırlığı: pozitif süre; sıfır/negatifse 1.0 (dejenere veriyi eşit say).</summary>
    private static double Weight(ChunkResult c)
    {
        double seconds = (c.TimeEnd - c.TimeStart).TotalSeconds;
        return seconds > 0 ? seconds : 1.0;
    }

    /// <summary>y'nin x (chunk zaman-ortası, saniye) üzerinden en küçük kareler regresyon
    /// eğimi. Zaman-tabanlı x, eşit olmayan chunk süreleri/boşluklarında trendi doğru tutar
    /// (master §6: toplantı zaman içinde ısındı/soğudu). x sabitse (den≈0) eğim 0.</summary>
    private static double Slope(double[] x, double[] y)
    {
        int n = y.Length;
        if (n < 2) return 0.0;
        double meanX = x.Average();
        double meanY = y.Average();
        double num = 0, den = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = x[i] - meanX;
            num += dx * (y[i] - meanY);
            den += dx * dx;
        }
        return den <= 1e-9 ? 0.0 : num / den;
    }

    /// <summary>Popülasyon standart sapması (oynaklık).</summary>
    private static double PopulationStdDev(double[] y)
    {
        int n = y.Length;
        if (n < 2) return 0.0;
        double mean = y.Average();
        double sumSq = y.Sum(v => (v - mean) * (v - mean));
        return System.Math.Sqrt(sumSq / n);
    }
}
```

- [ ] **Step 4: Testin geçtiğini doğrula**

Run: `dotnet test tests/SentimentAnalysis.Application.Tests`
Expected: PASS — 8 yeni test.

---

### Task 6: Türkçe doğal-dil özet cümlesi

`MeetingAggregator`'ın ürettiği metriklerden deterministik Türkçe özet cümlesi üretir ve `MeetingSummary.TurkishSummary`'ye yerleştirir. Emotion→Türkçe ad haritası burada (Application) yaşar; `Core` dil-nötr kalır.

**Files:**
- Create: `src/SentimentAnalysis.Application/Fusion/TurkishSummaryWriter.cs`
- Modify: `src/SentimentAnalysis.Application/Fusion/MeetingAggregator.cs` (özet cümlesini bağla)
- Create: `tests/SentimentAnalysis.Application.Tests/TurkishSummaryWriterTests.cs`

**Interfaces:**
- Consumes: `Emotion`, `MeetingSummary` (Core), `FusionConfig`
- Produces: `static class TurkishSummaryWriter` with `string Write(MeetingSummary metrics)` — metriklerden tam Türkçe cümle üretir (girdi `MeetingSummary`'nin `TurkishSummary` alanı hariç dolu olduğu varsayılır).

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Application.Tests/TurkishSummaryWriterTests.cs`:

```csharp
using System.Collections.Generic;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class TurkishSummaryWriterTests
{
    private static MeetingSummary Metrics(
        Emotion dominant, double trend, double volatility, double congruence) =>
        new(dominant, new Dictionary<Emotion, double> { [dominant] = 1.0 },
            trend, volatility, new List<ChunkResult>(),
            new ValenceArousal(0.0, 0.5), congruence, TurkishSummary: string.Empty);

    [Fact]
    public void Write_MentionsDominantEmotionInTurkish()
    {
        var text = TurkishSummaryWriter.Write(Metrics(Emotion.Mutlu, 0.1, 0.05, 0.9));
        Assert.Contains("Mutlu", text);
    }

    [Fact]
    public void Write_SaysWarming_ForPositiveTrend()
    {
        var text = TurkishSummaryWriter.Write(Metrics(Emotion.Mutlu, 0.2, 0.05, 0.9));
        Assert.Contains("ısındı", text);
    }

    [Fact]
    public void Write_SaysCooling_ForNegativeTrend()
    {
        var text = TurkishSummaryWriter.Write(Metrics(Emotion.Uzgun, -0.2, 0.05, 0.9));
        Assert.Contains("soğudu", text);
    }

    [Fact]
    public void Write_FlagsLowCongruence()
    {
        var text = TurkishSummaryWriter.Write(Metrics(Emotion.Gergin, 0.0, 0.05, 0.2));
        Assert.Contains("düşük", text);
    }

    [Fact]
    public void Write_IsNonEmpty()
    {
        var text = TurkishSummaryWriter.Write(Metrics(Emotion.Notr, 0.0, 0.0, 1.0));
        Assert.False(string.IsNullOrWhiteSpace(text));
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test tests/SentimentAnalysis.Application.Tests`
Expected: FAIL — `TurkishSummaryWriter` bulunamıyor.

- [ ] **Step 3: `TurkishSummaryWriter`'ı yaz**

`src/SentimentAnalysis.Application/Fusion/TurkishSummaryWriter.cs`:

```csharp
using System.Collections.Generic;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Fusion;

/// <summary>
/// Toplantı metriklerinden deterministik Türkçe özet cümlesi üretir. Emotion→Türkçe
/// ad haritası burada (Application/sunum) yaşar; Core dil-nötr kalır (master §D5, §12).
/// </summary>
public static class TurkishSummaryWriter
{
    private static readonly IReadOnlyDictionary<Emotion, string> Adlar = new Dictionary<Emotion, string>
    {
        [Emotion.Cosakulu] = "Coşkulu",
        [Emotion.Mutlu] = "Mutlu",
        [Emotion.Sakin] = "Sakin",
        [Emotion.Sikilmis] = "Sıkılmış",
        [Emotion.Uzgun] = "Üzgün",
        [Emotion.Gergin] = "Gergin",
        [Emotion.Kizgin] = "Kızgın",
        [Emotion.Notr] = "Nötr",
    };

    public static string Write(MeetingSummary m)
    {
        string dominant = Adlar[m.DominantEmotion];
        string trend = m.ValenceTrend > 0.05 ? "ısındı"
                     : m.ValenceTrend < -0.05 ? "soğudu"
                     : "sabit kaldı";
        string volatilite = m.EmotionalVolatility > 0.35 ? "yüksek"
                          : m.EmotionalVolatility > 0.15 ? "orta"
                          : "düşük";
        string congruence = m.AverageCongruence >= 0.7 ? "yüksek"
                          : m.AverageCongruence >= 0.4 ? "orta"
                          : "düşük";

        return $"Toplantının baskın duygusu {dominant}. "
             + $"Genel hava {trend}, duygusal oynaklık {volatilite}. "
             + $"Söz-ton örtüşmesi {congruence}.";
    }
}
```

- [ ] **Step 4: `MeetingAggregator`'ı özet cümlesini üretecek şekilde güncelle**

`src/SentimentAnalysis.Application/Fusion/MeetingAggregator.cs` içindeki `Aggregate` metodunun **son `return`'ünü** şu iki adımla değiştir. Boş-oturum dalını (erken `return`) aynen bırak.

Son `return new MeetingSummary(...)` bloğunu şununla değiştir:

```csharp
        var summary = new MeetingSummary(
            DominantEmotion: dominant,
            TimeShareByEmotion: timeShare,
            ValenceTrend: trend,
            EmotionalVolatility: volatility,
            PeakMoments: peaks,
            AverageVa: averageVa,
            AverageCongruence: avgCongruence,
            TurkishSummary: string.Empty);
        return summary with { TurkishSummary = TurkishSummaryWriter.Write(summary) };
```

- [ ] **Step 5: Testlerin geçtiğini doğrula (yeni + agregasyon regresyonu)**

Run: `dotnet test tests/SentimentAnalysis.Application.Tests`
Expected: PASS — yeni 5 test; Task 5'in testleri hâlâ geçer (metrikler değişmedi, yalnızca özet doldu).

---

### Task 7: DI kaydı — `AddFusion()` + tam çözüm doğrulaması

Füzyon servislerini DI'a bağlar; `Application` katmanının uçtan uca çözüldüğünü doğrular.

**Files:**
- Create: `src/SentimentAnalysis.Application/Fusion/FusionServiceCollectionExtensions.cs`
- Create: `tests/SentimentAnalysis.Application.Tests/FusionDiTests.cs`

**Interfaces:**
- Consumes: `IServiceCollection` (Microsoft.Extensions.DependencyInjection.Abstractions), `IEmotionFusion`, `IMeetingAggregator`, `AppConfig`; `EmotionFusion`, `MeetingAggregator`; `DependencyInjection.AddApplicationCore` (mevcut)
- Produces: `static class FusionServiceCollectionExtensions` with `IServiceCollection AddFusion(this IServiceCollection services)`.

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Application.Tests/FusionDiTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Fusion;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class FusionDiTests
{
    [Fact]
    public void AddFusion_ResolvesFusionAndAggregator()
    {
        var provider = new ServiceCollection()
            .AddApplicationCore()
            .AddFusion()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IEmotionFusion>());
        Assert.NotNull(provider.GetRequiredService<IMeetingAggregator>());
    }

    [Fact]
    public void ResolvedFusion_IsUsable()
    {
        var provider = new ServiceCollection()
            .AddApplicationCore()
            .AddFusion()
            .BuildServiceProvider();

        var fusion = provider.GetRequiredService<IEmotionFusion>();
        var r = fusion.Fuse(
            new Core.TextEmotion(new Core.ValenceArousal(0.8, 0.5), 0.9),
            new Core.ProsodyEmotion(new Core.ValenceArousal(0.8, 0.5), 0.9),
            new Core.QualitySignals(20, 1.0, System.TimeSpan.FromSeconds(10), 20, 0.0));
        Assert.Equal(Core.Emotion.Mutlu, r.PrimaryEmotion);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test tests/SentimentAnalysis.Application.Tests`
Expected: FAIL — `AddFusion` bulunamıyor.

- [ ] **Step 3: `AddFusion()`'ı yaz**

`src/SentimentAnalysis.Application/Fusion/FusionServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Application.Fusion;

/// <summary>Füzyon motoru DI kaydı. Saf/durumsuz olduğundan singleton.
/// `AddApplicationCore()` ile birlikte kullanılır (AppConfig'e bağımlı).</summary>
public static class FusionServiceCollectionExtensions
{
    public static IServiceCollection AddFusion(this IServiceCollection services)
    {
        services.AddSingleton<IEmotionFusion, EmotionFusion>();
        services.AddSingleton<IMeetingAggregator, MeetingAggregator>();
        return services;
    }
}
```

- [ ] **Step 4: Tüm çözümün derlendiğini ve tüm testlerin geçtiğini doğrula**

Run: `dotnet build && dotnet test`
Expected: Build succeeded; **tüm** testler PASS (Alt-proje 01'in 16 testi + bu plandaki tüm yeni testler). Uyarı yok.

---

## Self-Review (plan yazarı tarafından)

- **Spec coverage (spec 02 Kapsam/Algoritma + Kabul Kriteri):**
  - Eksen-başına ağırlık (arousal←ses, valence←metin) → Task 3 `Fuse` + testler `Arousal_LeansOnAudio`, `Valence_LeansOnText_WhenCongruent`. ✔
  - Ağırlıklar kalite ile dinamik modüle → Task 1 `QualityWeighting` + Task 3 kullanımı + `ZeroReliabilityBothSides...`. ✔
  - Uyumsuzluk tespiti + `IncongruencePattern` (Maskeleme/Şaka/Olası-ironi) → Task 2 + Task 3; **üç örüntü birim testli** (Kabul Kriteri). ✔
  - Uyumsuzda valence prozodiye yaslanır, iki sinyal de raporlanır → Task 3 `Incongruent_Masking...` (`TextValence`/`AudioValence` doğrulanır). ✔
  - Zıt kadran + düşük güven → belirsiz → Task 3 `Belirsiz_...` (**belirsiz-dönme koşulu testli**, Kabul Kriteri). ✔
  - `congruence` skoru → Task 2 `Congruence` + testler. ✔
  - Toplantı agregasyonu (baskın duygu, %süre, trend, oynaklık, tepe anlar, ort. congruence) → Task 5; Türkçe özet → Task 6. ✔
  - Eşikler `AppConfig`'ten, kalibrasyon 09'da → Task 1 config; hiçbir sabit koda gömülü karar değil. ✔
  - Saf fonksiyon, I/O yok → tüm sınıflar static/stateless; Task 3 `Deterministic_SameInputSameOutput`. ✔
- **Placeholder scan:** TODO/TBD yok. Task 5'te `TurkishSummary = string.Empty` bilinçli ara-durumdur ve Task 6 Step 4'te gerçek değerle değiştirilir (adım açıkça gösteriliyor) — asılı placeholder değil.
- **Type consistency:** `FusedResult` alan adları/sırası (Combined, PrimaryEmotion, SecondaryEmotion, Confidence, Agreement, Incongruent, Pattern, Congruence, TextValence, AudioValence) Alt-proje 01 ile birebir. `IEmotionFusion.Fuse` imzası korunuyor. `QualityWeighting.AudioReliability/TextReliability`, `IncongruenceAnalysis.Congruence/IsIncongruent/Classify`, `MeetingAggregator.Aggregate`, `TurkishSummaryWriter.Write`, `AddFusion` adları task'lar arası tutarlı. `FusionConfig` değişikliği additive (varsayılanlı) → mevcut `AppConfigTests` ve `AppConfig.Default` kırılmaz.
- **Mimari:** Tüm füzyon impl'i `Application` katmanında (spec 02 "Katman: Application"); `Core`'a yalnızca `MeetingSummary` (saf domain, BCL-only) eklendi. Bağımlılıklar içe doğru korunuyor. `IMeetingAggregator` mevcut arayüz dosyasına additive eklendi.
- **Git kuralı:** Commit adımları yok; her task `dotnet build && dotnet test` ile doğrulanıyor (proje sahibi kararı).
```


---

## Post-Review Düzeltmeleri (uygulama sırasında alınan kararlar)

Subagent-driven uygulama + review döngüsünde ortaya çıkan ve proje sahibi onayıyla
uygulanan sapmalar (kod bu düzeltmeleri içerir; testlerle doğrulanmıştır):

- **Task 2 (IncongruenceAnalysis):** Uyumsuz dalın fallback'i `OlasiIroni` yerine
  `Yok` döner. `OlasiIroni` yalnızca "olumlu söz + düz ton" (eşik-altı) içindir;
  adı konmamış ıraksama `Incongruent=true` bayrağıyla raporlanır, isim iddia etmez.
- **Task 3 (EmotionFusion):** Uyumsuz dalın valence hesabı da kalite güvenilirliğiyle
  (`rAudio/rText`) modüle edilir (master §5). `rAudio=rText=1` iken 0.75/0.25'e indirgenir.
- **Task 5 (MeetingAggregator):** `ValenceTrend` regresyonu x-ekseni olarak chunk
  zaman-ortasını (saniye) kullanır (indeks değil) — eşit olmayan süre/boşluklarda doğru.
- **Final review (I1):** `FusedResult.Flagged` türetilmiş özelliği eklendi
  (`Incongruent || Pattern != Yok`). Aşağı akış "şüpheli chunk" filtrelemesini tek
  noktadan bununla yapar (eşik-altı ironiyi kaçırmaz). Füzyon matematiği değişmedi.
- **Final review (I2):** `ResolveConfidence` girdi kalitesini hesaba katar —
  `rAudio*rText < FusionConfig.MinReliabilityForHighConfidence (0.5)` iken `Yuksek`
  güven verilmez (master §12.2 dürüstlük).
- **Final review (M1):** `QualityWeighting` payda dejenere (≤1e-9) olduğunda NaN yerine
  0.0 döner (Alt-proje 09 kalibrasyonunda config eşitlenirse güvenli).

**Kalan Minor (Alt-proje 09 / gelecek iş):** M2 (belirsiz "zıt valence işareti" kullanıyor,
master "zıt kadran"; planlı sadeleştirme), M3 (TurkishSummaryWriter sınır-eşik dalları test
edilmiyor), M4 (boş oturumda Türkçe özet boş). Hiçbiri blocker değil.

**Nihai durum:** 59/59 test geçiyor, 0 uyarı. Master §5/§6 uyum tablosu tam. Merge'e hazır.
