# Alt-Proje 08d Uygulama Planı — Yüzdelik Dağılım, Üçlü Görünüm, Mikrofon Spike

> **Agentic worker'lar için:** GEREKLİ ALT-BECERİ: Bu planı task-task uygulamak için
> `superpowers:subagent-driven-development` (önerilen) veya `superpowers:executing-plans`
> kullanın. Adımlar takip için checkbox (`- [ ]`) sözdizimi kullanır.

**Hedef:** Duygu çıktısını 8-duygu yüzde dağılımı olarak göstermek, metin/ses/nihai analizleri
canlı ekranda ayrı ayrı sunmak ve canlı mikrofon yakalamanın fizibilitesini gerçek donanımda
ölçmek.

**Mimari:** Yüzde dağılımı `Core`'da saf bir softmax fonksiyonudur (`EmotionWheel.Distribute`);
duygu merkezleri zaten orada olduğu için `Map` ile yapısal olarak çelişemez. Üçlü görünüm için
`FusedResult`, iki modelin tam VA'sını ve güvenini taşıyacak şekilde genişletilir ve SQLite
şeması geriye dönük uyumlu bir geçişle güncellenir. Sunum tarafında tek bir `SourceAnalysisVm`
üç sütunu da besler. Mikrofon dilimi bu planda **yalnızca ölçülür**; üretim kodu ölçüm
sonucuna göre ayrı bir planda yazılır.

**Teknoloji:** .NET 9 · Avalonia 11.3.1 · CommunityToolkit.Mvvm 8.4.2 · Microsoft.Data.Sqlite ·
xUnit 2.9.2 + Xunit.SkippableFact · macOS AudioToolbox (spike)

**Spec:** [`docs/superpowers/specs/subprojects/08d-yuzde-uclu-gorunum-mikrofon.md`](../specs/subprojects/08d-yuzde-uclu-gorunum-mikrofon.md)

## Global Kısıtlar

Her task'ın gereksinimleri örtük olarak bu bölümü içerir.

- **Test kapısı:** Her task `dotnet build && dotnet test` ile biter. Mevcut **193 test geçer,
  6 atlanır, 0 başarısız** — bu sayı düşmez. Task 3'te 3 mevcut test yeni alan adlarına
  uyarlanır (davranış iddiaları aynı kalır).
- **Sahte-yeşil yasak:** Gerçek donanım/model gerektiren testler `[SkippableFact]` +
  `Skip.If(...)` ile env-gated olur. Ölçülmemiş bir şey test tarafından "doğrulanmış" gibi
  gösterilmez.
- **Mimari yönü:** `Core` hiçbir projeye bağlı değildir. `Core` içine `AppConfig` sızdırılmaz —
  sıcaklık parametresi fonksiyona argüman olarak geçer.
- **`AppConfig` genişletmeleri additive:** yeni alanlar varsayılan değerle eklenir, mevcut
  çağıranlar bozulmaz.
- **Türkçe:** Kod içi doküman ve UI metni Türkçe; `Core` enum'ları dil-bağımsız kalır,
  yerelleştirme yalnızca `EmotionLocalizer`'da yapılır.
- **Dürüstlük dili:** Yüzde dağılımı için UI ve XML doc'ta **"yakınlık payı"** ifadesi kullanılır.
  "Olasılık", "ihtimal" veya "% doğruluk" iddiası yazılmaz — bu sayı kalibre edilmemiştir.
- **Sıcaklık varsayılanı:** `DistributionTemperature = 0.35` (kalibre edilmemiş başlangıç
  değeri; eval-09'un konusu).
- **Doğrulanamayan bağımlılık notu:** Bu plan yazılırken context7 erişilemez durumdaydı;
  Task 5'teki Avalonia XAML canlı dokümanla değil, 08'de çalıştığı doğrulanmış desenlerle
  yazılmıştır. XAML derleme ve görsel QA ile doğrulanır, varsayımla kabul edilmez.

## Dosya Yapısı

| Dosya | Sorumluluk | Task |
|---|---|---|
| `src/SentimentAnalysis.Core/EmotionShare.cs` | **Yeni.** Tek kayıt: duygu + yakınlık payı | 1 |
| `src/SentimentAnalysis.Core/EmotionWheel.cs` | **Değişir.** `Distribute` eklenir (merkezler zaten burada) | 1 |
| `src/SentimentAnalysis.Application/Configuration/AppConfig.cs` | **Değişir.** `DistributionTemperature` | 1 |
| `src/SentimentAnalysis.Desktop/PercentFormatter.cs` | **Yeni.** Toplamı tam 100 eden yuvarlama | 2 |
| `src/SentimentAnalysis.Core/Results.cs` | **Değişir.** `FusedResult` alanları | 3 |
| `src/SentimentAnalysis.Application/Fusion/EmotionFusion.cs` | **Değişir.** Yeni alanları doldurur | 3 |
| `src/SentimentAnalysis.Infrastructure.Storage/StorageSchema.cs` | **Değişir.** Şema + geçiş sütun listesi | 3 |
| `src/SentimentAnalysis.Infrastructure.Storage/SqliteSessionStore.cs` | **Değişir.** Geçiş + yaz/oku | 3 |
| `src/SentimentAnalysis.Desktop/SourceAnalysisVm.cs` | **Yeni.** Tek sütunun görünüm modeli | 4 |
| `src/SentimentAnalysis.Desktop/LiveViewModel.cs` | **Değişir.** Üç panel özelliği | 4 |
| `src/SentimentAnalysis.Desktop.Avalonia/Views/LiveView.axaml` | **Değişir.** Üç sütunlu düzen | 5 |
| `scratchpad/mic-spike/` | **Geçici.** Mikrofon ölçümü (repoya girmez) | 6 |

---

## Task 1: Yakınlık payı dağılımı (Core, saf)

**Dosyalar:**
- Oluştur: `src/SentimentAnalysis.Core/EmotionShare.cs`
- Değiştir: `src/SentimentAnalysis.Core/EmotionWheel.cs`
- Değiştir: `src/SentimentAnalysis.Application/Configuration/AppConfig.cs:4-21` (`FusionConfig`)
- Test: `tests/SentimentAnalysis.Core.Tests/EmotionDistributionTests.cs` (yeni)
- Test: `tests/SentimentAnalysis.Application.Tests/AppConfigTests.cs` (mevcut, ek test)

**Arayüzler:**
- Tüketir: `ValenceArousal`, `Emotion`, `EmotionWheel.Regions` (private), `ValenceArousal.DistanceTo`
- Üretir:
  - `public readonly record struct EmotionShare(Emotion Emotion, double Share)`
  - `public static IReadOnlyList<EmotionShare> EmotionWheel.Distribute(ValenceArousal va, double temperature)`
    — 8 öğe, paylar toplamı 1.0, azalan sırada, `[0].Emotion == Map(va)`
  - `FusionConfig.DistributionTemperature` (double, varsayılan `0.35`)

- [ ] **Adım 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Core.Tests/EmotionDistributionTests.cs` oluştur:

```csharp
using System;
using System.Linq;
using Xunit;

namespace SentimentAnalysis.Core.Tests;

public class EmotionDistributionTests
{
    private const double T = 0.35;   // FusionConfig.DistributionTemperature varsayılanı

    [Fact]
    public void Distribute_ReturnsAllEightEmotionsExactlyOnce()
    {
        var shares = EmotionWheel.Distribute(new ValenceArousal(0.2, 0.4), T);

        Assert.Equal(8, shares.Count);
        Assert.Equal(8, shares.Select(s => s.Emotion).Distinct().Count());
    }

    [Fact]
    public void Distribute_SharesSumToOne()
    {
        var shares = EmotionWheel.Distribute(new ValenceArousal(-0.3, 0.7), T);

        Assert.Equal(1.0, shares.Sum(s => s.Share), precision: 9);
    }

    [Fact]
    public void Distribute_AllSharesArePositive()
    {
        var shares = EmotionWheel.Distribute(new ValenceArousal(0.9, 0.95), T);

        Assert.All(shares, s => Assert.True(s.Share > 0.0, $"{s.Emotion} payı pozitif olmalı"));
    }

    [Fact]
    public void Distribute_IsSortedByShareDescending()
    {
        var shares = EmotionWheel.Distribute(new ValenceArousal(0.5, 0.2), T);

        for (int i = 1; i < shares.Count; i++)
            Assert.True(shares[i - 1].Share >= shares[i].Share, $"{i}. öğe sıralamayı bozuyor");
    }

    // En kritik test: Distribute ile Map asla çelişmez.
    [Theory]
    [InlineData(0.6, 0.9)]
    [InlineData(0.8, 0.5)]
    [InlineData(-0.8, 0.9)]
    [InlineData(0.0, 0.5)]
    [InlineData(0.13, 0.42)]
    [InlineData(-0.55, 0.66)]
    public void Distribute_TopShareAlwaysMatchesMap(double v, double a)
    {
        var va = new ValenceArousal(v, a);

        Assert.Equal(EmotionWheel.Map(va), EmotionWheel.Distribute(va, T)[0].Emotion);
    }

    [Fact]
    public void Distribute_LowerTemperature_SharpensDominantShare()
    {
        var va = new ValenceArousal(0.75, 0.5);

        double sharp = EmotionWheel.Distribute(va, 0.10)[0].Share;
        double flat  = EmotionWheel.Distribute(va, 1.00)[0].Share;

        Assert.True(sharp > flat, $"küçük T daha keskin olmalı (sharp={sharp}, flat={flat})");
    }

    [Fact]
    public void Distribute_VeryHighTemperature_ApproachesUniform()
    {
        var shares = EmotionWheel.Distribute(new ValenceArousal(0.5, 0.5), 1000.0);

        Assert.All(shares, s => Assert.Equal(0.125, s.Share, precision: 3));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    public void Distribute_NonPositiveTemperature_Throws(double t)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EmotionWheel.Distribute(new ValenceArousal(0.0, 0.5), t));
    }
}
```

- [ ] **Adım 2: Testi çalıştır, DERLENMEDİĞİNİ doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Core.Tests`
Beklenen: Derleme hatası — `'EmotionWheel' does not contain a definition for 'Distribute'`
ve `The type or namespace name 'EmotionShare' could not be found`.

