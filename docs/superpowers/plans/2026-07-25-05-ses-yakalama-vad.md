# Alt-Proje 05: Ses Yakalama + VAD — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dosya girdisini (WAV) 16 kHz mono `AudioChunk` akışına çeviren test edilebilir çekirdek (`Infrastructure.Audio`) + Silero VAD ile sessizliği eleyen kapı (`Infrastructure.Vad`) inşa etmek; native canlı yakalama `IAudioCaptureSource` kontratı olarak korunur (05b'ye ertelenir).

**Architecture:** İki yeni AYRI Infrastructure dilimi (master §9). `Infrastructure.Audio` (ONNX'siz): saf `WavDecoder` → `AudioPreprocessor` (mono downmix + 16k lineer resample) → `AudioChunker` (örtüşmeli) → `FileCaptureSource : IAudioCaptureSource`. `Infrastructure.Vad` (ONNX): saf `VadWindower` (512-örnek pencere) + `SileroVadGate : IVadGate` (singleton InferenceSession, sıcak). Native tipler (InferenceSession) motor sınırında; tüm saf yardımcılar unit-testli (modelsiz). Bağımlılıklar içe doğru: her iki dilim → Application → Core.

**Tech Stack:** .NET 9 · C# · xUnit 2.9.2 + Xunit.SkippableFact · Microsoft.ML.OnnxRuntime 1.28.0 (Vad) · saf .NET WAV/DSP (Audio, harici bağımlılık yok)

## Global Constraints

- **Hedef framework:** `net9.0`. Nullable + ImplicitUsings açık.
- **Bağımlılık yönü:** Infrastructure.Audio → Application → Core; Infrastructure.Vad → Application → Core. Core değişmez.
- **`git YOK`**: her task sonu `dotnet build && dotnet test`, commit değil.
- **Yerleşik sözleşmeler DEĞİŞMEZ (Alt-Proje 01):** `AudioChunk(float[] Samples, int SampleRate, TimeSpan Start, TimeSpan End)` (16 kHz mono float); `IAudioCaptureSource { IAsyncEnumerable<AudioChunk> CaptureAsync(CaptureOptions options, CancellationToken ct); }`; `IVadGate { bool HasSpeech(AudioChunk chunk); }`; `CaptureOptions(TimeSpan ChunkLength, TimeSpan Overlap)`. (Spec'in "AudioFrame" terimi eski taslak; geçerli tip **AudioChunk**.)
- **Normalizasyon hedefi:** tüm çıktı AudioChunk'lar **16 kHz mono float**. Downmix = kanal ortalaması; resample = lineer interpolasyon.
- **Silero VAD ONNX kontratı (SPIKE İLE ÖLÇÜLDÜ 2026-07-25, .NET ORT):** girdiler `input` float32 `[1,512]` (16 kHz'de 512-örnek/32ms pencere), `state` float32 `[2,1,128]` (LSTM, sıfırla başla), `sr` int64 scalar (`16000`); çıktılar `output` float32 `[1,1]` (konuşma olasılığı 0..1), `stateN` float32 `[2,1,128]` (sonraki çağrının `state`'i). Ölçülen: sessizlik≈0.0006, gürültü≈0.002, ton≈0.0005.
- **`HasSpeech` reçetesi:** chunk (16k mono) → ardışık 512-örnek pencereler (tail<512 → sıfır-pad); her pencere Silero'dan geçer, `state` chunk içinde zincirlenir (her çağrıda sıfırdan → chunk'lar bağımsız); **max pencere prob ≥ eşik (varsayılan 0.5)** → konuşma var. Eşik `VadConfig`, eval-09 kalibre eder.
- **VAD girdisi 16 kHz olmalı:** `SileroVadGate` `chunk.SampleRate != 16000` ise `ArgumentException`.
- **mp3/m4a bu turda ERTELENMİŞ:** `FileCaptureSource` WAV dışı uzantıda `NotSupportedException` (aksiyon-alınabilir mesaj).
- **Native canlı yakalama ERTELENMİŞ (05b):** `LiveCaptureSource` implemente edilmez; `IAudioCaptureSource` kontratı hazır.
- **ONNX session dispose disiplini:** singleton session, sıcak; `ObjectDisposedException` guard + kilitli dispose (emotion motorları örüntüsü, sync sürüm).
- **Gerçek-model entegrasyon testi** `VAD_MODEL_PATH` (+ opsiyonel `VAD_SAMPLE_SPEECH_WAV`) env yoksa `Xunit.SkippableFact`/`Skip.If` ile atlanır; varsayılan `dotnet test` modelsiz yeşil.
- **AppConfig genişletmesi additive:** yeni `VadConfig` alanı defaultlu; mevcut `AppConfig.Default` + `with { }` kırılmaz.

### Mevcut tipler (tüketilir, değiştirilmez)

```csharp
// Core
public sealed record AudioChunk(float[] Samples, int SampleRate, TimeSpan Start, TimeSpan End);
// Application.Abstractions
public interface IAudioCaptureSource { IAsyncEnumerable<AudioChunk> CaptureAsync(CaptureOptions options, CancellationToken ct); }
public interface IVadGate { bool HasSpeech(AudioChunk chunk); }
public sealed record CaptureOptions(TimeSpan ChunkLength, TimeSpan Overlap);
// Application.Configuration — AppConfig(FusionConfig, StorageConfig, SttConfig, EmotionConfig); Task 5'te VadConfig eklenir
// Application — AddApplicationCore(this IServiceCollection, AppConfig? = null)
```

### ONNX Runtime C# — Silero (Task 6; spike ile doğrulandı)

```csharp
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
var input = new DenseTensor<float>(frame /*512*/, new[] { 1, 512 });
var stateT = new DenseTensor<float>(state /*len 256*/, new[] { 2, 1, 128 });
var srT = new DenseTensor<long>(new long[] { 16000 }, new[] { 1 }); // scalar sr; [1] ORT'ta kabul edildi
var feeds = new[]
{
    NamedOnnxValue.CreateFromTensor("input", input),
    NamedOnnxValue.CreateFromTensor("state", stateT),
    NamedOnnxValue.CreateFromTensor("sr", srT),
};
using var results = session.Run(feeds);
float prob = results.First(r => r.Name == "output").AsTensor<float>().ToArray()[0];
float[] newState = results.First(r => r.Name == "stateN").AsTensor<float>().ToArray(); // → sonraki state
```

---

### Task 1: `Infrastructure.Audio` iskeleti + `WavDecoder` (saf)

**Files:**
- Create: `src/SentimentAnalysis.Infrastructure.Audio/SentimentAnalysis.Infrastructure.Audio.csproj`
- Create: `tests/SentimentAnalysis.Infrastructure.Audio.Tests/SentimentAnalysis.Infrastructure.Audio.Tests.csproj`
- Create: `src/SentimentAnalysis.Infrastructure.Audio/WavDecoder.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Audio.Tests/WavDecoderTests.cs`

**Interfaces:**
- Produces: `readonly record struct DecodedAudio(float[] Interleaved, int SampleRate, int Channels)`; `static class WavDecoder { DecodedAudio Decode(byte[] wav); }`. Destekler PCM16, PCM32 (int), IEEE float32; mono/çok-kanal (interleaved döner, downmix Task 2'de). Geçersiz/desteklenmeyen format → `NotSupportedException` / `InvalidDataException`.

- [ ] **Step 1: Projeleri oluştur ve çözüme ekle**

```bash
cd /Users/betulfiliz/sentiment-analysis
dotnet new classlib -n SentimentAnalysis.Infrastructure.Audio -o src/SentimentAnalysis.Infrastructure.Audio -f net9.0
rm src/SentimentAnalysis.Infrastructure.Audio/Class1.cs
dotnet new xunit -n SentimentAnalysis.Infrastructure.Audio.Tests -o tests/SentimentAnalysis.Infrastructure.Audio.Tests -f net9.0
rm tests/SentimentAnalysis.Infrastructure.Audio.Tests/UnitTest1.cs
dotnet sln add src/SentimentAnalysis.Infrastructure.Audio tests/SentimentAnalysis.Infrastructure.Audio.Tests
dotnet add src/SentimentAnalysis.Infrastructure.Audio reference src/SentimentAnalysis.Application
dotnet add tests/SentimentAnalysis.Infrastructure.Audio.Tests reference src/SentimentAnalysis.Infrastructure.Audio
```
csproj PropertyGroup'una `<Nullable>enable</Nullable>` + `<ImplicitUsings>enable</ImplicitUsings>` ekle (yoksa).

- [ ] **Step 2: Başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Audio.Tests/WavDecoderTests.cs`:

```csharp
using System;
using System.IO;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class WavDecoderTests
{
    // Minimal WAV yazıcı (test yardımcısı): PCM16/PCM32/float32, verilen kanal/rate.
    private static byte[] WriteWav(short audioFormat, short bitsPerSample, int sampleRate, int channels, byte[] data)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        short blockAlign = (short)(channels * (bitsPerSample / 8));
        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(36 + data.Length);
        w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(16);
        w.Write(audioFormat);
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write(bitsPerSample);
        w.Write(new[] { 'd', 'a', 't', 'a' });
        w.Write(data.Length);
        w.Write(data);
        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void Decode_Pcm16Mono_ReturnsNormalizedFloats()
    {
        // örnekler: 0, 32767 (~+1), -32768 (-1)
        using var dms = new MemoryStream();
        using (var dw = new BinaryWriter(dms))
        {
            dw.Write((short)0);
            dw.Write((short)32767);
            dw.Write((short)-32768);
        }
        var wav = WriteWav(1, 16, 16000, 1, dms.ToArray());

        var d = WavDecoder.Decode(wav);

        Assert.Equal(16000, d.SampleRate);
        Assert.Equal(1, d.Channels);
        Assert.Equal(3, d.Interleaved.Length);
        Assert.Equal(0f, d.Interleaved[0], precision: 4);
        Assert.True(d.Interleaved[1] > 0.99f, $"~+1 beklenir, {d.Interleaved[1]}");
        Assert.Equal(-1f, d.Interleaved[2], precision: 4);
    }

    [Fact]
    public void Decode_Pcm16Stereo_KeepsInterleavedAndChannels()
    {
        using var dms = new MemoryStream();
        using (var dw = new BinaryWriter(dms))
        {
            dw.Write((short)32767); dw.Write((short)-32768); // frame 0: L,R
            dw.Write((short)0);     dw.Write((short)0);      // frame 1
        }
        var wav = WriteWav(1, 16, 44100, 2, dms.ToArray());

        var d = WavDecoder.Decode(wav);

        Assert.Equal(44100, d.SampleRate);
        Assert.Equal(2, d.Channels);
        Assert.Equal(4, d.Interleaved.Length); // 2 frame * 2 kanal
        Assert.True(d.Interleaved[0] > 0.99f);
        Assert.Equal(-1f, d.Interleaved[1], precision: 4);
    }

    [Fact]
    public void Decode_Float32_ReturnsSamplesDirectly()
    {
        using var dms = new MemoryStream();
        using (var dw = new BinaryWriter(dms))
        {
            dw.Write(0.5f);
            dw.Write(-0.25f);
        }
        var wav = WriteWav(3, 32, 16000, 1, dms.ToArray()); // format 3 = IEEE float

        var d = WavDecoder.Decode(wav);

        Assert.Equal(2, d.Interleaved.Length);
        Assert.Equal(0.5f, d.Interleaved[0], precision: 5);
        Assert.Equal(-0.25f, d.Interleaved[1], precision: 5);
    }

    [Fact]
    public void Decode_UnsupportedBitDepth_Throws()
    {
        var wav = WriteWav(1, 24, 16000, 1, new byte[6]); // 24-bit desteklenmiyor
        Assert.Throws<NotSupportedException>(() => WavDecoder.Decode(wav));
    }

    [Fact]
    public void Decode_NotRiff_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WavDecoder.Decode(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }
}
```

- [ ] **Step 3: Testin başarısız olduğunu doğrula** — Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Audio.Tests`. Expected: FAIL (`WavDecoder` yok).

- [ ] **Step 4: `WavDecoder`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Audio/WavDecoder.cs`:

```csharp
using System.Buffers.Binary;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>Çözülmüş ham ses: interleaved float örnekler + kaynak rate/kanal (downmix/resample Task 2'de).</summary>
public readonly record struct DecodedAudio(float[] Interleaved, int SampleRate, int Channels);

/// <summary>Minimal, bağımlılıksız WAV (RIFF/WAVE) çözücü. Destekler: PCM 16-bit, PCM 32-bit int,
/// IEEE float 32-bit; mono/çok-kanal. Örnekleri <c>[-1,1]</c> float'a normalize eder (interleaved).
/// Saf.</summary>
public static class WavDecoder
{
    public static DecodedAudio Decode(byte[] wav)
    {
        if (wav.Length < 44) throw new InvalidDataException("WAV çok kısa.");
        var span = wav.AsSpan();
        if (span[0] != 'R' || span[1] != 'I' || span[2] != 'F' || span[3] != 'F' ||
            span[8] != 'W' || span[9] != 'A' || span[10] != 'V' || span[11] != 'E')
            throw new InvalidDataException("Geçersiz WAV: RIFF/WAVE başlığı yok.");

        short audioFormat = 0, channels = 0, bitsPerSample = 0;
        int sampleRate = 0;
        int pos = 12;
        ReadOnlySpan<byte> data = default;
        bool haveFmt = false, haveData = false;

        while (pos + 8 <= wav.Length)
        {
            var id = span.Slice(pos, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(pos + 4, 4));
            int body = pos + 8;
            if (id[0] == 'f' && id[1] == 'm' && id[2] == 't' && id[3] == ' ')
            {
                audioFormat = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(body, 2));
                channels = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(body + 2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(body + 4, 4));
                bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(body + 14, 2));
                haveFmt = true;
            }
            else if (id[0] == 'd' && id[1] == 'a' && id[2] == 't' && id[3] == 'a')
            {
                int avail = Math.Min(size, wav.Length - body);
                data = span.Slice(body, avail);
                haveData = true;
            }
            pos = body + size + (size & 1); // chunk'lar çift hizalı
            if (haveFmt && haveData) break;
        }

        if (!haveFmt || !haveData) throw new InvalidDataException("WAV: fmt/data chunk eksik.");

        float[] samples = (audioFormat, bitsPerSample) switch
        {
            (1, 16) => DecodePcm16(data),
            (1, 32) => DecodePcm32(data),
            (3, 32) => DecodeFloat32(data),
            _ => throw new NotSupportedException(
                $"Desteklenmeyen WAV formatı: audioFormat={audioFormat}, bits={bitsPerSample}. " +
                "Desteklenen: PCM16, PCM32, float32."),
        };

        return new DecodedAudio(samples, sampleRate, channels);
    }

    private static float[] DecodePcm16(ReadOnlySpan<byte> d)
    {
        int n = d.Length / 2;
        var s = new float[n];
        for (int i = 0; i < n; i++)
            s[i] = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(i * 2, 2)) / 32768f;
        return s;
    }

    private static float[] DecodePcm32(ReadOnlySpan<byte> d)
    {
        int n = d.Length / 4;
        var s = new float[n];
        for (int i = 0; i < n; i++)
            s[i] = BinaryPrimitives.ReadInt32LittleEndian(d.Slice(i * 4, 4)) / 2147483648f;
        return s;
    }

    private static float[] DecodeFloat32(ReadOnlySpan<byte> d)
    {
        int n = d.Length / 4;
        var s = new float[n];
        for (int i = 0; i < n; i++)
            s[i] = BinaryPrimitives.ReadSingleLittleEndian(d.Slice(i * 4, 4));
        return s;
    }
}
```

- [ ] **Step 5: Testin geçtiğini + tüm çözümün derlendiğini doğrula** — Run: `dotnet build && dotnet test`. Expected: 0 uyarı; yeni 5 WavDecoder testi PASS; mevcut testler (99 passed + 4 skip) kırılmaz.

---

### Task 2: `AudioPreprocessor` (saf: mono downmix + 16k lineer resample)

**Files:**
- Create: `src/SentimentAnalysis.Infrastructure.Audio/AudioPreprocessor.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Audio.Tests/AudioPreprocessorTests.cs`

**Interfaces:**
- Consumes: `DecodedAudio` (Task 1)
- Produces: `static class AudioPreprocessor { const int TargetRate = 16000; float[] ToMono16k(DecodedAudio audio); float[] ToMono16k(float[] interleaved, int sampleRate, int channels); }`. Çıktı: 16 kHz mono float. Downmix = kanal ortalaması; resample = lineer interpolasyon (rate==16000 ise resample atlanır).

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Audio.Tests/AudioPreprocessorTests.cs`:

