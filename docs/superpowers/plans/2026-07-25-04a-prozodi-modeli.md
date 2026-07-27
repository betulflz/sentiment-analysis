# Alt-Proje 04a: Prozodi Modeli — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** audeering wav2vec2 boyutsal duygu modelini ONNX Runtime ile lokal çalıştırıp `IProsodyEmotionModel` arkasında `AudioChunk` → `ProsodyEmotion` (valence-arousal + modelConfidence) üretmek.

**Architecture:** Yeni `SentimentAnalysis.Infrastructure.Emotion` projesi (master §9 Emotion dilimi; 04b metin modeli de buraya gelecek). ONNX Runtime tipleri motorun içinde; saf `AudioNormalizer` (girdi standardizasyonu) ve `ProsodyOutputMapper` (logits→VA + confidence) sınır DTO'ları üzerinde çalışır → tam unit-testli. Motor `InferenceSession`'ı bir kez yükler (singleton, sıcak). Bağımlılıklar içe doğru: Infrastructure.Emotion → Application → Core.

**Tech Stack:** .NET 9 · C# · xUnit 2.9.2 · Microsoft.ML.OnnxRuntime (CoreML EP + CPU fallback)

## Global Constraints

- **Hedef framework:** `net9.0`. Nullable + ImplicitUsings açık.
- **Bağımlılık yönü:** Infrastructure.Emotion → Application → Core.
- **`git YOK`**: her task sonu `dotnet build && dotnet test`, commit değil.
- **Girdi formatı:** 16 kHz mono float PCM. Motor `SampleRate != 16000` ise `ArgumentException`.
- **`ProsodyEmotion` domain tipi (Alt-proje 01) DEĞİŞMEZ:** `ProsodyEmotion(ValenceArousal Va, double ModelConfidence)`. Ses kalite sinyalleri (SNR/speech-ratio) bu modelde ÜRETİLMEZ — `QualitySignals` pipeline'da (Alt-proje 06) toplanır; spec 04'ün "prozodi kalite sinyali üretir" ifadesi orada karşılanır (YAGNI, arayüz sözleşmesine sadık).
- **ONNX kontratı (spike ile doğrulandı 2026-07-25):** girdi tensörü adı **`signal`**, şekil `[1, N]` (float, normalize edilmiş ham ses); çıktı **`logits`** `[1,3]` = **[arousal, dominance, valence]** (0..1). `hidden_states [1,1024]` kullanılmaz. Model dosyası ~631 MB f32.
- **Çıktı eşlemesi:** domain valence = `valence*2 − 1` (0..1 → −1..1); domain arousal = `arousal` (0..1, clamp). `dominance` yok sayılır.
- **Girdi ön-işleme:** zero-mean / unit-variance standardizasyon (`(x−mean)/sqrt(var+eps)`); sabit/sessiz sinyalde (var≈0) sıfır vektör (nötr).
- **Model varsayılanı:** audeering `wav2vec2-large-robust-12-ft-emotion-msp-dim` (MSP-Podcast, İngilizce; prozodi dil-agnostik D7). Kesin model/eşik → Alt-Proje 09.
- **Gerçek-model entegrasyon testi** `PROSODY_MODEL_PATH` (+ `PROSODY_SAMPLE_WAV`) env yoksa `Xunit.SkippableFact`/`Skip.If` ile atlanır (03'teki örüntü); varsayılan `dotnet test` yeşil kalır.

### Mevcut tipler (tüketilir, değiştirilmez)

```csharp
// Core
public sealed record AudioChunk(float[] Samples, int SampleRate, TimeSpan Start, TimeSpan End);
public readonly record struct ValenceArousal(double Valence, double Arousal) { static ValenceArousal Clamp(double v, double a); static ValenceArousal Neutral { get; } }
public sealed record ProsodyEmotion(ValenceArousal Va, double ModelConfidence);
// Application.Abstractions
public interface IProsodyEmotionModel { Task<ProsodyEmotion> AnalyzeAsync(AudioChunk chunk, CancellationToken ct); }
// Application.Configuration
public sealed record AppConfig(FusionConfig Fusion, StorageConfig Storage, SttConfig Stt) { public static AppConfig Default { get; } } // Task 1'de EmotionConfig eklenir
```

### ONNX Runtime C# API (context7 /microsoft/onnxruntime)

```csharp
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
var options = new SessionOptions();
// macOS Metal/ANE için CoreML EP (başarısızsa CPU fallback)
using var session = new InferenceSession(modelPath, options);
var input = new DenseTensor<float>(normalizedSamples, new[] { 1, normalizedSamples.Length });
var inputs = new[] { NamedOnnxValue.CreateFromTensor("signal", input) };
using var results = session.Run(inputs);
float[] logits = results.First(r => r.Name == "logits").AsTensor<float>().ToArray(); // [arousal, dominance, valence]
```

---

### Task 1: `Infrastructure.Emotion` iskeleti + `EmotionConfig` + model sağlayıcı

**Files:**
- Create: `src/SentimentAnalysis.Infrastructure.Emotion/SentimentAnalysis.Infrastructure.Emotion.csproj`
- Create: `tests/SentimentAnalysis.Infrastructure.Emotion.Tests/SentimentAnalysis.Infrastructure.Emotion.Tests.csproj`
- Modify: `src/SentimentAnalysis.Application/Configuration/AppConfig.cs` (`EmotionConfig` ekle + `AppConfig`'e alan)
- Create: `src/SentimentAnalysis.Infrastructure.Emotion/IEmotionModelProvider.cs`
- Create: `src/SentimentAnalysis.Infrastructure.Emotion/FileEmotionModelProvider.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Emotion.Tests/FileEmotionModelProviderTests.cs`

**Interfaces:**
- Consumes: `AppConfig`
- Produces: `sealed record EmotionConfig(string ProsodyModelFile, string? ModelDirectory, string ExecutionProvider)`; `AppConfig`'e `EmotionConfig Emotion` alanı (additive, defaultlu); `interface IEmotionModelProvider { string ResolveModelPath(string fileName); }`; `sealed class FileEmotionModelProvider : IEmotionModelProvider` (ctor `(AppConfig)`, `static string DefaultModelDirectory()`).

- [ ] **Step 1: Projeleri oluştur ve çözüme ekle**

```bash
cd /Users/betulfiliz/sentiment-analysis
dotnet new classlib -n SentimentAnalysis.Infrastructure.Emotion -o src/SentimentAnalysis.Infrastructure.Emotion -f net9.0
rm src/SentimentAnalysis.Infrastructure.Emotion/Class1.cs
dotnet new xunit -n SentimentAnalysis.Infrastructure.Emotion.Tests -o tests/SentimentAnalysis.Infrastructure.Emotion.Tests -f net9.0
rm tests/SentimentAnalysis.Infrastructure.Emotion.Tests/UnitTest1.cs
dotnet sln add src/SentimentAnalysis.Infrastructure.Emotion tests/SentimentAnalysis.Infrastructure.Emotion.Tests
dotnet add src/SentimentAnalysis.Infrastructure.Emotion reference src/SentimentAnalysis.Application
dotnet add src/SentimentAnalysis.Infrastructure.Emotion package Microsoft.ML.OnnxRuntime
dotnet add tests/SentimentAnalysis.Infrastructure.Emotion.Tests reference src/SentimentAnalysis.Infrastructure.Emotion
```

csproj PropertyGroup'una `<Nullable>enable</Nullable>` + `<ImplicitUsings>enable</ImplicitUsings>` ekle (yoksa).

- [ ] **Step 2: `EmotionConfig`'i ekle ve `AppConfig`'e bağla**

`src/SentimentAnalysis.Application/Configuration/AppConfig.cs` içine, `SttConfig`'ten sonra ekle:

```csharp
/// <summary>Duygu modelleri (prozodi + metin) ayarları (spec 04).</summary>
public sealed record EmotionConfig(
    string ProsodyModelFile = "prosody-msp-dim.onnx", // audeering wav2vec2 boyutsal (model.onnx yeniden adlandırılır)
    string? ModelDirectory = null,                    // null → FileEmotionModelProvider varsayılanı
    string ExecutionProvider = "coreml");             // "coreml" (macOS, CPU fallback) | "cpu"
```

Ve `AppConfig` kaydını + `Default`'unu şu şekilde genişlet (additive):

```csharp
public sealed record AppConfig(
    FusionConfig Fusion,
    StorageConfig Storage,
    SttConfig Stt,
    EmotionConfig Emotion)
{
    public static AppConfig Default => new(
        Fusion: new FusionConfig(),
        Storage: new StorageConfig(),
        Stt: new SttConfig(),
        Emotion: new EmotionConfig());
}
```

> Not: Bu, `AppConfig` kurucusuna zorunlu parametre ekler. Mevcut tek kullanım `AppConfig.Default` (güncelleniyor) ve testlerdeki `AppConfig.Default with { ... }` ifadeleridir — bunlar kırılmaz. Doğrudan `new AppConfig(a,b,c)` çağrısı yoktur (arama ile teyit et: yalnızca `AppConfig.Default` kullanılıyor).

- [ ] **Step 3: Başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Emotion.Tests/FileEmotionModelProviderTests.cs`:

```csharp
using System;
using System.IO;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class FileEmotionModelProviderTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-emo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ResolveModelPath_ReturnsPath_WhenFileExists()
    {
        var dir = NewTempDir();
        var file = Path.Combine(dir, "prosody-msp-dim.onnx");
        File.WriteAllText(file, "stub");
        var cfg = AppConfig.Default with { Emotion = AppConfig.Default.Emotion with { ModelDirectory = dir } };

        var provider = new FileEmotionModelProvider(cfg);
        Assert.Equal(file, provider.ResolveModelPath("prosody-msp-dim.onnx"));
    }

    [Fact]
    public void ResolveModelPath_Throws_WhenMissing()
    {
        var dir = NewTempDir();
        var cfg = AppConfig.Default with { Emotion = AppConfig.Default.Emotion with { ModelDirectory = dir } };
        var provider = new FileEmotionModelProvider(cfg);
        var ex = Assert.Throws<FileNotFoundException>(() => provider.ResolveModelPath("prosody-msp-dim.onnx"));
        Assert.Contains("prosody-msp-dim.onnx", ex.Message);
    }

    [Fact]
    public void DefaultModelDirectory_IsUnderApplicationData()
    {
        var def = FileEmotionModelProvider.DefaultModelDirectory();
        Assert.Contains("SentimentAnalysis", def);
        Assert.EndsWith("models", def);
    }
}
```

- [ ] **Step 4: Testin başarısız olduğunu doğrula** — `dotnet test tests/SentimentAnalysis.Infrastructure.Emotion.Tests` → FAIL (tipler yok).

- [ ] **Step 5: `IEmotionModelProvider`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Emotion/IEmotionModelProvider.cs`:

```csharp
namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>ONNX model dosya adını yerel yola çözer; yoksa aksiyon-alınabilir
/// <see cref="System.IO.FileNotFoundException"/> atar. (04a prozodi + 04b metin ortak.)</summary>
public interface IEmotionModelProvider
{
    string ResolveModelPath(string fileName);
}
```

- [ ] **Step 6: `FileEmotionModelProvider`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Emotion/FileEmotionModelProvider.cs`:

```csharp
using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>Modeli <c>{ModelDirectory}/{fileName}</c> yolundan çözer; ModelDirectory null ise
/// uygulama veri klasörü altındaki varsayılana düşer.</summary>
public sealed class FileEmotionModelProvider : IEmotionModelProvider
{
    private readonly EmotionConfig _cfg;

    public FileEmotionModelProvider(AppConfig config) => _cfg = config.Emotion;

    public string ResolveModelPath(string fileName)
    {
        var dir = _cfg.ModelDirectory ?? DefaultModelDirectory();
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Duygu modeli bulunamadı: {path}. ONNX modelini bu konuma koyun " +
                "(audeering wav2vec2 boyutsal model: Zenodo 6221127 → model.onnx).", path);
        return path;
    }

    public static string DefaultModelDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SentimentAnalysis", "models");
}
```

- [ ] **Step 7: Testin geçtiğini ve tüm çözümün derlendiğini doğrula** — `dotnet build && dotnet test` → yeni 3 test PASS; mevcut testler (01/02/03, 73+1skip) kırılmaz.

---

### Task 2: `AudioNormalizer` + `ProsodyOutputMapper` (saf)

**Files:**
- Create: `src/SentimentAnalysis.Infrastructure.Emotion/AudioNormalizer.cs`
- Create: `src/SentimentAnalysis.Infrastructure.Emotion/ProsodyOutputMapper.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Emotion.Tests/ProsodyMathTests.cs`

**Interfaces:**
- Consumes: `ValenceArousal`, `ProsodyEmotion` (Core)
- Produces: `static class AudioNormalizer { float[] Standardize(float[] samples) }`; `static class ProsodyOutputMapper { ProsodyEmotion Map(float[] logits) }` (logits = [arousal, dominance, valence]).

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Emotion.Tests/ProsodyMathTests.cs`:

```csharp
using System;
using System.Linq;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class ProsodyMathTests
{
    [Fact]
    public void Standardize_ProducesZeroMeanUnitVariance()
    {
        var x = new float[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var y = AudioNormalizer.Standardize(x);
        double mean = y.Average();
        double var = y.Select(v => (v - mean) * (v - mean)).Average();
        Assert.True(Math.Abs(mean) < 1e-4, $"ort ~0 bekleniyor, {mean}");
        Assert.True(Math.Abs(var - 1.0) < 1e-2, $"varyans ~1 bekleniyor, {var}");
    }

    [Fact]
    public void Standardize_ConstantSignal_ReturnsZeros()
    {
        var y = AudioNormalizer.Standardize(new float[] { 0.4f, 0.4f, 0.4f });
        Assert.All(y, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Standardize_Empty_ReturnsEmpty()
    {
        Assert.Empty(AudioNormalizer.Standardize(Array.Empty<float>()));
    }

    [Fact]
    public void Map_ValenceScaledToMinusOneOne_ArousalDirect()
    {
        // logits = [arousal, dominance, valence]
        var p = ProsodyOutputMapper.Map(new float[] { 0.9f, 0.5f, 0.75f });
        Assert.Equal(0.5, p.Va.Valence, precision: 5);   // 0.75*2-1 = 0.5
        Assert.Equal(0.9, p.Va.Arousal, precision: 5);   // direkt
    }

    [Fact]
    public void Map_NeutralOutput_LowConfidence()
    {
        // arousal 0.5, valence 0.5 → domain (0, 0.5) = nötr merkez → güven ~0
        var p = ProsodyOutputMapper.Map(new float[] { 0.5f, 0.5f, 0.5f });
        Assert.True(p.ModelConfidence < 0.05, $"nötr → düşük güven, {p.ModelConfidence}");
    }

    [Fact]
    public void Map_ExtremeOutput_HighConfidence()
    {
        // arousal 0.95, valence 1.0 → merkezden uzak → yüksek güven
        var p = ProsodyOutputMapper.Map(new float[] { 0.95f, 0.5f, 1.0f });
        Assert.True(p.ModelConfidence > 0.8, $"uç → yüksek güven, {p.ModelConfidence}");
    }

    [Fact]
    public void Map_ClampsOutOfRangeLogits()
    {
        var p = ProsodyOutputMapper.Map(new float[] { 1.3f, 0.5f, -0.2f });
        Assert.InRange(p.Va.Arousal, 0.0, 1.0);
        Assert.InRange(p.Va.Valence, -1.0, 1.0);
        Assert.InRange(p.ModelConfidence, 0.0, 1.0);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula** — FAIL (tipler yok).

- [ ] **Step 3: `AudioNormalizer`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Emotion/AudioNormalizer.cs`:

```csharp
namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>wav2vec2 girdisi için ham sesi zero-mean / unit-variance standardize eder
/// (audeering ön-işlemesiyle aynı). Sabit/sessiz sinyalde sıfır vektör döner. Saf.</summary>
public static class AudioNormalizer
{
    private const double Eps = 1e-7;

    public static float[] Standardize(float[] samples)
    {
        if (samples.Length == 0) return samples;

        double sum = 0;
        for (int i = 0; i < samples.Length; i++) sum += samples[i];
        double mean = sum / samples.Length;

        double sq = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double d = samples[i] - mean;
            sq += d * d;
        }
        double variance = sq / samples.Length;
        if (variance < Eps) return new float[samples.Length]; // sabit/sessiz → nötr

        double invStd = 1.0 / Math.Sqrt(variance + Eps);
        var result = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
            result[i] = (float)((samples[i] - mean) * invStd);
        return result;
    }
}
```

- [ ] **Step 4: `ProsodyOutputMapper`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Emotion/ProsodyOutputMapper.cs`:

```csharp
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>audeering çıktısını (`logits` = [arousal, dominance, valence], 0..1) domain
/// <see cref="ProsodyEmotion"/>'a eşler. valence 0..1 → −1..1; arousal direkt.
/// modelConfidence = nötr merkeze (0, 0.5) uzaklık (PROVISIONAL heuristik; Alt-Proje 09 kalibre eder).</summary>
public static class ProsodyOutputMapper
{
    // Merkezden en büyük Öklid uzaklık: sqrt(1^2 + 0.5^2).
    private static readonly double MaxDistance = Math.Sqrt(1.0 + 0.25);