- [ ] **Adım 3: `EmotionShare` kaydını yaz**

`src/SentimentAnalysis.Core/EmotionShare.cs` oluştur:

```csharp
namespace SentimentAnalysis.Core;

/// <summary>
/// Bir duyguya düşen <b>yakınlık payı</b> (0..1). Tüm payların toplamı 1.0'dır.
/// </summary>
/// <remarks>
/// DİKKAT: Bu bir <b>olasılık değildir</b>. Valence-Arousal uzayındaki geometrik yakınlıktan
/// türetilmiş, kalibre edilmemiş bir paydır. Sistemde 8 duygu üzerinde olasılık üreten bir
/// model yoktur. UI ve raporlarda "olasılık" veya "ihtimal" olarak sunulmamalıdır
/// (master §12: dürüstlük > kesinlik gösterisi). Kalibrasyon alt-proje 09'un konusudur.
/// </remarks>
public readonly record struct EmotionShare(Emotion Emotion, double Share);
```

- [ ] **Adım 4: `EmotionWheel.Distribute`'u yaz**

`src/SentimentAnalysis.Core/EmotionWheel.cs` içinde, `MapWithSecondary` metodundan sonra ekle:

```csharp
    /// <summary>
    /// VA noktasını 8 duyguya <b>yakınlık payı</b> olarak dağıtır (toplam 1.0, azalan sırada).
    /// Her merkeze Öklid uzaklığı <c>d</c> hesaplanır ve <c>softmax(−d² / T)</c> uygulanır.
    /// </summary>
    /// <param name="temperature">
    /// Keskinlik: küçük değer tek duyguyu domine ettirir, büyük değer dağılımı düzleştirir.
    /// Pozitif olmalıdır. Varsayılan <c>FusionConfig.DistributionTemperature</c>'dan gelir ve
    /// <b>kalibre edilmemiştir</b> (eval-09).
    /// </param>
    /// <remarks>
    /// Dönen ilk öğe her zaman <see cref="Map"/> ile aynı duygudur: pay, uzaklığın monoton
    /// azalan bir fonksiyonudur ve sıralama <see cref="Map"/> ile aynı kararlı (stable) LINQ
    /// sıralamasını kullanır — eşitlik durumunda bildirim sırası korunur.
    /// Sonuç bir olasılık değil, geometrik yakınlık payıdır (bkz. <see cref="EmotionShare"/>).
    /// </remarks>
    public static IReadOnlyList<EmotionShare> Distribute(ValenceArousal va, double temperature)
    {
        if (temperature <= 0.0)
            throw new ArgumentOutOfRangeException(
                nameof(temperature), temperature, "Dağılım sıcaklığı pozitif olmalıdır.");

        var logits = Regions
            .Select(r =>
            {
                double d = va.DistanceTo(r.Center);
                return -(d * d) / temperature;
            })
            .ToArray();

        // Taşma-güvenli softmax: üsleri almadan önce en büyük terim çıkarılır.
        double max = logits.Max();
        var weights = logits.Select(l => Math.Exp(l - max)).ToArray();
        double sum = weights.Sum();

        return Regions
            .Select((r, i) => new EmotionShare(r.Emotion, weights[i] / sum))
            .OrderByDescending(s => s.Share)
            .ToArray();
    }
```

- [ ] **Adım 5: Testleri çalıştır, geçtiklerini doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Core.Tests`
Beklenen: PASS — 8 yeni test dahil hepsi geçer.

- [ ] **Adım 6: `DistributionTemperature`'ı config'e ekle**

`src/SentimentAnalysis.Application/Configuration/AppConfig.cs` içinde `FusionConfig`'in
**son parametresi olarak** ekle (mevcut parametreler ve sırası değişmez — additive kural):

```csharp
    int PeakMomentCount = 3,                   // toplantı özetinde tepe an sayısı
    double MinReliabilityForHighConfidence = 0.5, // rAudio*rText bunun altındaysa "Yuksek" güven verilmez
    double DistributionTemperature = 0.35);    // 8-duygu yakınlık payı keskinliği — KALİBRE EDİLMEMİŞ (eval-09)
```

- [ ] **Adım 7: Config testini ekle**

`tests/SentimentAnalysis.Application.Tests/AppConfigTests.cs` içine ekle:

```csharp
    [Fact]
    public void Default_DistributionTemperature_IsPositive()
    {
        Assert.True(AppConfig.Default.Fusion.DistributionTemperature > 0.0);
    }
```

- [ ] **Adım 8: Tüm testleri çalıştır**

Çalıştır: `dotnet build && dotnet test`
Beklenen: 0 başarısız. Test sayısı 193 → 202 (8 dağılım + 1 config).

- [ ] **Adım 9: Commit**

```bash
git add src/SentimentAnalysis.Core/EmotionShare.cs \
        src/SentimentAnalysis.Core/EmotionWheel.cs \
        src/SentimentAnalysis.Application/Configuration/AppConfig.cs \
        tests/SentimentAnalysis.Core.Tests/EmotionDistributionTests.cs \
        tests/SentimentAnalysis.Application.Tests/AppConfigTests.cs
git commit -m "feat(core): 8-duygu yakınlık payı dağılımı (EmotionWheel.Distribute)"
```

---

## Task 2: Yüzde biçimlendirici (Desktop, saf)

**Dosyalar:**
- Oluştur: `src/SentimentAnalysis.Desktop/PercentFormatter.cs`
- Test: `tests/SentimentAnalysis.Desktop.Tests/PercentFormatterTests.cs` (yeni)

**Arayüzler:**
- Tüketir: yok (saf matematik)
- Üretir: `public static IReadOnlyList<int> PercentFormatter.ToPercents(IReadOnlyList<double> shares)`
  — girdi payları toplamı 1.0 iken çıktı tam sayıların toplamı **her zaman tam 100**

- [ ] **Adım 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Desktop.Tests/PercentFormatterTests.cs` oluştur:

```csharp
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class PercentFormatterTests
{
    [Fact]
    public void ToPercents_ExactValues_MapDirectly()
    {
        var result = PercentFormatter.ToPercents(new[] { 0.5, 0.3, 0.2 });

        Assert.Equal(new[] { 50, 30, 20 }, result);
    }

    [Fact]
    public void ToPercents_ThirdsSumToExactlyHundred()
    {
        var result = PercentFormatter.ToPercents(new[] { 1.0 / 3, 1.0 / 3, 1.0 / 3 });

        Assert.Equal(100, result.Sum());
    }

    // Naif yuvarlamanın bozulduğu asıl senaryo: 8 eşit pay (%12.5 → taban %12, toplam 96).
    [Fact]
    public void ToPercents_EightEqualShares_SumToExactlyHundred()
    {
        var shares = Enumerable.Repeat(0.125, 8).ToArray();

        var result = PercentFormatter.ToPercents(shares);

        Assert.Equal(100, result.Sum());
        Assert.Equal(8, result.Count);
    }

    [Fact]
    public void ToPercents_LargestRemainder_GoesToBiggestFraction()
    {
        // 0.455 → 45.5 (kalan .5), 0.445 → 44.5 (kalan .5), 0.10 → 10.0 (kalan 0)
        // Tabanlar: 45 + 44 + 10 = 99. Artan 1 puan en büyük kalana (ilk .5) gider.
        var result = PercentFormatter.ToPercents(new[] { 0.455, 0.445, 0.10 });

        Assert.Equal(100, result.Sum());
        Assert.Equal(46, result[0]);
        Assert.Equal(44, result[1]);
        Assert.Equal(10, result[2]);
    }

    [Fact]
    public void ToPercents_SingleShare_IsHundred()
    {
        Assert.Equal(new[] { 100 }, PercentFormatter.ToPercents(new[] { 1.0 }));
    }

    [Fact]
    public void ToPercents_Empty_ReturnsEmpty()
    {
        Assert.Empty(PercentFormatter.ToPercents(new List<double>()));
    }

    [Fact]
    public void ToPercents_NeverReturnsNegative()
    {
        var result = PercentFormatter.ToPercents(new[] { 0.999, 0.001 });

        Assert.All(result, p => Assert.True(p >= 0));
    }
}
```

