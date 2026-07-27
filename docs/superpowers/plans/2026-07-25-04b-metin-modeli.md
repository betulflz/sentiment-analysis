# Alt-Proje 04b: Metin Modeli — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** XLM-R tabanlı çok-dilli 11-duygu sınıflandırıcıyı ONNX Runtime ile lokal çalıştırıp `ITextEmotionModel` arkasında `Transcript` → `TextEmotion` (valence-arousal + entropi-tabanlı modelConfidence) üretmek.

**Architecture:** Mevcut `SentimentAnalysis.Infrastructure.Emotion` projesine eklenir (04a prozodi ile aynı dilim; `EmotionConfig`, `IEmotionModelProvider`, DI iskeleti hazır → additive genişletme). Boru: `Transcript.Text` → SentencePiece tokenize (`Microsoft.ML.Tokenizers`, XLM-R reçetesi) → `input_ids`/`attention_mask` (int64) → ONNX `logits[1,11]` → softmax → **VA lookup** (olasılık-ağırlıklı ortalama) + **entropi-confidence** → `TextEmotion`. 04a örüntüsünü izler: saf yardımcılar (`TextVaLookup`, `XlmrInputBuilder`) tam unit-testli; SentencePiece + ONNX Runtime tipleri motor sınırında (`XlmrTokenizerAdapter`, `OnnxTextEmotionModel`), gerçek-model/tokenizer testleri env-gated Skip. Motor session'ı + tokenizer'ı bir kez yükler (singleton, sıcak).

**Tech Stack:** .NET 9 · C# · xUnit 2.9.2 + Xunit.SkippableFact · Microsoft.ML.OnnxRuntime (CoreML EP + CPU fallback) · **Microsoft.ML.Tokenizers 2.0.0** (SentencePiece)

## Global Constraints

