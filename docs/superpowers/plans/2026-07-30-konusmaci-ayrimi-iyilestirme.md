# Konuşmacı Ayrımı İyileştirmesi — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Konuşmacı ayrımının yanlış küme sayısı üretmesini kök nedeninden gidermek, konuşmacıları konuşma sırasına göre A/B/C olarak numaralandırmak, konuşmacı duygusunu kişinin kendi sesinden ölçmek ve belirsiz atamaları "Bilinmeyen" yerine işaretli atama olarak göstermek.

**Architecture:** Tüm karar mantığı `SentimentAnalysis.Application/Diarization` altında saf, deterministik yardımcılara girer (numaralandırma, hizalama, bölge seçimi, ağırlıklı ortalama); native sherpa/ONNX yalnızca motor sınırında kalır. Yeniden numaralandırma `DiarizationJob` içinde, veriyi SAKLAMADAN önce çalışır — böylece kalıcı veri kanonik olur. Konuşmacı prozodisi oturum sonrası arka plan geçişinde, saklanmış sesten ölçülür.

**Tech Stack:** .NET 9, xUnit, sherpa-onnx (diarization), ONNX Runtime (prozodi), Avalonia (UI).

## Global Constraints

- Hedef çerçeve `net9.0`; çözüm kökündeki `global.json` SDK tabanını belirler.
- Tüm yeni karar mantığı **saf ve deterministik** olmalı: aynı girdi her zaman aynı çıktı.
- Sessiz veri kaybı yasak: atanamayan/analiz edilemeyen her şey sayılarak veya işaretlenerek raporlanır (master §12.2).
- Uydurma sonuç yasak: veri yoksa boş döner, tahmin üretilmez.
- Üretim kodunda `Console.Write*` yasak — `NoConsoleLoggingInProductionTests` bunu koruyor; teşhis gerekirse `IDiagnosticLog` kullanılır.
- Kalibre edilmemiş her sabit, yorumunda **KALİBRE EDİLMEMİŞ** olarak işaretlenir ve config'te yaşar.
- Türkçe kod yorumları ve Türkçe test adları mevcut dosyaların stiline uyar.
- Her task sonunda `dotnet test SentimentAnalysis.sln` tamamen yeşil olmalı.

---

### Task 1: Embedding modeli için boyut koruması

Yanlış embedding modeli bugün **sessizce** kabul ediliyor: dosya adı her iki modelde de `speaker-embedding.onnx`. Bu task, kaza eseri yanlış dosyayı anlaşılır bir hatayla yakalar.

**Files:**
- Modify: `src/SentimentAnalysis.Infrastructure.Diarization/FileDiarizationModelProvider.cs`
- Test: `tests/SentimentAnalysis.Infrastructure.Diarization.Tests/FileDiarizationModelProviderTests.cs` (yoksa oluştur)

**Interfaces:**
- Consumes: `IDiarizationModelProvider.EmbeddingModelPath` (mevcut), `AppConfig` (mevcut).
- Produces: Boyut aralığı dışındaki embedding dosyası için `InvalidDataException`. Sonraki task'lar bu davranışa dokunmaz.

- [ ] **Step 1: Testi yaz (kırmızı)**

`tests/SentimentAnalysis.Infrastructure.Diarization.Tests/FileDiarizationModelProviderTests.cs`:

```csharp
using System;
using System.IO;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Infrastructure.Diarization;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Diarization.Tests;

/// <summary>Yanlış embedding modeli SESSİZCE kabul edilmemeli. 2026-07-29'da model
/// 3dspeaker'dan wespeaker'a geçti ve kümeleme eşiği ona göre ayarlandı; dosya adı aynı
/// kaldığı için eski model hiçbir hata üretmeden yüklendi ve iki kişilik kayıtta altı
/// konuşmacı bulundu. Boyut kontrolü kriptografik garanti değildir — amacı kaza eseri
/// yanlış dosyayı yakalamaktır.</summary>
public class FileDiarizationModelProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "diar-model-" + Guid.NewGuid());

    public FileDiarizationModelProviderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private AppConfig ConfigForTempDir() =>
        AppConfig.Default with { Vad = AppConfig.Default.Vad with { ModelDirectory = _dir } };

    private void WriteEmbedding(long bytes)
    {
        var path = Path.Combine(_dir, "speaker-embedding.onnx");
        using var fs = File.Create(path);
        fs.SetLength(bytes);
    }

    [Fact]
    public void EmbeddingModelPath_BeklenenBoyutta_KabulEder()
    {
        WriteEmbedding(26_000_000);   // wespeaker_en_voxceleb_resnet34_LM ~26 MB
        var provider = new FileDiarizationModelProvider(ConfigForTempDir());

        Assert.EndsWith("speaker-embedding.onnx", provider.EmbeddingModelPath);
    }

    [Fact]
    public void EmbeddingModelPath_EskiBuyukModel_AnlasilirHataVerir()
    {
        WriteEmbedding(39_593_761);   // eski 3dspeaker modelinin gerçek boyutu
        var provider = new FileDiarizationModelProvider(ConfigForTempDir());

        var ex = Assert.Throws<InvalidDataException>(() => _ = provider.EmbeddingModelPath);
        Assert.Contains("wespeaker_en_voxceleb_resnet34_LM", ex.Message);
        Assert.Contains("39593761", ex.Message.Replace(".", "").Replace(",", ""));
    }

    [Fact]
    public void EmbeddingModelPath_CokKucukDosya_AnlasilirHataVerir()
    {
        WriteEmbedding(1_000);
        var provider = new FileDiarizationModelProvider(ConfigForTempDir());

        Assert.Throws<InvalidDataException>(() => _ = provider.EmbeddingModelPath);
    }

    [Fact]
    public void EmbeddingModelPath_DosyaYok_FileNotFoundVerir()
    {
        var provider = new FileDiarizationModelProvider(ConfigForTempDir());

        Assert.Throws<FileNotFoundException>(() => _ = provider.EmbeddingModelPath);
    }
}
```

- [ ] **Step 2: Kırmızıyı doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Infrastructure.Diarization.Tests --filter "FullyQualifiedName~FileDiarizationModelProviderTests"`
Beklenen: `EskiBuyukModel` ve `CokKucukDosya` testleri BAŞARISIZ (`InvalidDataException` fırlatılmıyor).

- [ ] **Step 3: Korumayı ekle**

`FileDiarizationModelProvider.cs` içinde `EmbeddingModelPath`'i değiştir ve yardımcıyı ekle:

```csharp
    // wespeaker_en_voxceleb_resnet34_LM.onnx ~26 MB'dır. Aralık geniş tutulur: amaç sürüm
    // farkını reddetmek değil, YANLIŞ MODELİ yakalamaktır (eski 3dspeaker dosyası 39,5 MB).
    private const long EmbeddingMinBytes = 15_000_000;
    private const long EmbeddingMaxBytes = 35_000_000;

    public string EmbeddingModelPath
    {
        get
        {
            var path = Resolve(
                EmbeddingFile,
                "konuşmacı embedding modeli",
                "github.com/k2-fsa/sherpa-onnx → releases/speaker-recongition-models/" +
                "wespeaker_en_voxceleb_resnet34_LM.onnx " +
                $"(indirip {EmbeddingFile} adıyla models/ altına koyun)");

            long size = new FileInfo(path).Length;
            if (size < EmbeddingMinBytes || size > EmbeddingMaxBytes)
                throw new InvalidDataException(
                    $"Konuşmacı embedding modeli beklenen boyutta değil ({size} bayt; beklenen " +
                    $"{EmbeddingMinBytes}-{EmbeddingMaxBytes} bayt). Kümeleme eşiği " +
                    "wespeaker_en_voxceleb_resnet34_LM modeline göre ölçüldü; başka bir model " +
                    "sessizce yanlış konuşmacı sayısı üretir. Doğru dosyayı indirip " +
                    $"{EmbeddingFile} adıyla değiştirin: {path}");

            return path;
        }
    }
```

- [ ] **Step 4: Yeşili doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Infrastructure.Diarization.Tests`
Beklenen: tümü PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SentimentAnalysis.Infrastructure.Diarization/FileDiarizationModelProvider.cs tests/SentimentAnalysis.Infrastructure.Diarization.Tests/FileDiarizationModelProviderTests.cs
git commit -m "fix(diarization): yanlış embedding modeli sessizce kabul edilmesin"
```

---

### Task 2: Doğru modeli kur ve eşiği ölçerek seç

Bu task kod değil **ölçüm** üretir; sonucu config'e ve yoruma yazılır. Task 1'in koruması bu adımın doğru yapıldığını zaten doğrular.

**Files:**
- Modify: `src/SentimentAnalysis.Application/Configuration/AppConfig.cs` (`DiarizationConfig.ClusteringThreshold`)
- Modify: `src/SentimentAnalysis.Infrastructure.Diarization/SherpaDiarizer.cs` (ölçüm notu)
- Create: `scratchpad/diar-threshold-2026-07-30.md` (ölçüm kaydı)

**Interfaces:**
- Consumes: Task 1'in boyut koruması.
- Produces: Ölçülmüş `ClusteringThreshold` değeri. Task 3+ bu değeri kullanır ama değerine bağımlı değildir.

- [ ] **Step 1: Mevcut dosyayı yedekle ve doğru modeli indir**

```bash
MODELS="$HOME/Library/Application Support/SentimentAnalysis/models"
mv "$MODELS/speaker-embedding.onnx" "$MODELS/speaker-embedding.3dspeaker.onnx.bak"
curl -L -o "$MODELS/speaker-embedding.onnx" \
  https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recognition-models/wespeaker_en_voxceleb_resnet34_LM.onnx