- [ ] **Adım 2: Testi çalıştır, DERLENMEDİĞİNİ doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Desktop.Tests`
Beklenen: Derleme hatası — `The name 'PercentFormatter' does not exist`.

- [ ] **Adım 3: Uygulamayı yaz**

`src/SentimentAnalysis.Desktop/PercentFormatter.cs` oluştur:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace SentimentAnalysis.Desktop;

/// <summary>
/// Payları (toplamı 1.0) toplamı <b>tam 100</b> olan tam sayı yüzdelere çevirir.
/// </summary>
/// <remarks>
/// En-büyük-kalan (largest remainder) yöntemi: her pay 100 ile çarpılıp tabanı alınır, 100'e
/// kadar eksik kalan puanlar en büyük ondalık kalana sahip öğelere birer birer dağıtılır.
/// Naif yuvarlama toplamı 99 veya 101 yapabilir; 8 eşit payda (%12.5) toplam 96'ya düşer.
/// Saf ve deterministik.
/// </remarks>
public static class PercentFormatter
{
    public static IReadOnlyList<int> ToPercents(IReadOnlyList<double> shares)
    {
        if (shares.Count == 0) return Array.Empty<int>();

        var scaled = shares.Select(s => s * 100.0).ToArray();
        var percents = scaled.Select(x => (int)Math.Floor(x)).ToArray();

        int remainder = 100 - percents.Sum();
        if (remainder <= 0) return percents;

        // Kalanı en büyük olan öğelerden başlayarak birer puan dağıt.
        var byRemainder = Enumerable.Range(0, shares.Count)
            .OrderByDescending(i => scaled[i] - percents[i])
            .ToArray();

        for (int k = 0; k < remainder; k++)
            percents[byRemainder[k % byRemainder.Length]]++;

        return percents;
    }
}
```

- [ ] **Adım 4: Testleri çalıştır, geçtiklerini doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Desktop.Tests`
Beklenen: PASS — 7 yeni test geçer.

- [ ] **Adım 5: Tüm testleri çalıştır**

Çalıştır: `dotnet build && dotnet test`
Beklenen: 0 başarısız, toplam 209 test.

- [ ] **Adım 6: Commit**

```bash
git add src/SentimentAnalysis.Desktop/PercentFormatter.cs \
        tests/SentimentAnalysis.Desktop.Tests/PercentFormatterTests.cs
git commit -m "feat(desktop): toplamı tam 100 eden yüzde biçimlendirici"
```

---

## Task 3: `FusedResult` genişletme + SQLite şema geçişi

> **Neden tek task:** Kayıt değişikliği ile şema geçişi birbirine bağlıdır. Yalnızca kaydı
> değiştirip şemayı bırakırsak, okuma tarafı var olmayan arousal/güven değerlerini
> **uydurmak** zorunda kalır — bu, projenin dürüstlük ilkesini çiğner. İkisi birlikte gider.

**Dosyalar:**
- Değiştir: `src/SentimentAnalysis.Core/Results.cs:6-25` (`FusedResult`)
- Değiştir: `src/SentimentAnalysis.Application/Fusion/EmotionFusion.cs:57-68`
- Değiştir: `src/SentimentAnalysis.Infrastructure.Storage/StorageSchema.cs`
- Değiştir: `src/SentimentAnalysis.Infrastructure.Storage/SqliteSessionStore.cs:29-34, 51-76, 172-197`
- Değiştir: `tests/SentimentAnalysis.Core.Tests/DomainRecordsTests.cs:22-23, 58`
- Değiştir: `tests/SentimentAnalysis.Application.Tests/EmotionFusionTests.cs:49-50`
- Değiştir: `tests/SentimentAnalysis.Desktop.Tests/LiveViewModelTests.cs:13-18` (`Chunk` yardımcısı)
- Test: `tests/SentimentAnalysis.Infrastructure.Storage.Tests/SchemaMigrationTests.cs` (yeni)

**Arayüzler:**
- Tüketir: `ValenceArousal`, `TextEmotion.Va`, `TextEmotion.ModelConfidence`,
  `ProsodyEmotion.Va`, `ProsodyEmotion.ModelConfidence`
- Üretir: `FusedResult` yeni alanları — `ValenceArousal TextVa`, `ValenceArousal ProsodyVa`,
  `double TextConfidence`, `double ProsodyConfidence`. `Flagged` türetilmiş özelliği ve diğer
  tüm alanlar korunur. `StorageSchema.ChunkColumnsAddedIn08d` geçiş listesi.

- [ ] **Adım 1: Şema geçişi testini yaz**

`tests/SentimentAnalysis.Infrastructure.Storage.Tests/SchemaMigrationTests.cs` oluştur:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Storage;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Storage.Tests;

public class SchemaMigrationTests : IDisposable
{
    private readonly string _dir;

    public SchemaMigrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sa-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>08d ÖNCESİ şema — dört yeni sütun yok.</summary>
    private const string LegacyCreateSql = """
        CREATE TABLE IF NOT EXISTS sessions (
            id TEXT PRIMARY KEY,
            started_at INTEGER NOT NULL,
            language INTEGER NOT NULL,
            audio_path TEXT,
            audio_saved_at INTEGER
        );
        CREATE TABLE IF NOT EXISTS chunks (
            session_id TEXT NOT NULL,
            seq INTEGER NOT NULL,
            time_start_ms INTEGER NOT NULL,
            time_end_ms INTEGER NOT NULL,
            transcript TEXT NOT NULL,
            combined_valence REAL NOT NULL,
            combined_arousal REAL NOT NULL,
            primary_emotion INTEGER NOT NULL,
            secondary_emotion INTEGER NOT NULL,
            confidence INTEGER NOT NULL,
            agreement INTEGER NOT NULL,
            incongruent INTEGER NOT NULL,
            pattern INTEGER NOT NULL,
            congruence REAL NOT NULL,
            text_valence REAL NOT NULL,
            audio_valence REAL NOT NULL,
            PRIMARY KEY (session_id, seq)
        );
        """;

    private AppConfig Config() => AppConfig.Default with
    {
        Storage = AppConfig.Default.Storage with { StorageDirectory = _dir },
    };

    private SqliteSessionStore NewStore()
    {
        var cfg = Config();
        var keys = new FileKeyProvider(cfg);
        return new SqliteSessionStore(cfg, new EncryptedAudioStore(cfg, new AudioEncryptor(), keys));
    }

    private void SeedLegacyDatabase(Guid sessionId)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = Path.Combine(_dir, "sessions.db") }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = LegacyCreateSql;
            cmd.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO sessions (id, started_at, language) VALUES ($id, 0, 0);
                INSERT INTO chunks (session_id, seq, time_start_ms, time_end_ms, transcript,
                    combined_valence, combined_arousal, primary_emotion, secondary_emotion,
                    confidence, agreement, incongruent, pattern, congruence, text_valence, audio_valence)
                VALUES ($id, 0, 0, 1000, 'eski kayıt', 0.4, 0.6, 1, 7, 1, 1, 0, 0, 0.9, 0.5, 0.3);
                """;
            cmd.Parameters.AddWithValue("$id", sessionId.ToString("N"));
            cmd.ExecuteNonQuery();
        }
    }

    private List<string> ChunkColumns()
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = Path.Combine(_dir, "sessions.db") }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(chunks)";
        using var rd = cmd.ExecuteReader();
        var names = new List<string>();
        while (rd.Read()) names.Add(rd.GetString(1));
        return names;
    }

    [Fact]
    public void Ctor_OnLegacyDatabase_AddsMissingColumns()
    {
        SeedLegacyDatabase(Guid.NewGuid());

        _ = NewStore();   // yapıcı geçişi çalıştırır

        var columns = ChunkColumns();
        Assert.Contains("text_arousal", columns);
        Assert.Contains("audio_arousal", columns);
        Assert.Contains("text_confidence", columns);
        Assert.Contains("audio_confidence", columns);
    }

    [Fact]
    public async Task Ctor_OnLegacyDatabase_OldRowsRemainReadable()
    {
        var id = Guid.NewGuid();
        SeedLegacyDatabase(id);

        var store = NewStore();
        var session = await store.GetSessionAsync(id, CancellationToken.None);

        Assert.NotNull(session);
        var chunk = Assert.Single(session!.Chunks);
        Assert.Equal("eski kayıt", chunk.Transcript);
        Assert.Equal(0.5, chunk.Fused.TextVa.Valence, precision: 6);   // eski sütun korunur
        Assert.Equal(0.0, chunk.Fused.TextVa.Arousal, precision: 6);   // yeni sütun varsayılanı: ölçülmedi
    }

    [Fact]
    public void Ctor_RunTwice_IsIdempotent()
    {
        SeedLegacyDatabase(Guid.NewGuid());

        _ = NewStore();
        _ = NewStore();   // ikinci kez: "duplicate column name" hatası VERMEMELİ

        Assert.Contains("text_arousal", ChunkColumns());
    }

    [Fact]
    public async Task NewDatabase_RoundTripsAllFourNewFields()
    {
        var store = NewStore();
        var id = Guid.NewGuid();
        var fused = new FusedResult(
            new ValenceArousal(0.1, 0.7), Emotion.Mutlu, Emotion.Sakin, Confidence.Orta,
            true, false, IncongruencePattern.Yok, 0.85,
            TextVa: new ValenceArousal(0.62, 0.41),
            ProsodyVa: new ValenceArousal(-0.33, 0.88),
            TextConfidence: 0.77,
            ProsodyConfidence: 0.44);

        await store.SaveChunkAsync(id, new ChunkResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), fused, "merhaba"),
            CancellationToken.None);
        var session = await store.GetSessionAsync(id, CancellationToken.None);

        var read = Assert.Single(session!.Chunks).Fused;
        Assert.Equal(0.62, read.TextVa.Valence, precision: 6);
        Assert.Equal(0.41, read.TextVa.Arousal, precision: 6);
        Assert.Equal(-0.33, read.ProsodyVa.Valence, precision: 6);
        Assert.Equal(0.88, read.ProsodyVa.Arousal, precision: 6);
        Assert.Equal(0.77, read.TextConfidence, precision: 6);
        Assert.Equal(0.44, read.ProsodyConfidence, precision: 6);
    }
}
```