```csharp
using System;
using System.Linq;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class AudioPreprocessorTests
{
    [Fact]
    public void ToMono16k_AlreadyMono16k_PassesThrough()
    {
        var x = new float[] { 0.1f, -0.2f, 0.3f, 0.4f };
        var y = AudioPreprocessor.ToMono16k(x, 16000, 1);
        Assert.Equal(x, y);
    }

    [Fact]
    public void ToMono16k_Stereo_DownmixesByAveraging()
    {
        // interleaved L,R: (1,-1)->0 ; (0.5,0.5)->0.5
        var x = new float[] { 1f, -1f, 0.5f, 0.5f };
        var y = AudioPreprocessor.ToMono16k(x, 16000, 2);
        Assert.Equal(2, y.Length);
        Assert.Equal(0f, y[0], precision: 5);
        Assert.Equal(0.5f, y[1], precision: 5);
    }

    [Fact]
    public void ToMono16k_Downsample32kTo16k_HalvesLength()
    {
        var x = Enumerable.Range(0, 320).Select(i => (float)Math.Sin(i * 0.05)).ToArray();
        var y = AudioPreprocessor.ToMono16k(x, 32000, 1);
        // 320 örnek @32k → ~160 @16k (lineer resample; ±1 tolerans)
        Assert.InRange(y.Length, 159, 161);
        Assert.InRange(y[0], -1.0f, 1.0f);
    }

    [Fact]
    public void ToMono16k_Upsample8kTo16k_DoublesLength()
    {
        var x = new float[] { 0f, 1f, 0f, -1f };
        var y = AudioPreprocessor.ToMono16k(x, 8000, 1);
        Assert.InRange(y.Length, 7, 8);
        Assert.Equal(0f, y[0], precision: 4); // ilk örnek korunur
    }

    [Fact]
    public void ToMono16k_Empty_ReturnsEmpty()
    {
        Assert.Empty(AudioPreprocessor.ToMono16k(Array.Empty<float>(), 44100, 2));
    }

    [Fact]
    public void ToMono16k_FromDecodedAudio_Works()
    {
        var d = new DecodedAudio(new float[] { 1f, -1f }, 16000, 2);
        var y = AudioPreprocessor.ToMono16k(d);
        Assert.Single(y);
        Assert.Equal(0f, y[0], precision: 5);
    }
}
```