ls -l "$MODELS/speaker-embedding.onnx"
```

Beklenen: dosya boyutu 15–35 MB aralığında (Task 1'in kabul aralığı).

- [ ] **Step 2: Eşiği tara**

`scratchpad/diar-spike/toplanti.wav` bilinen iki konuşmacılıdır. Eşik 0,3 / 0,4 / 0,5 / 0,6 için diarization'ı çalıştır ve **bulunan küme sayısını** kaydet. Ölçüm, mevcut `SherpaDiarizer`'ı kullanan küçük bir xUnit `SkippableFact` ile yapılır (gerçek model gerektirir, ortam değişkeniyle kapılıdır) veya doğrudan uygulamadan. Her koşu için şunu not et: eşik, bulunan konuşmacı sayısı, toplam konuşma süresi.

- [ ] **Step 3: Ölçümü yaz**

`scratchpad/diar-threshold-2026-07-30.md` dosyasına tabloyu yaz: kayıt adı, gerçek konuşmacı sayısı, her eşik için bulunan küme sayısı, seçilen değer ve seçim gerekçesi. Tek kayda dayandığını açıkça belirt.

- [ ] **Step 4: Config'i güncelle**

`AppConfig.cs` içindeki `DiarizationConfig.ClusteringThreshold` yorumunu ölçüm sonucuyla değiştir: hangi modelle, hangi kayıtla, hangi aralığın tam iki küme verdiği. Değeri tam iki küme veren aralığın ortası yap. `SherpaDiarizer` sınıf yorumundaki ölçüm notunu da aynı bilgiyle güncelle.

- [ ] **Step 5: Tüm testleri çalıştır ve commit et**

```bash
dotnet test SentimentAnalysis.sln
git add src/SentimentAnalysis.Application/Configuration/AppConfig.cs src/SentimentAnalysis.Infrastructure.Diarization/SherpaDiarizer.cs scratchpad/diar-threshold-2026-07-30.md
git commit -m "measure(diarization): wespeaker embedding ile kümeleme eşiği ölçüldü"
```

---

### Task 3: Konuşmacıları konuşma sırasına göre yeniden numaralandır

sherpa küme kimliklerini keyfi verir; ilk konuşan "Konuşmacı F" görünebiliyor.

**Files:**
- Create: `src/SentimentAnalysis.Application/Diarization/SpeakerRenumberer.cs`
- Modify: `src/SentimentAnalysis.Application/Diarization/DiarizationJob.cs`
- Test: `tests/SentimentAnalysis.Application.Tests/Diarization/SpeakerRenumbererTests.cs`
- Test: `tests/SentimentAnalysis.Application.Tests/Diarization/DiarizationJobTests.cs` (ekleme)

**Interfaces:**
- Consumes: `SpeakerSegment(SpeakerId Speaker, TimeSpan Start, TimeSpan End)`, `SpeakerId(int Index)`, `SpeakerId.Unknown`.
- Produces: `SpeakerRenumberer.ByFirstAppearance(IReadOnlyList<SpeakerSegment>) → IReadOnlyList<SpeakerSegment>`. Task 6 ve 7 bu fonksiyonun çıktısını (kanonik numaralar) varsayar.

- [ ] **Step 1: Testi yaz (kırmızı)**

`tests/SentimentAnalysis.Application.Tests/Diarization/SpeakerRenumbererTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

/// <summary>sherpa küme kimliklerini KEYFİ verir — ilk konuşana 0 verdiği garanti değildir.
/// Yeniden numaralandırma olmadan ilk konuşan kişi "Konuşmacı F" olarak görünebilir ve
/// kullanıcı bunu rastgele isim atama sanır.</summary>
public class SpeakerRenumbererTests
{
    private static SpeakerSegment Seg(int index, double startSec, double endSec) =>
        new(new SpeakerId(index), TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec));

    [Fact]
    public void IlkKonusan_SifirOlur()
    {
        var input = new[] { Seg(5, 0, 2), Seg(3, 2, 4) };

        var result = SpeakerRenumberer.ByFirstAppearance(input);

        Assert.Equal(0, result[0].Speaker.Index);
        Assert.Equal("Konuşmacı A", result[0].Speaker.DefaultLabel);
    }

    [Fact]
    public void Numaralar_BosluksuzArtar()
    {
        var input = new[] { Seg(3, 0, 1), Seg(7, 1, 2), Seg(1, 2, 3) };

        var result = SpeakerRenumberer.ByFirstAppearance(input);

        Assert.Equal(new[] { 0, 1, 2 }, result.Select(s => s.Speaker.Index));
    }

    [Fact]
    public void AyniKonusmaci_AyniNumarayiAlir()
    {
        var input = new[] { Seg(9, 0, 1), Seg(4, 1, 2), Seg(9, 2, 3) };

        var result = SpeakerRenumberer.ByFirstAppearance(input);

        Assert.Equal(0, result[0].Speaker.Index);
        Assert.Equal(1, result[1].Speaker.Index);
        Assert.Equal(0, result[2].Speaker.Index);
    }

    [Fact]
    public void GirdiSirasiKarisiksa_IlkKONUSMA_ANINA_GoreNumaralanir()
    {
        // Liste zaman sırasında değil: 5 numaralı küme 10. saniyede, 2 numaralı 1. saniyede.
        var input = new[] { Seg(5, 10, 12), Seg(2, 1, 3) };

        var result = SpeakerRenumberer.ByFirstAppearance(input);

        Assert.Equal(1, result[0].Speaker.Index);  // 5 → ikinci konuşan
        Assert.Equal(0, result[1].Speaker.Index);  // 2 → ilk konuşan
    }

    [Fact]
    public void CikisSirasi_GirdiSirasiniKorur()
    {
        var input = new[] { Seg(5, 10, 12), Seg(2, 1, 3) };

        var result = SpeakerRenumberer.ByFirstAppearance(input);

        Assert.Equal(TimeSpan.FromSeconds(10), result[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(1), result[1].Start);
    }

    [Fact]
    public void Bilinmeyen_DegismedenGecer()
    {
        var input = new[] { new SpeakerSegment(SpeakerId.Unknown, TimeSpan.Zero, TimeSpan.FromSeconds(1)), Seg(4, 1, 2) };

        var result = SpeakerRenumberer.ByFirstAppearance(input);

        Assert.Equal(SpeakerId.Unknown, result[0].Speaker);
        Assert.Equal(0, result[1].Speaker.Index);  // Bilinmeyen numara TÜKETMEZ
    }

    [Fact]
    public void BosListe_BosDoner()
    {
        Assert.Empty(SpeakerRenumberer.ByFirstAppearance(Array.Empty<SpeakerSegment>()));
    }

    [Fact]
    public void Deterministiktir()
    {
        var input = new[] { Seg(5, 0, 2), Seg(3, 2, 4), Seg(5, 4, 6) };

        var a = SpeakerRenumberer.ByFirstAppearance(input).Select(s => s.Speaker.Index);
        var b = SpeakerRenumberer.ByFirstAppearance(input).Select(s => s.Speaker.Index);

        Assert.Equal(a, b);
    }
}
```

- [ ] **Step 2: Kırmızıyı doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Application.Tests --filter "FullyQualifiedName~SpeakerRenumbererTests"`
Beklenen: derleme hatası — `SpeakerRenumberer` tanımlı değil.

- [ ] **Step 3: Uygula**

`src/SentimentAnalysis.Application/Diarization/SpeakerRenumberer.cs`:

```csharp
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Diarization motorunun keyfi küme kimliklerini konuşma sırasına göre kanonik
/// numaralara çevirir: ilk konuşan 0 ("Konuşmacı A"), ikinci 1 ("Konuşmacı B")… Boşluk bırakmaz.
/// <para><b>Neden gerekli:</b> sherpa-onnx küme kimliklerini keyfi verir; ilk konuşana 0
/// verdiği garanti değildir. Yeniden numaralandırılmazsa ilk konuşan kişi "Konuşmacı F"
/// olarak görünür ve kullanıcı bunu rastgele isim atama sanır.</para>
/// <para><see cref="SpeakerId.Unknown"/> değişmeden geçer ve numara TÜKETMEZ — o bir
/// konuşmacı değil, "atanamadı" durumudur.</para>
/// Saf ve deterministik.</summary>
public static class SpeakerRenumberer
{
    public static IReadOnlyList<SpeakerSegment> ByFirstAppearance(IReadOnlyList<SpeakerSegment> segments)
    {
        var map = new Dictionary<int, int>();
        int next = 0;

        // Numaralandırma sırası ZAMANA göredir; girdi listesinin sırasına güvenilmez.
        foreach (var seg in segments.OrderBy(s => s.Start).ThenBy(s => s.End))
        {
            if (seg.Speaker.Index < 0) continue;              // Bilinmeyen numara tüketmez
            if (map.ContainsKey(seg.Speaker.Index)) continue;
            map[seg.Speaker.Index] = next++;
        }

        // Çıktı GİRDİ sırasını korur: yalnızca kimlikler değişir, sıralama çağıranın işidir.
        return segments
            .Select(s => s.Speaker.Index < 0 ? s : s with { Speaker = new SpeakerId(map[s.Speaker.Index]) })
            .ToList();
    }
}
```

- [ ] **Step 4: Yeşili doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Application.Tests --filter "FullyQualifiedName~SpeakerRenumbererTests"`
Beklenen: tümü PASS.

- [ ] **Step 5: DiarizationJob testini yaz (kırmızı)**

`tests/SentimentAnalysis.Application.Tests/Diarization/DiarizationJobTests.cs` dosyasına ekle (mevcut sahteleri kullan; yoksa dosyadaki desene uy):

```csharp
    [Fact]
    public async Task RunAsync_SaklamadanOnce_KonusmaSirasinaGoreNumaralandirir()
    {
        // Motor keyfi kimlikler döndürür; SAKLANAN veri kanonik olmalı — aksi halde raporu
        // okuyan her yüzey ayrı ayrı düzeltmek zorunda kalır ve biri unutulur.
        var speakers = new[]
        {
            new SpeakerSegment(new SpeakerId(5), TimeSpan.Zero, TimeSpan.FromSeconds(2)),
            new SpeakerSegment(new SpeakerId(3), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)),
        };
        var store = new FakeStore(audio: new float[16000]);
        var job = new DiarizationJob(new FakeDiarizer(speakers), store, AppConfig.Default);

        var outcome = await job.RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DiarizationOutcome.Completed, outcome);
        Assert.Equal(new[] { 0, 1 }, store.Saved!.Select(s => s.Speaker.Index));
    }