> **Not:** `FileKeyProvider` / `EncryptedAudioStore` / `AudioEncryptor` yapıcı imzalarını
> mevcut `tests/SentimentAnalysis.Infrastructure.Storage.Tests/` içindeki testlerden **birebir
> kopyalayın**; yukarıdaki çağrılar oradaki desenle uyumlu olmalıdır. Farklıysa mevcut testlerdeki
> kurulum yardımcısını kullanın, yeni bir imza uydurmayın.

- [ ] **Adım 2: Testi çalıştır, DERLENMEDİĞİNİ doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Infrastructure.Storage.Tests`
Beklenen: Derleme hatası — `FusedResult` yapıcısında `TextVa` adlı parametre yok.

- [ ] **Adım 3: `FusedResult`'ı genişlet**

`src/SentimentAnalysis.Core/Results.cs` içinde son iki parametreyi değiştir:

```csharp
/// <summary>Füzyon sonucu (chunk düzeyi). Her iki modelin ham VA'sı ve güveni saydamlık için
/// taşınır — UI bunları "Metin Analizi" ve "Ses Analizi" sütunlarında ayrı ayrı gösterir.</summary>
public sealed record FusedResult(
    ValenceArousal Combined,
    Emotion PrimaryEmotion,
    Emotion SecondaryEmotion,
    Confidence Confidence,
    bool Agreement,
    bool Incongruent,
    IncongruencePattern Pattern,
    double Congruence,
    ValenceArousal TextVa,
    ValenceArousal ProsodyVa,
    double TextConfidence,
    double ProsodyConfidence)
{
```

`Flagged` özelliği ve XML doc'u **aynen kalır**.

- [ ] **Adım 4: `EmotionFusion`'ı güncelle**

`src/SentimentAnalysis.Application/Fusion/EmotionFusion.cs` içinde `return new FusedResult(...)`
çağrısının son iki satırını değiştir:

```csharp
            Congruence: congruence,
            TextVa: text.Va,
            ProsodyVa: prosody.Va,
            TextConfidence: text.ModelConfidence,
            ProsodyConfidence: prosody.ModelConfidence);
