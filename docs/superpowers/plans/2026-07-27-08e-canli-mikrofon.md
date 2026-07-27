# Alt-Proje 08e Uygulama Planı — Canlı Mikrofon Yakalama (Dilim 3)

> **Agentic worker'lar için:** GEREKLİ ALT-BECERİ: Bu planı task-task uygulamak için
> `superpowers:subagent-driven-development` (önerilen) veya `superpowers:executing-plans`
> kullanın. Adımlar takip için checkbox (`- [ ]`) sözdizimi kullanır.

**Hedef:** Mikrofondan canlı ses yakalayıp konuşurken analiz etmek — mevcut pipeline'a
dokunmadan, macOS AudioToolbox üzerinden, mikrofon izni dürüstçe ele alınarak.

**Architecture:** Native P/Invoke tek bir sınıfta (`MicrophoneCaptureSource`) motor sınırında
yaşar; etrafındaki her şey saf ve birim testlidir. Kaynak, mevcut `IAudioCaptureSource`
sözleşmesini (`IAsyncEnumerable<AudioChunk>`) uyguladığı için VAD, bounded channel, füzyon ve
oturum özeti hiç değişmeden çalışır. macOS mikrofon izni bir `.app` bundle gerektirdiğinden
paketleme de bu turun parçasıdır.

**Tech Stack:** .NET 9 · macOS AudioToolbox (AudioQueue C API, P/Invoke) ·
`System.Threading.Channels` · Avalonia 11.3.1 · xUnit 2.9.2 + Xunit.SkippableFact

**Spec:** [`08d-yuzde-uclu-gorunum-mikrofon.md`](../specs/subprojects/08d-yuzde-uclu-gorunum-mikrofon.md) (Dilim 3)
**Ölçüm:** [`08d-mikrofon-spike-olcumu.md`](../specs/subprojects/08d-mikrofon-spike-olcumu.md)

## Global Kısıtlar

Her task'ın gereksinimleri örtük olarak bu bölümü içerir.

- **Test kapısı:** Her task `dotnet build && dotnet test` ile biter. Başlangıç: **254 geçer,
  6 atlanır, 0 başarısız, 0 uyarı.** Bu sayı düşmez, yeni uyarı eklenmez.
- **Ölçülmüş kontrat — tahmin değil.** Aşağıdaki değerler gerçek donanımda ölçüldü, aynen
  kullanılacak:
  - `mSampleRate = 16000`, `mFormatID = 0x6C70636D` ('lpcm'), `mFormatFlags = 0xC`,
    `mBytesPerPacket = 2`, `mFramesPerPacket = 1`, `mBytesPerFrame = 2`,
    `mChannelsPerFrame = 1`, `mBitsPerChannel = 16`
  - 3 tampon × 8192 bayt
  - `AudioQueueNewInput`'a `runLoop`/`runLoopMode` olarak `IntPtr.Zero` geçilir
  - **Yeniden örnekleme YAZILMAZ** — AudioQueue 48 kHz cihazdan 16 kHz'e kendisi dönüştürür
- **Delegate pinlenmeli:** callback delegate'i `GCHandle.Alloc` ile tutulur ve kaynak
  dispose edilene kadar serbest bırakılmaz. Aksi halde GC delegate'i toplar ve native taraf
  çöker.
- **İzin reddi SESSİZDİR:** izin yoksa `AudioQueueNewInput` hata dönmez; akış gelir ama
  örnekler sıfırdır. Bu tespit edilip kullanıcıya dürüstçe söylenmeli — sessizce boş analiz
  üretmek yasak.
- **Sahte-yeşil yasak:** gerçek mikrofon testi `[SkippableFact]` + `Skip.If(...)` ile
  `MIC_TEST=1` ortam değişkenine bağlıdır. Mikrofonsuz/izinsiz ortamda atlanır.
- **Gerçek-mikrofon testi mutlak eşik kullanmaz.** Spike'ta ölçüldü: sessiz oda tabanı
  0,0098–0,0168; konuşma 0,0805. Test "sessizliğe göre oran" mantığı kurmalı, "0,05'ten
  büyükse ses vardır" gibi kırılgan bir eşik değil.
- **Mimari yönü:** `Core` bağımsız; native tipler yalnızca `MicrophoneCaptureSource` ve
  `AudioQueueInterop` içinde. Pipeline'a (`Application/Pipeline/`) dokunulmaz.
- **`AppConfig` genişletmeleri additive** (varsayılanlı).
- **Türkçe:** kod yorumları, XML doc ve UI metni Türkçe; `Core` enum'ları dil-bağımsız.
- **Platform:** macOS-önce. Windows (WASAPI) aynı arayüzün ikinci implementasyonu olarak
  sonraki tura kalır. macOS dışında `MicrophoneCaptureSource` anlaşılır bir
  `PlatformNotSupportedException` fırlatır.

## Dosya Yapısı

| Dosya | Sorumluluk | Task |
|---|---|---|
| `Infrastructure.Audio/Pcm16Converter.cs` | **Yeni.** PCM16 bayt → float örnek (saf) | 1 |
| `Infrastructure.Audio/StreamingChunkBuffer.cs` | **Yeni.** Akan örnek → örtüşmeli chunk (saf) | 1 |
| `Infrastructure.Audio/SilenceWatchdog.cs` | **Yeni.** Ölü akış tespiti (saf) | 2 |
| `Infrastructure.Audio/AudioQueueInterop.cs` | **Yeni.** P/Invoke bildirimleri, native yüzey | 3 |
| `Infrastructure.Audio/MicrophoneCaptureSource.cs` | **Yeni.** `IAudioCaptureSource` motoru | 3 |
| `Desktop.Avalonia/Info.plist` | **Yeni.** Bundle üst-verisi + mikrofon gerekçesi | 4 |
| `build-macos-app.sh` | **Yeni.** `.app` paketleme betiği | 4 |
| `Desktop/CaptureMode.cs` | **Yeni.** Kaynak modu (enum + paylaşılan durum) | 5 |
| `Desktop/LiveViewModel.cs` | **Değişir.** Mod seçimi, `CanStart` | 5 |
| `Desktop.Avalonia/CompositionRoot.cs` | **Değişir.** Moda göre kaynak üretimi | 5 |
| `Desktop.Avalonia/PipelineAnalysisRunner.cs` | **Değişir.** Mikrofonda kısa chunk | 5 |
| `Desktop.Avalonia/Views/LiveView.axaml` | **Değişir.** Dosya/Mikrofon seçimi | 5 |

---

## Task 1: Akış yardımcıları (saf, Infrastructure.Audio)

**Dosyalar:**
- Oluştur: `src/SentimentAnalysis.Infrastructure.Audio/Pcm16Converter.cs`
- Oluştur: `src/SentimentAnalysis.Infrastructure.Audio/StreamingChunkBuffer.cs`
- Test: `tests/SentimentAnalysis.Infrastructure.Audio.Tests/Pcm16ConverterTests.cs` (yeni)
- Test: `tests/SentimentAnalysis.Infrastructure.Audio.Tests/StreamingChunkBufferTests.cs` (yeni)