- **Hedef framework:** `net9.0`. Nullable + ImplicitUsings açık.
- **Bağımlılık yönü:** Infrastructure.Emotion → Application → Core. (Core değişmez.)
- **`git YOK`**: her task sonu `dotnet build && dotnet test`, commit değil.
- **`TextEmotion` domain tipi (Alt-proje 01) DEĞİŞMEZ:** `TextEmotion(ValenceArousal Va, double ModelConfidence)`.
- **`ITextEmotionModel` sözleşmesi DEĞİŞMEZ:** `Task<TextEmotion> AnalyzeAsync(Transcript transcript, CancellationToken ct)`.
- **Model (başlangıç):** `tabularisai/multilingual-emotion-classification` — XLM-R-base, **11 duygu**. Kesin model/eşik → Alt-Proje 09 (eval kalibre eder; bu plan başlangıç adayını sabitler, keyfi kalite iddiası vermez).
- **Tokenizer reçetesi (spike ile doğrulandı 2026-07-25, spec §04b):** `SentencePieceTokenizer` BOS/EOS **kapalı** oluşturulur → `EncodeToIds` ham SentencePiece piece id'leri verir → her id'ye **+1 fairseq offset** → başa `0` (`<s>`) → sona `2` (`</s>`). `attention_mask` = hepsi `1` (tek örnek, padding yok). 3 Türkçe metinde Python `XLMRobertaTokenizer` ile **birebir** eşleşti.
- **ONNX kontratı:** girdiler `input_ids` + `attention_mask`, her ikisi **int64** tensör şekil `[1, N]`; çıktı `logits` `[1, 11]` (float, softmax öncesi). Girdi/çıktı adları ve `logits` uzunluğu (11) model config'i ile doğrulanır (entegrasyon testi assert eder).
- **Etiket sırası (label order) KRİTİK:** `logits[i]` → `labels[i]` → VA. Etiket sırası modelin `config.json` `id2label` alanından gelir; `XlmrEmotionLabels.Ordered` sabiti bunu tutar ve gerçek-model entegrasyon testinde `logits.Length == 11` + sıra doğrulanır. VA tablosu **etiket adıyla** anahtarlanır (index'e değil) → sıra hatası VA'yı sessizce bozamaz.
- **VA lookup tablosu (BAŞLANGIÇ, spec §04b; eval 09 kalibre eder):**
  joy(+0.8,0.7) · love(+0.9,0.5) · gratitude(+0.7,0.4) · surprise(+0.2,0.8) · neutral(0.0,0.5) · anger(−0.8,0.8) · fear(−0.7,0.8) · sadness(−0.7,0.3) · disgust(−0.6,0.5) · frustration(−0.5,0.6) · contempt(−0.5,0.4). Valence −1..1, arousal 0..1.
- **modelConfidence = 1 − H/H_max**, H = dağılım entropisi (`−Σ p·ln p`), H_max = `ln(11)`. Düşük entropi (kararlı dağılım) → yüksek güven. `[0,1]`.
- **Girdi uzunluk sınırı:** XLM-R pozisyonel embedding 512. Ham piece id'leri **510** ile kırpılır (BOS+EOS ile 512). Uzun transkript ONNX'i patlatmaz.
- **ExecutionProvider:** `coreml` (macOS, CPU fallback) | `cpu`; 04a `TryAppendCoreML` deseni aynen.
- **Gerçek-model / gerçek-tokenizer testleri** env yoksa `Xunit.SkippableFact`/`Skip.If` ile atlanır (04a örüntüsü); varsayılan `dotnet test` modelsiz yeşil kalır.

### Mevcut tipler (tüketilir, DEĞİŞTİRİLMEZ)

```csharp
// Core (SentimentAnalysis.Core/Signals.cs)
public enum DetectedLanguage { Turkish, English, Other }
public sealed record Transcript(string Text, DetectedLanguage Language, double AvgLogProb);
public sealed record TextEmotion(ValenceArousal Va, double ModelConfidence);
public readonly record struct ValenceArousal(double Valence, double Arousal) { static ValenceArousal Clamp(double v, double a); static ValenceArousal Neutral { get; } }
// Application.Abstractions
public interface ITextEmotionModel { Task<TextEmotion> AnalyzeAsync(Transcript transcript, CancellationToken ct); }
```

### Mevcut 04a altyapısı (yeniden kullanılır, additive genişletilir)

```csharp
// EmotionConfig (Application/Configuration/AppConfig.cs) — 04a'da eklendi; 04b YENİ ALANLAR ekler (defaultlu, additive):
//   mevcut: ProsodyModelFile, ModelDirectory, ExecutionProvider
public interface IEmotionModelProvider { string ResolveModelPath(string fileName); } // 04a; metin de kullanır
public sealed class FileEmotionModelProvider : IEmotionModelProvider { FileEmotionModelProvider(AppConfig); ... } // 04a
public static class EmotionServiceCollectionExtensions { IServiceCollection AddProsodyEmotion(this IServiceCollection); } // 04a; 04b AddTextEmotion ekler
```

### Microsoft.ML.Tokenizers 2.0.0 API (MS Learn ile teyit 2026-07-25)

```csharp
using Microsoft.ML.Tokenizers;
// Model stream (sentencepiece.bpe.model). Create'de BOS/EOS DEFAULT true → KAPAT.
// DİKKAT: Create parametre adı MS docs'ta `addBeginOfSentence` / `addEndOfSentence`
// (spec taslağı `addBeginningOfSentence` yazmış). Doğru adı IntelliSense/derleyici teyit eder;
// yanlışsa DERLENMEZ (güvenli). EncodeToIds(text) ham piece id listesi (IReadOnlyList<int>) döner.
using var spmStream = File.OpenRead(spmPath);
SentencePieceTokenizer tok = SentencePieceTokenizer.Create(
    spmStream, addBeginOfSentence: false, addEndOfSentence: false);
IReadOnlyList<int> rawIds = tok.EncodeToIds(text);
```

### ONNX Runtime C# — int64 iki-girdili (context7 /microsoft/onnxruntime, 04a deseni)

```csharp
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
var idTensor   = new DenseTensor<long>(inputIds,      new[] { 1, inputIds.Length });
var maskTensor = new DenseTensor<long>(attentionMask, new[] { 1, attentionMask.Length });
var inputs = new[]
{
    NamedOnnxValue.CreateFromTensor("input_ids", idTensor),
    NamedOnnxValue.CreateFromTensor("attention_mask", maskTensor),
};
using var results = session.Run(inputs);
float[] logits = results.First(r => r.Name == "logits").AsTensor<float>().ToArray(); // [11]
```

---

### Task 1: `XlmrEmotionLabels` + `TextVaLookup` (saf: softmax + VA ortalama + entropi-confidence)

**Files:**
- Create: `src/SentimentAnalysis.Infrastructure.Emotion/XlmrEmotionLabels.cs`
- Create: `src/SentimentAnalysis.Infrastructure.Emotion/TextVaLookup.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Emotion.Tests/TextVaLookupTests.cs`

**Interfaces:**
- Consumes: `ValenceArousal`, `TextEmotion` (Core)
- Produces:
  - `static class XlmrEmotionLabels { IReadOnlyList<string> Ordered { get; } }` (11 etiket, model `config.json` id2label sırası — başlangıç: spec tablosu sırası).
  - `static class TextVaLookup { TextEmotion Map(IReadOnlyList<float> logits, IReadOnlyList<string> labels); IReadOnlyDictionary<string, ValenceArousal> Table { get; } }` — softmax(logits) → olasılık-ağırlıklı VA + entropi-confidence. `labels[i]` VA tablosunda yoksa `ArgumentException`. `logits.Count != labels.Count` ise `ArgumentException`.

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Emotion.Tests/TextVaLookupTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class TextVaLookupTests
{
    private static IReadOnlyList<string> Labels => XlmrEmotionLabels.Ordered;

    private static float[] OneHotLogits(string emotion, float hot = 20f)
    {
        // Seçilen duyguya çok büyük logit → softmax ~ tek-nokta dağılım.
        return Labels.Select(l => l == emotion ? hot : 0f).ToArray();
    }

    [Fact]
    public void Table_HasElevenEmotions_WithExpectedRanges()
    {
        Assert.Equal(11, TextVaLookup.Table.Count);
        Assert.Equal(11, XlmrEmotionLabels.Ordered.Count);
        foreach (var (name, va) in TextVaLookup.Table)
        {
            Assert.InRange(va.Valence, -1.0, 1.0);
            Assert.InRange(va.Arousal, 0.0, 1.0);
            Assert.Contains(name, XlmrEmotionLabels.Ordered); // etiket ↔ tablo tutarlı
        }
    }

    [Fact]
    public void Map_PeakedJoy_ReturnsJoyVa_HighConfidence()
    {
        var e = TextVaLookup.Map(OneHotLogits("joy"), Labels);
        Assert.Equal(0.8, e.Va.Valence, precision: 2);
        Assert.Equal(0.7, e.Va.Arousal, precision: 2);
        Assert.True(e.ModelConfidence > 0.95, $"kararlı dağılım → yüksek güven, {e.ModelConfidence}");
    }

    [Fact]
    public void Map_PeakedAnger_NegativeValence_HighArousal()
    {
        var e = TextVaLookup.Map(OneHotLogits("anger"), Labels);
        Assert.True(e.Va.Valence < -0.5, $"anger negatif valence, {e.Va.Valence}");
        Assert.True(e.Va.Arousal > 0.6, $"anger yüksek arousal, {e.Va.Arousal}");
    }

    [Fact]
    public void Map_UniformLogits_LowConfidence_AveragedVa()
    {
        var uniform = Labels.Select(_ => 1.0f).ToArray();
        var e = TextVaLookup.Map(uniform, Labels);
        Assert.True(e.ModelConfidence < 0.05, $"düz dağılım → düşük güven, {e.ModelConfidence}");
        // Ortalama VA: 11 duygunun aritmetik ortası, ~nötr civarı.
        double meanV = TextVaLookup.Table.Values.Average(v => v.Valence);
        Assert.Equal(meanV, e.Va.Valence, precision: 3);
        Assert.InRange(e.Va.Valence, -1.0, 1.0);
        Assert.InRange(e.Va.Arousal, 0.0, 1.0);
    }

    [Fact]
    public void Map_ThrowsOnLengthMismatch()
    {
        Assert.Throws<ArgumentException>(() => TextVaLookup.Map(new float[] { 1f, 2f }, Labels));
    }

    [Fact]
    public void Map_ThrowsOnUnknownLabel()
    {
        var labels = Labels.Take(10).Append("bilinmeyen").ToArray();
        var logits = labels.Select(_ => 1f).ToArray();
        Assert.Throws<ArgumentException>(() => TextVaLookup.Map(logits, labels));
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Emotion.Tests`
Expected: FAIL (`XlmrEmotionLabels`/`TextVaLookup` yok — derlenmez).

- [ ] **Step 3: `XlmrEmotionLabels`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Emotion/XlmrEmotionLabels.cs`:

```csharp
namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>tabularisai/multilingual-emotion-classification'ın çıktı etiketleri, `config.json`
/// `id2label` SIRASINDA (logits[i] ↔ Ordered[i]). BAŞLANGIÇ sırası spec §04b tablosudur;
/// gerçek model indirildiğinde entegrasyon testi (Task 3) sıra + uzunluğu (11) doğrular.
/// Kesin model/etiketler → Alt-Proje 09 kilitler.</summary>
public static class XlmrEmotionLabels
{
    public static IReadOnlyList<string> Ordered { get; } = new[]
    {
        "joy", "love", "gratitude", "surprise", "neutral",
        "anger", "fear", "sadness", "disgust", "frustration", "contempt",
    };
}
```

- [ ] **Step 4: `TextVaLookup`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Emotion/TextVaLookup.cs`:

```csharp
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>XLM-R 11-duygu logit'lerini domain <see cref="TextEmotion"/>'a eşler: softmax →
/// olasılık-ağırlıklı VA ortalaması + entropi-tabanlı güven. VA tablosu etiket ADIYLA anahtarlı
/// (index sırası hatası VA'yı sessizce bozamaz). VA değerleri BAŞLANGIÇ (spec §04b); Alt-Proje 09
/// Türkçe veriyle kalibre eder — keyfi değil.</summary>
public static class TextVaLookup
{
    public static IReadOnlyDictionary<string, ValenceArousal> Table { get; } =
        new Dictionary<string, ValenceArousal>
        {
            ["joy"]         = new(+0.8, 0.7),
            ["love"]        = new(+0.9, 0.5),
            ["gratitude"]   = new(+0.7, 0.4),
            ["surprise"]    = new(+0.2, 0.8),
            ["neutral"]     = new( 0.0, 0.5),
            ["anger"]       = new(-0.8, 0.8),
            ["fear"]        = new(-0.7, 0.8),
            ["sadness"]     = new(-0.7, 0.3),
            ["disgust"]     = new(-0.6, 0.5),
            ["frustration"] = new(-0.5, 0.6),
            ["contempt"]    = new(-0.5, 0.4),
        };

    /// <summary>logits → TextEmotion. labels[i] logits[i]'nin duygusudur (model id2label sırası).</summary>
    public static TextEmotion Map(IReadOnlyList<float> logits, IReadOnlyList<string> labels)
    {
        if (logits.Count != labels.Count)
            throw new ArgumentException(
                $"logits ({logits.Count}) ile labels ({labels.Count}) uzunluğu eşleşmiyor.", nameof(logits));
        if (logits.Count == 0)
            throw new ArgumentException("Boş logits.", nameof(logits));

        var probs = Softmax(logits);

        double valence = 0, arousal = 0, entropy = 0;
        for (int i = 0; i < probs.Length; i++)
        {
            if (!Table.TryGetValue(labels[i], out var va))
                throw new ArgumentException($"VA tablosunda etiket yok: '{labels[i]}'.", nameof(labels));
            valence += probs[i] * va.Valence;
            arousal += probs[i] * va.Arousal;
            if (probs[i] > 0) entropy -= probs[i] * Math.Log(probs[i]);
        }

        double maxEntropy = Math.Log(probs.Length);          // düzgün dağılımın entropisi
        double confidence = maxEntropy > 0
            ? Math.Clamp(1.0 - entropy / maxEntropy, 0.0, 1.0)
            : 1.0;

        return new TextEmotion(ValenceArousal.Clamp(valence, arousal), confidence);
    }

    private static double[] Softmax(IReadOnlyList<float> logits)
    {
        double max = double.NegativeInfinity;
        for (int i = 0; i < logits.Count; i++) if (logits[i] > max) max = logits[i];

        var exp = new double[logits.Count];
        double sum = 0;
        for (int i = 0; i < logits.Count; i++)
        {
            exp[i] = Math.Exp(logits[i] - max);              // sayısal kararlılık için max çıkar
            sum += exp[i];
        }
        for (int i = 0; i < exp.Length; i++) exp[i] /= sum;
        return exp;
    }
}
```

- [ ] **Step 5: Testin geçtiğini doğrula**

Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Emotion.Tests`
Expected: PASS (yeni 6 test; mevcut 04a testleri kırılmaz).

---

### Task 2: `EmotionConfig` genişletme + `XlmrInputBuilder` (saf) + `XlmrTokenizerAdapter` + tokenizer entegrasyon testi

**Files:**
- Modify: `src/SentimentAnalysis.Application/Configuration/AppConfig.cs` (`EmotionConfig`'e `TextModelFile`, `TokenizerFile` ekle — additive, defaultlu)
- Create: `src/SentimentAnalysis.Infrastructure.Emotion/XlmrInputBuilder.cs`
- Create: `src/SentimentAnalysis.Infrastructure.Emotion/XlmrTokenizerAdapter.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Emotion.Tests/XlmrInputBuilderTests.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Emotion.Tests/XlmrTokenizerAdapterTests.cs`

**Interfaces:**
- Consumes: `EmotionConfig`, `IEmotionModelProvider` (04a)
- Produces:
  - `EmotionConfig`'e `string TextModelFile = "text-emotion-xlmr.onnx"`, `string TokenizerFile = "sentencepiece.bpe.model"` (additive, defaultlu).
  - `readonly record struct XlmrInput(long[] InputIds, long[] AttentionMask)`.
  - `static class XlmrInputBuilder { XlmrInput Build(IReadOnlyList<int> rawPieceIds, int maxTokens = 512); const int BosId = 0; const int EosId = 2; const int FairseqOffset = 1; }` — spec §04b reçetesi (offset+BOS+EOS+kırpma), saf.
  - `sealed class XlmrTokenizerAdapter : IDisposable { XlmrTokenizerAdapter(Stream spmStream); XlmrInput Encode(string text, int maxTokens = 512); }` — SentencePiece sarmalayıcı; `XlmrInputBuilder`'ı besler.

- [ ] **Step 1: `EmotionConfig`'i genişlet (additive)**

`src/SentimentAnalysis.Application/Configuration/AppConfig.cs` — `EmotionConfig` kaydına **iki yeni defaultlu alan** ekle (04a'nın mevcut alanlarını KORU). Son hali:

```csharp
/// <summary>Duygu modelleri (prozodi + metin) ayarları (spec 04).</summary>
public sealed record EmotionConfig(
    string ProsodyModelFile = "prosody-msp-dim.onnx",       // 04a
    string? ModelDirectory = null,                          // 04a
    string ExecutionProvider = "coreml",                    // 04a
    string TextModelFile = "text-emotion-xlmr.onnx",        // 04b — XLM-R 11-duygu ONNX
    string TokenizerFile = "sentencepiece.bpe.model");      // 04b — XLM-R-base SentencePiece (~5 MB)
```

> Not: Additive (yeni alanlar defaultlu, sona eklendi). `AppConfig.Default` ve `... with { Emotion = ... with { ... } }` çağrıları kırılmaz. Değişiklikten önce `grep -rn "new EmotionConfig(" src tests` ile pozisyonel kurucu çağrısı olmadığını teyit et (yalnızca `EmotionConfig()` / `with` beklenir).

- [ ] **Step 2: `XlmrInputBuilder` için başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Emotion.Tests/XlmrInputBuilderTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class XlmrInputBuilderTests
{
    [Fact]
    public void Build_AddsBosOffsetEos()
    {
        // Ham SP id'leri → +1 offset, başa <s>=0, sona </s>=2.
        var input = XlmrInputBuilder.Build(new[] { 10, 20, 30 });
        Assert.Equal(new long[] { 0, 11, 21, 31, 2 }, input.InputIds);
        Assert.Equal(new long[] { 1, 1, 1, 1, 1 }, input.AttentionMask);
    }

    [Fact]
    public void Build_EmptyText_JustBosEos()
    {
        var input = XlmrInputBuilder.Build(System.Array.Empty<int>());
        Assert.Equal(new long[] { 0, 2 }, input.InputIds);
        Assert.Equal(new long[] { 1, 1 }, input.AttentionMask);
    }

    [Fact]
    public void Build_TruncatesToMaxTokens()
    {
        // 600 ham id + BOS + EOS → 512'ye kırpılmalı (510 ham + 2 özel).
        var raw = Enumerable.Range(5, 600).ToList();
        var input = XlmrInputBuilder.Build(raw, maxTokens: 512);
        Assert.Equal(512, input.InputIds.Length);
        Assert.Equal(0, input.InputIds[0]);                       // <s>
        Assert.Equal(2, input.InputIds[^1]);                      // </s>
        Assert.Equal(510, input.InputIds.Skip(1).SkipLast(1).Count());
        Assert.All(input.AttentionMask, m => Assert.Equal(1, m));
        Assert.Equal(512, input.AttentionMask.Length);
    }

    [Fact]
    public void Build_MaskMatchesIdsLength()
    {
        var input = XlmrInputBuilder.Build(new[] { 7, 8 });
        Assert.Equal(input.InputIds.Length, input.AttentionMask.Length);
    }
}
```

- [ ] **Step 3: Başarısız olduğunu doğrula** — Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Emotion.Tests`. Expected: FAIL (`XlmrInputBuilder` yok).

- [ ] **Step 4: `XlmrInputBuilder`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Emotion/XlmrInputBuilder.cs`:

```csharp
namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>Tek örnek için ONNX girdisi.</summary>
public readonly record struct XlmrInput(long[] InputIds, long[] AttentionMask);

/// <summary>Ham SentencePiece piece id'lerini XLM-R `input_ids`/`attention_mask`'ine çevirir
/// (spec §04b, spike ile doğrulandı): +1 fairseq offset, başa &lt;s&gt;=0, sona &lt;/s&gt;=2,
/// pozisyonel sınır için ham id'ler maxTokens-2'ye kırpılır. attention_mask hepsi 1 (padding yok). Saf.</summary>
public static class XlmrInputBuilder
{
    public const int BosId = 0;         // XLM-R <s>
    public const int EosId = 2;         // XLM-R </s>
    public const int FairseqOffset = 1; // sentencepiece → fairseq vocab kaydırması

    public static XlmrInput Build(IReadOnlyList<int> rawPieceIds, int maxTokens = 512)
    {
        int maxRaw = System.Math.Max(0, maxTokens - 2);       // BOS + EOS payı
        int count = System.Math.Min(rawPieceIds.Count, maxRaw);

        var ids = new long[count + 2];
        ids[0] = BosId;
        for (int i = 0; i < count; i++) ids[i + 1] = rawPieceIds[i] + FairseqOffset;
        ids[^1] = EosId;

        var mask = new long[ids.Length];
        for (int i = 0; i < mask.Length; i++) mask[i] = 1;    // tek örnek, padding yok

        return new XlmrInput(ids, mask);
    }
}
```

- [ ] **Step 5: `XlmrInputBuilder` testlerinin geçtiğini doğrula** — Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Emotion.Tests`. Expected: PASS.

- [ ] **Step 6: Tokenizer paketini ekle**

```bash
cd /Users/betulfiliz/sentiment-analysis
dotnet add src/SentimentAnalysis.Infrastructure.Emotion package Microsoft.ML.Tokenizers --version 2.0.0
```

- [ ] **Step 7: `XlmrTokenizerAdapter` için env-gated başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Emotion.Tests/XlmrTokenizerAdapterTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class XlmrTokenizerAdapterTests
{
    // Gerçek SentencePiece modeli (~5 MB) olmadan tokenize edilemez → env-gated.
    // XLMR_SPM_PATH = XLM-R-base sentencepiece.bpe.model dosyası.
    [SkippableFact]
    public void Encode_ProducesBosEosWrappedIds()
    {
        var spm = Environment.GetEnvironmentVariable("XLMR_SPM_PATH");
        Skip.If(string.IsNullOrWhiteSpace(spm) || !File.Exists(spm),
            "XLMR_SPM_PATH ayarlı değil; gerçek-tokenizer testi atlandı.");

        using var stream = File.OpenRead(spm!);
        using var adapter = new XlmrTokenizerAdapter(stream);
        var input = adapter.Encode("Bugün çok mutluyum.");

        Assert.Equal(0, input.InputIds[0]);                   // <s>
        Assert.Equal(2, input.InputIds[^1]);                  // </s>
        Assert.True(input.InputIds.Length > 2, "en az bir gerçek token beklenir");
        Assert.Equal(input.InputIds.Length, input.AttentionMask.Length);
        Assert.All(input.AttentionMask, m => Assert.Equal(1, m));
        // Spike referansı: XLM-R iç token'ları > 3 (özel token bölgesi 0..3'ün ötesinde).
        Assert.All(input.InputIds.Skip(1).SkipLast(1), id => Assert.True(id > 3, $"beklenmedik özel token {id}"));
    }
}
```

- [ ] **Step 8: Başarısız/Skip olduğunu doğrula** — Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Emotion.Tests`. Expected: `XlmrTokenizerAdapter` yoksa FAIL (derlenmez); tip eklendikten sonra env yoksa **Skipped**.

- [ ] **Step 9: `XlmrTokenizerAdapter`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Emotion/XlmrTokenizerAdapter.cs`:

```csharp
using Microsoft.ML.Tokenizers;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>XLM-R SentencePiece tokenizer sarmalayıcısı (spec §04b reçetesi). BOS/EOS'u SentencePiece
/// seviyesinde KAPATIR; XLM-R özel token'larını (<s>/</s>) + fairseq offset'i <see cref="XlmrInputBuilder"/>
/// deterministik ekler. Tokenizer bir kez yüklenir (motor tarafından singleton tutulur).</summary>
public sealed class XlmrTokenizerAdapter : IDisposable
{
    private readonly SentencePieceTokenizer _tokenizer;
    private bool _disposed;

    /// <param name="spmStream">XLM-R-base `sentencepiece.bpe.model` içeriği.</param>
    public XlmrTokenizerAdapter(Stream spmStream)
    {
        // BOS/EOS kapalı → EncodeToIds ham SentencePiece piece id'leri verir; özel token'ları biz ekleriz.
        // NOT: Create parametre adları MS docs'ta addBeginOfSentence/addEndOfSentence. Derleyici teyit eder.
        _tokenizer = SentencePieceTokenizer.Create(
            spmStream, addBeginOfSentence: false, addEndOfSentence: false);
    }

    public XlmrInput Encode(string text, int maxTokens = 512)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var raw = _tokenizer.EncodeToIds(text ?? string.Empty);
        return XlmrInputBuilder.Build(raw, maxTokens);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        (_tokenizer as IDisposable)?.Dispose();   // sürüme göre IDisposable olabilir/olmayabilir
    }
}
```

- [ ] **Step 10: Tam çözümün derlendiğini + testlerin geçtiğini doğrula**

Run: `dotnet build && dotnet test`
Expected: Build 0 uyarı; `XlmrInputBuilder` testleri PASS; tokenizer entegrasyon testi **Skipped** (XLMR_SPM_PATH yok); mevcut testler (01/02/03/04a) kırılmaz.

---

### Task 3: `OnnxTextEmotionModel` motoru + guard + gerçek-model entegrasyon testi

**Files:**
- Create: `src/SentimentAnalysis.Infrastructure.Emotion/OnnxTextEmotionModel.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Emotion.Tests/OnnxTextEmotionModelTests.cs`

**Interfaces:**
- Consumes: `ITextEmotionModel`, `Transcript`, `TextEmotion` (Core/Application), `EmotionConfig`, `IEmotionModelProvider` (04a), `XlmrTokenizerAdapter`, `XlmrInputBuilder`, `TextVaLookup`, `XlmrEmotionLabels`; Microsoft.ML.OnnxRuntime
- Produces: `sealed class OnnxTextEmotionModel : ITextEmotionModel, IDisposable` (ctor `(AppConfig, IEmotionModelProvider)`). Session + tokenizer lazy, bir kez yüklenir (sıcak singleton).

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Emotion.Tests/OnnxTextEmotionModelTests.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class OnnxTextEmotionModelTests
{
    private sealed class FixedModelProvider(string dir) : IEmotionModelProvider
    {
        public string ResolveModelPath(string fileName) => Path.Combine(dir, fileName);
    }

    private sealed class UnusedModelProvider : IEmotionModelProvider
    {
        public string ResolveModelPath(string fileName) =>
            throw new InvalidOperationException("dispose sonrası model çözülmemeliydi");
    }

    [Fact]
    public async Task AnalyzeAsync_AfterDispose_Throws()
    {
        var model = new OnnxTextEmotionModel(AppConfig.Default, new UnusedModelProvider());
        model.Dispose();
        var t = new Transcript("merhaba", DetectedLanguage.Turkish, -0.2);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.AnalyzeAsync(t, CancellationToken.None));
    }

    [SkippableFact]
    public async Task AnalyzeAsync_RealModel_ProducesValenceArousal()
    {
        // TEXT_MODEL_DIR = hem text-emotion-xlmr.onnx hem sentencepiece.bpe.model'i içeren klasör.
        var dir = Environment.GetEnvironmentVariable("TEXT_MODEL_DIR");
        Skip.If(string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir),
            "TEXT_MODEL_DIR ayarlı değil; gerçek-model testi atlandı.");
        Skip.If(!File.Exists(Path.Combine(dir!, "text-emotion-xlmr.onnx")) ||
                !File.Exists(Path.Combine(dir!, "sentencepiece.bpe.model")),
            "text-emotion-xlmr.onnx / sentencepiece.bpe.model bulunamadı.");

        var cfg = AppConfig.Default with { Emotion = AppConfig.Default.Emotion with { ExecutionProvider = "cpu" } };
        using var model = new OnnxTextEmotionModel(cfg, new FixedModelProvider(dir!));

        var positive = new Transcript("Bugün harika bir gündü, çok mutluyum!", DetectedLanguage.Turkish, -0.15);
        var negative = new Transcript("Bu durumdan gerçekten çok korkuyorum ve üzgünüm.", DetectedLanguage.Turkish, -0.15);

        var pe = await model.AnalyzeAsync(positive, CancellationToken.None);
        var ne = await model.AnalyzeAsync(negative, CancellationToken.None);

        Assert.InRange(pe.Va.Valence, -1.0, 1.0);
        Assert.InRange(pe.Va.Arousal, 0.0, 1.0);
        Assert.InRange(pe.ModelConfidence, 0.0, 1.0);
        // Yön kontrolü: olumlu metin olumsuzdan daha yüksek valence vermeli (model çalışıyor).
        Assert.True(pe.Va.Valence > ne.Va.Valence,
            $"olumlu ({pe.Va.Valence:F3}) > olumsuz ({ne.Va.Valence:F3}) beklenir");
    }
}
```

- [ ] **Step 2: Başarısız/Skip olduğunu doğrula** — Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Emotion.Tests`. Expected: `OnnxTextEmotionModel` yok → FAIL (derlenmez). (Gerçek-model testi env yoksa Skip.)

- [ ] **Step 3: `OnnxTextEmotionModel`'i yaz**

`src/SentimentAnalysis.Infrastructure.Emotion/OnnxTextEmotionModel.cs`:

```csharp
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>XLM-R çok-dilli 11-duygu sınıflandırıcıyı ONNX Runtime ile çalıştırır. Session + SentencePiece
/// tokenizer açılışta bir kez yüklenir, sıcak tutulur (singleton). Boru: transcript → tokenize (spec §04b) →
/// input_ids/attention_mask (int64) → logits[1,11] → <see cref="TextVaLookup"/> → TextEmotion.</summary>
public sealed class OnnxTextEmotionModel : ITextEmotionModel, IDisposable
{
    private const string InputIdsName = "input_ids";
    private const string AttentionMaskName = "attention_mask";
    private const string OutputName = "logits";

    private readonly EmotionConfig _cfg;
    private readonly IEmotionModelProvider _models;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private InferenceSession? _session;
    private XlmrTokenizerAdapter? _tokenizer;
    private bool _disposed;

    public OnnxTextEmotionModel(AppConfig config, IEmotionModelProvider models)
    {
        _cfg = config.Emotion;
        _models = models;
    }

    public async Task<TextEmotion> AnalyzeAsync(Transcript transcript, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var input = _tokenizer!.Encode(transcript.Text);

        await _runLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var idTensor = new DenseTensor<long>(input.InputIds, new[] { 1, input.InputIds.Length });
            var maskTensor = new DenseTensor<long>(input.AttentionMask, new[] { 1, input.AttentionMask.Length });
            var inputs = new[]
            {
                NamedOnnxValue.CreateFromTensor(InputIdsName, idTensor),
                NamedOnnxValue.CreateFromTensor(AttentionMaskName, maskTensor),
            };
            using var results = _session!.Run(inputs);
            var logits = results.First(r => r.Name == OutputName).AsTensor<float>().ToArray();
            return TextVaLookup.Map(logits, XlmrEmotionLabels.Ordered);
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_session is not null && _tokenizer is not null) return;
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_tokenizer is null)
            {
                var spmPath = _models.ResolveModelPath(_cfg.TokenizerFile);
                using var spmStream = File.OpenRead(spmPath);
                _tokenizer = new XlmrTokenizerAdapter(spmStream);
            }
            if (_session is null)
            {
                var modelPath = _models.ResolveModelPath(_cfg.TextModelFile);
                using var options = new SessionOptions();
                if (string.Equals(_cfg.ExecutionProvider, "coreml", StringComparison.OrdinalIgnoreCase))
                    TryAppendCoreML(options);
                _session = new InferenceSession(modelPath, options);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>CoreML EP; bu ORT sürümünde/makinede yoksa sessizce CPU'ya düşer (correctness CPU'da da sağlanır).</summary>
    private static void TryAppendCoreML(SessionOptions options)
    {
        try { options.AppendExecutionProvider_CoreML(); }
        catch { /* CoreML yoksa CPU fallback */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Devam eden Init/Run biterken native kaynakları serbest bırak (04a deseni).
        _initLock.Wait();
        try
        {
            _runLock.Wait();
            try
            {
                _session?.Dispose();
                _tokenizer?.Dispose();
            }
            finally { _runLock.Release(); }
        }
        finally
        {
            _initLock.Release();
            _initLock.Dispose();
            _runLock.Dispose();
        }
    }
}
```

- [ ] **Step 4: Guard testinin geçtiğini doğrula (entegrasyon Skip)** — Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Emotion.Tests`. Expected: `AnalyzeAsync_AfterDispose_Throws` PASS; gerçek-model testi **Skipped**.

- [ ] **Step 5: (Model varsa) gerçek modelle bir kez doğrula**

Model dosyaları bu session'da yoksa ATLA (Skip beklenir — beklenen davranış). Varsa:

```bash
export TEXT_MODEL_DIR="/path/to/xlmr-emotion"   # text-emotion-xlmr.onnx + sentencepiece.bpe.model
dotnet test tests/SentimentAnalysis.Infrastructure.Emotion.Tests --filter "FullyQualifiedName~OnnxTextEmotionModelTests.AnalyzeAsync_RealModel"
unset TEXT_MODEL_DIR
```
Expected: PASS — olumlu metnin valence'ı olumsuzdan yüksek; hepsi aralıkta. `logits.Length == 11` teyit (değilse `XlmrEmotionLabels.Ordered`/model uyuşmuyor → rapora yaz, Alt-Proje 09'a taşı). Sonucu ledger'a yaz.

---

### Task 4: `AddTextEmotion()` DI + tam çözüm doğrulaması

**Files:**
- Modify: `src/SentimentAnalysis.Infrastructure.Emotion/EmotionServiceCollectionExtensions.cs` (04a; `AddTextEmotion` ekle)
- Create: `tests/SentimentAnalysis.Infrastructure.Emotion.Tests/TextEmotionDiTests.cs`

**Interfaces:**
- Produces: `static class EmotionServiceCollectionExtensions { IServiceCollection AddTextEmotion(this IServiceCollection); }` (04a `AddProsodyEmotion`'ın yanına). Singleton kayıt; `IEmotionModelProvider` 04a ile paylaşılır (çift kayıt sorunu yok — `TryAddSingleton`).

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Emotion.Tests/TextEmotionDiTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class TextEmotionDiTests
{
    [Fact]
    public void AddTextEmotion_ResolvesTextModelAndProvider()
    {
        var provider = new ServiceCollection()
            .AddApplicationCore()
            .AddTextEmotion()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ITextEmotionModel>());
        Assert.NotNull(provider.GetRequiredService<IEmotionModelProvider>());
    }

    [Fact]
    public void AddProsodyAndText_BothResolve_SingleProvider()
    {
        var provider = new ServiceCollection()
            .AddApplicationCore()
            .AddProsodyEmotion()
            .AddTextEmotion()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IProsodyEmotionModel>());
        Assert.NotNull(provider.GetRequiredService<ITextEmotionModel>());
        // IEmotionModelProvider iki çağrıda da kaydedilir ama tek instance çözülür (TryAdd).
        Assert.Single(provider.GetServices<IEmotionModelProvider>());
    }
}
```

- [ ] **Step 2: Başarısız olduğunu doğrula** — Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Emotion.Tests`. Expected: FAIL (`AddTextEmotion` yok).

- [ ] **Step 3: `AddTextEmotion()`'ı ekle ve provider kaydını `TryAddSingleton`'a çevir**

`src/SentimentAnalysis.Infrastructure.Emotion/EmotionServiceCollectionExtensions.cs` — mevcut `AddProsodyEmotion`'ı KORU; `IEmotionModelProvider` kayıtlarını `TryAddSingleton` yap (iki uzantı birlikte çağrılınca çift kayıt olmasın), `AddTextEmotion` ekle:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;   // TryAddSingleton
using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>Duygu modelleri DI kaydı. Singleton (model + tokenizer açılışta bir kez, sıcak).
/// `AddApplicationCore()` ile kullanılır. Prozodi (04a) ve metin (04b) bağımsız eklenebilir;
/// ortak <see cref="IEmotionModelProvider"/> TryAdd ile tek kez kaydedilir.</summary>
public static class EmotionServiceCollectionExtensions
{
    public static IServiceCollection AddProsodyEmotion(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmotionModelProvider, FileEmotionModelProvider>();
        services.AddSingleton<IProsodyEmotionModel, OnnxProsodyEmotionModel>();
        return services;
    }

    public static IServiceCollection AddTextEmotion(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmotionModelProvider, FileEmotionModelProvider>();
        services.AddSingleton<ITextEmotionModel, OnnxTextEmotionModel>();
        return services;
    }
}
```

> Not: 04a `AddProsodyEmotion` `AddSingleton<IEmotionModelProvider,...>` kullanıyordu; `TryAddSingleton`'a çevirmek davranışı korur (tek kayıtta aynı) ve iki uzantı birlikte çağrılınca çift kaydı önler. 04a DI testi (`AddProsodyEmotion_ResolvesModelAndProvider`) kırılmaz.

- [ ] **Step 4: Tam çözümün derlendiğini ve testlerin geçtiğini doğrula**

Run: `dotnet build && dotnet test`
Expected: Build **0 uyarı**; **tüm** testler PASS (01/02/03/04a + 04b yeni testleri); gerçek-model/tokenizer testleri **Skipped** (TEXT_MODEL_DIR / XLMR_SPM_PATH yok). Toplam: 86 (mevcut) + yeni birim testler geçer; env-gated skip sayısı 2 → 4'e çıkar (STT, prozodi, tokenizer, metin).

---

## Self-Review (plan yazarı tarafından)

**1. Spec coverage (§04b):**
- Model tabularisai XLM-R 11-duygu → `XlmrEmotionLabels` + `TextVaLookup.Table` + Task 3 motor. ✔
- Ayrık sınıflandırma + VA lookup (doğrudan VA-regresyon değil) → `TextVaLookup` olasılık-ağırlıklı ortalama. ✔
- Akış transcript → SP tokenize → ONNX → softmax → VA lookup → TextEmotion → Task 2 (tokenize) + Task 3 (ONNX+map). ✔
- modelConfidence = entropi → `TextVaLookup` `1 − H/ln(11)`. ✔
- Tokenizer reçetesi (Create BOS/EOS kapalı, +1 offset, <s>=0/</s>=2, mask=1) → `XlmrInputBuilder` (saf, testli) + `XlmrTokenizerAdapter`. ✔
- Önerilen mimari (saf `XlmrTokenizerAdapter`+`TextVaLookup`+entropi + `OnnxTextEmotionModel` singleton, `IEmotionModelProvider` reuse, `AddTextEmotion` DI, int64 tensör) → birebir uygulandı. ✔
- ONNX-export edilebilirlik kabul kriteri → standart XLMRobertaForSequenceClassification (temiz export); export hattı spec'te (HF Optimum), model dosyası build-time üretilir. ✔

**2. Kapsam dışı (bilinçli):** ONNX export scriptleri (build-time, repo dışı — model dosyası indiriliyor/üretiliyor, runtime değil); INT8 quantization (optimizasyon, 06/09); İngilizce tune (D5, sonra); sidecar kontratı (04 spec, ertelendi); kesin VA kalibrasyonu + model kilidi → Alt-Proje 09.

**3. Placeholder scan:** TODO/TBD yok. VA değerleri ve etiket sırası "BAŞLANGIÇ (eval 09 kalibre)" olarak açıkça işaretli — sahte-kesinlik yok. Entegrasyon testleri gerçek env-gated Skip (sahte-yeşil değil). Tüm kod blokları tam.

**4. Type consistency:**
- `XlmrInput(long[] InputIds, long[] AttentionMask)` — Task 2 tanımlar, Task 3 kullanır (`input.InputIds`/`input.AttentionMask`). ✔
- `XlmrInputBuilder.Build(rawPieceIds, maxTokens)` — Task 2; `XlmrTokenizerAdapter.Encode` çağırır. ✔
- `TextVaLookup.Map(IReadOnlyList<float>, IReadOnlyList<string>)` — Task 1; Task 3 `TextVaLookup.Map(logits, XlmrEmotionLabels.Ordered)`. `logits` `float[]` → `IReadOnlyList<float>` uyumlu. ✔
- `XlmrEmotionLabels.Ordered` (11) ↔ `TextVaLookup.Table` (11) anahtarları birebir aynı 11 ad. ✔
- `OnnxTextEmotionModel(AppConfig, IEmotionModelProvider)` ctor → Task 3 test + Task 4 DI tutarlı. ✔
- `EmotionConfig` yeni alanları `TextModelFile`/`TokenizerFile` → Task 2 tanım, Task 3 `_cfg.TextModelFile`/`_cfg.TokenizerFile` kullanımı tutarlı. ✔
- `AddApplicationCore()` (Application) + `AddProsodyEmotion`/`AddTextEmotion` — 04a ile aynı ad. ✔

**5. Mimari:** Infrastructure.Emotion → Application → Core. Native tipler (SentencePieceTokenizer, InferenceSession) `XlmrTokenizerAdapter`/`OnnxTextEmotionModel` sınırında; `TextVaLookup`/`XlmrInputBuilder` saf + tam unit-testli (modelsiz). ✔

**6. Eşzamanlılık/kaynak:** iki semaphore (init/run) + `ObjectDisposedException` guard'ları (04a Post-Review dersinden); `Dispose` init+run kilidini alıp native session+tokenizer'ı serbest bırakır; CancellationToken akar. ✔

**Riskler / dikkat:**
- **Etiket sırası (label order):** `XlmrEmotionLabels.Ordered` model `config.json` id2label ile birebir olmalı; VA tablosu ADIYLA anahtarlı olduğu için sıra hatası yanlış-VA verir ama crash etmez → gerçek-model testinde `logits.Length==11` + yön kontrolü (olumlu>olumsuz) yakalar; kesin kilit Alt-Proje 09. **Model indirildiğinde config.json id2label'ı Ordered ile karşılaştır.**
- **`SentencePieceTokenizer.Create` parametre adı:** MS docs `addBeginOfSentence`; spec taslağı `addBeginningOfSentence`. Yanlış ad DERLENMEZ (güvenli) — derleyici/IntelliSense teyit eder.
- **Tokenizer IDisposable mı:** sürüme göre; `(_tokenizer as IDisposable)?.Dispose()` ile güvenli.
- **ONNX girdi adları** (`input_ids`/`attention_mask`) ve çıktı (`logits`) modelden modele değişebilir → export sırasında bu adlar sabitlenmeli; entegrasyon testi `results.First(r => r.Name == "logits")` ile doğrular (yoksa net hata).
- **Git kuralı:** commit yok; her task sonu `dotnet build && dotnet test`.

**Not (model dosyaları):** XLM-R ONNX + `sentencepiece.bpe.model` bu session scratchpad'inde yok → tokenizer + gerçek-model testleri Skip (beklenen). Model kaynağı: `tabularisai/multilingual-emotion-classification` (HF Optimum ile ONNX export), spm: XLM-R-base `sentencepiece.bpe.model` (~5 MB). Dosyalar `EmotionConfig.ModelDirectory` (varsayılan uygulama-veri `models/`) altına konur.

---

## Post-Review Düzeltmeleri (uygulama sırasında)

- **Task 2 (API düzeltmesi):** `SentencePieceTokenizer.Create`'in doğru parametre adı **`addBeginningOfSentence`** çıktı — plandaki (MS docs'tan alınan) `addBeginOfSentence` derlenmedi (CS1739). Derleyici yakaladı; spec §04b'nin orijinal reçetesi doğruymuş. `addEndOfSentence` olduğu gibi eşleşti. BOS/EOS-kapalı davranışı korundu.
- **Task 3 (Critical — concurrency use-after-dispose):** Plandaki motor `_tokenizer.Encode(...)`'ı `_runLock` DIŞINDA çağırıyordu (init sonrası, run-lock öncesi). `_tokenizer` paylaşılan native durum; eşzamanlı `Dispose()` encode ortasında tokenizer'ı serbest bırakabilirdi (native SentencePiece handle use-after-dispose; entry + post-lock guard'lar bu pencereyi kapatmıyordu). Düzeltme: `Encode` çağrısı `_runLock` içindeki post-lock `ObjectDisposedException.ThrowIf` sonrasına taşındı → tüm paylaşılan-disposable erişim kilit altında (04a disiplini). Tokenizasyon inference ile serileşir (kabul edilen küçük throughput maliyeti; doğruluk kazanır). Re-review: ADDRESSED, yeni kırılma yok.

**Nihai durum:** 99 test geçti + 4 gerçek-model/tokenizer Skip (env-gated: STT, prozodi, tokenizer, metin), 0 başarısız, 0 uyarı. Final whole-branch review (opus): **CLEAN, merge-ready**.

**Biriken Minor'lar:** YOK. **Parked:** YOK.

**Eval-09'a devredilen (bilinçli, blocker değil):** `XlmrEmotionLabels.Ordered` etiket sırası gerçek model `config.json` `id2label` ile birebir kilitlenecek (VA tablosu ad-anahtarlı olduğu için sıra hatası sessiz-bozulma yapmaz, gerçek-model testi `logits.Length==11` + yön kontrolüyle yakalar); VA lookup değerleri + eşikler Türkçe veriyle kalibre edilecek; kesin model seçimi. Model+spm indirilince gerçek-model testleri (`TEXT_MODEL_DIR`, `XLMR_SPM_PATH`) çalıştırılabilir.

---

## Model Entegrasyonu Sonrası Düzeltme (2026-07-26)

**Etiket sırası gerçek modelle kilitlendi.** Model kurulunca `config.json` `id2label` okundu; gerçek sıra **alfabetik**: 0:anger, 1:contempt, 2:disgust, 3:fear, 4:frustration, 5:gratitude, 6:joy, 7:love, 8:neutral, 9:sadness, 10:surprise. Koddaki başlangıç sırası (spec taslağından: joy, love, …) YANLIŞTI → `XlmrEmotionLabels.Ordered` gerçek sıraya güncellendi (VA tablosu ada göre anahtarlı olduğundan çökme yoktu ama yanlış duygu veriyordu). Plan/spec'in "model inince config.json ile karşılaştır" notu tam da bunu öngörmüştü. Gerçek-model testi (`AnalyzeAsync_RealModel_ProducesValenceArousal`, olumlu>olumsuz valence) geçti. Text ONNX HF Optimum ile export edildi (optimum 2.1.0 + optimum-onnx; girdi input_ids/attention_mask → logits[batch,11]).