```

- [ ] **Adım 5: Şemayı ve geçiş listesini yaz**

`src/SentimentAnalysis.Infrastructure.Storage/StorageSchema.cs` — `chunks` tablosundaki
`audio_valence REAL NOT NULL,` satırından sonra dört sütun ekle ve geçiş listesini tanımla:

```csharp
            text_valence REAL NOT NULL,
            audio_valence REAL NOT NULL,
            text_arousal REAL NOT NULL DEFAULT 0,
            audio_arousal REAL NOT NULL DEFAULT 0,
            text_confidence REAL NOT NULL DEFAULT 0,
            audio_confidence REAL NOT NULL DEFAULT 0,
            PRIMARY KEY (session_id, seq)
        );
        """;

    /// <summary>08d ile eklenen `chunks` sütunları. Mevcut veritabanlarına
    /// <c>ALTER TABLE</c> ile eklenir (bkz. <c>SqliteSessionStore</c> geçişi).
    /// Varsayılan 0 = "bu satır 08d öncesinden, değer ölçülmedi" — uydurma değer üretilmez.</summary>
    public static readonly (string Name, string Definition)[] ChunkColumnsAddedIn08d =
    [
        ("text_arousal",     "REAL NOT NULL DEFAULT 0"),
        ("audio_arousal",    "REAL NOT NULL DEFAULT 0"),
        ("text_confidence",  "REAL NOT NULL DEFAULT 0"),
        ("audio_confidence", "REAL NOT NULL DEFAULT 0"),
    ];
```

- [ ] **Adım 6: Geçişi ve yaz/oku yollarını güncelle**

`src/SentimentAnalysis.Infrastructure.Storage/SqliteSessionStore.cs`:

**(a)** Yapıcıda şema oluşturmadan sonra geçişi çağır (satır 33'ten sonra):

```csharp
        cmd.CommandText = StorageSchema.CreateSql;
        cmd.ExecuteNonQuery();

        Migrate(conn);
```

**(b)** Sınıfa geçiş metodunu ekle:

```csharp
    /// <summary>08d öncesi bir veritabanına eksik sütunları ekler. `CREATE TABLE IF NOT EXISTS`
    /// mevcut tabloya sütun eklemez; bu adım olmadan eski kurulumlar sessizce bozulur.
    /// İdempotenttir: eksik olmayan sütun atlanır.</summary>
    private static void Migrate(SqliteConnection conn)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var q = conn.CreateCommand())
        {
            q.CommandText = "PRAGMA table_info(chunks)";
            using var rd = q.ExecuteReader();
            while (rd.Read()) existing.Add(rd.GetString(1));   // sütun 1 = ad
        }

        foreach (var (name, definition) in StorageSchema.ChunkColumnsAddedIn08d)
        {
            if (existing.Contains(name)) continue;
            using var alter = conn.CreateCommand();
            // Sütun adı/tanımı sabit literal listeden gelir (kullanıcı girdisi değil) —
            // SQLite ALTER TABLE sütun adını parametre olarak kabul etmez.
            alter.CommandText = $"ALTER TABLE chunks ADD COLUMN {name} {definition}";
            alter.ExecuteNonQuery();
        }
    }
```

**(c)** `SaveChunkAsync` INSERT'ünü genişlet — sütun listesi, VALUES listesi ve parametreler:

```csharp
        cmd.CommandText = """
            INSERT INTO chunks (session_id, seq, time_start_ms, time_end_ms, transcript,
                combined_valence, combined_arousal, primary_emotion, secondary_emotion, confidence,
                agreement, incongruent, pattern, congruence, text_valence, audio_valence,
                text_arousal, audio_arousal, text_confidence, audio_confidence)
            VALUES ($sid,
                (SELECT COALESCE(MAX(seq), -1) + 1 FROM chunks WHERE session_id = $sid),
                $ts, $te, $tr, $cv, $ca, $pe, $se, $cf, $ag, $inc, $pat, $cong, $tv, $av,
                $ta, $aa, $tc, $ac)
            """;
```

Parametre bloğunda son iki satırı değiştirip dördünü ekle:

```csharp
        cmd.Parameters.AddWithValue("$tv", f.TextVa.Valence);
        cmd.Parameters.AddWithValue("$av", f.ProsodyVa.Valence);
        cmd.Parameters.AddWithValue("$ta", f.TextVa.Arousal);
        cmd.Parameters.AddWithValue("$aa", f.ProsodyVa.Arousal);
        cmd.Parameters.AddWithValue("$tc", f.TextConfidence);
        cmd.Parameters.AddWithValue("$ac", f.ProsodyConfidence);
```

**(d)** `ReadChunksAsync` SELECT'ini ve kayıt kurulumunu güncelle:

```csharp
        cmd.CommandText = """
            SELECT time_start_ms, time_end_ms, transcript, combined_valence, combined_arousal,
                primary_emotion, secondary_emotion, confidence, agreement, incongruent, pattern,
                congruence, text_valence, audio_valence,
                text_arousal, audio_arousal, text_confidence, audio_confidence
            FROM chunks WHERE session_id = $sid ORDER BY seq
            """;
```

```csharp
            var fused = new FusedResult(
                new ValenceArousal(rd.GetDouble(3), rd.GetDouble(4)),
                (Emotion)rd.GetInt32(5), (Emotion)rd.GetInt32(6), (Confidence)rd.GetInt32(7),
                rd.GetInt32(8) != 0, rd.GetInt32(9) != 0, (IncongruencePattern)rd.GetInt32(10),
                rd.GetDouble(11),
                TextVa: new ValenceArousal(rd.GetDouble(12), rd.GetDouble(14)),
                ProsodyVa: new ValenceArousal(rd.GetDouble(13), rd.GetDouble(15)),
                TextConfidence: rd.GetDouble(16),
                ProsodyConfidence: rd.GetDouble(17));
```

- [ ] **Adım 7: Mevcut üç testi yeni alan adlarına uyarla**

`tests/SentimentAnalysis.Core.Tests/DomainRecordsTests.cs` — satır 22-23:

```csharp
            TextVa: new ValenceArousal(0.6, 0.5),
            ProsodyVa: new ValenceArousal(-0.6, 0.7),
            TextConfidence: 0.8,
            ProsodyConfidence: 0.6);
```

Aynı dosyada satır 58'deki kurulumu da düzelt:

```csharp
            IncongruencePattern.OlasiIroni, Congruence: 0.7,
            TextVa: new ValenceArousal(0.6, 0.5), ProsodyVa: new ValenceArousal(0.0, 0.5),
            TextConfidence: 0.8, ProsodyConfidence: 0.6);
```

`tests/SentimentAnalysis.Application.Tests/EmotionFusionTests.cs` — satır 49-50:

```csharp
        Assert.Equal(0.6, r.TextVa.Valence, precision: 10);
        Assert.Equal(-0.6, r.ProsodyVa.Valence, precision: 10);
```

`tests/SentimentAnalysis.Desktop.Tests/LiveViewModelTests.cs` — `Chunk` yardımcısı:

```csharp
    private static ChunkResult Chunk(double t, Emotion e, double v, bool flagged = false) =>
        new(TimeSpan.FromSeconds(t), TimeSpan.FromSeconds(t + 1),
            new FusedResult(new ValenceArousal(v, 0.6), e, Emotion.Notr, Confidence.Orta,
                !flagged, flagged, flagged ? IncongruencePattern.Maskeleme : IncongruencePattern.Yok,
                flagged ? 0.2 : 0.9,
                TextVa: new ValenceArousal(v, 0.6), ProsodyVa: new ValenceArousal(v, 0.6),
                TextConfidence: 0.7, ProsodyConfidence: 0.7),
            $"metin-{t}");
```

> Derleme başka yerlerde de `FusedResult` kurulumu bulursa, hepsini aynı desenle uyarla —
> **davranış iddialarını değiştirme**, yalnızca yapıcı çağrısını.

- [ ] **Adım 8: Tüm testleri çalıştır**

Çalıştır: `dotnet build && dotnet test`
Beklenen: 0 başarısız. Test sayısı 209 → 213 (4 yeni geçiş testi).

- [ ] **Adım 9: Commit**

```bash
git add src/SentimentAnalysis.Core/Results.cs \
        src/SentimentAnalysis.Application/Fusion/EmotionFusion.cs \
        src/SentimentAnalysis.Infrastructure.Storage/ \
        tests/
git commit -m "feat: FusedResult metin/ses VA ve güvenini taşısın + SQLite şema geçişi"
```

---

## Task 4: Üç kaynak görünüm modeli (Desktop, headless TDD)

**Dosyalar:**
- Oluştur: `src/SentimentAnalysis.Desktop/SourceAnalysisVm.cs`
- Değiştir: `src/SentimentAnalysis.Desktop/LiveViewModel.cs`
- Test: `tests/SentimentAnalysis.Desktop.Tests/SourceAnalysisVmTests.cs` (yeni)
- Test: `tests/SentimentAnalysis.Desktop.Tests/LiveViewModelTests.cs` (mevcut, ek testler)

**Arayüzler:**
- Tüketir: `EmotionWheel.Distribute`, `PercentFormatter.ToPercents`, `EmotionLocalizer.Emotion`,
  `EmotionLocalizer.Confidence`, `FusedResult.TextVa/ProsodyVa/Combined/Confidence`,
  `AppConfig.Default.Fusion.DistributionTemperature`
- Üretir:
  - `public sealed record EmotionShareVm(string Emotion, int Percent)`
  - `public sealed class SourceAnalysisVm` — `Title`, `Emotion`, `Valence`, `Arousal`,
    `Shares`, `HasData`; yapıcı `(string title, ValenceArousal va, double temperature)`;
    fabrika `SourceAnalysisVm.Empty(string title)`
  - `LiveViewModel.TextAnalysis`, `.AudioAnalysis`, `.FinalAnalysis` (`SourceAnalysisVm`),
    `LiveViewModel.CurrentConfidence` (`string`)
  - `LiveViewModel` yapıcısına opsiyonel `AppConfig? config = null` parametresi

- [ ] **Adım 1: `SourceAnalysisVm` testini yaz**

`tests/SentimentAnalysis.Desktop.Tests/SourceAnalysisVmTests.cs` oluştur:

```csharp
using System.Linq;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class SourceAnalysisVmTests
{
    private const double T = 0.35;

    [Fact]
    public void Ctor_ExposesTitleAndAxes()
    {
        var vm = new SourceAnalysisVm("Metin Analizi", new ValenceArousal(0.62, 0.41), T);

        Assert.Equal("Metin Analizi", vm.Title);
        Assert.Equal(0.62, vm.Valence, precision: 6);
        Assert.Equal(0.41, vm.Arousal, precision: 6);
        Assert.True(vm.HasData);
    }

    [Fact]
    public void Ctor_DominantEmotionIsLocalizedAndMatchesWheel()
    {
        var va = new ValenceArousal(-0.8, 0.9);

        var vm = new SourceAnalysisVm("Ses Analizi", va, T);

        Assert.Equal("Kızgın", vm.Emotion);
        Assert.Equal("Kızgın", vm.Shares[0].Emotion);
    }

    [Fact]
    public void Ctor_ProducesEightSharesSummingToHundred()
    {
        var vm = new SourceAnalysisVm("Nihai Karar", new ValenceArousal(0.1, 0.5), T);

        Assert.Equal(8, vm.Shares.Count);
        Assert.Equal(100, vm.Shares.Sum(s => s.Percent));
    }

    [Fact]
    public void Ctor_SharesAreSortedDescending()
    {
        var vm = new SourceAnalysisVm("Nihai Karar", new ValenceArousal(0.4, 0.7), T);

        for (int i = 1; i < vm.Shares.Count; i++)
            Assert.True(vm.Shares[i - 1].Percent >= vm.Shares[i].Percent);
    }

    [Fact]
    public void Empty_HasNoDataAndNoShares()
    {
        var vm = SourceAnalysisVm.Empty("Metin Analizi");

        Assert.Equal("Metin Analizi", vm.Title);
        Assert.False(vm.HasData);
        Assert.Empty(vm.Shares);
        Assert.Equal("—", vm.Emotion);
    }
}
```

- [ ] **Adım 2: Testi çalıştır, DERLENMEDİĞİNİ doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Desktop.Tests`
Beklenen: Derleme hatası — `The name 'SourceAnalysisVm' does not exist`.

- [ ] **Adım 3: `SourceAnalysisVm`'i yaz**

`src/SentimentAnalysis.Desktop/SourceAnalysisVm.cs` oluştur:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Bir duygunun lokalize adı ve yakınlık payı yüzdesi (toplamları tam 100).</summary>
public sealed record EmotionShareVm(string Emotion, int Percent);

/// <summary>
/// Tek bir analiz kaynağının (Metin / Ses / Nihai) görünüm modeli: baskın duygu, V/A ekseni ve
/// 8 duygunun yakınlık payı dağılımı. Üç sütun da bu tipi kullanır — salt-okunur, saf.
/// </summary>
/// <remarks>
/// Yüzdeler <b>olasılık değildir</b>; VA uzayındaki geometrik yakınlıktan türetilmiştir
/// (bkz. <see cref="EmotionShare"/>). UI bu ayrımı açıkça belirtmelidir.
/// </remarks>
public sealed class SourceAnalysisVm
{
    public string Title { get; }
    public string Emotion { get; }
    public double Valence { get; }
    public double Arousal { get; }
    public IReadOnlyList<EmotionShareVm> Shares { get; }

    /// <summary>Henüz ölçüm gelmediyse false — UI boş durumu dürüstçe gösterir.</summary>
    public bool HasData { get; }

    public SourceAnalysisVm(string title, ValenceArousal va, double temperature)
    {
        Title = title;
        Valence = va.Valence;
        Arousal = va.Arousal;
        HasData = true;

        var shares = EmotionWheel.Distribute(va, temperature);
        Emotion = EmotionLocalizer.Emotion(shares[0].Emotion);

        var percents = PercentFormatter.ToPercents(shares.Select(s => s.Share).ToArray());
        Shares = shares
            .Select((s, i) => new EmotionShareVm(EmotionLocalizer.Emotion(s.Emotion), percents[i]))
            .ToArray();
    }

    private SourceAnalysisVm(string title)
    {
        Title = title;
        Emotion = "—";
        Shares = Array.Empty<EmotionShareVm>();
        HasData = false;
    }

    /// <summary>Analiz başlamadan önceki boş durum.</summary>
    public static SourceAnalysisVm Empty(string title) => new(title);
}
```

- [ ] **Adım 4: Testleri çalıştır, geçtiklerini doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Desktop.Tests`
Beklenen: PASS — 5 yeni test geçer.

- [ ] **Adım 5: `LiveViewModel` testlerini yaz**

`tests/SentimentAnalysis.Desktop.Tests/LiveViewModelTests.cs` içine ekle (mevcut `FakeRunner`
ve `Chunk` yardımcıları kullanılır):

```csharp
    /// <summary>Metin ve ses birbirinden AYRI VA taşıyan chunk — panellerin karışmadığını gösterir.</summary>
    private static ChunkResult SplitChunk(ValenceArousal text, ValenceArousal prosody, ValenceArousal combined) =>
        new(TimeSpan.Zero, TimeSpan.FromSeconds(1),
            new FusedResult(combined, EmotionWheel.Map(combined), Emotion.Notr, Confidence.Dusuk,
                false, true, IncongruencePattern.Maskeleme, 0.1,
                TextVa: text, ProsodyVa: prosody, TextConfidence: 0.8, ProsodyConfidence: 0.6),
            "harika, çok memnunum");

    [Fact]
    public void Panels_BeforeAnyChunk_AreEmpty()
    {
        var vm = new LiveViewModel(new FakeRunner());

        Assert.False(vm.TextAnalysis.HasData);
        Assert.False(vm.AudioAnalysis.HasData);
        Assert.False(vm.FinalAnalysis.HasData);
        Assert.Equal("Metin Analizi", vm.TextAnalysis.Title);
        Assert.Equal("Ses Analizi", vm.AudioAnalysis.Title);
        Assert.Equal("Nihai Karar", vm.FinalAnalysis.Title);
    }

    [Fact]
    public void Report_FeedsEachPanelFromItsOwnSource()
    {
        var vm = new LiveViewModel(new FakeRunner());

        vm.Report(SplitChunk(
            text:     new ValenceArousal(0.80, 0.50),   // "Mutlu"
            prosody:  new ValenceArousal(-0.50, 0.80),  // "Gergin"
            combined: new ValenceArousal(0.00, 0.50))); // "Nötr"

        Assert.Equal("Mutlu", vm.TextAnalysis.Emotion);
        Assert.Equal("Gergin", vm.AudioAnalysis.Emotion);
        Assert.Equal("Nötr", vm.FinalAnalysis.Emotion);

        // Eksenler de kendi kaynağından gelir — karışmıyor.
        Assert.Equal(0.80, vm.TextAnalysis.Valence, precision: 6);
        Assert.Equal(-0.50, vm.AudioAnalysis.Valence, precision: 6);
    }

    [Fact]
    public void Report_PanelsExposeFullDistribution()
    {
        var vm = new LiveViewModel(new FakeRunner());

        vm.Report(Chunk(0, Emotion.Mutlu, 0.5));

        Assert.Equal(8, vm.FinalAnalysis.Shares.Count);
        Assert.Equal(100, vm.FinalAnalysis.Shares.Sum(s => s.Percent));
        Assert.True(vm.FinalAnalysis.HasData);
    }

    [Fact]
    public void Report_SetsLocalizedConfidence()
    {
        var vm = new LiveViewModel(new FakeRunner());

        vm.Report(Chunk(0, Emotion.Mutlu, 0.5));   // Confidence.Orta

        Assert.Equal("Orta", vm.CurrentConfidence);
    }
```

Dosyanın başına gereken using'leri ekle: `using System.Linq;` (varsa tekrarlama).

- [ ] **Adım 6: Testi çalıştır, başarısız olduğunu doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Desktop.Tests`
Beklenen: Derleme hatası — `'LiveViewModel' does not contain a definition for 'TextAnalysis'`.

- [ ] **Adım 7: `LiveViewModel`'i güncelle**

`src/SentimentAnalysis.Desktop/LiveViewModel.cs`:

**(a)** Using ekle: `using SentimentAnalysis.Application.Configuration;`

**(b)** Alan ekle (mevcut `_selected` alanından sonra):

```csharp
    private readonly double _temperature;
```

**(c)** Gözlemlenebilir özellikleri ekle (mevcut `_selectedFile` alanından sonra):

```csharp
    [ObservableProperty] private string _currentConfidence = "—";
    [ObservableProperty] private SourceAnalysisVm _textAnalysis = SourceAnalysisVm.Empty("Metin Analizi");
    [ObservableProperty] private SourceAnalysisVm _audioAnalysis = SourceAnalysisVm.Empty("Ses Analizi");
    [ObservableProperty] private SourceAnalysisVm _finalAnalysis = SourceAnalysisVm.Empty("Nihai Karar");
```

**(d)** Yapıcıya opsiyonel config ekle (mevcut çağıranlar bozulmaz — additive):

```csharp
    public LiveViewModel(IAnalysisRunner runner, IAudioFilePicker? picker = null,
                         ISelectedAudio? selected = null, AppConfig? config = null)
    {
        _runner = runner;
        _picker = picker;
        _selected = selected;
        _temperature = (config ?? AppConfig.Default).Fusion.DistributionTemperature;
    }
```

**(e)** `Report`'u genişlet (mevcut satırlar korunur, sonuna eklenir):

```csharp
    public void Report(ChunkResult r)
    {
        var row = new ChunkRowVm(r);
        Timeline.Add(row);
        CurrentEmotion = row.Emotion;
        CurrentValence = row.Valence;
        CurrentArousal = row.Arousal;
        CongruenceBadge = row.Badge;

        // Üç kaynak ayrı ayrı — söz-ton uyumsuzluğu böylece ekranda görünür hale gelir.
        CurrentConfidence = EmotionLocalizer.Confidence(r.Fused.Confidence);
        TextAnalysis  = new SourceAnalysisVm("Metin Analizi", r.Fused.TextVa, _temperature);
        AudioAnalysis = new SourceAnalysisVm("Ses Analizi", r.Fused.ProsodyVa, _temperature);
        FinalAnalysis = new SourceAnalysisVm("Nihai Karar", r.Fused.Combined, _temperature);
    }
```

- [ ] **Adım 8: Testleri çalıştır, geçtiklerini doğrula**

Çalıştır: `dotnet test tests/SentimentAnalysis.Desktop.Tests`
Beklenen: PASS — 4 yeni test dahil hepsi geçer; mevcut `Report_AddsRowAndUpdatesCurrent`
testi de geçmeye devam eder (regresyon yok).

- [ ] **Adım 9: Tüm testleri çalıştır**

Çalıştır: `dotnet build && dotnet test`
Beklenen: 0 başarısız. Test sayısı 213 → 222.

- [ ] **Adım 10: Commit**

```bash
git add src/SentimentAnalysis.Desktop/ tests/SentimentAnalysis.Desktop.Tests/
git commit -m "feat(desktop): metin/ses/nihai üçlü analiz görünüm modeli"
```

---

## Task 5: Üç sütunlu canlı görünüm (Avalonia XAML)

> **Uyarı:** Avalonia XAML headless olarak doğrulanamaz. Derleme hatayı yakalar ama düzeni
> yakalamaz — **görsel QA kullanıcıdadır** (08'de olduğu gibi). Bu task'ta "çalışıyor" iddiası
> yalnızca derleme + kullanıcının gözüyle doğrulanır.

**Dosyalar:**
- Değiştir: `src/SentimentAnalysis.Desktop.Avalonia/Views/LiveView.axaml`

**Arayüzler:**
- Tüketir: `LiveViewModel.TextAnalysis/.AudioAnalysis/.FinalAnalysis` (`SourceAnalysisVm`),
  `.CurrentConfidence`, `.CongruenceBadge`, `.IsCapturing`, `.Timeline`;
  `SourceAnalysisVm.Title/.Emotion/.Valence/.Arousal/.Shares/.HasData`;
  `EmotionShareVm.Emotion/.Percent`
- Üretir: yok (yaprak katman)

- [ ] **Adım 1: Kaynak şablonunu ve üç sütunu yaz**

`src/SentimentAnalysis.Desktop.Avalonia/Views/LiveView.axaml` — kök `<UserControl>` etiketine
Desktop ad alanını ekle:

```xml
             xmlns:desktop="clr-namespace:SentimentAnalysis.Desktop;assembly=SentimentAnalysis.Desktop"
```

Kök etiketten hemen sonra kaynak şablonunu ekle:

```xml
    <UserControl.Resources>
        <DataTemplate x:Key="SourcePanel" DataType="desktop:SourceAnalysisVm">
            <Border BorderBrush="{DynamicResource SystemBaseLowColor}" BorderThickness="1"
                    CornerRadius="6" Padding="12" Margin="4">
                <StackPanel Spacing="6">
                    <TextBlock Text="{Binding Title}" FontWeight="SemiBold" FontSize="13"
                               Foreground="{DynamicResource SystemBaseMediumColor}" />
                    <TextBlock Text="{Binding Emotion}" FontSize="22" FontWeight="Bold" />

                    <ItemsControl ItemsSource="{Binding Shares}" IsVisible="{Binding HasData}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate DataType="desktop:EmotionShareVm">
                                <Grid ColumnDefinitions="70,*,38" Margin="0,1">
                                    <TextBlock Grid.Column="0" Text="{Binding Emotion}" FontSize="11"
                                               VerticalAlignment="Center" />
                                    <ProgressBar Grid.Column="1" Minimum="0" Maximum="100"
                                                 Value="{Binding Percent}" Height="6"
                                                 VerticalAlignment="Center" Margin="4,0" />
                                    <TextBlock Grid.Column="2" Text="{Binding Percent, StringFormat='%{0}'}"
                                               FontSize="11" TextAlignment="Right"
                                               VerticalAlignment="Center" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <TextBlock IsVisible="{Binding !HasData}" FontStyle="Italic" FontSize="11"
                               Foreground="{DynamicResource SystemBaseMediumColor}"
                               Text="henüz ölçüm yok" />

                    <TextBlock FontSize="11" IsVisible="{Binding HasData}"
                               Foreground="{DynamicResource SystemBaseMediumColor}">
                        <TextBlock.Text>
                            <MultiBinding StringFormat="V {0:F2}  ·  A {1:F2}">
                                <Binding Path="Valence" />
                                <Binding Path="Arousal" />
                            </MultiBinding>
                        </TextBlock.Text>
                    </TextBlock>
                </StackPanel>
            </Border>
        </DataTemplate>
    </UserControl.Resources>
```

Mevcut "Güncel Durum" `<Border>` bloğunun tamamını (kontrol düğmelerinin altındaki blok) şununla
değiştir:

```xml
            <Grid ColumnDefinitions="*,*,*">
                <ContentControl Grid.Column="0" Content="{Binding TextAnalysis}"
                                ContentTemplate="{StaticResource SourcePanel}" />
                <ContentControl Grid.Column="1" Content="{Binding AudioAnalysis}"
                                ContentTemplate="{StaticResource SourcePanel}" />
                <ContentControl Grid.Column="2" Content="{Binding FinalAnalysis}"
                                ContentTemplate="{StaticResource SourcePanel}" />
            </Grid>

            <StackPanel Orientation="Horizontal" Spacing="16" Margin="4,0">
                <TextBlock Text="{Binding CurrentConfidence, StringFormat='Güven: {0}'}"
                           FontSize="12" VerticalAlignment="Center" />
                <TextBlock Text="{Binding CongruenceBadge}" Foreground="DarkOrange"
                           FontStyle="Italic" FontSize="12" VerticalAlignment="Center" />
            </StackPanel>

            <TextBlock FontSize="10" TextWrapping="Wrap"
                       Foreground="{DynamicResource SystemBaseMediumColor}" Margin="4,0"
                       Text="Yüzdeler, valence-arousal uzayındaki yakınlık paylarıdır — kalibre edilmiş olasılık değildir." />
```

- [ ] **Adım 2: Derle**

Çalıştır: `dotnet build`
Beklenen: 0 hata, 0 uyarı. XAML derleyicisi bilinmeyen binding yolu veya tip hatası verirse
düzelt (08'de karşılaşılan tuzaklar: `ItemsSource` — `Items` değil; `DataType` ad alanı öneki).

- [ ] **Adım 3: Tüm testleri çalıştır**

Çalıştır: `dotnet build && dotnet test`
Beklenen: 0 başarısız, 222 test (XAML testi yok — bu katman headless doğrulanmaz).

- [ ] **Adım 4: Uygulamayı çalıştır ve görsel QA iste**

Çalıştır: `dotnet run --project src/SentimentAnalysis.Desktop.Avalonia`

Kullanıcıdan doğrulamasını iste:
- Canlı sekmesinde üç sütun yan yana görünüyor mu (Metin Analizi · Ses Analizi · Nihai Karar)?
- Analiz başlamadan önce üçü de "henüz ölçüm yok" diyor mu?
- Bir ses dosyası analiz edilince üç sütun da doluyor ve yüzde çubukları görünüyor mu?
- Yüzdeler toplamı %100 görünüyor mu, sütunlar taşmıyor mu?

**Kullanıcı onaylamadan bu task tamamlanmış sayılmaz.**

- [ ] **Adım 5: Commit**

```bash
git add src/SentimentAnalysis.Desktop.Avalonia/Views/LiveView.axaml
git commit -m "feat(ui): canlı ekranda metin/ses/nihai üç sütunlu yüzde görünümü"
```

---

## Task 6: Mikrofon yakalama SPIKE (ölçüm — üretim kodu yok)

> **Bu task kod teslim etmez, ÖLÇÜM teslim eder.** Amaç, AudioToolbox yolunun gerçekten
> çalıştığını ve hangi kısıtlarla çalıştığını öğrenmektir. Ölçmeden plan yazılmaz
> (master §12.4). Spike çıktısı `scratchpad/` altındadır ve repoya girmez.

**Dosyalar:**
- Oluştur: `scratchpad/mic-spike/` (geçici .NET konsol projesi — `.gitignore` kapsamında)
- Oluştur: `docs/superpowers/specs/subprojects/08d-mikrofon-spike-olcumu.md` (ölçüm raporu)

**Arayüzler:**
- Tüketir: macOS `AudioToolbox.framework` (`AudioQueueNewInput`, `AudioQueueAllocateBuffer`,
  `AudioQueueEnqueueBuffer`, `AudioQueueStart`, `AudioQueueStop`, `AudioQueueDispose`)
- Üretir: ölçüm raporu — Dilim 3 planının girdisi

- [ ] **Adım 1: Spike konsol projesini kur**

```bash
mkdir -p scratchpad/mic-spike
cd scratchpad/mic-spike
dotnet new console -n MicSpike
```

- [ ] **Adım 2: AudioQueue P/Invoke ile kayıt denemesini yaz**

`scratchpad/mic-spike/MicSpike/Program.cs`:

```csharp
using System.Runtime.InteropServices;

// macOS AudioToolbox AudioQueue ile mikrofon yakalama fizibilite ölçümü.
// Amaç: örnek akıyor mu, hangi formatta, izin nasıl davranıyor?

const int SampleRate = 16000;
const int Channels = 1;
const int BitsPerChannel = 16;
const int BufferBytes = 8192;
const int BufferCount = 3;

[StructLayout(LayoutKind.Sequential)]
struct AudioStreamBasicDescription
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

[StructLayout(LayoutKind.Sequential)]
struct AudioQueueBuffer
{
    public uint mAudioDataBytesCapacity;
    public IntPtr mAudioData;
    public uint mAudioDataByteSize;
    public IntPtr mUserData;
    public uint mPacketDescriptionCapacity;
    public IntPtr mPacketDescriptions;
    public uint mPacketDescriptionCount;
}

const string AT = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

[DllImport(AT)]
static extern int AudioQueueNewInput(ref AudioStreamBasicDescription format,
    InputCallback callback, IntPtr userData, IntPtr runLoop, IntPtr runLoopMode,
    uint flags, out IntPtr audioQueue);

[DllImport(AT)]
static extern int AudioQueueAllocateBuffer(IntPtr audioQueue, uint bufferByteSize, out IntPtr buffer);

[DllImport(AT)]
static extern int AudioQueueEnqueueBuffer(IntPtr audioQueue, IntPtr buffer,
    uint numPacketDescs, IntPtr packetDescs);

[DllImport(AT)]
static extern int AudioQueueStart(IntPtr audioQueue, IntPtr startTime);

[DllImport(AT)]
static extern int AudioQueueStop(IntPtr audioQueue, bool immediate);

[DllImport(AT)]
static extern int AudioQueueDispose(IntPtr audioQueue, bool immediate);

delegate void InputCallback(IntPtr userData, IntPtr audioQueue, IntPtr buffer,
    IntPtr startTime, uint numPackets, IntPtr packetDescs);

long totalBytes = 0;
int callbackCount = 0;
double peakAmplitude = 0.0;
IntPtr queue = IntPtr.Zero;

// GC'nin delegate'i toplamasını engelle — native taraf referansı tutmaz.
InputCallback callback = (userData, aq, bufferPtr, startTime, numPackets, packetDescs) =>
{
    var buf = Marshal.PtrToStructure<AudioQueueBuffer>(bufferPtr);
    int byteCount = (int)buf.mAudioDataByteSize;
    callbackCount++;
    totalBytes += byteCount;

    if (byteCount > 0)
    {
        var pcm = new short[byteCount / 2];
        Marshal.Copy(buf.mAudioData, pcm, 0, pcm.Length);
        foreach (var s in pcm)
        {
            double a = Math.Abs(s) / 32768.0;
            if (a > peakAmplitude) peakAmplitude = a;
        }
    }

    AudioQueueEnqueueBuffer(aq, bufferPtr, 0, IntPtr.Zero);
};
var callbackHandle = GCHandle.Alloc(callback);

var format = new AudioStreamBasicDescription
{
    mSampleRate = SampleRate,
    mFormatID = 0x6C70636D,                 // 'lpcm'
    mFormatFlags = 0xC,                     // SignedInteger | IsPacked
    mBytesPerPacket = (uint)(Channels * BitsPerChannel / 8),
    mFramesPerPacket = 1,
    mBytesPerFrame = (uint)(Channels * BitsPerChannel / 8),
    mChannelsPerFrame = Channels,
    mBitsPerChannel = BitsPerChannel,
    mReserved = 0,
};

Console.WriteLine($"İstenen format: {SampleRate} Hz, {Channels} kanal, PCM{BitsPerChannel}");

int status = AudioQueueNewInput(ref format, callback, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out queue);
Console.WriteLine($"AudioQueueNewInput  → {status} {(status == 0 ? "(OK)" : "(HATA)")}");
if (status != 0)
{
    Console.WriteLine("SONUÇ: kuyruk oluşturulamadı. İzin reddi veya format desteklenmiyor olabilir.");
    return;
}

for (int i = 0; i < BufferCount; i++)
{
    AudioQueueAllocateBuffer(queue, BufferBytes, out var b);
    AudioQueueEnqueueBuffer(queue, b, 0, IntPtr.Zero);
}

status = AudioQueueStart(queue, IntPtr.Zero);
Console.WriteLine($"AudioQueueStart     → {status} {(status == 0 ? "(OK)" : "(HATA)")}");
Console.WriteLine("5 saniye kayıt — LÜTFEN YÜKSEK SESLE KONUŞUN...");

Thread.Sleep(5000);

AudioQueueStop(queue, true);
AudioQueueDispose(queue, true);
callbackHandle.Free();

double seconds = totalBytes / 2.0 / SampleRate;
Console.WriteLine();
Console.WriteLine("=== ÖLÇÜM ===");
Console.WriteLine($"Callback sayısı     : {callbackCount}");
Console.WriteLine($"Toplam bayt         : {totalBytes}");
Console.WriteLine($"Ses süresi (16k/16b): {seconds:F2} sn");
Console.WriteLine($"Tepe genlik         : {peakAmplitude:F4}");
Console.WriteLine();
Console.WriteLine(callbackCount == 0
    ? "SONUÇ: HİÇ callback gelmedi → izin reddedilmiş veya callback marshal edilmiyor."
    : peakAmplitude < 0.001
        ? "SONUÇ: callback geliyor ama ses SIFIR → büyük olasılıkla mikrofon izni yok (TCC sessiz reddi)."
        : "SONUÇ: GERÇEK SES YAKALANDI — AudioToolbox yolu çalışıyor.");
```

- [ ] **Adım 3: Spike'ı çalıştır ve ölç**

```bash
dotnet run --project scratchpad/mic-spike/MicSpike
```

Çalışırken **yüksek sesle konuşun**. Üç sonuçtan biri çıkar:

| Çıktı | Anlamı | Sonraki adım |
|---|---|---|
| "GERÇEK SES YAKALANDI" | Yol açık | Dilim 3 planı yazılır |
| "ses SIFIR" | İzin sorunu (TCC) | Adım 4'ü çalıştır |
| "HİÇ callback gelmedi" | Marshal veya kuyruk sorunu | Adım 5'e geç |

- [ ] **Adım 4: İzin davranışını ölç**

macOS mikrofon izni istemi çıktı mı? Çıkmadıysa:

```bash
open "x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone"
```

Listede terminal uygulamanız (Terminal / iTerm / VS Code) görünüyor mu? İzin verilip spike
tekrar çalıştırıldığında ses geliyor mu? Bunu **not edin** — üretim uygulamasının
`NSMicrophoneUsageDescription` içeren bir `.app` bundle'a ihtiyacı olup olmadığını bu belirler.

- [ ] **Adım 5: Ölçüm raporunu yaz**

`docs/superpowers/specs/subprojects/08d-mikrofon-spike-olcumu.md` oluştur. **Ölçülen gerçek
değerleri** yaz — tahmin veya beklenti değil:

```markdown
# 08d Mikrofon Spike Ölçümü

> Tarih: <ölçüm tarihi> · Donanım: MacBook Air M1, macOS <sürüm> · .NET 9

## Sonuç
<GEÇTİ | KALDI> — <tek cümle>

## Ölçülen değerler
| Soru | Ölçüm |
|---|---|
| AudioQueueNewInput dönüş kodu | <değer> |
| Callback sayısı (5 sn) | <değer> |
| Yakalanan ses süresi | <değer> sn |
| Tepe genlik (konuşurken) | <değer> |
| İstenen 16 kHz doğrudan kabul edildi mi | <evet/hayır — hayırsa cihazın verdiği hız> |
| İzin istemi çıktı mı | <evet/hayır> |
| .app bundle gerekiyor mu | <evet/hayır — gerekçe> |

## Dilim 3 planına etkisi
- <yeniden örnekleme gerekiyor mu>
- <izin akışı UI'da nasıl ele alınacak>
- <karşılaşılan tuzaklar>
```

- [ ] **Adım 6: Spike artıklarını temizle ve raporu commit'le**

```bash
rm -rf scratchpad/mic-spike
git add docs/superpowers/specs/subprojects/08d-mikrofon-spike-olcumu.md
git commit -m "docs: mikrofon yakalama spike ölçüm raporu"
```

- [ ] **Adım 7: DUR — kullanıcıya raporla**

Ölçüm sonucunu kullanıcıya sun ve karar iste:
- **Spike geçtiyse:** Dilim 3 (mikrofon üretim kodu) için ayrı bir plan yazılacak. Onay iste.
- **Spike kaldıysa:** cross-platform NuGet yoluna (PortAudioSharp2 / Silk.NET-OpenAL) geçilsin
  mi diye sor. **Kendi başına yol değiştirme** — bu bir tasarım forku, kullanıcının kararı.

---

## Öz-Denetim

**1. Spec kapsamı:**

| Spec gereksinimi | Task |
|---|---|
| `EmotionWheel.Distribute` + softmax + sayısal kararlılık | 1 |
| `Distribute[0] == Map` garantisi | 1 (test) |
| `DistributionTemperature` additive config | 1 |
| Dürüstlük dili ("yakınlık payı") | 1 (XML doc), 5 (UI metni) |
| `PercentFormatter` en-büyük-kalan | 2 |
| `FusedResult` alan değişimi | 3 |
| SQLite şema + `PRAGMA`/`ALTER TABLE` geçişi | 3 |
| `SourceAnalysisVm` | 4 |
| `LiveViewModel` üç panel | 4 |
| XAML üç sütun | 5 |
| Mikrofon spike (üç soru) | 6 |
| Mikrofon üretim kodu (`MicrophoneCaptureSource`, `StreamingChunkBuffer`) | **Ayrı plan** — spike ölçümünden sonra yazılır |

**Kasıtlı kapsam kararı:** Dilim 3'ün üretim kodu bu plana **dahil edilmedi**. Sebebi
"No Placeholders" kuralıdır: cihaz formatı ve izin davranışı ölçülmeden `MicrophoneCaptureSource`
için gerçek kod yazılamaz — yalnızca uydurulabilirdi. Task 6 ölçümü tamamlandıktan sonra
Dilim 3 için ayrı bir plan yazılacaktır.

**2. Placeholder taraması:** Yapıldı. Tüm test ve uygulama adımları gerçek kod içerir.
Task 6'daki `<açılı parantezler>` yalnızca **ölçüm raporu şablonundadır** — bunlar plan
placeholder'ı değil, ölçüm sonucu doldurulacak alanlardır.

**3. Tip tutarlılığı:** Doğrulandı.
- `EmotionShare(Emotion, double Share)` → Task 1 tanımlar, Task 4 tüketir ✓
- `Distribute(ValenceArousal, double)` → Task 1 tanımlar, Task 4 tüketir ✓
- `ToPercents(IReadOnlyList<double>) → IReadOnlyList<int>` → Task 2 tanımlar, Task 4 tüketir ✓
- `FusedResult.TextVa/ProsodyVa/TextConfidence/ProsodyConfidence` → Task 3 tanımlar,
  Task 3 (depolama) ve Task 4 (VM) tüketir ✓
- `SourceAnalysisVm.Title/Emotion/Valence/Arousal/Shares/HasData` + `EmotionShareVm(Emotion, Percent)`
  → Task 4 tanımlar, Task 5 XAML binding'leri birebir bu adları kullanır ✓
- `LiveViewModel.CurrentConfidence` → Task 4 tanımlar, Task 5 bind eder ✓