    public static ProsodyEmotion Map(float[] logits)
    {
        double arousal = logits.Length > 0 ? logits[0] : 0.5;
        double valence01 = logits.Length > 2 ? logits[2] : 0.5;

        var va = ValenceArousal.Clamp(valence01 * 2.0 - 1.0, arousal);

        double dv = va.Valence;                 // nötr valence 0
        double da = va.Arousal - 0.5;           // nötr arousal 0.5
        double confidence = Math.Clamp(Math.Sqrt(dv * dv + da * da) / MaxDistance, 0.0, 1.0);

        return new ProsodyEmotion(va, confidence);
    }
}
```

- [ ] **Step 5: Testin geçtiğini doğrula** — `dotnet test tests/SentimentAnalysis.Infrastructure.Emotion.Tests` → PASS.

---

### Task 3: `OnnxProsodyEmotionModel` motoru + guard + atlanabilir entegrasyon testi

**Files:**
- Create: `src/SentimentAnalysis.Infrastructure.Emotion/OnnxProsodyEmotionModel.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Emotion.Tests/OnnxProsodyEmotionModelTests.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Emotion.Tests/WavReader.cs` (test yardımcısı — 03'tekiyle aynı)

**Interfaces:**
- Consumes: `IProsodyEmotionModel`, `AudioChunk`, `ProsodyEmotion` (Core/Application), `IEmotionModelProvider`, `AudioNormalizer`, `ProsodyOutputMapper`; Microsoft.ML.OnnxRuntime
- Produces: `sealed class OnnxProsodyEmotionModel : IProsodyEmotionModel, IDisposable` (ctor `(AppConfig, IEmotionModelProvider)`).

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Emotion.Tests/WavReader.cs`:

```csharp
using System;
using System.IO;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

/// <summary>Minimal 16-bit PCM mono WAV okuyucu (yalnızca test için).</summary>
internal static class WavReader
{
    public static float[] ReadMono16(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.ReadBytes(12); // "RIFF"<size>"WAVE"
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var id = new string(reader.ReadChars(4));
            int size = reader.ReadInt32();
            if (id == "data")
            {
                int count = size / 2;
                var samples = new float[count];
                for (int i = 0; i < count; i++) samples[i] = reader.ReadInt16() / 32768f;
                return samples;
            }
            reader.BaseStream.Seek(size, SeekOrigin.Current);
        }
        throw new InvalidDataException("WAV 'data' chunk bulunamadı.");
    }
}
```

`tests/SentimentAnalysis.Infrastructure.Emotion.Tests/OnnxProsodyEmotionModelTests.cs`:

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

public class OnnxProsodyEmotionModelTests
{
    private sealed class UnusedModelProvider : IEmotionModelProvider
    {
        public string ResolveModelPath(string fileName) =>
            throw new InvalidOperationException("guard'dan önce model çözülmemeliydi");
    }

    private sealed class FixedModelProvider(string path) : IEmotionModelProvider
    {
        public string ResolveModelPath(string fileName) => path;
    }

    [Fact]
    public async Task AnalyzeAsync_Throws_ForNon16kHz()
    {
        using var model = new OnnxProsodyEmotionModel(AppConfig.Default, new UnusedModelProvider());
        var chunk = new AudioChunk(new float[8000], SampleRate: 8000, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<ArgumentException>(() => model.AnalyzeAsync(chunk, CancellationToken.None));
    }

    [SkippableFact]
    public async Task AnalyzeAsync_RealModel_ProducesValenceArousal()
    {
        var modelPath = Environment.GetEnvironmentVariable("PROSODY_MODEL_PATH");
        var wavPath = Environment.GetEnvironmentVariable("PROSODY_SAMPLE_WAV");
        Skip.If(string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath),
            "PROSODY_MODEL_PATH ayarlı değil; gerçek-model testi atlandı.");
        Skip.If(string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath),
            "PROSODY_SAMPLE_WAV ayarlı değil; örnek ses yok.");