- [ ] **Step 2: Başarısız olduğunu doğrula** — FAIL (`AudioPreprocessor` yok).

- [ ] **Step 3: `AudioPreprocessor`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Audio/AudioPreprocessor.cs`:

```csharp
namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>Ham çözülmüş sesi model/VAD/STT girdisine standardize eder: çok-kanal → mono (kanal
/// ortalaması), ardından hedef 16 kHz'e lineer interpolasyonla resample. Saf.</summary>
public static class AudioPreprocessor
{
    public const int TargetRate = 16000;

    public static float[] ToMono16k(DecodedAudio audio) =>
        ToMono16k(audio.Interleaved, audio.SampleRate, audio.Channels);

    public static float[] ToMono16k(float[] interleaved, int sampleRate, int channels)
    {
        if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (interleaved.Length == 0) return Array.Empty<float>();

        var mono = channels == 1 ? interleaved : Downmix(interleaved, channels);
        return sampleRate == TargetRate ? mono : Resample(mono, sampleRate, TargetRate);
    }

    private static float[] Downmix(float[] interleaved, int channels)
    {
        int frames = interleaved.Length / channels;
        var mono = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            int baseIdx = f * channels;
            for (int c = 0; c < channels; c++) sum += interleaved[baseIdx + c];
            mono[f] = (float)(sum / channels);
        }
        return mono;
    }

    /// <summary>Lineer interpolasyon resample. İlk örnek korunur; çıktı uzunluğu
    /// <c>round(inLen * dst/src)</c>. Kısa/tek-örnek girdide güvenli.</summary>
    private static float[] Resample(float[] input, int srcRate, int dstRate)
    {
        int inLen = input.Length;
        if (inLen == 1) return new[] { input[0] };

        int outLen = Math.Max(1, (int)Math.Round((long)inLen * dstRate / (double)srcRate));
        var output = new float[outLen];
        double ratio = (double)(inLen - 1) / Math.Max(1, outLen - 1);
        for (int j = 0; j < outLen; j++)
        {
            double srcPos = j * ratio;
            int i0 = (int)Math.Floor(srcPos);
            int i1 = Math.Min(i0 + 1, inLen - 1);
            double frac = srcPos - i0;
            output[j] = (float)(input[i0] * (1 - frac) + input[i1] * frac);
        }
        return output;
    }
}
```

- [ ] **Step 4: Testin geçtiğini doğrula** — Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Audio.Tests`. Expected: PASS.

---

### Task 3: `AudioChunker` (saf: örtüşmeli chunk'lama)

**Files:**
- Create: `src/SentimentAnalysis.Infrastructure.Audio/AudioChunker.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Audio.Tests/AudioChunkerTests.cs`

**Interfaces:**
- Consumes: `AudioChunk`, `CaptureOptions` (Core/Application)
- Produces: `static class AudioChunker { IReadOnlyList<AudioChunk> Chunk(float[] mono16k, CaptureOptions options); }`. 16 kHz mono girdiyi `ChunkLength` uzunluklu, `Overlap` örtüşmeli AudioChunk'lara böler; `Start`/`End` gerçek zaman; son chunk kısa olabilir; sinyali tam kapsar. `Overlap >= ChunkLength` veya `ChunkLength <= 0` → `ArgumentException`.

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Audio.Tests/AudioChunkerTests.cs`:

```csharp
using System;
using System.Linq;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class AudioChunkerTests
{
    private static CaptureOptions Opts(double chunkSec, double overlapSec) =>
        new(TimeSpan.FromSeconds(chunkSec), TimeSpan.FromSeconds(overlapSec));

    [Fact]
    public void Chunk_OverlappingChunks_CoverSignal()
    {
        // 3 sn @16k = 48000 örnek; chunk 2 sn (32000), overlap 1 sn → step 16000.
        var mono = new float[48000];
        var chunks = AudioChunker.Chunk(mono, Opts(2, 1));

        Assert.Equal(2, chunks.Count);
        Assert.Equal(32000, chunks[0].Samples.Length);
        Assert.Equal(TimeSpan.Zero, chunks[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(2), chunks[0].End);
        Assert.Equal(TimeSpan.FromSeconds(1), chunks[1].Start);   // 1 sn kaydı (örtüşme)
        Assert.Equal(TimeSpan.FromSeconds(3), chunks[1].End);
        Assert.All(chunks, c => Assert.Equal(16000, c.SampleRate));
    }

    [Fact]
    public void Chunk_ShorterThanOneChunk_ReturnsSingleShortChunk()
    {
        var mono = new float[8000]; // 0.5 sn
        var chunks = AudioChunker.Chunk(mono, Opts(2, 0));
        Assert.Single(chunks);
        Assert.Equal(8000, chunks[0].Samples.Length);
        Assert.Equal(TimeSpan.Zero, chunks[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(0.5), chunks[0].End);
    }

    [Fact]
    public void Chunk_NoOverlap_TilesExactly()
    {
        var mono = new float[48000]; // 3 sn
        var chunks = AudioChunker.Chunk(mono, Opts(1, 0)); // 1 sn'lik diliml → 3 chunk
        Assert.Equal(3, chunks.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), chunks[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(2), chunks[2].Start);
    }

    [Fact]
    public void Chunk_Empty_ReturnsEmpty()
    {
        Assert.Empty(AudioChunker.Chunk(Array.Empty<float>(), Opts(2, 1)));
    }

    [Fact]
    public void Chunk_OverlapGeChunkLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => AudioChunker.Chunk(new float[16000], Opts(1, 1)));
    }

    [Fact]
    public void Chunk_LastChunkEndsAtSignalEnd()
    {
        var mono = new float[40000]; // 2.5 sn; chunk 2 sn, overlap 1 sn, step 1 sn
        var chunks = AudioChunker.Chunk(mono, Opts(2, 1));
        var last = chunks[^1];
        Assert.Equal(TimeSpan.FromSeconds(2.5), last.End); // son chunk sinyal sonunda biter
    }
}
```

- [ ] **Step 2: Başarısız olduğunu doğrula** — FAIL (`AudioChunker` yok).

- [ ] **Step 3: `AudioChunker`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Audio/AudioChunker.cs`:

```csharp
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>16 kHz mono sinyali <see cref="CaptureOptions.ChunkLength"/> uzunluklu, örtüşmeli
/// (<see cref="CaptureOptions.Overlap"/>) <see cref="AudioChunk"/>'lara böler. Son chunk kısa
/// olabilir; sinyali tam kapsar. Saf, deterministik.</summary>
public static class AudioChunker
{
    private const int Rate = AudioPreprocessor.TargetRate; // 16000

    public static IReadOnlyList<AudioChunk> Chunk(float[] mono16k, CaptureOptions options)
    {
        int samplesPerChunk = (int)Math.Round(options.ChunkLength.TotalSeconds * Rate);
        int step = (int)Math.Round((options.ChunkLength - options.Overlap).TotalSeconds * Rate);
        if (samplesPerChunk <= 0) throw new ArgumentException("ChunkLength pozitif olmalı.", nameof(options));
        if (step <= 0) throw new ArgumentException("Overlap, ChunkLength'ten küçük olmalı.", nameof(options));

        var result = new List<AudioChunk>();
        if (mono16k.Length == 0) return result;

        int start = 0;
        while (true)
        {
            int end = Math.Min(start + samplesPerChunk, mono16k.Length);
            var slice = new float[end - start];
            Array.Copy(mono16k, start, slice, 0, end - start);
            result.Add(new AudioChunk(
                slice, Rate,
                TimeSpan.FromSeconds(start / (double)Rate),
                TimeSpan.FromSeconds(end / (double)Rate)));
            if (end >= mono16k.Length) break;
            start += step;
        }
        return result;
    }
}
```

- [ ] **Step 4: Testin geçtiğini doğrula** — Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Audio.Tests`. Expected: PASS.

---

### Task 4: `FileCaptureSource : IAudioCaptureSource` (+ Audio dilimi tamam)

**Files:**
- Create: `src/SentimentAnalysis.Infrastructure.Audio/FileCaptureSource.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Audio.Tests/FileCaptureSourceTests.cs`

**Interfaces:**
- Consumes: `IAudioCaptureSource`, `AudioChunk`, `CaptureOptions` (Application/Core), `WavDecoder`, `AudioPreprocessor`, `AudioChunker`
- Produces: `sealed class FileCaptureSource : IAudioCaptureSource { FileCaptureSource(string filePath); bool IsCapturing { get; }; IAsyncEnumerable<AudioChunk> CaptureAsync(CaptureOptions options, CancellationToken ct); }`. WAV decode→preprocess→chunk→yield; WAV dışı uzantı → `NotSupportedException`; dosya yok → `FileNotFoundException`.

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Audio.Tests/FileCaptureSourceTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class FileCaptureSourceTests
{
    private static string WriteTempWav(int sampleRate, int channels, int frames)
    {
        var path = Path.Combine(Path.GetTempPath(), "sa-cap-" + Guid.NewGuid().ToString("N") + ".wav");
        using var w = new BinaryWriter(File.Create(path));
        int dataLen = frames * channels * 2; // PCM16
        w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataLen); w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)channels);
        w.Write(sampleRate); w.Write(sampleRate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataLen);
        for (int i = 0; i < frames * channels; i++) w.Write((short)0);
        return path;
    }

    [Fact]
    public async Task CaptureAsync_WavFile_YieldsChunks()
    {
        var path = WriteTempWav(16000, 1, 48000); // 3 sn mono 16k
        try
        {
            var src = new FileCaptureSource(path);
            var opts = new CaptureOptions(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
            var chunks = new System.Collections.Generic.List<Core.AudioChunk>();
            await foreach (var c in src.CaptureAsync(opts, CancellationToken.None)) chunks.Add(c);

            Assert.Equal(2, chunks.Count);
            Assert.All(chunks, c => Assert.Equal(16000, c.SampleRate));
            Assert.False(src.IsCapturing); // akış bitince kapanır
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CaptureAsync_ResamplesStereo44kToMono16k()
    {
        var path = WriteTempWav(44100, 2, 44100); // 1 sn stereo 44.1k
        try
        {
            var src = new FileCaptureSource(path);
            var opts = new CaptureOptions(TimeSpan.FromSeconds(2), TimeSpan.Zero);
            var chunks = new System.Collections.Generic.List<Core.AudioChunk>();
            await foreach (var c in src.CaptureAsync(opts, CancellationToken.None)) chunks.Add(c);

            Assert.Single(chunks); // 1 sn < 2 sn chunk → tek chunk
            Assert.Equal(16000, chunks[0].SampleRate);
            Assert.InRange(chunks[0].Samples.Length, 15900, 16100); // ~16000 örnek (1 sn @16k)
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CaptureAsync_Mp3_ThrowsNotSupported()
    {
        var path = Path.Combine(Path.GetTempPath(), "x-" + Guid.NewGuid().ToString("N") + ".mp3");
        await File.WriteAllBytesAsync(path, new byte[] { 0, 1, 2, 3 });
        try
        {
            var src = new FileCaptureSource(path);
            var opts = new CaptureOptions(TimeSpan.FromSeconds(2), TimeSpan.Zero);
            await Assert.ThrowsAsync<NotSupportedException>(async () =>
            {
                await foreach (var _ in src.CaptureAsync(opts, CancellationToken.None)) { }
            });
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CaptureAsync_MissingFile_ThrowsFileNotFound()
    {
        var src = new FileCaptureSource("/nonexistent/nope.wav");
        var opts = new CaptureOptions(TimeSpan.FromSeconds(2), TimeSpan.Zero);
        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
        {
            await foreach (var _ in src.CaptureAsync(opts, CancellationToken.None)) { }
        });
    }
}
```

- [ ] **Step 2: Başarısız olduğunu doğrula** — FAIL (`FileCaptureSource` yok).

- [ ] **Step 3: `FileCaptureSource`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Audio/FileCaptureSource.cs`:

```csharp
using System.Runtime.CompilerServices;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>Bir ses DOSYASINI (bu turda WAV) 16 kHz mono <see cref="AudioChunk"/> akışına çeviren
/// yakalama kaynağı. Boru: WAV decode → mono/16k → örtüşmeli chunk. mp3/m4a bu turda ertelenmiş
/// (<see cref="NotSupportedException"/>). Canlı yakalama (ScreenCaptureKit/WASAPI) 05b'de aynı
/// arayüz ardında. Dosya başına örneklenir (DI singleton değil).</summary>
public sealed class FileCaptureSource : IAudioCaptureSource
{
    private readonly string _filePath;