**Arayüzler:**
- Tüketir: `AudioChunk(float[] Samples, int SampleRate, TimeSpan Start, TimeSpan End)`,
  `CaptureOptions(TimeSpan ChunkLength, TimeSpan Overlap)`
- Üretir:
  - `Pcm16Converter.ToFloat(ReadOnlySpan<byte> pcm16le, Span<float> destination) → int`
  - `StreamingChunkBuffer(int sampleRate, TimeSpan chunkLength, TimeSpan overlap)`,
    `.Append(ReadOnlySpan<float>) → IReadOnlyList<AudioChunk>`, `.Flush() → AudioChunk?`

- [ ] **Adım 1: `Pcm16Converter` testini yaz**

`tests/SentimentAnalysis.Infrastructure.Audio.Tests/Pcm16ConverterTests.cs`:

```csharp
using System;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class Pcm16ConverterTests
{
    [Fact]
    public void ToFloat_ReturnsSampleCount()
    {
        var bytes = new byte[8];
        var dest = new float[4];

        Assert.Equal(4, Pcm16Converter.ToFloat(bytes, dest));
    }

    [Fact]
    public void ToFloat_ZeroBytes_AreSilence()
    {
        var dest = new float[2];

        Pcm16Converter.ToFloat(new byte[4], dest);

        Assert.All(dest, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void ToFloat_ReadsLittleEndian()
    {
        // 0x0100 little-endian = 256
        var bytes = new byte[] { 0x00, 0x01 };
        var dest = new float[1];

        Pcm16Converter.ToFloat(bytes, dest);

        Assert.Equal(256f / 32768f, dest[0], precision: 6);
    }

    [Fact]
    public void ToFloat_HandlesNegativeSamples()
    {
        // -32768 = 0x8000 little-endian
        var bytes = new byte[] { 0x00, 0x80 };
        var dest = new float[1];

        Pcm16Converter.ToFloat(bytes, dest);

        Assert.Equal(-1.0f, dest[0], precision: 6);
    }

    [Fact]
    public void ToFloat_MaxPositive_IsJustUnderOne()
    {
        // 32767 = 0xFF7F little-endian
        var bytes = new byte[] { 0xFF, 0x7F };
        var dest = new float[1];

        Pcm16Converter.ToFloat(bytes, dest);

        Assert.True(dest[0] > 0.999f && dest[0] < 1.0f);
    }

    [Fact]
    public void ToFloat_OddByteCount_Throws()
    {
        Assert.Throws<ArgumentException>(() => Pcm16Converter.ToFloat(new byte[3], new float[2]));
    }

    [Fact]
    public void ToFloat_DestinationTooSmall_Throws()
    {
        Assert.Throws<ArgumentException>(() => Pcm16Converter.ToFloat(new byte[8], new float[3]));
    }
}
```

- [ ] **Adım 2: Testi çalıştır, DERLENMEDİĞİNİ doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Infrastructure.Audio.Tests`
Beklenen: Derleme hatası — `The name 'Pcm16Converter' does not exist`.

- [ ] **Adım 3: `Pcm16Converter`'ı yaz**

```csharp
using System.Buffers.Binary;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>Ham PCM16 (little-endian, işaretli) baytları −1..+1 aralığında float örneklere çevirir.
/// AudioQueue'nun verdiği format budur (spike ile ölçüldü). Saf, tahsissiz, deterministik.</summary>
public static class Pcm16Converter
{
    /// <summary>Böleni 32768'dir: −32768 tam olarak −1.0'a, +32767 ise 1.0'ın hemen altına düşer.
    /// Asimetri kasıtlıdır — PCM16'nın kendi asimetrisidir, kırpma yapılmaz.</summary>
    private const float Scale = 32768f;