        var samples = WavReader.ReadMono16(wavPath!);
        // CPU EP ile deterministik test (CoreML EP makineye bağlı).
        var cfg = AppConfig.Default with { Emotion = AppConfig.Default.Emotion with { ExecutionProvider = "cpu" } };
        using var model = new OnnxProsodyEmotionModel(cfg, new FixedModelProvider(modelPath!));
        var chunk = new AudioChunk(samples, 16000, TimeSpan.Zero, TimeSpan.FromSeconds(samples.Length / 16000.0));

        var p = await model.AnalyzeAsync(chunk, CancellationToken.None);

        Assert.InRange(p.Va.Valence, -1.0, 1.0);
        Assert.InRange(p.Va.Arousal, 0.0, 1.0);
        Assert.InRange(p.ModelConfidence, 0.0, 1.0);
        // Spike referansı: sakin-olumlu Türkçe örnekte valence hafif pozitif (~0.24), arousal ~0.59.
        Assert.True(p.Va.Valence > 0.0, $"örnek hafif olumlu beklenir, {p.Va.Valence}");
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula** — FAIL (`OnnxProsodyEmotionModel` yok). (Entegrasyon testi env yoksa Skip.)

- [ ] **Step 3: `OnnxProsodyEmotionModel`'i yaz**

`src/SentimentAnalysis.Infrastructure.Emotion/OnnxProsodyEmotionModel.cs`:

```csharp
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>audeering wav2vec2 boyutsal SER'i ONNX Runtime ile çalıştırır. Session açılışta
/// bir kez yüklenir, sıcak tutulur (singleton). Girdi 16 kHz mono float; zero-mean/unit-var
/// standardize edilir. Çıktı logits=[arousal,dominance,valence] → ProsodyEmotion.</summary>
public sealed class OnnxProsodyEmotionModel : IProsodyEmotionModel, IDisposable
{
    private const string InputName = "signal";
    private const string OutputName = "logits";

    private readonly EmotionConfig _cfg;
    private readonly IEmotionModelProvider _models;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private InferenceSession? _session;
    private bool _disposed;

    public OnnxProsodyEmotionModel(AppConfig config, IEmotionModelProvider models)
    {
        _cfg = config.Emotion;
        _models = models;
    }

    public async Task<ProsodyEmotion> AnalyzeAsync(AudioChunk chunk, CancellationToken ct)
    {
        if (chunk.SampleRate != 16000)
            throw new ArgumentException(
                $"Prozodi modeli 16 kHz mono bekler; gelen {chunk.SampleRate} Hz.", nameof(chunk));

        var session = await EnsureSessionAsync(ct).ConfigureAwait(false);
        var normalized = AudioNormalizer.Standardize(chunk.Samples);

        await _runLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var input = new DenseTensor<float>(normalized, new[] { 1, normalized.Length });
            var inputs = new[] { NamedOnnxValue.CreateFromTensor(InputName, input) };
            using var results = session.Run(inputs);
            var logits = results.First(r => r.Name == OutputName).AsTensor<float>().ToArray();
            return ProsodyOutputMapper.Map(logits);
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task<InferenceSession> EnsureSessionAsync(CancellationToken ct)
    {
        if (_session is not null) return _session;
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_session is null)
            {
                var modelPath = _models.ResolveModelPath(_cfg.ProsodyModelFile);
                var options = new SessionOptions();
                if (string.Equals(_cfg.ExecutionProvider, "coreml", StringComparison.OrdinalIgnoreCase))
                    TryAppendCoreML(options);
                _session = new InferenceSession(modelPath, options);
            }
            return _session;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>CoreML EP'yi ekler; bu ORT sürümünde/makinede yoksa sessizce CPU'ya düşer
    /// (correctness CPU'da da sağlanır, CoreML yalnızca hızlandırma).</summary>
    private static void TryAppendCoreML(SessionOptions options)
    {
        try { options.AppendExecutionProvider_CoreML(); }
        catch { /* CoreML yoksa CPU fallback */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session?.Dispose();
        _initLock.Dispose();
        _runLock.Dispose();
    }
}
```

- [ ] **Step 4: Guard testinin geçtiğini doğrula (entegrasyon Skip)** — `dotnet test tests/SentimentAnalysis.Infrastructure.Emotion.Tests` → guard PASS; gerçek-model testi **Skipped**.

- [ ] **Step 5: (Önerilen) gerçek modelle bir kez doğrula**

Spike modeli + örnek ses mevcut:

```bash
export PROSODY_MODEL_PATH="/private/tmp/claude-501/-Users-betulfiliz-sentiment-analysis/0d902239-6d1f-42de-a74e-de5212025fb0/scratchpad/prosody-spike/unpacked/model.onnx"
export PROSODY_SAMPLE_WAV="/private/tmp/claude-501/-Users-betulfiliz-sentiment-analysis/0d902239-6d1f-42de-a74e-de5212025fb0/scratchpad/stt-spike/sample.wav"
dotnet test tests/SentimentAnalysis.Infrastructure.Emotion.Tests --filter "FullyQualifiedName~RealModel"
unset PROSODY_MODEL_PATH PROSODY_SAMPLE_WAV
```
Expected: PASS — valence hafif pozitif (spike'ta ~0.24), arousal ~0.59, hepsi aralıkta. Sonucu rapora yaz. (Dosyalar yoksa atla; Skip beklenir.)

---

### Task 4: `AddProsodyEmotion()` DI + tam çözüm doğrulaması

**Files:**
- Create: `src/SentimentAnalysis.Infrastructure.Emotion/EmotionServiceCollectionExtensions.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Emotion.Tests/EmotionDiTests.cs`
- Modify: `tests/SentimentAnalysis.Infrastructure.Emotion.Tests/*.csproj` (concrete DI + Xunit.SkippableFact paketleri)

**Interfaces:**
- Produces: `static class EmotionServiceCollectionExtensions { IServiceCollection AddProsodyEmotion(this IServiceCollection) }`.

- [ ] **Step 1: Test paketlerini ekle**

```bash
cd /Users/betulfiliz/sentiment-analysis
dotnet add tests/SentimentAnalysis.Infrastructure.Emotion.Tests package Microsoft.Extensions.DependencyInjection --version 10.0.10
dotnet add tests/SentimentAnalysis.Infrastructure.Emotion.Tests package Xunit.SkippableFact
```
(Concrete DI yalnızca test projesine — üretim Emotion projesine değil. `Xunit.SkippableFact` Task 3'teki `[SkippableFact]` için.)

- [ ] **Step 2: Başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Emotion.Tests/EmotionDiTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class EmotionDiTests
{
    [Fact]
    public void AddProsodyEmotion_ResolvesModelAndProvider()
    {
        var provider = new ServiceCollection()
            .AddApplicationCore()
            .AddProsodyEmotion()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IProsodyEmotionModel>());
        Assert.NotNull(provider.GetRequiredService<IEmotionModelProvider>());
    }
}
```

- [ ] **Step 3: Testin başarısız olduğunu doğrula** — FAIL (`AddProsodyEmotion` yok).

- [ ] **Step 4: `AddProsodyEmotion()`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Emotion/EmotionServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>Duygu modelleri DI kaydı. Singleton (model açılışta bir kez, sıcak).
/// `AddApplicationCore()` ile kullanılır. (04b metin modeli buraya AddTextEmotion ekleyecek.)</summary>
public static class EmotionServiceCollectionExtensions
{
    public static IServiceCollection AddProsodyEmotion(this IServiceCollection services)
    {
        services.AddSingleton<IEmotionModelProvider, FileEmotionModelProvider>();
        services.AddSingleton<IProsodyEmotionModel, OnnxProsodyEmotionModel>();
        return services;
    }
}
```

- [ ] **Step 5: Tam çözümün derlendiğini ve testlerin geçtiğini doğrula** — `dotnet build && dotnet test` → Build 0 uyarı; **tüm** testler PASS (01/02/03 + 04a); prozodi gerçek-model testi **Skipped** (env yok).

---

## Self-Review (plan yazarı tarafından)

- **Spec coverage (04a):** ONNX Runtime ile prozodi VA → Task 3 motor; audeering model + eşleme → Task 2/3; girdi standardizasyonu → Task 2; CoreML EP + CPU fallback → Task 3; model bir kez yükle (singleton) → Task 3 lazy + Task 4 DI; ONNX-export edilebilirlik (kabul kriteri) → hazır ONNX kullanılıyor (standart mimari, tuzak yok); dil-agnostik prozodi → doğal. `IProsodyEmotionModel` VA çıktısı (04a hedefi) → karşılanır. ✔
- **Kapsam dışı (bilinçli):** ses kalite sinyalleri (SNR/speech-ratio) → 06; INT8 quantization → optimizasyon (06/09); sidecar kontratı → 04 spec, 04b/sonra; metin modeli → 04b.
- **Placeholder scan:** TODO/TBD yok. modelConfidence heuristiği "provisional" olarak açıkça işaretli (eval 09). Entegrasyon testi env-gated gerçek Skip.
- **Type consistency:** `EmotionConfig`, `IEmotionModelProvider.ResolveModelPath`, `AudioNormalizer.Standardize`, `ProsodyOutputMapper.Map`, `OnnxProsodyEmotionModel(AppConfig, IEmotionModelProvider)`, `AddProsodyEmotion` adları tutarlı. `ProsodyEmotion(Va, ModelConfidence)` domain tipine sadık. ONNX I/O adları (`signal`/`logits`) ve çıktı sırası spike ile doğrulandı. `AppConfig` genişletmesi: **DİKKAT** kurucuya zorunlu `EmotionConfig` eklenir — Task 1 Step 2 notu, tek kullanımın `AppConfig.Default` olduğunu ve kırılmayacağını belirtir (implementer önce `grep -rn "new AppConfig("` ile teyit etmeli).
- **Mimari:** Infrastructure.Emotion → Application → Core. ONNX tipleri motor sınırında; AudioNormalizer/ProsodyOutputMapper saf ve unit-testli.
- **Kaynak/eşzamanlılık:** iki ayrı semaphore (init/run) → reentrancy/deadlock yok; IDisposable session+semaphore'ları serbest bırakır; CancellationToken akar.
- **Git kuralı:** commit yok; `dotnet build && dotnet test`.
- **Risk:** `Microsoft.ML.OnnxRuntime` CoreML EP C# API'si (`AppendExecutionProvider_CoreML`) sürüme göre değişebilir → try/catch CPU fallback ile korundu; ilk restore/build doğrular. Model ~631 MB f32; Whisper (779 MB) ile birlikte ~1.4 GB — 8 GB'da güvenli.

---

## Post-Review Düzeltmeleri (uygulama sırasında)

- **Task 3 (Important — dispose/Run native yarışı):** Plandaki naif `Dispose()` (kilitsiz session dispose)
  düzeltildi: `Dispose()` artık `_initLock` + `_runLock` alıp devam eden Run/Init biterken native session'ı
  serbest bırakır; `AnalyzeAsync` başına `ObjectDisposedException.ThrowIf(_disposed, this)` guard'ı eklendi;
  `SessionOptions` artık `using`. Yeni test: `AnalyzeAsync_AfterDispose_Throws`.
- **Gerçek-model doğrulaması:** Task 3'te audeering ONNX'i ile entegrasyon testi çalıştırıldı ve GEÇTİ —
  .NET çıktısı Python spike ile **birebir**: valence 0.243, arousal 0.589, confidence 0.231.

**Nihai durum:** 86 test geçti + 2 gerçek-model Skip (env-gated: STT + prozodi), 0 başarısız, 0 uyarı.

**Biriken Minor'lar (final review: hepsi "merge sonrası", blocker değil):** M-a ProsodyOutputMapper DRY
(`DistanceTo(Neutral)`); M-b kısa-logits savunma testi; M-c AudioNormalizer Eps-sınırı testi; M-d
EnsureSessionAsync volatile okuma (teorik); M-e CoreML fallback boş-catch (ILogger gelince).
**Follow-up (03):** WhisperSpeechToText'e aynı dispose/Run şablonu uygulanabilir.