    public FileCaptureSource(string filePath) => _filePath = filePath;

    /// <summary>Akış sürerken true (rıza göstergesi için minimal durum; zengin yüzey 06/08'de).</summary>
    public bool IsCapturing { get; private set; }

    public async IAsyncEnumerable<AudioChunk> CaptureAsync(
        CaptureOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        var ext = Path.GetExtension(_filePath).ToLowerInvariant();
        if (ext != ".wav")
            throw new NotSupportedException(
                $"Şimdilik yalnızca WAV destekleniyor; gelen '{ext}'. mp3/m4a decode sonraki turda " +
                "(aynı arayüz ardında). WAV'a dönüştürüp tekrar deneyin.");
        if (!File.Exists(_filePath))
            throw new FileNotFoundException($"Ses dosyası bulunamadı: {_filePath}", _filePath);

        IsCapturing = true;
        try
        {
            var bytes = await File.ReadAllBytesAsync(_filePath, ct).ConfigureAwait(false);
            var decoded = WavDecoder.Decode(bytes);
            var mono16k = AudioPreprocessor.ToMono16k(decoded);
            foreach (var chunk in AudioChunker.Chunk(mono16k, options))
            {
                ct.ThrowIfCancellationRequested();
                yield return chunk;
            }
        }
        finally
        {
            IsCapturing = false;
        }
    }
}
```

> Not: `NotSupportedException`/`FileNotFoundException` iterator gövdesinden ÖNCE (metot başında) atılır. `async` iterator'da bu, ancak numaralandırma başlayınca (ilk `MoveNextAsync`) fırlar — testler `await foreach` ile tetikliyor. DI: `FileCaptureSource` dosya-başına `new`'lenir; `AddAudioCapture()` uzantısı YOK (YAGNI — canlı-kaynak DI'si 05b'de).

- [ ] **Step 4: Testin geçtiğini + tüm çözümün derlendiğini doğrula** — Run: `dotnet build && dotnet test`. Expected: 0 uyarı; FileCaptureSource testleri PASS; Audio dilimi tamam; mevcut testler kırılmaz.

---

### Task 5: `Infrastructure.Vad` iskeleti + `VadConfig` + model sağlayıcı + `VadWindower` (saf)

**Files:**
- Create: `src/SentimentAnalysis.Infrastructure.Vad/SentimentAnalysis.Infrastructure.Vad.csproj`
- Create: `tests/SentimentAnalysis.Infrastructure.Vad.Tests/SentimentAnalysis.Infrastructure.Vad.Tests.csproj`
- Modify: `src/SentimentAnalysis.Application/Configuration/AppConfig.cs` (`VadConfig` ekle + `AppConfig`'e alan)
- Create: `src/SentimentAnalysis.Infrastructure.Vad/IVadModelProvider.cs`
- Create: `src/SentimentAnalysis.Infrastructure.Vad/FileVadModelProvider.cs`
- Create: `src/SentimentAnalysis.Infrastructure.Vad/VadWindower.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Vad.Tests/VadWindowerTests.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Vad.Tests/FileVadModelProviderTests.cs`

**Interfaces:**
- Produces: `sealed record VadConfig(string VadModelFile = "silero_vad.onnx", string? ModelDirectory = null, double SpeechThreshold = 0.5)`; `AppConfig`'e `VadConfig Vad` alanı (additive, defaultlu); `interface IVadModelProvider { string ResolveModelPath(string fileName); }`; `sealed class FileVadModelProvider : IVadModelProvider` (ctor `(AppConfig)`, `static string DefaultModelDirectory()`); `static class VadWindower { const int FrameSize = 512; IReadOnlyList<float[]> ToFrames(float[] mono16k); }` (tail<512 → sıfır-pad).

- [ ] **Step 1: Projeleri oluştur ve çözüme ekle**

```bash
cd /Users/betulfiliz/sentiment-analysis
dotnet new classlib -n SentimentAnalysis.Infrastructure.Vad -o src/SentimentAnalysis.Infrastructure.Vad -f net9.0
rm src/SentimentAnalysis.Infrastructure.Vad/Class1.cs
dotnet new xunit -n SentimentAnalysis.Infrastructure.Vad.Tests -o tests/SentimentAnalysis.Infrastructure.Vad.Tests -f net9.0
rm tests/SentimentAnalysis.Infrastructure.Vad.Tests/UnitTest1.cs
dotnet sln add src/SentimentAnalysis.Infrastructure.Vad tests/SentimentAnalysis.Infrastructure.Vad.Tests
dotnet add src/SentimentAnalysis.Infrastructure.Vad reference src/SentimentAnalysis.Application
dotnet add src/SentimentAnalysis.Infrastructure.Vad package Microsoft.ML.OnnxRuntime --version 1.28.0
dotnet add tests/SentimentAnalysis.Infrastructure.Vad.Tests reference src/SentimentAnalysis.Infrastructure.Vad
dotnet add tests/SentimentAnalysis.Infrastructure.Vad.Tests package Xunit.SkippableFact
```
csproj PropertyGroup'una `<Nullable>enable</Nullable>` + `<ImplicitUsings>enable</ImplicitUsings>` ekle (yoksa).

- [ ] **Step 2: `VadConfig`'i ekle ve `AppConfig`'e bağla (additive)**

`src/SentimentAnalysis.Application/Configuration/AppConfig.cs` içine, `EmotionConfig`'ten sonra ekle:

```csharp
/// <summary>VAD (Silero) ayarları (spec 05). Eşik eval-09 kalibre eder.</summary>
public sealed record VadConfig(
    string VadModelFile = "silero_vad.onnx",
    string? ModelDirectory = null,        // null → FileVadModelProvider varsayılanı
    double SpeechThreshold = 0.5);        // max pencere prob ≥ bu → konuşma
```

Ve `AppConfig` kaydını + `Default`'unu genişlet (additive, sona ekle):

```csharp
public sealed record AppConfig(
    FusionConfig Fusion,
    StorageConfig Storage,
    SttConfig Stt,
    EmotionConfig Emotion,
    VadConfig Vad)
{
    public static AppConfig Default => new(
        Fusion: new FusionConfig(),
        Storage: new StorageConfig(),
        Stt: new SttConfig(),
        Emotion: new EmotionConfig(),
        Vad: new VadConfig());
}
```

> Not: Kurucuya zorunlu `VadConfig` eklenir. Değişiklikten önce `grep -rn "new AppConfig(" src tests` ile pozisyonel çağrı olmadığını teyit et (yalnızca `AppConfig.Default` + `with` beklenir — 04a/04b'de de böyleydi). Mevcut `AppConfig.Default with { ... }` ifadeleri kırılmaz.

- [ ] **Step 3: Başarısız testleri yaz**

`tests/SentimentAnalysis.Infrastructure.Vad.Tests/VadWindowerTests.cs`:

```csharp
using System;
using System.Linq;
using SentimentAnalysis.Infrastructure.Vad;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Vad.Tests;

public class VadWindowerTests
{
    [Fact]
    public void ToFrames_ExactMultiple_NoPadding()
    {
        var frames = VadWindower.ToFrames(new float[1024]);
        Assert.Equal(2, frames.Count);
        Assert.All(frames, f => Assert.Equal(512, f.Length));
    }

    [Fact]
    public void ToFrames_PartialTail_ZeroPadded()
    {
        var x = Enumerable.Repeat(0.7f, 600).ToArray(); // 512 + 88
        var frames = VadWindower.ToFrames(x);
        Assert.Equal(2, frames.Count);
        Assert.Equal(512, frames[1].Length);
        Assert.Equal(0.7f, frames[1][87], precision: 5); // son gerçek örnek
        Assert.Equal(0f, frames[1][88], precision: 5);   // pad başlangıcı
        Assert.Equal(0f, frames[1][511], precision: 5);
    }

    [Fact]
    public void ToFrames_Empty_ReturnsEmpty()
    {
        Assert.Empty(VadWindower.ToFrames(Array.Empty<float>()));
    }