    /// <returns>Yazılan örnek sayısı (<paramref name="pcm16le"/> uzunluğunun yarısı).</returns>
    public static int ToFloat(ReadOnlySpan<byte> pcm16le, Span<float> destination)
    {
        if (pcm16le.Length % 2 != 0)
            throw new ArgumentException(
                $"PCM16 bayt sayısı çift olmalı; gelen {pcm16le.Length}.", nameof(pcm16le));

        int count = pcm16le.Length / 2;
        if (destination.Length < count)
            throw new ArgumentException(
                $"Hedef tampon küçük: {count} örnek gerekiyor, {destination.Length} verildi.",
                nameof(destination));

        for (int i = 0; i < count; i++)
            destination[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm16le.Slice(i * 2, 2)) / Scale;

        return count;
    }
}
```

- [ ] **Adım 4: Testleri çalıştır**

Çalıştır: `dotnet test tests/SentimentAnalysis.Infrastructure.Audio.Tests`
Beklenen: PASS — 7 yeni test.

- [ ] **Adım 5: `StreamingChunkBuffer` testini yaz**

`tests/SentimentAnalysis.Infrastructure.Audio.Tests/StreamingChunkBufferTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class StreamingChunkBufferTests
{
    private const int Rate = 16000;

    private static StreamingChunkBuffer New(double chunkSeconds = 1.0, double overlapSeconds = 0.25) =>
        new(Rate, TimeSpan.FromSeconds(chunkSeconds), TimeSpan.FromSeconds(overlapSeconds));

    private static float[] Ramp(int count, int startValue = 0)
    {
        var a = new float[count];
        for (int i = 0; i < count; i++) a[i] = startValue + i;
        return a;
    }

    [Fact]
    public void Append_BelowChunkLength_EmitsNothing()
    {
        var buffer = New();

        Assert.Empty(buffer.Append(new float[Rate - 1]));
    }

    [Fact]
    public void Append_ExactlyChunkLength_EmitsOneChunk()
    {
        var buffer = New();

        var chunks = buffer.Append(new float[Rate]);

        var chunk = Assert.Single(chunks);
        Assert.Equal(Rate, chunk.Samples.Length);
        Assert.Equal(Rate, chunk.SampleRate);
        Assert.Equal(TimeSpan.Zero, chunk.Start);
        Assert.Equal(TimeSpan.FromSeconds(1), chunk.End);
    }

    [Fact]
    public void Append_AccumulatesAcrossCalls()
    {
        var buffer = New();

        Assert.Empty(buffer.Append(new float[Rate / 2]));
        var chunks = buffer.Append(new float[Rate / 2]);

        Assert.Single(chunks);
    }

    [Fact]
    public void Append_SecondChunkStartsAfterStep_NotAfterFullChunk()
    {
        // chunk 1.0 sn, overlap 0.25 sn → adım 0.75 sn
        var buffer = New();
        buffer.Append(new float[Rate]);              // 1. chunk: 0.00 - 1.00

        var chunks = buffer.Append(new float[(int)(Rate * 0.75)]);

        var chunk = Assert.Single(chunks);
        Assert.Equal(TimeSpan.FromSeconds(0.75), chunk.Start);
        Assert.Equal(TimeSpan.FromSeconds(1.75), chunk.End);
    }

    [Fact]
    public void Append_OverlapRegion_IsActuallyRepeated()
    {
        var buffer = New();
        var first = buffer.Append(Ramp(Rate))[0];
        var second = buffer.Append(Ramp((int)(Rate * 0.75), startValue: Rate))[0];

        // 1. chunk'ın son 0.25 sn'si = 2. chunk'ın ilk 0.25 sn'si
        int overlapCount = (int)(Rate * 0.25);
        var tailOfFirst = first.Samples.Skip(first.Samples.Length - overlapCount).ToArray();
        var headOfSecond = second.Samples.Take(overlapCount).ToArray();

        Assert.Equal(tailOfFirst, headOfSecond);
    }

    [Fact]
    public void Append_LongInput_EmitsMultipleChunksAtOnce()
    {
        var buffer = New();

        // 2.5 sn veri, 0.75 sn adım → 0.00, 0.75, 1.50 başlangıçlı 3 tam chunk
        var chunks = buffer.Append(new float[(int)(Rate * 2.5)]);

        Assert.Equal(3, chunks.Count);
        Assert.Equal(TimeSpan.Zero, chunks[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(0.75), chunks[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(1.50), chunks[2].Start);
    }

    [Fact]
    public void Flush_EmitsRemainderNotYetCovered()
    {
        var buffer = New();
        buffer.Append(new float[Rate]);                    // kapsanan: 0.00 - 1.00
        buffer.Append(new float[(int)(Rate * 0.5)]);       // toplam 1.5 sn, yeni chunk yok

        var tail = buffer.Flush();

        Assert.NotNull(tail);
        Assert.Equal(TimeSpan.FromSeconds(1.5), tail!.End);
    }

    [Fact]
    public void Flush_WhenEverythingAlreadyCovered_ReturnsNull()
    {
        var buffer = New();
        buffer.Append(new float[Rate]);   // tam olarak 1 chunk, artık yok

        Assert.Null(buffer.Flush());
    }

    [Fact]
    public void Flush_OnEmptyBuffer_ReturnsNull()
    {
        Assert.Null(New().Flush());
    }

    [Fact]
    public void Ctor_OverlapNotLessThanChunk_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new StreamingChunkBuffer(Rate, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Ctor_NonPositiveChunkLength_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new StreamingChunkBuffer(Rate, TimeSpan.Zero, TimeSpan.Zero));
    }

    [Fact]
    public void NoSampleIsLost_AcrossManyRandomSizedAppends()
    {
        var buffer = New();
        var emitted = new List<AudioChunk>();
        int total = 0;
        // 100 × 700 örnek = 70.000 örnek (4,375 sn)
        for (int i = 0; i < 100; i++)
        {
            emitted.AddRange(buffer.Append(Ramp(700, startValue: total)));
            total += 700;
        }
        var tail = buffer.Flush();
        if (tail is not null) emitted.Add(tail);

        // Her chunk'ın kapsadığı aralıkların birleşimi [0, total) olmalı — boşluk yok.
        double lastEnd = 0;
        foreach (var chunk in emitted)
        {
            Assert.True(chunk.Start.TotalSeconds <= lastEnd + 1e-9,
                $"boşluk var: {chunk.Start.TotalSeconds} > {lastEnd}");
            lastEnd = Math.Max(lastEnd, chunk.End.TotalSeconds);
        }
        Assert.Equal(total / (double)Rate, lastEnd, precision: 6);
    }
}
```

- [ ] **Adım 6: Testi çalıştır, DERLENMEDİĞİNİ doğrula**

Beklenen: `The name 'StreamingChunkBuffer' does not exist`.

- [ ] **Adım 7: `StreamingChunkBuffer`'ı yaz**

```csharp
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>
/// Sürekli akan örnekleri biriktirip chunk sınırına ulaşınca örtüşmeli
/// <see cref="AudioChunk"/> üretir. <see cref="AudioChunker"/> dizi tabanlıdır (tüm ses elde
/// olduğunda çalışır); bu sınıf canlı akış içindir.
/// </summary>
/// <remarks>
/// Zaman damgaları akışın başından itibaren MUTLAK örnek indeksinden hesaplanır, böylece
/// zaman çizelgesi kayması olmaz. Saf ve deterministik — iş parçacığı güvenliği YOKTUR,
/// çağıran tek bir üreticiden beslemelidir.
/// </remarks>
public sealed class StreamingChunkBuffer
{
    private readonly int _sampleRate;
    private readonly int _samplesPerChunk;
    private readonly int _step;
    private readonly List<float> _pending = new();

    /// <summary>_pending[0]'ın akış başından itibaren mutlak örnek indeksi.</summary>
    private long _absoluteStart;

    /// <summary>Şimdiye kadar üretilmiş chunk'ların kapsadığı en son mutlak indeks.</summary>
    private long _coveredUntil;

    public StreamingChunkBuffer(int sampleRate, TimeSpan chunkLength, TimeSpan overlap)
    {
        _sampleRate = sampleRate;
        _samplesPerChunk = (int)Math.Round(chunkLength.TotalSeconds * sampleRate);
        _step = (int)Math.Round((chunkLength - overlap).TotalSeconds * sampleRate);

        if (_samplesPerChunk <= 0)
            throw new ArgumentException("ChunkLength pozitif olmalı.", nameof(chunkLength));
        if (_step <= 0)
            throw new ArgumentException("Overlap, ChunkLength'ten küçük olmalı.", nameof(overlap));
    }

    /// <summary>Gelen örnekleri ekler ve tamamlanan chunk'ları döndürür (yoksa boş liste).</summary>
    public IReadOnlyList<AudioChunk> Append(ReadOnlySpan<float> samples)
    {
        foreach (var s in samples) _pending.Add(s);

        var produced = new List<AudioChunk>();
        while (_pending.Count >= _samplesPerChunk)
        {
            produced.Add(Take(_samplesPerChunk));
            _pending.RemoveRange(0, _step);
            _absoluteStart += _step;
        }
        return produced;
    }

    /// <summary>Akış bittiğinde henüz hiçbir chunk'ın kapsamadığı artık varsa onu döndürür.</summary>
    public AudioChunk? Flush()
    {
        if (_pending.Count == 0) return null;
        if (_absoluteStart + _pending.Count <= _coveredUntil) return null;
        return Take(_pending.Count);
    }

    private AudioChunk Take(int count)
    {
        var slice = new float[count];
        _pending.CopyTo(0, slice, 0, count);

        long end = _absoluteStart + count;
        _coveredUntil = Math.Max(_coveredUntil, end);

        return new AudioChunk(
            slice, _sampleRate,
            TimeSpan.FromSeconds(_absoluteStart / (double)_sampleRate),
            TimeSpan.FromSeconds(end / (double)_sampleRate));
    }
}
```

- [ ] **Adım 8: Testleri çalıştır**

Beklenen: PASS — 12 yeni test.

- [ ] **Adım 9: Tüm testleri çalıştır**

Çalıştır: `dotnet build && dotnet test`
Beklenen: 0 başarısız, 254 → 273.

- [ ] **Adım 10: Commit**

```bash
git add src/SentimentAnalysis.Infrastructure.Audio/Pcm16Converter.cs \
        src/SentimentAnalysis.Infrastructure.Audio/StreamingChunkBuffer.cs \
        tests/SentimentAnalysis.Infrastructure.Audio.Tests/
git commit -m "feat(audio): canlı akış için PCM16 dönüştürücü ve örtüşmeli chunk tamponu"
```

---

## Task 2: Ölü akış nöbetçisi (saf)

> **Neden var:** Spike'ta ölçüldü — mikrofon izni yoksa `AudioQueueNewInput` **hata dönmez**.
> Akış normal hızda gelir, sadece örnekler sessizdir. Bu sınıf olmadan uygulama sessizce
> boş analiz üretir ve kullanıcı neden hiçbir şey olmadığını anlayamaz.

**Dosyalar:**
- Oluştur: `src/SentimentAnalysis.Infrastructure.Audio/SilenceWatchdog.cs`
- Test: `tests/SentimentAnalysis.Infrastructure.Audio.Tests/SilenceWatchdogTests.cs` (yeni)

**Arayüzler:**
- Üretir: `SilenceWatchdog(int sampleRate, TimeSpan window, double silenceCeiling = 0.001)`,
  `.Observe(ReadOnlySpan<float>)`, `.WindowElapsed`, `.LooksDead`, `.PeakSeen`

- [ ] **Adım 1: Testi yaz**

```csharp
using System;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class SilenceWatchdogTests
{
    private const int Rate = 16000;

    private static SilenceWatchdog New() => new(Rate, TimeSpan.FromSeconds(2));

    private static float[] Constant(int count, float value)
    {
        var a = new float[count];
        Array.Fill(a, value);
        return a;
    }

    [Fact]
    public void BeforeWindowElapses_IsNotDeclaredDead()
    {
        var w = New();

        w.Observe(new float[Rate]);   // 1 sn dijital sessizlik, pencere 2 sn

        Assert.False(w.WindowElapsed);
        Assert.False(w.LooksDead);
    }

    [Fact]
    public void DigitalSilenceForFullWindow_LooksDead()
    {
        var w = New();

        w.Observe(new float[Rate * 2]);

        Assert.True(w.WindowElapsed);
        Assert.True(w.LooksDead);
    }

    // Spike ölçümü: izin VERİLMİŞ sessiz odanın tabanı 0,0098-0,0168.
    // Bu "ölü akış" DEĞİLDİR — yalnızca sessiz bir oda.
    [Fact]
    public void QuietRoomNoiseFloor_IsNotDead()
    {
        var w = New();

        w.Observe(Constant(Rate * 2, 0.0098f));

        Assert.True(w.WindowElapsed);
        Assert.False(w.LooksDead);
    }

    [Fact]
    public void Speech_IsNotDead()
    {
        var w = New();

        w.Observe(Constant(Rate * 2, 0.0805f));

        Assert.False(w.LooksDead);
    }

    [Fact]
    public void PeakSeen_TracksAbsoluteMaximum()
    {
        var w = New();

        w.Observe(new[] { 0.2f, -0.7f, 0.3f });

        Assert.Equal(0.7, w.PeakSeen, precision: 6);
    }

    [Fact]
    public void SingleLoudSampleInSilentWindow_ClearsTheAlarm()
    {
        var w = New();

        w.Observe(new float[Rate]);
        w.Observe(new[] { 0.5f });
        w.Observe(new float[Rate]);

        Assert.True(w.WindowElapsed);
        Assert.False(w.LooksDead);
    }
}
```

- [ ] **Adım 2: Testi çalıştır, DERLENMEDİĞİNİ doğrula**

- [ ] **Adım 3: `SilenceWatchdog`'u yaz**

```csharp
namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>
/// Akışın <b>tamamen ölü</b> olup olmadığını izler — macOS mikrofon izni reddedildiğinde
/// AudioQueue hata dönmez, normal hızda sessiz tampon gönderir. Bu sınıf o durumu yakalar.
/// </summary>
/// <remarks>
/// Eşik (<c>silenceCeiling</c>) DİJİTAL sessizliği ayırt etmek içindir, "konuşma var mı"
/// sorusunu yanıtlamaz — o iş VAD'ındır (<c>SileroVadGate</c>). Spike ölçümü (2026-07-27):
/// izin verilmiş sessiz odanın tabanı 0,0098-0,0168; konuşma 0,0805; izin reddinde ise
/// örnekler tam sıfırdır. Varsayılan 0,001 bu üçünü doğru ayırır.
/// Saf ve deterministik.
/// </remarks>
public sealed class SilenceWatchdog
{
    private readonly long _windowSamples;
    private readonly double _silenceCeiling;
    private long _seenSamples;

    public SilenceWatchdog(int sampleRate, TimeSpan window, double silenceCeiling = 0.001)
    {
        _windowSamples = (long)Math.Round(window.TotalSeconds * sampleRate);
        _silenceCeiling = silenceCeiling;
    }

    /// <summary>Gözlem penceresi doldu mu (karar vermek için yeterli veri geldi mi)?</summary>
    public bool WindowElapsed => _seenSamples >= _windowSamples;

    /// <summary>Görülen en büyük mutlak genlik.</summary>
    public double PeakSeen { get; private set; }

    /// <summary>Pencere doldu VE hiçbir zaman dijital sessizliğin üstüne çıkılmadı.</summary>
    public bool LooksDead => WindowElapsed && PeakSeen <= _silenceCeiling;

    public void Observe(ReadOnlySpan<float> samples)
    {
        foreach (var s in samples)
        {
            double a = Math.Abs(s);
            if (a > PeakSeen) PeakSeen = a;
        }
        _seenSamples += samples.Length;
    }
}
```

- [ ] **Adım 4: Testleri çalıştır**

Beklenen: PASS — 6 yeni test.

- [ ] **Adım 5: Tüm testleri çalıştır**

Beklenen: 273 → 279, 0 başarısız.

- [ ] **Adım 6: Commit**

```bash
git add src/SentimentAnalysis.Infrastructure.Audio/SilenceWatchdog.cs \
        tests/SentimentAnalysis.Infrastructure.Audio.Tests/SilenceWatchdogTests.cs
git commit -m "feat(audio): sessiz izin reddini yakalayan ölü-akış nöbetçisi"
```

---

## Task 3: Mikrofon yakalama motoru (native)

**Dosyalar:**
- Oluştur: `src/SentimentAnalysis.Infrastructure.Audio/AudioQueueInterop.cs`
- Oluştur: `src/SentimentAnalysis.Infrastructure.Audio/MicrophoneCaptureSource.cs`
- Test: `tests/SentimentAnalysis.Infrastructure.Audio.Tests/MicrophoneCaptureSourceTests.cs` (yeni)

**Arayüzler:**
- Tüketir: `IAudioCaptureSource`, `CaptureOptions`, `AudioChunk`, `Pcm16Converter`,
  `StreamingChunkBuffer`, `SilenceWatchdog`
- Üretir: `MicrophoneCaptureSource : IAudioCaptureSource, IDisposable`;
  `MicrophonePermissionException : Exception`

- [ ] **Adım 1: P/Invoke yüzeyini yaz**

`AudioQueueInterop.cs` — spike'ta ölçülen imzalar, birebir:

```csharp
using System.Runtime.InteropServices;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>macOS AudioToolbox / AudioQueue C API'sinin P/Invoke yüzeyi. Native tipler
/// yalnızca burada ve <see cref="MicrophoneCaptureSource"/> içinde yaşar (motor sınırı).
/// İmzalar gerçek donanımda spike ile doğrulandı (bkz. 08d-mikrofon-spike-olcumu.md).</summary>
internal static class AudioQueueInterop
{
    internal const string Library =
        "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    internal const uint FormatLinearPcm = 0x6C70636D;  // 'lpcm'
    internal const uint FlagsSignedIntegerPacked = 0xC; // SignedInteger | IsPacked

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioStreamBasicDescription
    {
        public double mSampleRate;
        public uint mFormatID;
        public uint mFormatFlags;
        public uint mBytesPerPacket;
        public uint mFramesPerPacket;
        public uint mBytesPerFrame;
        public uint mChannelsPerFrame;
        public uint mBitsPerChannel;
        public uint mReserved;
    }

    /// <summary>Alan ofsetleri 64-bit hizalamayla native struct'a birebir oturur
    /// (spike'ta <c>mAudioDataByteSize</c> doğru okunarak doğrulandı).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioQueueBuffer
    {
        public uint mAudioDataBytesCapacity;
        public IntPtr mAudioData;
        public uint mAudioDataByteSize;
        public IntPtr mUserData;
        public uint mPacketDescriptionCapacity;
        public IntPtr mPacketDescriptions;
        public uint mPacketDescriptionCount;
    }

    internal delegate void InputCallback(
        IntPtr userData, IntPtr audioQueue, IntPtr buffer,
        IntPtr startTime, uint numPackets, IntPtr packetDescs);

    [DllImport(Library)]
    internal static extern int AudioQueueNewInput(
        ref AudioStreamBasicDescription format, InputCallback callback, IntPtr userData,
        IntPtr runLoop, IntPtr runLoopMode, uint flags, out IntPtr audioQueue);

    [DllImport(Library)]
    internal static extern int AudioQueueAllocateBuffer(
        IntPtr audioQueue, uint bufferByteSize, out IntPtr buffer);

    [DllImport(Library)]
    internal static extern int AudioQueueEnqueueBuffer(
        IntPtr audioQueue, IntPtr buffer, uint numPacketDescs, IntPtr packetDescs);

    [DllImport(Library)]
    internal static extern int AudioQueueStart(IntPtr audioQueue, IntPtr startTime);

    [DllImport(Library)]
    internal static extern int AudioQueueStop(IntPtr audioQueue, bool immediate);

    [DllImport(Library)]
    internal static extern int AudioQueueDispose(IntPtr audioQueue, bool immediate);
}
```

- [ ] **Adım 2: `MicrophoneCaptureSource`'u yaz**

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>Mikrofon izni verilmemiş görünüyorsa fırlatılır. Sessiz reddi dürüstçe yüzeye çıkarır.</summary>
public sealed class MicrophonePermissionException : Exception
{
    public MicrophonePermissionException(string message) : base(message) { }
}

/// <summary>
/// macOS mikrofonundan canlı 16 kHz mono <see cref="AudioChunk"/> akışı üretir
/// (AudioToolbox / AudioQueue). Native tipler yalnızca burada ve
/// <see cref="AudioQueueInterop"/>'ta; chunk'lama, dönüştürme ve sessizlik tespiti saf
/// yardımcılarda ve ayrı ayrı test edilir.
/// </summary>
/// <remarks>
/// Spike ölçümü (2026-07-27): AudioQueue 16 kHz mono PCM16'yı DOĞRUDAN üretir — cihaz
/// 48 kHz olsa bile dönüşümü kendisi yapar, bu yüzden burada yeniden örnekleme yoktur.
/// Callback delegate'i <see cref="GCHandle"/> ile pinlenir; aksi halde GC onu toplar ve
/// native taraf çöker.
/// </remarks>
public sealed class MicrophoneCaptureSource : IAudioCaptureSource, IDisposable
{
    private const int TargetRate = 16000;
    private const uint BufferBytes = 8192;
    private const int BufferCount = 3;

    /// <summary>Ölü-akış kararı için gözlem penceresi. Bu süre boyunca dijital sessizlik
    /// gelirse izin reddi varsayılır.</summary>
    private static readonly TimeSpan DeadStreamWindow = TimeSpan.FromSeconds(3);

    private IntPtr _queue;
    private GCHandle _callbackHandle;
    private AudioQueueInterop.InputCallback? _callback;
    private bool _disposed;

    public async IAsyncEnumerable<AudioChunk> CaptureAsync(
        CaptureOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException(
                "Canlı mikrofon yakalama şu an yalnızca macOS'ta destekleniyor. " +
                "Windows (WASAPI) implementasyonu sonraki turda; dosya analizi her platformda çalışır.");

        var chunks = Channel.CreateUnbounded<AudioChunk>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        var buffer = new StreamingChunkBuffer(TargetRate, options.ChunkLength, options.Overlap);
        var watchdog = new SilenceWatchdog(TargetRate, DeadStreamWindow);
        bool deadStreamReported = false;

        _callback = (userData, queue, bufferPtr, startTime, numPackets, packetDescs) =>
        {
            var native = Marshal.PtrToStructure<AudioQueueInterop.AudioQueueBuffer>(bufferPtr);
            int byteCount = (int)native.mAudioDataByteSize;

            if (byteCount > 0)
            {
                var raw = new byte[byteCount];
                Marshal.Copy(native.mAudioData, raw, 0, byteCount);

                var samples = new float[byteCount / 2];
                Pcm16Converter.ToFloat(raw, samples);

                watchdog.Observe(samples);
                if (watchdog.LooksDead && !deadStreamReported)
                {
                    deadStreamReported = true;
                    chunks.Writer.TryComplete(new MicrophonePermissionException(
                        "Mikrofondan yalnızca sessizlik geliyor. Büyük olasılıkla mikrofon izni " +
                        "verilmemiş: Sistem Ayarları → Gizlilik ve Güvenlik → Mikrofon bölümünden " +
                        "bu uygulamaya izin verin ve tekrar deneyin."));
                    return;
                }

                foreach (var chunk in buffer.Append(samples))
                    chunks.Writer.TryWrite(chunk);
            }

            // Tamponu kuyruğa geri ver (durdurulmuşsa hata döner, önemsizdir).
            AudioQueueInterop.AudioQueueEnqueueBuffer(queue, bufferPtr, 0, IntPtr.Zero);
        };
        _callbackHandle = GCHandle.Alloc(_callback);

        Start();

        try
        {
            await foreach (var chunk in chunks.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return chunk;
        }
        finally
        {
            StopQueue();
            var tail = buffer.Flush();
            if (tail is not null && !ct.IsCancellationRequested) yield return tail;
        }
    }

    private void Start()
    {
        var format = new AudioQueueInterop.AudioStreamBasicDescription
        {
            mSampleRate = TargetRate,
            mFormatID = AudioQueueInterop.FormatLinearPcm,
            mFormatFlags = AudioQueueInterop.FlagsSignedIntegerPacked,
            mBytesPerPacket = 2,
            mFramesPerPacket = 1,
            mBytesPerFrame = 2,
            mChannelsPerFrame = 1,
            mBitsPerChannel = 16,
            mReserved = 0,
        };

        int status = AudioQueueInterop.AudioQueueNewInput(
            ref format, _callback!, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out _queue);
        if (status != 0)
            throw new InvalidOperationException(
                $"Mikrofon kuyruğu oluşturulamadı (AudioQueueNewInput → {status}). " +
                "Ses giriş cihazı bulunamamış olabilir.");

        for (int i = 0; i < BufferCount; i++)
        {
            AudioQueueInterop.AudioQueueAllocateBuffer(_queue, BufferBytes, out var b);
            AudioQueueInterop.AudioQueueEnqueueBuffer(_queue, b, 0, IntPtr.Zero);
        }

        status = AudioQueueInterop.AudioQueueStart(_queue, IntPtr.Zero);
        if (status != 0)
            throw new InvalidOperationException($"Mikrofon başlatılamadı (AudioQueueStart → {status}).");
    }

    private void StopQueue()
    {
        if (_queue == IntPtr.Zero) return;
        AudioQueueInterop.AudioQueueStop(_queue, true);
        AudioQueueInterop.AudioQueueDispose(_queue, true);
        _queue = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopQueue();
        if (_callbackHandle.IsAllocated) _callbackHandle.Free();
        _callback = null;
    }
}
```

- [ ] **Adım 3: Env-gated gerçek mikrofon testini yaz**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class MicrophoneCaptureSourceTests
{
    /// <summary>Gerçek mikrofon gerektirir: MIC_TEST=1 ile açılır. Kullanıcı testin
    /// çalıştığı ~6 saniye boyunca KONUŞMALIDIR.</summary>
    [SkippableFact]
    public async Task CaptureAsync_RealMicrophone_ProducesChunksWithAudibleSignal()
    {
        Skip.If(Environment.GetEnvironmentVariable("MIC_TEST") != "1",
            "Gerçek mikrofon testi: MIC_TEST=1 ile çalıştırın ve test sırasında konuşun.");
        Skip.IfNot(OperatingSystem.IsMacOS(), "Mikrofon yakalama şu an yalnızca macOS'ta.");

        using var source = new MicrophoneCaptureSource();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var options = new CaptureOptions(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(0.5));

        var collected = new List<AudioChunk>();
        try
        {
            await foreach (var chunk in source.CaptureAsync(options, cts.Token))
            {
                collected.Add(chunk);
                if (collected.Count >= 2) break;
            }
        }
        catch (OperationCanceledException) { /* süre doldu — topladıklarımızı değerlendiririz */ }

        Assert.NotEmpty(collected);
        Assert.All(collected, c => Assert.Equal(16000, c.SampleRate));
        Assert.All(collected, c => Assert.NotEmpty(c.Samples));

        // Zaman damgaları ilerlemeli (mutlak indeksten hesaplanıyor).
        for (int i = 1; i < collected.Count; i++)
            Assert.True(collected[i].Start > collected[i - 1].Start);

        // MUTLAK EŞİK KULLANMA (spike dersi): sessiz oda tabanı 0,0098-0,0168 ölçüldü,
        // konuşma 0,0805. Dijital sessizlikten AÇIKÇA ayrıldığını doğrula.
        double peak = collected.SelectMany(c => c.Samples).Max(Math.Abs);
        Assert.True(peak > 0.005,
            $"Mikrofondan anlamlı sinyal gelmedi (tepe {peak:F4}). İzin verilmiş mi? " +
            "Test sırasında konuştunuz mu?");
    }

    [Fact]
    public void Dispose_WithoutCapture_DoesNotThrow()
    {
        var source = new MicrophoneCaptureSource();

        source.Dispose();
        source.Dispose();   // idempotent
    }
}
```

Test projesine `Xunit.SkippableFact` paketi gerekiyorsa ekleyin (diğer Infrastructure test
projelerindeki sürümün aynısı).

- [ ] **Adım 4: Testleri çalıştır (mikrofon KAPALI)**

Çalıştır: `dotnet build && dotnet test`
Beklenen: gerçek-mikrofon testi **Skip**, `Dispose` testi geçer. 279 → 280 geçer, 6 → 7 atlanır.

- [ ] **Adım 5: Commit**

```bash
git add src/SentimentAnalysis.Infrastructure.Audio/AudioQueueInterop.cs \
        src/SentimentAnalysis.Infrastructure.Audio/MicrophoneCaptureSource.cs \
        tests/SentimentAnalysis.Infrastructure.Audio.Tests/MicrophoneCaptureSourceTests.cs
git commit -m "feat(audio): AudioToolbox ile canlı mikrofon yakalama kaynağı"
```

---

## Task 4: macOS `.app` bundle ve mikrofon izni

> **Neden gerekli:** Spike'ta ölçüldü — izin, çalıştıran uygulamaya atfedilir. Terminalden
> `dotnet run` ile çalıştırıldığında izin terminale gider. Son kullanıcı uygulamayı çift
> tıklayarak açacağı için iznin uygulamanın kendisine verilmesi, bunun için de
> `NSMicrophoneUsageDescription` içeren bir `.app` bundle gerekir.

**Dosyalar:**
- Oluştur: `src/SentimentAnalysis.Desktop.Avalonia/Info.plist`
- Oluştur: `build-macos-app.sh` (repo kökü)

**Arayüzler:** yok (paketleme)

- [ ] **Adım 1: `Info.plist`'i yaz**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Sesli Duygu Analizi</string>
    <key>CFBundleDisplayName</key>
    <string>Sesli Duygu Analizi</string>
    <key>CFBundleIdentifier</key>
    <string>com.betulfiliz.sentimentanalysis</string>
    <key>CFBundleVersion</key>
    <string>1.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>SentimentAnalysis.Desktop.Avalonia</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSMicrophoneUsageDescription</key>
    <string>Konuşmanızın duygu analizini yapabilmek için mikrofona erişmesi gerekiyor. Ses kaydınız yalnızca bu bilgisayarda işlenir, hiçbir sunucuya gönderilmez.</string>
</dict>
</plist>
```

- [ ] **Adım 2: Paketleme betiğini yaz**

`build-macos-app.sh`:

```bash
#!/usr/bin/env bash
# macOS .app paketi üretir. Mikrofon izni bundle kimliğine bağlandığı için canlı
# mikrofon yalnızca bu paketle çalışır (terminalden `dotnet run` iznini terminale atfeder).
set -euo pipefail

PROJECT="src/SentimentAnalysis.Desktop.Avalonia/SentimentAnalysis.Desktop.Avalonia.csproj"
PLIST="src/SentimentAnalysis.Desktop.Avalonia/Info.plist"
APP="dist/Sesli Duygu Analizi.app"
RID="${RID:-osx-arm64}"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

dotnet publish "$PROJECT" -c Release -r "$RID" --self-contained false -o "$APP/Contents/MacOS"

cp "$PLIST" "$APP/Contents/Info.plist"

# Ad-hoc imza: TCC'nin izni kalıcı olarak hatırlaması için bundle imzalı olmalı.
codesign --force --deep --sign - "$APP"

echo "Hazır: $APP"
echo "Çalıştırmak için: open '$APP'"
```

Betiği çalıştırılabilir yapın: `chmod +x build-macos-app.sh`

- [ ] **Adım 3: Paketi üret**

Çalıştır: `./build-macos-app.sh`
Beklenen: `dist/Sesli Duygu Analizi.app` oluşur, `codesign` hatasız biter.

- [ ] **Adım 4: `dist/`'i git dışında tut**

`.gitignore`'a ekleyin (yoksa):

```
# Üretilen macOS uygulama paketi
dist/
```

- [ ] **Adım 5: Tüm testleri çalıştır**

Çalıştır: `dotnet build && dotnet test`
Beklenen: değişiklik yok — 280 geçer, 7 atlanır, 0 başarısız.

- [ ] **Adım 6: Commit**

```bash
git add src/SentimentAnalysis.Desktop.Avalonia/Info.plist build-macos-app.sh .gitignore
git commit -m "build: mikrofon izni için macOS .app paketi ve Info.plist"
```

---

## Task 5: Kaynak seçimi ve bağlantı (UI + DI)

**Dosyalar:**
- Oluştur: `src/SentimentAnalysis.Desktop/CaptureMode.cs`
- Değiştir: `src/SentimentAnalysis.Desktop/LiveViewModel.cs`
- Değiştir: `src/SentimentAnalysis.Desktop.Avalonia/CompositionRoot.cs`
- Değiştir: `src/SentimentAnalysis.Desktop.Avalonia/PipelineAnalysisRunner.cs`
- Değiştir: `src/SentimentAnalysis.Desktop.Avalonia/Views/LiveView.axaml`
- Test: `tests/SentimentAnalysis.Desktop.Tests/LiveViewModelTests.cs` (ek testler)

**Arayüzler:**
- Üretir: `enum CaptureMode { Dosya, Mikrofon }`,
  `ICaptureModeState { CaptureMode Mode { get; set; } }`, `CaptureModeState`
- `LiveViewModel` yapıcısına opsiyonel `ICaptureModeState? captureMode = null` (additive)
- `LiveViewModel.UseMicrophone` (bool, iki yönlü bağlanır), `.CanStart` mikrofonda dosya istemez

- [ ] **Adım 1: `CaptureMode`'u yaz**

```csharp
namespace SentimentAnalysis.Desktop;

/// <summary>Canlı analizin ses kaynağı.</summary>
public enum CaptureMode
{
    /// <summary>Diskten seçilen bir ses dosyası.</summary>
    Dosya,

    /// <summary>Canlı mikrofon.</summary>
    Mikrofon,
}

/// <summary>Seçili kaynak modunu ViewModel ile kompozisyon kökü arasında paylaşır
/// (kaynak fabrikası hangi <c>IAudioCaptureSource</c>'u üreteceğini buradan öğrenir).</summary>
public interface ICaptureModeState
{
    CaptureMode Mode { get; set; }
}

public sealed class CaptureModeState : ICaptureModeState
{
    public CaptureMode Mode { get; set; } = CaptureMode.Dosya;
}
```

- [ ] **Adım 2: `LiveViewModel` testlerini yaz**

`LiveViewModelTests.cs` içine ekleyin:

```csharp
    [Fact]
    public void CanStart_FileMode_RequiresSelectedFile()
    {
        var selected = new SelectedAudio();
        var mode = new CaptureModeState { Mode = CaptureMode.Dosya };
        var vm = new LiveViewModel(new FakeRunner(), null, selected, null, mode);

        Assert.False(vm.StartCommand.CanExecute(null));

        selected.Path = "/tmp/ses.wav";
        vm.UseMicrophone = false;   // CanExecute yeniden değerlendirilsin

        Assert.True(vm.StartCommand.CanExecute(null));
    }

    [Fact]
    public void CanStart_MicrophoneMode_DoesNotRequireFile()
    {
        var mode = new CaptureModeState();
        var vm = new LiveViewModel(new FakeRunner(), null, new SelectedAudio(), null, mode);

        vm.UseMicrophone = true;

        Assert.True(vm.StartCommand.CanExecute(null));
        Assert.Equal(CaptureMode.Mikrofon, mode.Mode);
    }

    [Fact]
    public void UseMicrophone_WritesThroughToSharedState()
    {
        var mode = new CaptureModeState();
        var vm = new LiveViewModel(new FakeRunner(), null, new SelectedAudio(), null, mode);

        vm.UseMicrophone = true;
        Assert.Equal(CaptureMode.Mikrofon, mode.Mode);

        vm.UseMicrophone = false;
        Assert.Equal(CaptureMode.Dosya, mode.Mode);
    }
```

- [ ] **Adım 3: Testi çalıştır, DERLENMEDİĞİNİ doğrula**

- [ ] **Adım 4: `LiveViewModel`'i güncelle**

Alan ve yapıcı (mevcut opsiyonel parametrelerin **sonuna** eklenir — additive):

```csharp
    private readonly ICaptureModeState? _captureMode;

    public LiveViewModel(IAnalysisRunner runner, IAudioFilePicker? picker = null,
                         ISelectedAudio? selected = null, AppConfig? config = null,
                         ICaptureModeState? captureMode = null)
    {
        _runner = runner;
        _picker = picker;
        _selected = selected;
        _temperature = (config ?? AppConfig.Default).Fusion.DistributionTemperature;
        _captureMode = captureMode;
    }
```

Mikrofon anahtarı:

```csharp
    /// <summary>Canlı mikrofon mu kullanılsın (aksi halde seçili dosya)?</summary>
    [ObservableProperty] private bool _useMicrophone;

    partial void OnUseMicrophoneChanged(bool value)
    {
        if (_captureMode is not null)
            _captureMode.Mode = value ? CaptureMode.Mikrofon : CaptureMode.Dosya;
        StartCommand.NotifyCanExecuteChanged();
    }
```

`CanStart` güncellenir:

```csharp
    private bool CanStart() => !IsCapturing && (UseMicrophone || _selected?.Path != null);
```

- [ ] **Adım 5: `PipelineAnalysisRunner`'ı güncelle**

Mikrofonda 30 saniyelik chunk, ilk sonucu yarım dakika geciktirir. Canlı kullanımda daha kısa
parça gerekir:

```csharp
    private readonly ICaptureModeState? _captureMode;

    public PipelineAnalysisRunner(EmotionPipeline pipeline, Func<IAudioCaptureSource?> sourceFactory,
                                  AppConfig config, ICaptureModeState? captureMode = null)
    {
        _pipeline = pipeline;
        _sourceFactory = sourceFactory;
        _config = config;
        _captureMode = captureMode;
    }
```

`RunAsync` içinde:

```csharp
        var source = _sourceFactory()
            ?? throw new InvalidOperationException(
                "Analiz için bir ses kaynağı seçilmedi. Dosya modunda bir ses dosyası seçin " +
                "veya mikrofon modunu açın.");

        // Canlı mikrofonda kısa parça = hızlı geri bildirim; dosyada uzun parça = daha iyi bağlam.
        var options = _captureMode?.Mode == CaptureMode.Mikrofon
            ? new CaptureOptions(ChunkLength: TimeSpan.FromSeconds(10), Overlap: TimeSpan.FromSeconds(2))
            : new CaptureOptions(ChunkLength: TimeSpan.FromSeconds(30), Overlap: TimeSpan.FromSeconds(5));
```

- [ ] **Adım 6: `CompositionRoot`'u güncelle**

`using SentimentAnalysis.Infrastructure.Audio;` ekleyin, sonra:

```csharp
        services.AddSingleton<ICaptureModeState, CaptureModeState>();
        services.AddSingleton<Func<IAudioCaptureSource?>>(sp =>
        {
            var selected = sp.GetRequiredService<ISelectedAudio>();
            var mode = sp.GetRequiredService<ICaptureModeState>();
            var audioSourceFactory = sp.GetRequiredService<IAudioSourceFactory>();
            return () => mode.Mode == CaptureMode.Mikrofon
                ? new MicrophoneCaptureSource()
                : selected.Path is null ? null : audioSourceFactory.Create(selected.Path);
        });
```

`LiveViewModel` kaydına yeni bağımlılıkları geçin:

```csharp
        services.AddSingleton<LiveViewModel>(sp => new LiveViewModel(
            sp.GetRequiredService<IAnalysisRunner>(),
            sp.GetRequiredService<IAudioFilePicker>(),
            sp.GetRequiredService<ISelectedAudio>(),
            sp.GetRequiredService<AppConfig>(),
            sp.GetRequiredService<ICaptureModeState>()));
```

- [ ] **Adım 7: XAML'e kaynak seçimini ekle**

`LiveView.axaml`'de "Dosya Seç" düğmesinin bulunduğu `StackPanel`'in başına ekleyin:

```xml
                <ToggleSwitch IsChecked="{Binding UseMicrophone}"
                              OnContent="Mikrofon" OffContent="Dosya"
                              IsEnabled="{Binding !IsCapturing}" VerticalAlignment="Center" />
```

"Dosya Seç" düğmesini ve dosya adı metnini mikrofon modunda gizleyin:

```xml
                <Button Content="Dosya Seç" Command="{Binding SelectFileCommand}"
                        IsEnabled="{Binding !IsCapturing}" IsVisible="{Binding !UseMicrophone}" />
                <TextBlock Text="{Binding SelectedFile}" VerticalAlignment="Center" FontStyle="Italic"
                           IsVisible="{Binding !UseMicrophone}"
                           Foreground="{DynamicResource SystemBaseMediumColor}" />
```

- [ ] **Adım 8: Tüm testleri çalıştır**

Çalıştır: `dotnet build && dotnet test`
Beklenen: 0 başarısız. 280 → 283 geçer, 7 atlanır.

- [ ] **Adım 9: Gerçek uçtan uca doğrulama (kullanıcıyla)**

```bash
./build-macos-app.sh
open "dist/Sesli Duygu Analizi.app"
```

Kullanıcıdan doğrulamasını isteyin:
- Uygulama ilk açılışta **mikrofon izni istemi** gösteriyor mu? (`Info.plist` metni görünmeli)
- Mikrofon anahtarı açılıp "Başlat"a basıldığında konuşurken zaman çizelgesi doluyor mu?
- Üç panel (Metin / Ses / Nihai) canlı güncelleniyor mu?
- "Durdur" akışı temiz durduruyor mu?
- İzin **reddedilirse** anlaşılır Türkçe uyarı çıkıyor mu (sessizce boş sonuç değil)?

**Kullanıcı onaylamadan bu task tamamlanmış sayılmaz.**

- [ ] **Adım 10: Commit**

```bash
git add src/SentimentAnalysis.Desktop/ src/SentimentAnalysis.Desktop.Avalonia/ tests/
git commit -m "feat(ui): canlı ekranda mikrofon/dosya kaynak seçimi ve mikrofon bağlantısı"
```

---

## Öz-Denetim

**1. Spec kapsamı (08d spec'i Dilim 3):**

| Spec gereksinimi | Task |
|---|---|
| `MicrophoneCaptureSource : IAudioCaptureSource` | 3 |
| Native P/Invoke yalnızca motor sınırında | 3 (`AudioQueueInterop` + kaynak) |
| `StreamingChunkBuffer` (saf, örtüşmeli, zaman damgalı) | 1 |
| Cihaz formatından 16 kHz mono'ya | **Gerekmiyor** — spike ölçtü: AudioQueue doğrudan 16 kHz veriyor |
| Pipeline'da değişiklik yok | Tasarım gereği — hiçbir task `Application/Pipeline/`'a dokunmuyor |
| UI kaynak seçimi (Dosya/Mikrofon) | 5 |
| `CanStart` mikrofonda dosya istemez | 5 |
| İzin hatası kullanıcıya açıkça gösterilir | 2 (tespit) + 3 (istisna) + 5 (uçtan uca doğrulama) |
| Saf kısımlar tam birim testli | 1, 2 |
| Gerçek mikrofon testi env-gated | 3 |
| `.app` bundle + `NSMicrophoneUsageDescription` | 4 |

**Spike ölçümünden gelen sapma:** Spec "cihaz ne verirse alınır, `AudioPreprocessor` ile 16 kHz'e
indirilir" diyordu. Ölçüm bunun **gereksiz** olduğunu gösterdi — AudioQueue dönüşümü kendisi
yapıyor. Plan ölçümü izliyor, spec'in tahminini değil. `AudioPreprocessor` dosya yolunda
kullanılmaya devam ediyor.

**2. Placeholder taraması:** Yapıldı. Tüm adımlar gerçek, derlenebilir kod içeriyor; "TBD",
"benzer şekilde" veya kod bloğu olmayan uygulama adımı yok.

**3. Tip tutarlılığı:**
- `Pcm16Converter.ToFloat(ReadOnlySpan<byte>, Span<float>) → int` → Task 1 tanımlar, Task 3 tüketir ✓
- `StreamingChunkBuffer(int, TimeSpan, TimeSpan)` / `.Append` / `.Flush` → Task 1 tanımlar, Task 3 tüketir ✓
- `SilenceWatchdog(int, TimeSpan, double)` / `.Observe` / `.LooksDead` → Task 2 tanımlar, Task 3 tüketir ✓
- `MicrophoneCaptureSource` parametresiz yapıcı → Task 3 tanımlar, Task 5 `CompositionRoot`'ta çağırır ✓
- `ICaptureModeState.Mode` → Task 5 tanımlar; `LiveViewModel`, `PipelineAnalysisRunner` ve
  `CompositionRoot` aynı adı kullanır ✓
- `CFBundleExecutable` = `SentimentAnalysis.Desktop.Avalonia` → publish çıktısındaki
  yürütülebilir dosya adıyla eşleşir ✓
