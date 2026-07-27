# Alt-Proje 08d — Yüzdelik Dağılım, Üçlü Analiz Görünümü, Canlı Mikrofon

> Tarih: 2026-07-27 · Bağımlılık: 01, 02, 05, 06, 08 (tamamlandı) · Katman: Core + Application
> + Infrastructure.Audio + Infrastructure.Storage + Desktop/Desktop.Avalonia

## Hedef

Üç kullanıcı isteği, artan risk sırasıyla:

1. Duygu çıktısını **yüzdelik dağılım** olarak göster (tek etiket yerine 8 duyguya pay).
2. **Metin analizi · Ses analizi · Nihai karar** üçünü ayrı ayrı, yan yana göster.
3. **Canlı mikrofonla** ses yakalayıp konuşurken analiz et.

İlk iki dilim saf ve deterministik (ML'siz, hızlı TDD). Üçüncü dilim native ve risklidir;
kod yazılmadan önce gerçek donanımda ölçülür (master §12.4).

---

## Dilim 1 — 8-duygu yüzde dağılımı (saf, Core)

### Ne

`EmotionWheel` bugün bir VA noktasını yalnızca **en yakın** duyguya haritalıyor
(`Map` / `MapWithSecondary`). Buna üçüncü bir yetenek eklenir: aynı noktayı **8 duyguya
dağıtmak**.

```csharp
/// <summary>Bir duyguya düşen yakınlık payı (0..1). Tüm payların toplamı 1.0'dır.</summary>
public readonly record struct EmotionShare(Emotion Emotion, double Share);

// EmotionWheel içinde:
public static IReadOnlyList<EmotionShare> Distribute(ValenceArousal va, double temperature);
```

### Formül

Her duygu merkezine Öklid uzaklığı `d` hesaplanır, ardından `softmax(−d² / T)` uygulanır:

```
w_i = exp(−d_i² / T)
share_i = w_i / Σ w_j
```

`T` (sıcaklık) keskinliği belirler: küçük `T` → tek duygu domine eder; büyük `T` → daha düz
dağılım. Sonuç **azalan pay sırasında** döner.

Sayısal kararlılık için softmax, üsleri almadan önce en büyük terimi çıkararak hesaplanır
(`exp` taşmasını önler).

### Nerede yaşar

`Distribute`, `EmotionWheel`'in **içine** girer — 8 duygu merkezi zaten orada `private` olarak
tanımlı. Böylece merkezler tek kaynakta kalır ve `Map` ile `Distribute` yapısal olarak
çelişemez. Bu bir testle kilitlenir: `Distribute(va)`'nın ilk elemanı her zaman `Map(va)`
sonucuna eşittir.

### Sıcaklık ayarı

`T`, `AppConfig`'e **additive** (varsayılanlı) eklenir:

```csharp
public sealed record FusionConfig(
    ...,
    double DistributionTemperature = 0.35);
```

Varsayılan `0.35` seçilmiştir: VA uzayında komşu duygu merkezleri arası tipik uzaklık ~0.4–0.6
olduğundan bu değer baskın duyguya belirgin ama ezici olmayan bir pay verir. **Bu değer
kalibre edilmemiştir** — eval-09'un kalibrasyon kalemidir (bkz. Açık Uçlar).

### Dürüstlük kısıtı (master §12 — "dürüstlük > kesinlik gösterisi")

Bu sayı **kalibre edilmiş bir olasılık değildir**; VA uzayındaki geometrik bir yakınlık payıdır.
Sistemde 8 duygu üzerinde olasılık üreten bir model yoktur (prozodi modeli regresyon yapar,
metin modelinin softmax'ı 11 farklı etiket üzerinedir ve VA'ya çevrilir).

Bu nedenle:
- UI'da başlık **"yakınlık payı"** ifadesini kullanır, "olasılık" veya "ihtimal" değil.
- Panelde tek satırlık bir açıklama bulunur: paylar VA uzayındaki yakınlıktan türetilmiştir.
- Kod dokümantasyonu (XML doc) aynı uyarıyı taşır.

### Yüzdeye çevirme

Ham paylar (0..1 double) ekranda tam sayı yüzdeye çevrilirken naif yuvarlama toplamı 99 veya
101 yapabilir. Bunu önlemek için Desktop katmanında saf bir yardımcı:

```csharp
public static class PercentFormatter
{
    /// <summary>Payları toplamı tam 100 olan tam sayı yüzdelere çevirir (en-büyük-kalan yöntemi).</summary>
    public static IReadOnlyList<int> ToPercents(IReadOnlyList<double> shares);
}
```

En-büyük-kalan (largest remainder) yöntemi: her payı 100 ile çarp, taban al, kalan 100'e kadar
olan farkı en büyük ondalık kalana sahip öğelere birer birer dağıt.

### Testler (saf, tam TDD)

- Paylar toplamı 1.0 (tolerans içinde).
- Tüm paylar pozitif (softmax hiçbir zaman sıfır vermez).
- İlk eleman `Map(va)` ile aynı duygu.
- Bir duygu merkezinin tam üzerindeki VA noktası → o duygu en yüksek payı alır.
- Küçük `T` → baskın payı büyür; büyük `T` → dağılım düzleşir (monotonluk).
- `Distribute` her zaman 8 öğe döner (hiçbir duygu düşmez).
- `ToPercents`: toplam her zaman tam 100; en-büyük-kalan davranışı; tek öğe; eşit paylar.

---

## Dilim 2 — Metin / Ses / Nihai üç sütunlu görünüm

### Sorun

`FusedResult` şu an her iki modelden yalnızca **valence**'ı taşıyor:

```csharp
double TextValence,
double AudioValence
```

Arousal ve model güvenleri füzyonun içinde kalıp kayboluyor. Metin ve ses sütunlarını dürüstçe
doldurmak (her biri için duygu + dağılım + V/A) bu kayıp veriyi gerektirir.

### Domain değişikliği

`FusedResult`'ta iki `double` alan, tam bilgi taşıyan alanlarla **değiştirilir**:

```
- double TextValence,
- double AudioValence
+ ValenceArousal TextVa,
+ ValenceArousal ProsodyVa,
+ double TextConfidence,
+ double ProsodyConfidence
```

`AppConfig` için geçerli olan "additive" kuralı yapılandırmayı bağlar; burada iki gereksiz
alanı korumak yerine değiştirmek daha temizdir. Etki alanı ölçülmüştür ve dardır:

| Yer | Değişiklik |
|---|---|
| `Core/Results.cs` | Kayıt tanımı |
| `Application/Fusion/EmotionFusion.cs` | Yapıcı çağrısı (değerler zaten elde mevcut) |
| `Infrastructure.Storage/SqliteSessionStore.cs` | Yazma + okuma |
| `Infrastructure.Storage/StorageSchema.cs` | Şema + geçiş |
| `Core.Tests`, `Application.Tests` | 3 test yeri |

`Flagged` türetilmiş özelliği ve diğer tüm alanlar aynen korunur.

### SQLite şeması ve geçiş

`chunks` tablosuna dört sütun eklenir:

```sql
text_arousal REAL NOT NULL,
audio_arousal REAL NOT NULL,
text_confidence REAL NOT NULL,
audio_confidence REAL NOT NULL
```

**Kritik:** `CREATE TABLE IF NOT EXISTS` mevcut bir veritabanına sütun **eklemez**. Bu yüzden
şema kurulumundan sonra çalışan küçük bir geçiş adımı gerekir:

1. `PRAGMA table_info(chunks)` ile mevcut sütunlar okunur.
2. Eksik olan her yeni sütun için `ALTER TABLE chunks ADD COLUMN <ad> REAL NOT NULL DEFAULT 0`
   çalıştırılır.

Bu adım idempotenttir (ikinci çalıştırmada hiçbir şey yapmaz). Varsayılan `0`, eski satırların
bu alanlarının ölçülmediğini gösterir; okuma tarafı bunları olduğu gibi döndürür — uydurma
değer üretilmez.

**Test:** eski şemayla (4 sütunsuz) oluşturulmuş bir veritabanı dosyası açıldığında geçişin
sütunları eklediği ve eski satırların okunabilir kaldığı doğrulanır.

### Görünüm modeli

Üç sütunu tek bir yeniden-kullanılabilir VM besler:

```csharp
public sealed class SourceAnalysisVm
{
    public string Title { get; }                          // "Metin Analizi" | "Ses Analizi" | "Nihai Karar"
    public string Emotion { get; }                        // lokalize baskın duygu
    public double Valence { get; }
    public double Arousal { get; }
    public IReadOnlyList<EmotionShareVm> Shares { get; }   // 8 duygu, azalan; lokalize ad + tam sayı yüzde
}

public sealed record EmotionShareVm(string Emotion, int Percent);
```

`LiveViewModel`'e üç özellik eklenir: `TextAnalysis`, `AudioAnalysis`, `FinalAnalysis`. Her
`Report(ChunkResult)` çağrısında üçü birden güncellenir:

- `TextAnalysis` ← `r.Fused.TextVa`
- `AudioAnalysis` ← `r.Fused.ProsodyVa`
- `FinalAnalysis` ← `r.Fused.Combined`

Mevcut `CurrentEmotion` / `CurrentValence` / `CurrentArousal` / `CongruenceBadge` özellikleri
**korunur** (mevcut testler ve rapor akışı bunlara dayanıyor); `FinalAnalysis` bunların yanında
zengin görünümü sağlar.

### XAML

`LiveView.axaml`'deki tek "Güncel Durum" kutusu, eşit genişlikte üç sütunlu bir `Grid`'e
dönüşür. Her sütun aynı `DataTemplate`'i kullanır:

- Sütun başlığı (kaynak adı)
- Baskın duygu (büyük punto)
- 8 duygu yüzde çubuğu (duygu adı · orantılı çubuk · `%NN`)
- `V ±0.00 · A 0.00` satırı

Nihai sütununda ek olarak güven düzeyi (Yüksek/Orta/Düşük) ve uyumsuzluk rozeti
(`CongruenceBadge`) gösterilir. Paneller altında tek satırlık dürüstlük açıklaması yer alır.

Zaman çizelgesi bu turda değişmez.

### Kazanç

Metin "Mutlu", ses "Gergin" derken nihai "Nötr" çıktığında, projenin asıl değeri olan **söz-ton
uyumsuzluğu** ekranda gözle görünür hale gelir. Bu görünüm maskeleme tespitinin vitrinidir.

### Testler (headless, saf)

- `SourceAnalysisVm` payları azalan sırada, yüzdeler toplamı 100.
- `Report()` sonrası üç panel de dolu ve doğru kaynaktan besleniyor (metin paneli metin VA'sını
  gösteriyor, ses paneli ses VA'sını — karışmıyor).
- Uyumsuz bir chunk'ta üç panelin farklı baskın duygular göstermesi.
- Mevcut `CurrentEmotion` vb. özelliklerin davranışının bozulmadığı (regresyon).

---

## Dilim 3 — Canlı mikrofon yakalama (native, önce SPIKE)

### Ölçülmüş başlangıç durumu (2026-07-27, bu makinede)

| Bulgu | Sonuç |
|---|---|
| `ffmpeg`, `sox`, `rec` | **Kurulu değil** |
| macOS stok kayıt CLI'ı | **Yok** (`afconvert`/`afplay`/`afinfo` var; kaydedici yok) |
| `AudioToolbox.framework` | **Mevcut** → P/Invoke uygulanabilir |
| Uygulama `.app` bundle'ı | **Yok** → mikrofon izni (TCC) şu an terminale/`dotnet`'e atfedilir |

Karar: **AudioToolbox / AudioQueue P/Invoke** (dış bağımlılık sıfır, gizlilik-öncelikli mimariye
uygun, native tip motor sınırında kalır).

### SPIKE (kod yazmadan önce — master §12.4)

Plan yazılmadan önce küçük bir spike üç soruyu **ölçer**:

1. `AudioQueueNewInput` + buffer callback ile .NET'e gerçekten örnek akıyor mu (native
   callback'in yönetilen koda marshal edilmesi, delegate ömrü, GC pinning)?
2. Cihazın verdiği gerçek format nedir (örnekleme hızı, kanal sayısı, PCM16 mı float32 mi)?
   Donanım 16 kHz'i doğrudan verebiliyor mu, yoksa 48 kHz alıp yeniden örnekleme mi gerekiyor?
3. Mikrofon izni (TCC) `dotnet run` altında nasıl davranıyor — istem çıkıyor mu, sessizce sıfır
   örnek mi geliyor, yoksa `NSMicrophoneUsageDescription` içeren bir `.app` bundle şart mı?

Spike sonucu kullanıcıya raporlanır ve plan ona göre yazılır. **Spike başarısız olursa** yol
cross-platform NuGet kütüphanesine (PortAudioSharp2 / Silk.NET-OpenAL) döner — bu durumda
kullanıcıya tekrar sorulur. Ölçmeden söz verilmez.

Spike'ın gerekçesi somuttur: alt-proje 05'te Silero VAD girdisinin `[1,512]` olduğu varsayılmış,
gerçek modelle yapılan test bunun `[1,576]` olduğunu ortaya çıkarmıştı.

### Mimari (spike yeşilse)

**`Infrastructure.Audio` → `MicrophoneCaptureSource : IAudioCaptureSource`**

- Native P/Invoke **yalnızca bu sınıfta** yaşar (motor sınırı kuralı).
- Cihaz hangi formatı veriyorsa alınır (ör. 48 kHz mono/stereo PCM16).
- 16 kHz mono'ya indirme, **mevcut ve test edilmiş** `AudioPreprocessor` ile yapılır — yeniden
  yazım yok.
- `IDisposable`: durdurmada kuyruk durdurulur, buffer'lar serbest bırakılır, delegate referansı
  bırakılır.

**Yeni saf sınıf: `StreamingChunkBuffer`**

Mevcut `AudioChunker` dizi tabanlıdır (tüm ses elde olduğunda çalışır); canlı akış için uygun
değildir. Yeni sınıf sürekli akan örnekleri biriktirir ve chunk sınırına ulaşınca örtüşmeli
`AudioChunk` üretir:

```csharp
public sealed class StreamingChunkBuffer
{
    public StreamingChunkBuffer(int sampleRate, TimeSpan chunkLength, TimeSpan overlap);
    /// <summary>Gelen örnekleri ekler; tamamlanan chunk'ları döndürür (yoksa boş).</summary>
    public IReadOnlyList<AudioChunk> Append(ReadOnlySpan<float> samples);
    /// <summary>Akış bittiğinde kalan kısmı (varsa) chunk olarak döndürür.</summary>
    public AudioChunk? Flush();
}
```

Saf ve deterministik → tam birim testli. Zaman damgaları akışın başından itibaren birikimli
olarak hesaplanır.

**Pipeline'da değişiklik yok.** `IAudioCaptureSource` sözleşmesi zaten
`IAsyncEnumerable<AudioChunk>` döndürüyor; VAD kapısı, bounded channel, füzyon ve oturum özeti
olduğu gibi çalışır. Bu, 05/06'da doğru çizilmiş sınırın karşılığıdır.

### UI bağlantısı

- Canlı ekrana kaynak seçimi eklenir: **Dosya** / **Mikrofon**.
- Mikrofon seçiliyken "Başlat" dosya seçimi gerektirmez (`CanStart` mantığı buna göre güncellenir).
- `CompositionRoot`, seçili moda göre `FileCaptureSource` veya `MicrophoneCaptureSource` üretir.
- `PipelineAnalysisRunner`'daki "ses kaynağı seçilmedi" istisnası artık gerçek bir kaynakla
  dolar; mikrofon erişimi başarısız olursa hata kullanıcıya **açıkça** gösterilir (sessiz yutma
  veya sahte veri yok).
- İzin reddedilirse kullanıcıya ne yapması gerektiğini söyleyen anlaşılır bir mesaj gösterilir.

### Testler

- `StreamingChunkBuffer`: tam birim testli (chunk sınırları, örtüşme, zaman damgaları, kısmi
  besleme, `Flush`, örnek sayısı akışta korunuyor mu).
- Format dönüşümü yardımcıları: saf, birim testli.
- Gerçek mikrofon testi: **env-gated `[SkippableFact]`** (`MIC_TEST=1`) — mikrofonsuz veya
  izinsiz ortamda `Skip`. Sahte-yeşil test yazılmaz.

---

## Kapsam DIŞI (bilerek)

- Windows WASAPI mikrofon implementasyonu (`IAudioCaptureSource` arayüzü hazır; ikinci
  implementasyon sonraki tura).
- Ham mikrofon kaydının şifreli diske yazılması (07b ile birlikte değerlendirilir).
- 8-duygu çarkı ve trend grafiği, sürükle-bırak (08b).
- Zaman çizelgesi satırlarının genişletilebilir üçlü detayı (bu turda üst panel yeterli).
- `DistributionTemperature`'ın veriyle kalibrasyonu → **eval-09**.

## Açık Uçlar (eval-09'a devredilen)

- `DistributionTemperature = 0.35` bir başlangıç değeridir, ölçülmemiştir. Etiketli Türkçe veri
  üzerinde kalibre edilmelidir.
- Yakınlık payının gerçek olasılıkla ne kadar örtüştüğü (kalibrasyon eğrisi) eval-09'da
  ölçülebilir; ölçülene kadar UI dilinde "olasılık" iddiası yer almaz.

## Kabul Kriterleri

1. `EmotionWheel.Distribute` 8 duygu için toplamı 1.0 olan paylar döndürür ve ilk elemanı her
   zaman `Map` ile aynıdır.
2. Canlı ekranda Metin · Ses · Nihai üç sütun yan yana görünür; her biri kendi baskın duygusunu
   ve 8-duygu yüzde dağılımını (toplam tam %100) gösterir.
3. Söz-ton uyumsuzluğu olan bir chunk'ta üç sütun farklı duygular gösterir ve uyumsuzluk rozeti
   görünür.
4. Mevcut bir SQLite veritabanı yeni sürümle açıldığında geçiş sütunları ekler; eski satırlar
   okunabilir kalır.
5. Mikrofon seçilip "Başlat"a basıldığında konuşurken zaman çizelgesi dolar ve üç panel canlı
   güncellenir (gerçek donanımda, kullanıcı tarafından doğrulanır).
6. `dotnet build && dotnet test` yeşil, 0 başarısız. `FusedResult` alan değişikliği nedeniyle
   3 mevcut test **uyarlanır** (yeni alan adlarını kullanacak şekilde) — davranış iddiaları
   aynı kalır; başka hiçbir mevcut testin davranışı değişmez.

## Sıra

Dilim 1 (saf) → Dilim 2 (saf + şema) → SPIKE (ölç + raporla) → Dilim 3 (native).

Her task sonrası `dotnet build && dotnet test` çalıştırılır ve kullanıcı onayı beklenir
(hız tercihi: hızlı iterasyon, saf task'larda hafif review, task araları onay kapısı).