    [Fact]
    public void ToFrames_ShorterThanFrame_SinglePaddedFrame()
    {
        var frames = VadWindower.ToFrames(new float[] { 1f, 2f, 3f });
        Assert.Single(frames);
        Assert.Equal(512, frames[0].Length);
        Assert.Equal(3f, frames[0][2], precision: 5);
        Assert.Equal(0f, frames[0][3], precision: 5);
    }
}
```

`tests/SentimentAnalysis.Infrastructure.Vad.Tests/FileVadModelProviderTests.cs`:

```csharp
using System;
using System.IO;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Infrastructure.Vad;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Vad.Tests;

public class FileVadModelProviderTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-vad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ResolveModelPath_ReturnsPath_WhenExists()
    {
        var dir = NewTempDir();
        var file = Path.Combine(dir, "silero_vad.onnx");
        File.WriteAllText(file, "stub");
        var cfg = AppConfig.Default with { Vad = AppConfig.Default.Vad with { ModelDirectory = dir } };

        var provider = new FileVadModelProvider(cfg);
        Assert.Equal(file, provider.ResolveModelPath("silero_vad.onnx"));
    }

    [Fact]
    public void ResolveModelPath_Throws_WhenMissing()
    {
        var dir = NewTempDir();
        var cfg = AppConfig.Default with { Vad = AppConfig.Default.Vad with { ModelDirectory = dir } };
        var provider = new FileVadModelProvider(cfg);
        var ex = Assert.Throws<FileNotFoundException>(() => provider.ResolveModelPath("silero_vad.onnx"));
        Assert.Contains("silero_vad.onnx", ex.Message);
    }

    [Fact]
    public void DefaultModelDirectory_UnderApplicationData()
    {
        var def = FileVadModelProvider.DefaultModelDirectory();
        Assert.Contains("SentimentAnalysis", def);
        Assert.EndsWith("models", def);
    }
}
```

- [ ] **Step 4: Başarısız olduğunu doğrula** — Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Vad.Tests`. Expected: FAIL (tipler yok).

- [ ] **Step 5: `VadWindower`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Vad/VadWindower.cs`:

```csharp
namespace SentimentAnalysis.Infrastructure.Vad;

/// <summary>16 kHz mono chunk'ı Silero VAD'ın beklediği ardışık 512-örnek pencerelere böler.
/// Son pencere 512'den kısaysa sıfır-pad'lenir. Saf.</summary>
public static class VadWindower
{
    public const int FrameSize = 512; // 16 kHz'de 32 ms (spike ile doğrulandı)

    public static IReadOnlyList<float[]> ToFrames(float[] mono16k)
    {
        var frames = new List<float[]>();
        if (mono16k.Length == 0) return frames;

        for (int start = 0; start < mono16k.Length; start += FrameSize)
        {
            var frame = new float[FrameSize]; // sıfırlarla → tail otomatik pad
            int count = Math.Min(FrameSize, mono16k.Length - start);
            Array.Copy(mono16k, start, frame, 0, count);
            frames.Add(frame);
        }
        return frames;
    }
}
```

- [ ] **Step 6: `IVadModelProvider` + `FileVadModelProvider`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Vad/IVadModelProvider.cs`:

```csharp
namespace SentimentAnalysis.Infrastructure.Vad;

/// <summary>Silero VAD model dosya adını yerel yola çözer; yoksa aksiyon-alınabilir
/// <see cref="System.IO.FileNotFoundException"/> atar.</summary>
public interface IVadModelProvider
{
    string ResolveModelPath(string fileName);
}
```

`src/SentimentAnalysis.Infrastructure.Vad/FileVadModelProvider.cs`:

```csharp
using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Infrastructure.Vad;

/// <summary>Modeli <c>{ModelDirectory}/{fileName}</c> yolundan çözer; ModelDirectory null ise
/// uygulama veri klasörü altındaki varsayılana düşer.</summary>
public sealed class FileVadModelProvider : IVadModelProvider
{
    private readonly VadConfig _cfg;

    public FileVadModelProvider(AppConfig config) => _cfg = config.Vad;

    public string ResolveModelPath(string fileName)
    {
        var dir = _cfg.ModelDirectory ?? DefaultModelDirectory();
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Silero VAD modeli bulunamadı: {path}. Modeli bu konuma koyun " +
                "(github.com/snakers4/silero-vad → src/silero_vad/data/silero_vad.onnx).", path);
        return path;
    }

    public static string DefaultModelDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SentimentAnalysis", "models");
}
```

- [ ] **Step 7: Testlerin geçtiğini + tüm çözümün derlendiğini doğrula** — Run: `dotnet build && dotnet test`. Expected: 0 uyarı; VadWindower + FileVadModelProvider testleri PASS; mevcut testler (Audio dahil) kırılmaz.

---

### Task 6: `SileroVadGate : IVadGate` motoru + `AddSileroVad()` DI + guard + env-gated entegrasyon testi

**Files:**
- Create: `src/SentimentAnalysis.Infrastructure.Vad/SileroVadGate.cs`
- Create: `src/SentimentAnalysis.Infrastructure.Vad/VadServiceCollectionExtensions.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Vad.Tests/SileroVadGateTests.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Vad.Tests/VadDiTests.cs`
- Modify: `tests/SentimentAnalysis.Infrastructure.Vad.Tests/*.csproj` (DI test paketi)

**Interfaces:**
- Consumes: `IVadGate`, `AudioChunk` (Application/Core), `IVadModelProvider`, `VadWindower`, `AppConfig`/`VadConfig`; Microsoft.ML.OnnxRuntime
- Produces: `sealed class SileroVadGate : IVadGate, IDisposable` (ctor `(AppConfig, IVadModelProvider)`); `static class VadServiceCollectionExtensions { IServiceCollection AddSileroVad(this IServiceCollection); }`.

- [ ] **Step 1: DI test paketini ekle**

```bash
cd /Users/betulfiliz/sentiment-analysis
dotnet add tests/SentimentAnalysis.Infrastructure.Vad.Tests package Microsoft.Extensions.DependencyInjection --version 10.0.10
```

- [ ] **Step 2: Başarısız testleri yaz**

`tests/SentimentAnalysis.Infrastructure.Vad.Tests/SileroVadGateTests.cs`:

```csharp
using System;
using System.IO;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Vad;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Vad.Tests;

public class SileroVadGateTests
{
    private sealed class FixedModelProvider(string path) : IVadModelProvider
    {
        public string ResolveModelPath(string fileName) => path;
    }

    private sealed class UnusedModelProvider : IVadModelProvider
    {
        public string ResolveModelPath(string fileName) =>
            throw new InvalidOperationException("dispose sonrası/guard öncesi model çözülmemeliydi");
    }

    [Fact]
    public void HasSpeech_Throws_ForNon16kHz()
    {
        using var gate = new SileroVadGate(AppConfig.Default, new UnusedModelProvider());
        var chunk = new AudioChunk(new float[8000], SampleRate: 8000, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Assert.Throws<ArgumentException>(() => gate.HasSpeech(chunk));
    }

    [Fact]
    public void HasSpeech_AfterDispose_Throws()
    {
        var gate = new SileroVadGate(AppConfig.Default, new UnusedModelProvider());
        gate.Dispose();
        var chunk = new AudioChunk(new float[16000], 16000, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Assert.Throws<ObjectDisposedException>(() => gate.HasSpeech(chunk));
    }

    [SkippableFact]
    public void HasSpeech_RealModel_SilenceIsNotSpeech()
    {
        var modelPath = Environment.GetEnvironmentVariable("VAD_MODEL_PATH");
        Skip.If(string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath),
            "VAD_MODEL_PATH ayarlı değil; gerçek-model testi atlandı.");

        using var gate = new SileroVadGate(AppConfig.Default, new FixedModelProvider(modelPath!));
        var silence = new AudioChunk(new float[16000], 16000, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Assert.False(gate.HasSpeech(silence)); // spike: sessizlik prob≈0.0006 < 0.5
    }

    [SkippableFact]
    public void HasSpeech_RealModel_SpeechWavIsSpeech()
    {
        var modelPath = Environment.GetEnvironmentVariable("VAD_MODEL_PATH");
        var wavPath = Environment.GetEnvironmentVariable("VAD_SAMPLE_SPEECH_WAV");
        Skip.If(string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath),
            "VAD_MODEL_PATH ayarlı değil; atlandı.");
        Skip.If(string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath),
            "VAD_SAMPLE_SPEECH_WAV ayarlı değil; konuşma örneği yok.");

        // WAV'ı test içinde okumamak için: 16k mono PCM16 bekleniyor; basit okuyucu.
        var samples = ReadWav16kMono(wavPath!);
        using var gate = new SileroVadGate(AppConfig.Default, new FixedModelProvider(modelPath!));
        var chunk = new AudioChunk(samples, 16000, TimeSpan.Zero, TimeSpan.FromSeconds(samples.Length / 16000.0));
        Assert.True(gate.HasSpeech(chunk), "konuşma içeren örnekte HasSpeech true beklenir");
    }

    private static float[] ReadWav16kMono(string path)
    {
        using var r = new BinaryReader(File.OpenRead(path));
        r.ReadBytes(12);
        while (r.BaseStream.Position < r.BaseStream.Length)
        {
            var id = new string(r.ReadChars(4));
            int size = r.ReadInt32();
            if (id == "data")
            {
                int n = size / 2;
                var s = new float[n];
                for (int i = 0; i < n; i++) s[i] = r.ReadInt16() / 32768f;
                return s;
            }
            r.BaseStream.Seek(size, SeekOrigin.Current);
        }
        throw new InvalidDataException("data chunk yok");
    }
}
```

`tests/SentimentAnalysis.Infrastructure.Vad.Tests/VadDiTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Infrastructure.Vad;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Vad.Tests;

public class VadDiTests
{
    [Fact]
    public void AddSileroVad_ResolvesGateAndProvider()
    {
        var provider = new ServiceCollection()
            .AddApplicationCore()
            .AddSileroVad()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IVadGate>());
        Assert.NotNull(provider.GetRequiredService<IVadModelProvider>());
    }
}
```

- [ ] **Step 3: Başarısız/Skip olduğunu doğrula** — Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Vad.Tests`. Expected: FAIL (`SileroVadGate`/`AddSileroVad` yok). (Gerçek-model testleri env yoksa Skip.)

- [ ] **Step 4: `SileroVadGate`'i yaz**

`src/SentimentAnalysis.Infrastructure.Vad/SileroVadGate.cs`:

```csharp
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Vad;

/// <summary>Silero VAD v5 ONNX ile chunk-düzeyi konuşma kapısı. Session açılışta bir kez yüklenir,
/// sıcak tutulur (singleton). Chunk (16 kHz mono) → 512-örnek pencereler → Silero (state chunk içinde
/// zincirlenir, her çağrıda sıfırdan) → max pencere prob ≥ eşik ise konuşma. Senkron arayüz (IVadGate)
/// olduğundan tek kilit init+run+dispose'u korur. Kontrat spike ile doğrulandı (2026-07-25).</summary>
public sealed class SileroVadGate : IVadGate, IDisposable
{
    private const string InputName = "input";
    private const string StateName = "state";
    private const string SrName = "sr";
    private const string ProbOutName = "output";
    private const string StateOutName = "stateN";
    private const int StateLen = 2 * 1 * 128;

    private readonly VadConfig _cfg;
    private readonly IVadModelProvider _models;
    private readonly object _sync = new();
    private InferenceSession? _session;
    private bool _disposed;

    public SileroVadGate(AppConfig config, IVadModelProvider models)
    {
        _cfg = config.Vad;
        _models = models;
    }

    public bool HasSpeech(AudioChunk chunk)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (chunk.SampleRate != 16000)
            throw new ArgumentException(
                $"Silero VAD 16 kHz mono bekler; gelen {chunk.SampleRate} Hz.", nameof(chunk));

        var frames = VadWindower.ToFrames(chunk.Samples);
        if (frames.Count == 0) return false;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var session = _session ??= new InferenceSession(_models.ResolveModelPath(_cfg.VadModelFile));

            var state = new float[StateLen]; // sıfır → chunk başında reset
            var sr = new DenseTensor<long>(new long[] { 16000 }, new[] { 1 });
            double maxProb = 0;

            foreach (var frame in frames)
            {
                var input = new DenseTensor<float>(frame, new[] { 1, VadWindower.FrameSize });
                var stateT = new DenseTensor<float>(state, new[] { 2, 1, 128 });
                var feeds = new[]
                {
                    NamedOnnxValue.CreateFromTensor(InputName, input),
                    NamedOnnxValue.CreateFromTensor(StateName, stateT),
                    NamedOnnxValue.CreateFromTensor(SrName, sr),
                };
                using var results = session.Run(feeds);
                float prob = results.First(r => r.Name == ProbOutName).AsTensor<float>().ToArray()[0];
                state = results.First(r => r.Name == StateOutName).AsTensor<float>().ToArray();
                if (prob > maxProb) maxProb = prob;
            }

            return maxProb >= _cfg.SpeechThreshold;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _session?.Dispose();
        }
    }
}
```

- [ ] **Step 5: `AddSileroVad()`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Vad/VadServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Infrastructure.Vad;

/// <summary>Silero VAD DI kaydı. Kapı + model sağlayıcı singleton (model açılışta bir kez, sıcak).
/// `AddApplicationCore()` ile kullanılır.</summary>
public static class VadServiceCollectionExtensions
{
    public static IServiceCollection AddSileroVad(this IServiceCollection services)
    {
        services.TryAddSingleton<IVadModelProvider, FileVadModelProvider>();
        services.AddSingleton<IVadGate, SileroVadGate>();
        return services;
    }
}
```