```

Not: `FakeStore` sınıfı `Saved` özelliğiyle `SaveSpeakerSegmentsAsync`'e gelen listeyi tutmalı; dosyada zaten bir sahte varsa ona `Saved` ekle, yoksa şunu ekle:

```csharp
    private sealed class FakeStore : IDiarizationStore
    {
        private readonly float[]? _audio;
        public IReadOnlyList<SpeakerSegment>? Saved { get; private set; }
        public FakeStore(float[]? audio) => _audio = audio;
        public Task<float[]?> GetSessionAudioAsync(Guid sessionId, CancellationToken ct) => Task.FromResult(_audio);
        public Task SaveSpeakerSegmentsAsync(Guid sessionId, IReadOnlyList<SpeakerSegment> segments, CancellationToken ct)
        {
            Saved = segments;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDiarizer(IReadOnlyList<SpeakerSegment> result) : IDiarizer
    {
        public Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(float[] mono16k, CancellationToken ct) =>
            Task.FromResult(result);
    }
```

- [ ] **Step 6: Kırmızıyı doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Application.Tests --filter "FullyQualifiedName~DiarizationJobTests"`
Beklenen: yeni test BAŞARISIZ — saklanan kimlikler `{5, 3}`.

- [ ] **Step 7: DiarizationJob'a bağla**

`DiarizationJob.RunAsync` içinde, `SaveSpeakerSegmentsAsync` çağrısından hemen önce:

```csharp
        // Motorun keyfi küme kimlikleri KALICI veriye sızmasın: kanonik numaralar (ilk konuşan
        // A) burada, saklamadan önce verilir — böylece okuyan her yüzey aynı numarayı görür.
        var canonical = SpeakerRenumberer.ByFirstAppearance(speakers);

        // Boş liste de saklanır: "diarization çalıştı, konuşmacı ayırt edilemedi" ile
        // "diarization hiç çalışmadı" farklı durumlardır ve rapor bunları ayırt etmelidir.
        await _store.SaveSpeakerSegmentsAsync(sessionId, canonical, ct).ConfigureAwait(false);
```

- [ ] **Step 8: Yeşili doğrula ve commit**

```bash
dotnet test SentimentAnalysis.sln
git add src/SentimentAnalysis.Application/Diarization/SpeakerRenumberer.cs src/SentimentAnalysis.Application/Diarization/DiarizationJob.cs tests/SentimentAnalysis.Application.Tests/Diarization/
git commit -m "feat(diarization): konuşmacılar konuşma sırasına göre numaralanır (A, B, C)"
```

---

### Task 4: Belirsiz atama — "Bilinmeyen" yerine işaretli atama

**Files:**
- Modify: `src/SentimentAnalysis.Application/Diarization/SpeakerAligner.cs`
- Test: `tests/SentimentAnalysis.Application.Tests/Diarization/SpeakerAlignerTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record AlignedSegment(TranscriptSegment Segment, SpeakerId Speaker, bool Uncertain);`
  - `public sealed record AlignedChunk(ChunkResult Chunk, SpeakerId Speaker, bool Uncertain);`
  - `SpeakerAligner.Align(IReadOnlyList<TranscriptSegment>, IReadOnlyList<SpeakerSegment>, double) → IReadOnlyList<AlignedSegment>`
  - `SpeakerAligner.AlignChunks(IReadOnlyList<ChunkResult>, IReadOnlyList<SpeakerSegment>, double) → IReadOnlyList<AlignedChunk>`
- Consumes: mevcut `SpeakerSegment`, `TranscriptSegment`, `ChunkResult`.
- Not: Task 5 (`SpeakerLineBuilder`) ve `SpeakerEmotionAggregator` bu tiplere göre güncellenir; bu task derlemeyi kırmamak için o iki çağıranı da uyarlar.

- [ ] **Step 1: Testi yaz (kırmızı)**

`SpeakerAlignerTests.cs` dosyasına ekle:

```csharp
    [Fact]
    public void EsikAlti_AtanirAmaBelirsizIsaretlenir()
    {
        // Örtüşme var ama %50'nin altında: "Bilinmeyen" demek bilgiyi çöpe atmaktı;
        // uydurma atama da yanlış. Doğrusu: en iyi adayı ata, belirsizliği İŞARETLE.
        var transcript = new[] { new TranscriptSegment("merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(10)) };
        var speakers = new[] { new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(3)) };

        var result = SpeakerAligner.Align(transcript, speakers, minOverlapRatio: 0.5);

        Assert.Equal(new SpeakerId(0), result[0].Speaker);
        Assert.True(result[0].Uncertain);
    }

    [Fact]
    public void EsikUstu_KesinAtanir()
    {
        var transcript = new[] { new TranscriptSegment("merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(10)) };
        var speakers = new[] { new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(9)) };

        var result = SpeakerAligner.Align(transcript, speakers, minOverlapRatio: 0.5);

        Assert.Equal(new SpeakerId(0), result[0].Speaker);
        Assert.False(result[0].Uncertain);
    }

    [Fact]
    public void HicOrtusmeYok_BilinmeyenKalir()
    {
        // Aday bile yok: burada atamak uydurmak olurdu.
        var transcript = new[] { new TranscriptSegment("merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(2)) };
        var speakers = new[] { new SpeakerSegment(new SpeakerId(0), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(9)) };

        var result = SpeakerAligner.Align(transcript, speakers, minOverlapRatio: 0.5);

        Assert.Equal(SpeakerId.Unknown, result[0].Speaker);
        Assert.True(result[0].Uncertain);
    }

    [Fact]
    public void HicKonusmaciYok_BilinmeyenDoner()
    {
        var transcript = new[] { new TranscriptSegment("merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(2)) };

        var result = SpeakerAligner.Align(transcript, Array.Empty<SpeakerSegment>(), minOverlapRatio: 0.5);

        Assert.Single(result);
        Assert.Equal(SpeakerId.Unknown, result[0].Speaker);
    }

    [Fact]
    public void AlignChunks_EsikAlti_AtanirAmaBelirsizIsaretlenir()
    {
        var chunk = new ChunkResult(TimeSpan.Zero, TimeSpan.FromSeconds(10),
            new FusedResult(ValenceArousal.Neutral, Emotion.Notr, Emotion.Notr, Confidence.Orta,
                true, false, IncongruencePattern.Yok, 1,
                TextVa: ValenceArousal.Neutral, ProsodyVa: ValenceArousal.Neutral,
                TextConfidence: 0, ProsodyConfidence: 0),
            "metin");
        var speakers = new[] { new SpeakerSegment(new SpeakerId(1), TimeSpan.Zero, TimeSpan.FromSeconds(2)) };

        var result = SpeakerAligner.AlignChunks(new[] { chunk }, speakers, minOverlapRatio: 0.5);

        Assert.Equal(new SpeakerId(1), result[0].Speaker);
        Assert.True(result[0].Uncertain);
    }
```

Mevcut testlerde `result[i].Item2` / tuple erişimi varsa `.Speaker` olarak güncelle.

- [ ] **Step 2: Kırmızıyı doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Application.Tests --filter "FullyQualifiedName~SpeakerAlignerTests"`
Beklenen: derleme hatası — `AlignedSegment` / `.Uncertain` yok.

- [ ] **Step 3: Uygula**

`SpeakerAligner.cs`'i şu hâle getir (tuple dönüşleri kayıtlara çevrilir):

```csharp
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Bir transkript segmentinin konuşmacı ataması. <paramref name="Uncertain"/> true ise
/// atama eşik-altı örtüşmeye dayanır: kullanıcıya gösterilirken bu belirsizlik GÖRÜNÜR
/// kılınmalıdır (master §12.2 — sistem emin olmadığı yerde emin gibi davranmaz).</summary>
public sealed record AlignedSegment(TranscriptSegment Segment, SpeakerId Speaker, bool Uncertain);

/// <summary>Bir chunk'ın konuşmacı ataması. Bkz. <see cref="AlignedSegment"/>.</summary>
public sealed record AlignedChunk(ChunkResult Chunk, SpeakerId Speaker, bool Uncertain);

/// <summary>Zaman damgalı transkript segmentlerini konuşmacı bölgeleriyle eşler (alt-proje 10b).
/// Saf ve deterministik: diarization'ın belirsizliği motor sınırında kalır, "bu cümle kimin?"
/// kararı burada test edilebilir matematiktir.
/// <para><b>Üç durum vardır:</b> eşik üstü örtüşme → kesin atama; eşik altı ama sıfırdan büyük
/// örtüşme → en iyi adaya atama + <c>Uncertain</c>; hiç örtüşme yok → <see cref="SpeakerId.Unknown"/>
/// (aday bile yoktur, atamak uydurmak olurdu).</para>
/// <para><b>Zaman ekseni:</b> her iki taraf da MUTLAK zamanlı olmalıdır. Transkript segmentleri
/// <c>ChunkProcessor</c>'da chunk-göreliden mutlağa çevrilir; diarization zaten mutlak üretir.
/// Eksenler karışırsa hizalama sessizce yanlış konuşmacıya metin atar.</para></summary>
public static class SpeakerAligner
{
    public static IReadOnlyList<AlignedSegment> Align(
        IReadOnlyList<TranscriptSegment> transcript,
        IReadOnlyList<SpeakerSegment> speakers,
        double minOverlapRatio)
    {
        var result = new List<AlignedSegment>(transcript.Count);
        foreach (var seg in transcript)
        {
            var (speaker, uncertain) = BestSpeaker(seg.Start, seg.End, speakers, minOverlapRatio);
            result.Add(new AlignedSegment(seg, speaker, uncertain));
        }
        return result;
    }

    public static IReadOnlyList<AlignedChunk> AlignChunks(
        IReadOnlyList<ChunkResult> chunks,
        IReadOnlyList<SpeakerSegment> speakers,
        double minOverlapRatio)
    {
        var result = new List<AlignedChunk>(chunks.Count);
        foreach (var chunk in chunks)
        {
            var (speaker, uncertain) = BestSpeaker(chunk.TimeStart, chunk.TimeEnd, speakers, minOverlapRatio);
            result.Add(new AlignedChunk(chunk, speaker, uncertain));
        }
        return result;
    }

    /// <summary>En çok örtüşen konuşmacıyı ve atamanın kesin olup olmadığını döndürür.
    /// Beraberlikte İLK bölge kazanır (kesin ">" karşılaştırması) — sonuç koşudan koşuya
    /// değişmesin diye.</summary>
    private static (SpeakerId Speaker, bool Uncertain) BestSpeaker(
        TimeSpan start, TimeSpan end, IReadOnlyList<SpeakerSegment> speakers, double minOverlapRatio)
    {
        double duration = (end - start).TotalSeconds;
        if (duration <= 0) return (SpeakerId.Unknown, true);

        SpeakerId best = SpeakerId.Unknown;
        double bestOverlap = 0;

        foreach (var sp in speakers)
        {
            double overlap =
                Math.Min(end.TotalSeconds, sp.End.TotalSeconds) -
                Math.Max(start.TotalSeconds, sp.Start.TotalSeconds);
            if (overlap > bestOverlap) { bestOverlap = overlap; best = sp.Speaker; }
        }

        if (bestOverlap <= 0) return (SpeakerId.Unknown, true);   // aday yok
        return (best, bestOverlap / duration < minOverlapRatio);
    }
}
```

- [ ] **Step 4: Çağıranları uyarla**

`SpeakerEmotionAggregator.Aggregate` içinde `aligned` artık `AlignedChunk` listesidir:

```csharp
        return aligned
            .GroupBy(a => a.Speaker)
            .Select(g => new SpeakerEmotionSummary(
                g.Key,
                Average(g.Select(x => x.Chunk.Fused.TextVa)),
                Average(g.Select(x => x.Chunk.Fused.ProsodyVa)),
                Average(g.Select(x => x.Chunk.Fused.Combined)),
                g.Count()))
            .OrderBy(s => s.Speaker.Index < 0 ? int.MaxValue : s.Speaker.Index)
            .ToList();
```

(Yalnızca `x.Chunk` erişimi değişir; gerisi aynı kalır.)

- [ ] **Step 5: Yeşili doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Application.Tests`
Beklenen: `SpeakerLineBuilder` derleme hatası verirse Task 5'e geç; diğer tüm testler PASS.

- [ ] **Step 6: Commit (Task 5 ile birlikte)**

Bu task tek başına derlenmeyebilir (`SpeakerLineBuilder` imzası değişecek); Task 5 bittikten sonra birlikte commit et.

---

### Task 5: Satır birleştirme kesinliğe saygı duysun

**Files:**
- Modify: `src/SentimentAnalysis.Application/Diarization/SpeakerLineBuilder.cs`
- Modify: `src/SentimentAnalysis.Desktop/ReportViewModel.cs` (`SpeakerLineVm`)
- Test: `tests/SentimentAnalysis.Application.Tests/Diarization/SpeakerLineBuilderTests.cs`

**Interfaces:**
- Consumes: `AlignedSegment` (Task 4).
- Produces: `SpeakerLine(SpeakerId Speaker, string Text, TimeSpan Start, TimeSpan End, bool Uncertain)`; `SpeakerLineBuilder.Build(IReadOnlyList<AlignedSegment>) → IReadOnlyList<SpeakerLine>`. Task 8 (UI) `SpeakerLine.Uncertain`'a dayanır.

- [ ] **Step 1: Testi yaz (kırmızı)**

`SpeakerLineBuilderTests.cs` dosyasına ekle:

```csharp
    private static AlignedSegment Aligned(int speaker, string text, double startSec, double endSec, bool uncertain) =>
        new(new TranscriptSegment(text, TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec)),
            new SpeakerId(speaker), uncertain);

    [Fact]
    public void AyniKonusmaci_FarkliKesinlik_Birlesmez()
    {
        // Emin bir cümleyle belirsiz bir cümle aynı satıra karışırsa, satırın HANGİ kısmına
        // güvenileceği kaybolur — kullanıcı yanlış alıntı yapar.
        var aligned = new[]
        {
            Aligned(0, "kesin cümle", 0, 1, uncertain: false),
            Aligned(0, "belirsiz cümle", 1, 2, uncertain: true),
        };

        var lines = SpeakerLineBuilder.Build(aligned);

        Assert.Equal(2, lines.Count);
        Assert.False(lines[0].Uncertain);
        Assert.True(lines[1].Uncertain);
    }

    [Fact]
    public void AyniKonusmaci_AyniKesinlik_Birlesir()
    {
        var aligned = new[]
        {
            Aligned(0, "birinci", 0, 1, uncertain: false),
            Aligned(0, "ikinci", 1, 2, uncertain: false),
        };

        var lines = SpeakerLineBuilder.Build(aligned);

        Assert.Single(lines);
        Assert.Equal("birinci ikinci", lines[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(2), lines[0].End);
    }
```

Mevcut testlerdeki tuple tabanlı `Build` çağrılarını `AlignedSegment` kullanacak şekilde güncelle.

- [ ] **Step 2: Kırmızıyı doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Application.Tests --filter "FullyQualifiedName~SpeakerLineBuilderTests"`
Beklenen: derleme hatası — `SpeakerLine.Uncertain` yok.

- [ ] **Step 3: Uygula**

`SpeakerLineBuilder.cs`:

```csharp
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Okunabilir bir konuşma satırı: "Konuşmacı A: merhaba nasılsın".
/// <paramref name="Uncertain"/> true ise atama eşik-altı örtüşmeye dayanır ve UI bunu
/// görünür kılmalıdır.</summary>
public sealed record SpeakerLine(SpeakerId Speaker, string Text, TimeSpan Start, TimeSpan End, bool Uncertain);

/// <summary>Hizalanmış segmentleri konuşma satırlarına indirger: ardışık, AYNI konuşmacıya ait
/// ve AYNI kesinlikteki segmentler tek satırda birleşir. Saf ve deterministik.
/// <para>Kesinlik de birleştirme anahtarıdır: emin bir cümle belirsiz bir cümleyle aynı satıra
/// karışırsa satırın hangi kısmına güvenileceği kaybolur.</para>
/// <para>Birleştirme yalnızca ardışıklığa bakar; konuşmacı değişip geri dönerse ayrı satır olur
/// (A→B→A üç satırdır) — çünkü aradaki B satırı zaman sırasını korumalıdır.</para></summary>
public static class SpeakerLineBuilder
{
    public static IReadOnlyList<SpeakerLine> Build(IReadOnlyList<AlignedSegment> aligned)
    {
        var lines = new List<SpeakerLine>();

        foreach (var a in aligned)
        {
            if (lines.Count > 0 && lines[^1].Speaker == a.Speaker && lines[^1].Uncertain == a.Uncertain)
            {
                var last = lines[^1];
                lines[^1] = last with { Text = $"{last.Text} {a.Segment.Text}", End = a.Segment.End };
            }
            else
            {
                lines.Add(new SpeakerLine(a.Speaker, a.Segment.Text, a.Segment.Start, a.Segment.End, a.Uncertain));
            }
        }

        return lines;
    }
}
```

- [ ] **Step 4: SpeakerLineVm'e bayrağı taşı**

`ReportViewModel.cs` içindeki `SpeakerLineVm`:

```csharp
public sealed class SpeakerLineVm
{
    public string Label { get; }
    public string Text { get; }
    public string TimeRange { get; }

    /// <summary>Atama eşik-altı örtüşmeye dayanıyor: UI bunu soluk renk + "?" rozetiyle
    /// gösterir. Gizlenirse sistem emin olmadığı yerde emin görünür.</summary>
    public bool Uncertain { get; }

    public SpeakerLineVm(SpeakerLine line, string? userGivenName = null)
    {
        Label = userGivenName ?? line.Speaker.DefaultLabel;
        Text = line.Text;
        TimeRange = $"{line.Start:mm\\:ss}–{line.End:mm\\:ss}";
        Uncertain = line.Uncertain;
    }
}
```

- [ ] **Step 5: Yeşili doğrula ve commit**

```bash
dotnet test SentimentAnalysis.sln
git add src/SentimentAnalysis.Application/Diarization/SpeakerAligner.cs src/SentimentAnalysis.Application/Diarization/SpeakerLineBuilder.cs src/SentimentAnalysis.Application/Diarization/SpeakerEmotionAggregator.cs src/SentimentAnalysis.Desktop/ReportViewModel.cs tests/SentimentAnalysis.Application.Tests/Diarization/
git commit -m "feat(diarization): eşik-altı atamalar Bilinmeyen yerine işaretli atanır"
```

---

### Task 6: Konuşmacı prozodisi — bölge seçimi ve ağırlıklı ortalama

**Files:**
- Create: `src/SentimentAnalysis.Application/Diarization/SpeakerProsodyAnalyzer.cs`
- Modify: `src/SentimentAnalysis.Application/Configuration/AppConfig.cs` (`DiarizationConfig`)
- Test: `tests/SentimentAnalysis.Application.Tests/Diarization/SpeakerProsodyAnalyzerTests.cs`

**Interfaces:**
- Consumes: `IProsodyEmotionModel.AnalyzeAsync(AudioChunk, CancellationToken) → Task<ProsodyEmotion>`; `ProsodyEmotion(ValenceArousal Va, double ModelConfidence)`; `AudioChunk(float[] Samples, int SampleRate, TimeSpan Start, TimeSpan End)`.
- Produces:
  - `public sealed record SpeakerProsodySummary(SpeakerId Speaker, ValenceArousal Va, double AnalyzedSeconds, int RegionCount);`
  - `SpeakerRegionSelector.Select(IEnumerable<SpeakerSegment> regions, double minSeconds, double budgetSeconds) → IReadOnlyList<SpeakerSegment>`
  - `SpeakerProsodyAnalyzer.AnalyzeAsync(float[] mono16k, IReadOnlyList<SpeakerSegment> speakers, CancellationToken ct) → Task<IReadOnlyList<SpeakerProsodySummary>>`
  - Config: `DiarizationConfig.SpeakerProsodyMinRegionSeconds` (1.0), `DiarizationConfig.SpeakerProsodyBudgetSeconds` (60.0)

- [ ] **Step 1: Testi yaz (kırmızı)**

`tests/SentimentAnalysis.Application.Tests/Diarization/SpeakerProsodyAnalyzerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

/// <summary>Konuşmacı duygusu, chunk ortalaması yerine konuşmacının KENDİ sesinden ölçülür.
/// Eski yöntemde 10–30 sn'lik chunk'lar birden fazla kişiyi içerdiğinde hiçbirine %50 örtüşme
/// yetişmiyor ve chunk ölçümden tamamen düşüyordu — yani en çok karşılıklı konuşulan anlar
/// sistematik olarak eleniyordu.</summary>
public class SpeakerProsodyAnalyzerTests
{
    private static SpeakerSegment Seg(int speaker, double startSec, double endSec) =>
        new(new SpeakerId(speaker), TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec));

    /// <summary>Verilen valence'ı aynen döndürür; hangi seslerin modele gittiğini kaydeder.</summary>
    private sealed class FakeProsody : IProsodyEmotionModel
    {
        private readonly Func<AudioChunk, double> _valence;
        public List<int> SampleCounts { get; } = new();
        public FakeProsody(Func<AudioChunk, double>? valence = null) => _valence = valence ?? (_ => 0.5);
        public Task<ProsodyEmotion> AnalyzeAsync(AudioChunk chunk, CancellationToken ct)
        {
            SampleCounts.Add(chunk.Samples.Length);
            return Task.FromResult(new ProsodyEmotion(new ValenceArousal(_valence(chunk), 0.5), 0.8));
        }
    }

    private static float[] Audio(double seconds) => new float[(int)(seconds * 16000)];

    [Fact]
    public void RegionSelector_KisaBolgeleriEler()
    {
        var regions = new[] { Seg(0, 0, 0.5), Seg(0, 1, 3) };

        var selected = SpeakerRegionSelector.Select(regions, minSeconds: 1.0, budgetSeconds: 60);

        Assert.Single(selected);
        Assert.Equal(TimeSpan.FromSeconds(1), selected[0].Start);
    }

    [Fact]
    public void RegionSelector_UzunBolgeyiOnceAlir()
    {
        var regions = new[] { Seg(0, 0, 2), Seg(0, 10, 20) };

        var selected = SpeakerRegionSelector.Select(regions, minSeconds: 1.0, budgetSeconds: 60);

        Assert.Equal(TimeSpan.FromSeconds(10), selected[0].Start);   // 10 sn'lik önce
    }

    [Fact]
    public void RegionSelector_ButceyiAsmaz()
    {
        var regions = new[] { Seg(0, 0, 30), Seg(0, 40, 70), Seg(0, 80, 110) };

        var selected = SpeakerRegionSelector.Select(regions, minSeconds: 1.0, budgetSeconds: 60);

        Assert.Equal(2, selected.Count);
        Assert.Equal(60, selected.Sum(s => (s.End - s.Start).TotalSeconds));
    }

    [Fact]
    public void RegionSelector_Deterministiktir()
    {
        var regions = new[] { Seg(0, 5, 10), Seg(0, 0, 5), Seg(0, 20, 25) };

        var a = SpeakerRegionSelector.Select(regions, 1.0, 60).Select(s => s.Start);
        var b = SpeakerRegionSelector.Select(regions, 1.0, 60).Select(s => s.Start);

        Assert.Equal(a, b);
    }

    [Fact]
    public async Task AnalyzeAsync_HerKonusmaciIcinKendiSesiniKullanir()
    {
        // Konuşmacı 0: 0–4 sn, konuşmacı 1: 4–8 sn. Sahte model, sesin uzunluğuna göre
        // farklı valence döndürmez; burada önemli olan HER konuşmacı için ayrı sonuç üretilmesi.
        var speakers = new[] { Seg(0, 0, 4), Seg(1, 4, 8) };
        var analyzer = new SpeakerProsodyAnalyzer(new FakeProsody(), AppConfig.Default);

        var result = await analyzer.AnalyzeAsync(Audio(8), speakers, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(new SpeakerId(0), result[0].Speaker);
        Assert.Equal(new SpeakerId(1), result[1].Speaker);
        Assert.Equal(4.0, result[0].AnalyzedSeconds, precision: 3);
    }

    [Fact]
    public async Task AnalyzeAsync_SureAgirlikliOrtalamaAlir()
    {
        // 9 sn'lik bölge valence +1, 1 sn'lik bölge valence −1 → ağırlıklı ortalama +0,8.
        var speakers = new[] { Seg(0, 0, 9), Seg(0, 10, 11) };
        var model = new FakeProsody(chunk => chunk.Samples.Length > 5 * 16000 ? 1.0 : -1.0);
        var analyzer = new SpeakerProsodyAnalyzer(model, AppConfig.Default);

        var result = await analyzer.AnalyzeAsync(Audio(12), speakers, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(0.8, result[0].Va.Valence, precision: 3);
        Assert.Equal(2, result[0].RegionCount);
    }

    [Fact]
    public async Task AnalyzeAsync_YeterliSesiOlmayanKonusmaci_SonucUretmez()
    {
        // 0,5 sn'lik tek bölge: prozodi için yetersiz. Uydurma sonuç ÜRETİLMEZ.
        var speakers = new[] { Seg(0, 0, 0.5), Seg(1, 1, 5) };
        var analyzer = new SpeakerProsodyAnalyzer(new FakeProsody(), AppConfig.Default);

        var result = await analyzer.AnalyzeAsync(Audio(6), speakers, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(new SpeakerId(1), result[0].Speaker);
    }

    [Fact]
    public async Task AnalyzeAsync_Bilinmeyen_AnalizEdilmez()
    {
        var speakers = new[]
        {
            new SpeakerSegment(SpeakerId.Unknown, TimeSpan.Zero, TimeSpan.FromSeconds(4)),
            Seg(0, 4, 8),
        };
        var analyzer = new SpeakerProsodyAnalyzer(new FakeProsody(), AppConfig.Default);

        var result = await analyzer.AnalyzeAsync(Audio(8), speakers, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(new SpeakerId(0), result[0].Speaker);
    }

    [Fact]
    public async Task AnalyzeAsync_SesSinirlariniAsanBolge_Kirpilir()
    {
        // Bölge 10 sn'ye kadar diyor ama elimizde 5 sn ses var: taşma çökertmemeli.
        var speakers = new[] { Seg(0, 0, 10) };
        var model = new FakeProsody();
        var analyzer = new SpeakerProsodyAnalyzer(model, AppConfig.Default);

        var result = await analyzer.AnalyzeAsync(Audio(5), speakers, CancellationToken.None);

        Assert.Single(result);
        Assert.All(model.SampleCounts, n => Assert.True(n <= 5 * 16000));
    }
}
```

- [ ] **Step 2: Kırmızıyı doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Application.Tests --filter "FullyQualifiedName~SpeakerProsodyAnalyzerTests"`
Beklenen: derleme hatası — `SpeakerProsodyAnalyzer` / `SpeakerRegionSelector` yok.

- [ ] **Step 3: Config'e bütçeleri ekle**

`AppConfig.cs` içindeki `DiarizationConfig` kaydına, `MaxRecordedMinutes` satırından sonra ekle:

```csharp
    int MaxRecordedMinutes = 90,          // ses biriktirme üst sınırı (16 kHz PCM16 ≈ 115 MB/saat)
    // Konuşmacı prozodisi (10d): duygu, konuşmacının KENDİ sesinden ölçülür.
    // 1 sn altı bölgeler prozodi için yetersiz bağlam taşır; 60 sn ise ortalama için fazlasıyla
    // yeterlidir — tüm sesi işlemek doğruluk kazandırmadan süreyi katlar.
    // BAŞLANGIÇ DEĞERLERİ, KALİBRE EDİLMEMİŞ (eval-09).
    double SpeakerProsodyMinRegionSeconds = 1.0,
    double SpeakerProsodyBudgetSeconds = 60.0);
```

(`MaxRecordedMinutes` satırının sonundaki `)` kaldırılır, son parametreye taşınır.)

- [ ] **Step 4: Uygula**

`src/SentimentAnalysis.Application/Diarization/SpeakerProsodyAnalyzer.cs`:

```csharp
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Bir konuşmacının kendi sesinden ölçülmüş prozodi duygusu.
/// <paramref name="AnalyzedSeconds"/> saydamlık için taşınır: az sese dayanan bir ortalamaya
/// fazla güvenilmemelidir (master §12.2).</summary>
public sealed record SpeakerProsodySummary(
    SpeakerId Speaker, ValenceArousal Va, double AnalyzedSeconds, int RegionCount);

/// <summary>Bir konuşmacının hangi konuşma bölgelerinin analiz edileceğini seçer. Saf ve
/// deterministik: uzun bölgeler önce (daha çok bağlam), süre bütçesi dolunca durulur.</summary>
public static class SpeakerRegionSelector
{
    public static IReadOnlyList<SpeakerSegment> Select(
        IEnumerable<SpeakerSegment> regions, double minSeconds, double budgetSeconds)
    {
        var selected = new List<SpeakerSegment>();
        double used = 0;

        // Uzun önce; eşitlikte erken başlayan önce → sonuç koşudan koşuya değişmez.
        foreach (var r in regions
            .Where(r => (r.End - r.Start).TotalSeconds >= minSeconds)
            .OrderByDescending(r => (r.End - r.Start).TotalSeconds)
            .ThenBy(r => r.Start))
        {
            double length = (r.End - r.Start).TotalSeconds;
            if (used + length > budgetSeconds) continue;   // sığmayan bölgeyi atla, aramayı sürdür
            selected.Add(r);
            used += length;
        }

        return selected;
    }
}

/// <summary>Konuşmacı-başına duyguyu, kişinin KENDİ konuşma bölgelerinin sesinden ölçer
/// (10d). Oturum sonrası arka plan geçişinde çalışır — canlı hattı yavaşlatmaz.
/// <para><b>Neden chunk ortalaması değil:</b> 10–30 sn'lik chunk'lar gerçek bir diyalogda
/// birden fazla kişiyi içerir; hiçbirine örtüşme eşiği yetişmediğinde chunk ölçümden tamamen
/// düşerdi — yani en çok karşılıklı konuşulan anlar sistematik olarak elenirdi.</para></summary>
public sealed class SpeakerProsodyAnalyzer
{
    private const int SampleRate = 16000;

    private readonly IProsodyEmotionModel _prosody;
    private readonly DiarizationConfig _cfg;

    public SpeakerProsodyAnalyzer(IProsodyEmotionModel prosody, AppConfig config)
    {
        _prosody = prosody;
        _cfg = config.Diarization;
    }

    public async Task<IReadOnlyList<SpeakerProsodySummary>> AnalyzeAsync(
        float[] mono16k, IReadOnlyList<SpeakerSegment> speakers, CancellationToken ct)
    {
        var summaries = new List<SpeakerProsodySummary>();

        // Bilinmeyen bir konuşmacı değil, "atanamadı" durumudur: sesi ölçülmez.
        foreach (var group in speakers
            .Where(s => s.Speaker.Index >= 0)
            .GroupBy(s => s.Speaker)
            .OrderBy(g => g.Key.Index))
        {
            ct.ThrowIfCancellationRequested();

            var regions = SpeakerRegionSelector.Select(
                group, _cfg.SpeakerProsodyMinRegionSeconds, _cfg.SpeakerProsodyBudgetSeconds);

            double weightedValence = 0, weightedArousal = 0, totalSeconds = 0;
            int used = 0;

            foreach (var region in regions)
            {
                var slice = Slice(mono16k, region);
                if (slice.Length < _cfg.SpeakerProsodyMinRegionSeconds * SampleRate) continue;

                var chunk = new AudioChunk(slice, SampleRate, region.Start, region.End);
                var emotion = await _prosody.AnalyzeAsync(chunk, ct).ConfigureAwait(false);

                double seconds = slice.Length / (double)SampleRate;
                weightedValence += emotion.Va.Valence * seconds;
                weightedArousal += emotion.Va.Arousal * seconds;
                totalSeconds += seconds;
                used++;
            }

            // Yeterli sesi olmayan konuşmacı için sonuç ÜRETİLMEZ — uydurma ortalama yerine yokluk.
            if (used == 0 || totalSeconds <= 0) continue;

            summaries.Add(new SpeakerProsodySummary(
                group.Key,
                ValenceArousal.Clamp(weightedValence / totalSeconds, weightedArousal / totalSeconds),
                totalSeconds,
                used));
        }

        return summaries;
    }

    /// <summary>Bölgeyi ses dizisinden keser; sınır dışına taşan bölge kırpılır (diarization
    /// zaman damgaları saklanan sesten uzun olabilir).</summary>
    private static float[] Slice(float[] mono16k, SpeakerSegment region)
    {
        int start = Math.Clamp((int)(region.Start.TotalSeconds * SampleRate), 0, mono16k.Length);
        int end = Math.Clamp((int)(region.End.TotalSeconds * SampleRate), start, mono16k.Length);
        return mono16k[start..end];
    }
}
```

- [ ] **Step 5: Yeşili doğrula ve commit**

```bash
dotnet test SentimentAnalysis.sln
git add src/SentimentAnalysis.Application/Diarization/SpeakerProsodyAnalyzer.cs src/SentimentAnalysis.Application/Configuration/AppConfig.cs tests/SentimentAnalysis.Application.Tests/Diarization/SpeakerProsodyAnalyzerTests.cs
git commit -m "feat(diarization): konuşmacı duygusu kendi sesinden ölçülür"
```

---

### Task 7: Arka plan geçişine bağla ve rapora taşı

**Files:**
- Modify: `src/SentimentAnalysis.Application/Diarization/SpeakerAnalysisService.cs`
- Modify: `src/SentimentAnalysis.Desktop/ReportViewModel.cs`
- Modify: `src/SentimentAnalysis.Desktop.Avalonia/CompositionRoot.cs`
- Test: `tests/SentimentAnalysis.Application.Tests/Diarization/SpeakerAnalysisServiceTests.cs` (yoksa oluştur)

**Interfaces:**
- Consumes: `SpeakerProsodyAnalyzer.AnalyzeAsync` (Task 6), `IDiarizationStore.GetSessionAudioAsync` (mevcut).
- Produces: `SpeakerAnalysisResult.SpeakerProsody` (`IReadOnlyList<SpeakerProsodySummary>`, varsayılan boş liste); `ReportViewModel.ApplySpeakerEmotions(IReadOnlyList<ChunkResult>, IReadOnlyList<SpeakerSegment>, double, IReadOnlyList<SpeakerProsodySummary>)`.

- [ ] **Step 1: Testi yaz (kırmızı)**

`tests/SentimentAnalysis.Application.Tests/Diarization/SpeakerAnalysisServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

public class SpeakerAnalysisServiceTests
{
    private sealed class FakeState : IDiarizationState { public bool Enabled { get; set; } = true; }

    private sealed class FakeDiarizer(IReadOnlyList<SpeakerSegment> result) : IDiarizer
    {
        public Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(float[] mono16k, CancellationToken ct) =>
            Task.FromResult(result);
    }

    private sealed class FakeStore(float[]? audio, IReadOnlyList<SpeakerSegment> saved) : IDiarizationStore
    {
        public IReadOnlyList<SpeakerSegment> Saved { get; private set; } = saved;
        public Task<float[]?> GetSessionAudioAsync(Guid sessionId, CancellationToken ct) => Task.FromResult(audio);
        public Task SaveSpeakerSegmentsAsync(Guid sessionId, IReadOnlyList<SpeakerSegment> segments, CancellationToken ct)
        {
            Saved = segments;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeReader(IReadOnlyList<SpeakerSegment> speakers) : ISpeakerDataReader
    {
        public Task<IReadOnlyList<TranscriptSegment>> GetTranscriptSegmentsAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TranscriptSegment>>(Array.Empty<TranscriptSegment>());
        public Task<IReadOnlyList<SpeakerSegment>> GetSpeakerSegmentsAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(speakers);
        public Task<IReadOnlyList<ChunkResult>> GetChunkResultsAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ChunkResult>>(Array.Empty<ChunkResult>());
    }

    private sealed class FakeProsody : IProsodyEmotionModel
    {
        public Task<ProsodyEmotion> AnalyzeAsync(AudioChunk chunk, CancellationToken ct) =>
            Task.FromResult(new ProsodyEmotion(new ValenceArousal(0.6, 0.7), 0.9));
    }

    [Fact]
    public async Task RunAsync_KonusmaciProzodisiniDeUretir()
    {
        var speakers = new[]
        {
            new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(4)),
            new SpeakerSegment(new SpeakerId(1), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)),
        };
        var audio = new float[8 * 16000];
        var store = new FakeStore(audio, speakers);
        var job = new DiarizationJob(new FakeDiarizer(speakers), store, AppConfig.Default);
        var analyzer = new SpeakerProsodyAnalyzer(new FakeProsody(), AppConfig.Default);
        var service = new SpeakerAnalysisService(job, new FakeReader(speakers), new FakeState(), analyzer, store);

        var result = await service.RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DiarizationOutcome.Completed, result.Outcome);
        Assert.Equal(2, result.SpeakerProsody.Count);
        Assert.Equal(0.6, result.SpeakerProsody[0].Va.Valence, precision: 3);
    }

    [Fact]
    public async Task RunAsync_SesYoksa_ProzodiListesiBosKalir()
    {
        var store = new FakeStore(audio: null, saved: Array.Empty<SpeakerSegment>());
        var job = new DiarizationJob(new FakeDiarizer(Array.Empty<SpeakerSegment>()), store, AppConfig.Default);
        var analyzer = new SpeakerProsodyAnalyzer(new FakeProsody(), AppConfig.Default);
        var service = new SpeakerAnalysisService(job, new FakeReader(Array.Empty<SpeakerSegment>()), new FakeState(), analyzer, store);

        var result = await service.RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DiarizationOutcome.NoAudio, result.Outcome);
        Assert.Empty(result.SpeakerProsody);
    }
}
```

- [ ] **Step 2: Kırmızıyı doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Application.Tests --filter "FullyQualifiedName~SpeakerAnalysisServiceTests"`
Beklenen: derleme hatası — `SpeakerAnalysisService` altı parametreli kurucuya ve `SpeakerProsody` alanına sahip değil.

- [ ] **Step 3: Uygula**

`SpeakerAnalysisService.cs`:

```csharp
public sealed record SpeakerAnalysisResult(
    DiarizationOutcome Outcome,
    IReadOnlyList<TranscriptSegment> Transcript,
    IReadOnlyList<SpeakerSegment> Speakers,
    IReadOnlyList<ChunkResult>? Chunks = null)
{
    public IReadOnlyList<ChunkResult> Chunks { get; init; } = Chunks ?? Array.Empty<ChunkResult>();

    /// <summary>Konuşmacının KENDİ sesinden ölçülmüş prozodi duygusu (10d). Additive:
    /// varsayılan boş liste, eski çağrılar etkilenmez.</summary>
    public IReadOnlyList<SpeakerProsodySummary> SpeakerProsody { get; init; } =
        Array.Empty<SpeakerProsodySummary>();

    public static SpeakerAnalysisResult Disabled { get; } =
        new(DiarizationOutcome.NoAudio, Array.Empty<TranscriptSegment>(), Array.Empty<SpeakerSegment>());
}
```

`SpeakerAnalysisService` sınıfını güncelle:

```csharp
    private readonly DiarizationJob _job;
    private readonly ISpeakerDataReader _reader;
    private readonly IDiarizationState _state;
    private readonly SpeakerProsodyAnalyzer _prosody;
    private readonly IDiarizationStore _store;

    public SpeakerAnalysisService(
        DiarizationJob job, ISpeakerDataReader reader, IDiarizationState state,
        SpeakerProsodyAnalyzer prosody, IDiarizationStore store)
    {
        _job = job; _reader = reader; _state = state;
        _prosody = prosody; _store = store;
    }

    public bool Enabled => _state.Enabled;

    public async Task<SpeakerAnalysisResult> RunAsync(Guid sessionId, CancellationToken ct)
    {
        var outcome = await _job.RunAsync(sessionId, ct).ConfigureAwait(false);

        if (outcome != DiarizationOutcome.Completed)
            return new SpeakerAnalysisResult(outcome, Array.Empty<TranscriptSegment>(), Array.Empty<SpeakerSegment>());

        var transcript = await _reader.GetTranscriptSegmentsAsync(sessionId, ct).ConfigureAwait(false);
        var speakers = await _reader.GetSpeakerSegmentsAsync(sessionId, ct).ConfigureAwait(false);
        var chunks = await _reader.GetChunkResultsAsync(sessionId, ct).ConfigureAwait(false);

        // Konuşmacı duygusu, kişinin KENDİ sesinden ölçülür (10d). Ses zaten diarization için
        // saklanmıştı; ek kayıt maliyeti yoktur.
        var audio = await _store.GetSessionAudioAsync(sessionId, ct).ConfigureAwait(false);
        var prosody = audio is null || audio.Length == 0
            ? Array.Empty<SpeakerProsodySummary>()
            : await _prosody.AnalyzeAsync(audio, speakers, ct).ConfigureAwait(false);

        return new SpeakerAnalysisResult(outcome, transcript, speakers, chunks) { SpeakerProsody = prosody };
    }
```

- [ ] **Step 4: Raporu güncelle**

`ReportViewModel.cs` — `SpeakerEmotionVm`'in ses sütunu artık ölçülmüş prozodiyi kullanır; ayrıca üç panel de nötr yarıçapını alır (canlı ekranla ayrışmasın diye):

```csharp
public sealed class SpeakerEmotionVm
{
    public string Label { get; }
    public SourceAnalysisVm TextAnalysis { get; }
    public SourceAnalysisVm AudioAnalysis { get; }
    public SourceAnalysisVm CombinedAnalysis { get; }

    /// <summary>Ses ölçümünün kaç saniyelik konuşmaya dayandığı (10d). 0 = konuşmacının
    /// prozodi için yeterli sesi yoktu; UI bunu göstermelidir.</summary>
    public double AnalyzedSeconds { get; }

    /// <summary>Ses ölçümü gerçekten yapıldı mı? UI bu bayrağa bağlanır (XAML'de
    /// "sayı > 0" karşılaştırması yapılamaz).</summary>
    public bool HasAudioMeasurement => AnalyzedSeconds > 0;

    public SpeakerEmotionVm(
        SpeakerEmotionSummary s, double temperature, double? neutralRadius,
        SpeakerProsodySummary? prosody = null, string? userGivenName = null)
    {
        Label = userGivenName ?? s.Speaker.DefaultLabel;
        var audioVa = prosody?.Va ?? s.Audio;
        AnalyzedSeconds = prosody?.AnalyzedSeconds ?? 0;
        TextAnalysis = new SourceAnalysisVm("Metin Analizi", s.Text, temperature, neutralRadius);
        AudioAnalysis = new SourceAnalysisVm("Ses Analizi", audioVa, temperature, neutralRadius);
        CombinedAnalysis = new SourceAnalysisVm("Nihai Karar", audioVa, temperature, neutralRadius);
    }
}
```

`ReportViewModel` alanına `_neutralRadius` ekle (kurucuda `config.Fusion.NeutralRadius`'tan) ve `ApplySpeakerEmotions`'ı güncelle:

```csharp
    public void ApplySpeakerEmotions(
        IReadOnlyList<ChunkResult> chunks,
        IReadOnlyList<SpeakerSegment> speakers,
        double minOverlapRatio,
        IReadOnlyList<SpeakerProsodySummary> speakerProsody)
    {
        var bySpeaker = speakerProsody.ToDictionary(p => p.Speaker);
        var summaries = SpeakerEmotionAggregator.Aggregate(chunks, speakers, minOverlapRatio);
        SpeakerEmotions = summaries
            .Select(s => new SpeakerEmotionVm(
                s, _temperature, _neutralRadius,
                bySpeaker.TryGetValue(s.Speaker, out var p) ? p : null))
            .ToList();
        OnPropertyChanged(nameof(SpeakerEmotions));
    }
```

`MainViewModel`'deki çağrıyı yeni imzaya uyarla (`result.SpeakerProsody` geçilir).

- [ ] **Step 5: DI'ı güncelle**

`CompositionRoot.cs`'te `ISpeakerAnalysis` kaydından önce analizörü kaydet:

```csharp
        services.AddSingleton<SpeakerProsodyAnalyzer>();
```

`SpeakerProsodyAnalyzer` kurucusu `IProsodyEmotionModel` ve `AppConfig` ister; ikisi de zaten kayıtlıdır.

- [ ] **Step 6: Yeşili doğrula ve commit**

```bash
dotnet test SentimentAnalysis.sln
git add src/SentimentAnalysis.Application/Diarization/SpeakerAnalysisService.cs src/SentimentAnalysis.Desktop/ReportViewModel.cs src/SentimentAnalysis.Desktop/MainViewModel.cs src/SentimentAnalysis.Desktop.Avalonia/CompositionRoot.cs tests/SentimentAnalysis.Application.Tests/Diarization/SpeakerAnalysisServiceTests.cs
git commit -m "feat(diarization): ölçülmüş konuşmacı prozodisi rapora bağlandı"
```

---

### Task 8: UI — belirsiz satırı görünür kıl

**Files:**
- Modify: `src/SentimentAnalysis.Desktop.Avalonia/Views/ReportView.axaml`
- Test: `tests/SentimentAnalysis.Desktop.Tests/ReportViewModelTests.cs` (ekleme)

**Interfaces:**
- Consumes: `SpeakerLineVm.Uncertain` (Task 5), `SpeakerEmotionVm.AnalyzedSeconds` (Task 7).

- [ ] **Step 1: Testi yaz (kırmızı)**

`ReportViewModelTests.cs` dosyasına ekle:

```csharp
    [Fact]
    public void ApplySpeakers_BelirsizAtama_SatiraYansir()
    {
        // Eşik-altı örtüşme: cümle en iyi adaya atanır ama UI'ın uyarabilmesi için
        // belirsizlik satıra kadar taşınmalıdır.
        var vm = new ReportViewModel(new StubAggregator());
        var transcript = new[] { new TranscriptSegment("merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(10)) };
        var speakers = new[] { new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(3)) };

        vm.ApplySpeakers(transcript, speakers, minOverlapRatio: 0.5);

        Assert.Single(vm.SpeakerLines);
        Assert.Equal("Konuşmacı A", vm.SpeakerLines[0].Label);
        Assert.True(vm.SpeakerLines[0].Uncertain);
    }
```

(`StubAggregator` dosyada mevcut değilse, testlerdeki mevcut sahte agregatörü kullan.)

- [ ] **Step 2: Kırmızıyı doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Desktop.Tests --filter "FullyQualifiedName~ReportViewModelTests"`
Beklenen: Task 5 tamamlandıysa PASS; değilse önce Task 5'i bitir.

- [ ] **Step 3: XAML'i güncelle**

`ReportView.axaml` içinde konuşmacı satırı şablonuna, mevcut metin bloğunun yanına ekle:

```xml
                            <TextBlock Text="?" FontWeight="Bold" FontSize="12"
                                       IsVisible="{Binding Uncertain}"
                                       ToolTip.Tip="Bu satırın hangi konuşmacıya ait olduğu kesin değil."
                                       Foreground="{DynamicResource SystemAccentColor}"
                                       VerticalAlignment="Center" Margin="4,0,0,0" />
```

Satırın metnini soluklaştırmak için metin bloğuna ekle:

```xml
                                       Opacity="{Binding Uncertain, Converter={StaticResource UncertainOpacityConverter}}"
```

`src/SentimentAnalysis.Desktop.Avalonia/Converters/UncertainOpacityConverter.cs` oluştur:

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SentimentAnalysis.Desktop.Avalonia.Converters;

/// <summary>Belirsiz atamayı soluklaştırır (true → 0,6). Belirsizlik yalnızca rozetle değil
/// görsel ağırlıkla da anlatılır: kullanıcı rozeti kaçırsa bile satırın zayıf olduğunu görür.</summary>
public sealed class UncertainOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 0.6 : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

`ReportView.axaml` kaynaklarına kaydet (mevcut `StringNotEmptyConverter` kaydının yanına):

```xml
        <conv:UncertainOpacityConverter x:Key="UncertainOpacityConverter" />
```

`xmlns:conv` bildirimi yoksa kök öğeye ekle:

```xml
             xmlns:conv="using:SentimentAnalysis.Desktop.Avalonia.Converters"
```

- [ ] **Step 4: Konuşmacı duygu paneline analiz süresini ekle**

Konuşmacı duygu kartına ekle (`HasAudioMeasurement` Task 7'de tanımlandı):

```xml
                    <TextBlock FontSize="10" FontStyle="Italic"
                               Foreground="{DynamicResource SystemBaseMediumColor}"
                               Text="{Binding AnalyzedSeconds, StringFormat='Ses ölçümü {0:F0} sn konuşmaya dayanıyor'}"
                               IsVisible="{Binding HasAudioMeasurement}" />
```

- [ ] **Step 5: Uygulamayı çalıştır ve gözle doğrula**

```bash
./build-macos-app.sh && open "dist/Sesli Duygu Analizi.app"
```

Konuşmacı ayrımını Ayarlar'dan aç, iki kişilik kısa bir kayıt al, Durdur'a bas ve Rapor'da şunları doğrula: konuşmacılar A/B olarak ve konuşma sırasında; belirsiz satırlarda "?" rozeti; her konuşmacı kartında ses ölçümünün kaç saniyeye dayandığı.

- [ ] **Step 6: Commit**

```bash
dotnet test SentimentAnalysis.sln
git add src/SentimentAnalysis.Desktop.Avalonia/Views/ReportView.axaml src/SentimentAnalysis.Desktop.Avalonia/Converters/ tests/SentimentAnalysis.Desktop.Tests/ReportViewModelTests.cs
git commit -m "feat(ui): belirsiz konuşmacı ataması ve ses ölçüm süresi görünür"
```