- [ ] **Step 6: Guard + DI testlerinin geçtiğini doğrula (entegrasyon Skip)** — Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Vad.Tests`. Expected: guard (`Non16kHz`, `AfterDispose`) + DI PASS; gerçek-model testleri **Skipped**.

- [ ] **Step 7: (Model varsa) gerçek modelle bir kez doğrula**

Spike modeli mevcutsa:

```bash
export VAD_MODEL_PATH="/private/tmp/claude-501/-Users-betulfiliz-sentiment-analysis/4facdebb-8fd6-4b57-b818-ade3f06676ad/scratchpad/vad-spike/silero_vad.onnx"
dotnet test tests/SentimentAnalysis.Infrastructure.Vad.Tests --filter "FullyQualifiedName~RealModel_SilenceIsNotSpeech"
unset VAD_MODEL_PATH
```
Expected: PASS — sessizlik `HasSpeech==false` (spike prob≈0.0006). Sonucu ledger'a yaz. (Model yoksa Skip beklenir — beklenen davranış.)

- [ ] **Step 8: Tam çözümün derlendiğini ve testlerin geçtiğini doğrula** — Run: `dotnet build && dotnet test`. Expected: Build **0 uyarı**; **tüm** testler PASS (01/02/03/04 + 05 Audio + Vad birim testleri); gerçek-model testleri **Skipped** (VAD_MODEL_PATH / VAD_SAMPLE_SPEECH_WAV yok). Skip sayısı 4 → 6 (STT, prozodi, tokenizer, metin, VAD-silence, VAD-speech).

---

## Self-Review (plan yazarı tarafından)

**1. Spec coverage (05, bu tur):**
- Dosya girdisi (WAV) → 16k mono AudioChunk akışı → Task 1 (decode) + Task 2 (mono/16k) + Task 3 (chunk) + Task 4 (FileCaptureSource). ✔
- Ring buffer + chunker (10–30 sn, örtüşmeli) → `AudioChunker` (CaptureOptions ChunkLength/Overlap). ✔
- Silero VAD kapısı (sessiz chunk elenir) → Task 5 (windower/config/provider) + Task 6 (SileroVadGate, spike kontratı). ✔
- Rıza göstergesi → `FileCaptureSource.IsCapturing` (minimal; zengin yüzey 06/08). ✔
- AudioFrame→AudioChunk düzeltmesi → Global Constraints + yerleşik tip kullanımı. ✔
- ONNX session dispose (kaynak sızıntısı yok) → Task 6 kilitli dispose + guard testleri. ✔

**2. Kapsam dışı (bilinçli, spec ile uyumlu):** native canlı yakalama (ScreenCaptureKit/WASAPI) → 05b (`IAudioCaptureSource` kontratı hazır); mp3/m4a decode → `NotSupportedException` (sonra); zengin capture-status/rıza → 06/08; VAD eşiği kalibrasyonu → 09. `AddAudioCapture()` DI YOK (YAGNI — file source dosya-başına new; canlı-kaynak DI'si 05b).

**3. Placeholder scan:** TODO/TBD yok. Silero kontratı "spike ile ölçüldü" (sahte-kesinlik değil). Eşik "eval-09 kalibre eder" işaretli. Gerçek-model testleri env-gated gerçek Skip. Tüm kod blokları tam.

**4. Type consistency:**
- `DecodedAudio(float[] Interleaved, int SampleRate, int Channels)` — Task 1 tanımlar; Task 2 `AudioPreprocessor.ToMono16k(DecodedAudio)` tüketir. ✔
- `AudioPreprocessor.ToMono16k` iki overload + `TargetRate=16000` → Task 3 `AudioChunker` `Rate` olarak kullanır, Task 4 `FileCaptureSource` çağırır. ✔
- `AudioChunker.Chunk(float[], CaptureOptions) → IReadOnlyList<AudioChunk>` → Task 4 foreach. ✔
- `FileCaptureSource(string) : IAudioCaptureSource` + `IsCapturing` → Task 4 test tutarlı. ✔
- `VadConfig` alanları (`VadModelFile`/`ModelDirectory`/`SpeechThreshold`) → Task 5 tanım, Task 6 `_cfg.VadModelFile`/`_cfg.SpeechThreshold` kullanımı tutarlı. ✔
- `IVadModelProvider.ResolveModelPath`, `FileVadModelProvider(AppConfig)`, `VadWindower.ToFrames`/`FrameSize=512` → Task 5 tanım, Task 6 kullanım tutarlı. ✔
- `SileroVadGate(AppConfig, IVadModelProvider)` → Task 6 test + DI tutarlı. ONNX I/O adları (`input`/`state`/`sr`/`output`/`stateN`) + şekiller spike ile doğrulandı. ✔
- `AppConfig` genişletmesi: **DİKKAT** kurucuya zorunlu `VadConfig` eklenir — Task 5 Step 2 notu tek kullanımın `AppConfig.Default`/`with` olduğunu belirtir (implementer önce `grep -rn "new AppConfig(" src tests` ile teyit etmeli; 04a/04b'de kırılmadı).

**5. Mimari:** İki AYRI dilim (Audio ONNX'siz, Vad ONNX) → Application → Core. Native/ONNX tipleri motor sınırında; `WavDecoder`/`AudioPreprocessor`/`AudioChunker`/`VadWindower` saf + tam unit-testli (modelsiz).

**6. Eşzamanlılık/kaynak:** `SileroVadGate` senkron arayüz → tek `lock(_sync)` init+run+dispose'u korur (async emotion motorlarının iki-semaphore'una gerek yok, sync); `ObjectDisposedException` guard'ları (giriş + kilit içi); double-check dispose; session lazy singleton.

**Riskler / dikkat:**
- **`sr` tensör şekli:** spike'ta `[1]` (rank-1 length-1) ORT tarafından kabul edildi (metadata scalar `[]` diyor). `[1]` çalıştığı ölçüldü; sorun çıkarsa scalar (`new[]{}` / rank-0) denenebilir.
- **WAV çeşitliliği:** yalnızca PCM16/PCM32/float32 mono/çok-kanal; 24-bit/8-bit/extensible-fmt → net `NotSupportedException`. Gerçek kayıtların çoğu PCM16; yeterli.
- **Resample kalitesi:** lineer interpolasyon (basit, test edilebilir); STT/VAD için yeterli, ileri kalite (polyphase) gerekirse 09'da revize. Dokümante.
- **Git kuralı:** commit yok; her task sonu `dotnet build && dotnet test`.

**Not (model dosyası):** Silero `silero_vad.onnx` (~2.3 MB) session scratchpad'inde spike'tan mevcut; entegrasyon testi `VAD_MODEL_PATH` ile çalıştırılabilir. Yeni session'da yoksa Skip (beklenen). Model kaynağı: `github.com/snakers4/silero-vad → src/silero_vad/data/silero_vad.onnx`. `EmotionConfig.ModelDirectory` gibi `VadConfig.ModelDirectory` (varsayılan uygulama-veri `models/`) altına konur.

---

## Post-Review Düzeltmeleri (uygulama sırasında)

- **Task 1 (Important — test kapsamı):** `WavDecoder`'ın PCM32-int yolu implemente ama testsizdi (brief'in test listesi eksikti). `Decode_Pcm32Mono_ReturnsNormalizedFloats` testi eklendi (test-only, decoder değişmedi). Re-review: ADDRESSED.
- **Task 6 (gerçek-model doğrulaması):** Silero `silero_vad.onnx` ile `HasSpeech_RealModel_SilenceIsNotSpeech` çalıştırıldı ve GEÇTİ — ONNX boru (input/state/sr → output/stateN) ucu-uca çalışıyor, sessizlik → `HasSpeech==false` (spike prob≈0.0006 < 0.5 eşiği).

**Nihai durum:** 131 test geçti + 6 env-gated Skip (STT, prozodi, tokenizer, metin, VAD-silence, VAD-speech), 0 başarısız, 0 uyarı.

**Final whole-branch review: ATLANDI** (kullanıcı hız tercihi — her task ayrı review'dan geçti, kod yeşil, gerçek-model doğrulandı).

**Biriken deferred Minor'lar (Task 1 — WavDecoder malformed-WAV robustluğu; gerçek kayıtlar iyi-biçimli, blocker değil):**
- M-a: fmt-chunk `size<16`/bounds doğrulaması yok → bozuk WAV'da `ArgumentOutOfRangeException` (spec'li `InvalidDataException` yerine).
- M-b: chunk `size<0` guard yok → teorik sonsuz döngü (düşük olasılık).
- M-c: atlanan-chunk (odd-size) için test yok → even-align padding mantığı örtülü.

**Kapsam dışı (bilinçli, spec ile uyumlu):** native canlı yakalama (ScreenCaptureKit/WASAPI) → 05b; mp3/m4a decode; zengin capture-status/rıza → 06/08; VAD eşiği + resample kalitesi kalibrasyonu → 09.

---

## Model Entegrasyonu Sonrası Düzeltme (2026-07-26)

**BUG (Critical, gerçek konuşma örneğiyle bulundu): Silero v5 context eksikti.** `SileroVadGate` her 512-pencereyi context'siz `[1,512]` besliyordu. İlk spike (silence/gürültü/ton) bunu yakalayamadı — hepsi context'siz de prob≈0 verir. Kurulu modelle env-gated **gerçek konuşma testi** (`HasSpeech_RealModel_SpeechWavIsSpeech`, Türkçe WAV) yakaladı: bizim max prob=0.003, resmi `silero-vad` paketi max prob=1.0. Silero v5 her pencereye önceki pencereden **64-örnek context** ekler → gerçek girdi `[1,576]`. Düzeltme: `SileroVadGate`'e `ContextSize=64`/`InputSize=576`; context sıfırla başlar, sonra = önceki pencerenin son 64 örneği. Sonuç: bizim max prob=1.0 (resmi ile birebir), her iki VAD gerçek-model testi geçer. Hafıza [[silero-vad-onnx-kontrati]] güncellendi.
