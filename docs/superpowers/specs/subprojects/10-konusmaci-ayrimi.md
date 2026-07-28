# Alt-Proje 10 — Konuşmacı Ayrımı (Diarization)

> Tarih: 2026-07-27 · Bağımlılık: 01, 02, 03, 05, 06, 07, 08, 08d, 08e (tamamlandı)
> · Katman: Core + Application + **Infrastructure.Diarization (yeni)** + Infrastructure.Storage
> + Desktop/Desktop.Avalonia

## Hedef

Ses kaydında (canlı mikrofon veya yüklenen dosya, fark etmez) **farklı kişilerin konuştuğunu
algılamak**, transkripti konuşmacıya göre düzenlemek ve duygu analizini **konuşmacı başına**
ayrıştırmak. Toplantı raporu artık "toplantı gergindi" değil, "Konuşmacı A gergin, Konuşmacı B
sakin" diyebilir.

## Master tasarımla ilişkisi — D9 DEĞİŞİYOR

Master §2 D9 şöyleydi:

> | D9 | Konuşmacı ayrımı | V1 toplam-akış (oda), mimari konuşmacı-hazır (`SpeakerId` alanı) | Diarization ağır ve çoğu Python; borcu minimize et |

Bu alt-proje D9'un birinci yarısını (V1 toplam-akış) **sona erdirir**.

**Dürüst tespit:** D9'un ikinci yarısı ("mimari konuşmacı-hazır, `SpeakerId` alanı") kodda
karşılıksızdı. `SpeakerId` diye bir alan hiçbir zaman yazılmadı; `grep -rn "Speaker" src/`
yalnızca master dokümanının kendi satırını buluyor. Dolayısıyla bu iş "hazır yuvayı doldurmak"
değil, **gerçek bir mimari genişletmedir**. Bu spec'i okuyan kimse "zaten hazırdı" varsayımıyla
işe başlamamalı.

D9'un "diarization çoğu Python" gerekçesi ise artık geçerli değil: sherpa-onnx cross-platform
bir NuGet paketi olarak C#'tan diarization sunuyor (aşağıda doğrulama).

---

## Kararlar (bu alt-projeye özel)

| # | Karar | Seçim | Neden |
|---|---|---|---|
| S1 | Kapsam | Etiketli transkript **ve** konuşmacı-başına duygu | Ürünün asıl değeri "kim ne hissetti"; sadece transkript yarım kalır |
| S2 | Canlı davranış | ~~Canlı anlık geçici etiket + Durdur'da nihai düzeltme~~ → **REVİZE 2026-07-27 (10a ölçümü):** canlı etiket **YOK**; Durdur'da rapor hemen gelir, diarization **arka planda** çalışıp raporu sonradan günceller | Ölçüldü: kısa bölgelerde (<4 sn) konuşmacı ayrımı mümkün değil (BOŞLUK negatif) → canlı etiket uydurma olurdu. Ayrıca RTF 0,44 → 30 dk toplantıda 13 dk donma. Bkz. [10a ölçümü](10a-diarization-spike-olcumu.md) |
| S3 | Motor | **sherpa-onnx** (`org.k2fsa.sherpa.onnx`) | C# API'si var, cross-platform, pyannote segmentation + embedding + clustering hazır |
| S4 | Kimlik kalıcılığı | Oturum-içi anonim (`Konuşmacı A/B/…`) + elle isim; **ses parmak izi diske YAZILMAZ** | Gizlilik varsayılan (master §12.5); kalıcı biyometri hukuki ağırlık getirir |
| S5 | Analiz birimi | Chunk kalır; bir chunk **konuşmacı başına birden fazla** `ChunkResult` üretir | Whisper tüm chunk'ta çalışıp bağlamı korur; bölme yerine **hizalama** |
| S6 | Örtüşen konuşma | `Bilinmeyen` işaretlenir, konuşmacı-başına duyguya sayılmaz | Belirsizse belirsiz de (master §12.2) |
| S7 | Varsayılan | Özellik **kapalı** gelir (`AppConfig` additive) | Mevcut davranış hiç değişmez; opt-in |

---

## Fizibilite doğrulaması — ÖLÇÜLDÜ (2026-07-27)

> **Donanımda ölçüldü, tahmin değil:** [10a Diarization Spike Ölçümü](10a-diarization-spike-olcumu.md).
> Özet: native çakışma **yok** (üç motor aynı süreçte gerçek çıkarım yaptı, 289 MB);
> nihai diarization kalitesi **%92,9 doğruluk / 0,48 sn sınır sapması** (2 kişi, uzak alan
> mikrofon); maliyet **RTF 0,44 / ~390 MB**. Ölçüm S2'yi değiştirdi (yukarıdaki tabloya bak).

Masa başı araştırmasının doğrulanan kısmı:

- `org.k2fsa.sherpa.onnx` NuGet paketi mevcut (güncel 1.13.4), macOS/Windows/Linux destekli.
- C# API'si speaker diarization içerir (1.10.28'den beri); pyannote-segmentation-3.0 ONNX'e
  çevrilmiş, 3D-Speaker/NeMo embedding çıkarıcıları ve agglomerative clustering mevcut.
- **Sınır:** diarization yalnızca **offline** (tüm ses elde). Streaming/online diarization YOK.
  Bu, S2 kararının teknik sebebidir: canlı geçiş kendi yazacağımız takipçiyle, nihai geçiş
  sherpa'nın offline diarization'ıyla yapılır.

~~**Ölçülmemiş varsayım:** üçünün aynı süreçte çakışmadığı kanıtlanmadan 10b'ye geçilmez.~~
**KAPANDI (2026-07-27):** çakışma yok — her iki yükleme sırasında da üç motor yüklendi *ve*
aynı ses üzerinde gerçek çıkarım yaptı (sherpa embedding 512 boyut, Silero VAD, Whisper.net
Türkçe transkripsiyon), tepe RSS 289 MB. Master D3/D4 escape-hatch'ine gerek kalmadı.

---

## Mimari

### Analiz birimi: bölme değil, hizalama

Naif yaklaşım "chunk'ı konuşmacı sınırlarından kes, her parçayı ayrı analiz et" olurdu. Bu
**reddedildi**: Whisper birkaç saniyelik kırpılmış parçalarda bağlamı kaybeder ve belirgin
şekilde kötüleşir — STT kalitesi tüm metin-duygu zincirinin girdisidir (garbage in, garbage out).

Onun yerine üç iş chunk'ın tamamında paralel yürür ve sonra **zaman ekseninde hizalanır**:

```
chunk (10–30 sn)
   ├─ Whisper TÜM chunk'ta        → zaman damgalı SttSegment[]  (bağlam korunur)
   ├─ Diarization TÜM chunk'ta    → SpeakerSegment[]  [A: 0–4sn, B: 4–9sn, A: 9–12sn]
   └─ SpeakerAligner (saf C#)     → her SttSegment bir SpeakerId'ye atanır
        └─ konuşmacı başına: metin duygusu + o kişinin sesinden prozodi → füzyon
             → N adet ChunkResult (konuşmacı başına bir tane)
```

Sonuç: belirsizlik (ML) motor sınırında kalır; "kim kimdir" kararının tamamı saf,
deterministik, unit-testli C#'tır.

### Konuşmacı ataması: iki akış (S2 revizyonundan sonra)

10a ölçümü canlı anlık etiketlemeyi elemiştir (kısa bölgelerde ayrım ölçülebilir değil).
Geriye **tek bir diarization geçişi** kalır; ne zaman çalıştığı moda göre değişir:

**Dosya analizi (basit yol):** Tüm ses zaten elde. Önce diarize edilir, sonra chunk'lar
işlenir — konuşmacı bilgisi normal akışın içinde, ilk sonuçla birlikte gelir. Bekleme
kullanıcının zaten beklediği analiz süresine eklenir.

**Canlı mikrofon (arka plan yolu):** "Durdur" → rapor **bugünkü gibi hemen** üretilir
(konuşmacısız). Ardından diarization **arka planda** kaydedilmiş ses üzerinde çalışır; bitince
rapor konuşmacı bilgisiyle kendini günceller. Kullanıcı 13 dakika donmuş ekrana bakmaz.

| | Dosya analizi | Canlı (arka plan) |
|---|---|---|
| Ne zaman | Analizden önce | Rapor üretildikten sonra |
| Kullanıcı ne görür | Sonuç zaten konuşmacılı | Önce konuşmacısız rapor → "konuşmacılar hesaplanıyor…" → güncellenmiş rapor |
| Girdi | Yüklenen dosya | **Saklanmış oturum sesi** (aşağıdaki uyarıya bak) |

**Dürüstlük gereği görünürlük (08e dersi):** arka plan işi sessiz olmaz. Kullanıcı durumu
("konuşmacılar hesaplanıyor…", "konuşmacılar hazır", "başarısız") ve raporun hangi hâlde
olduğunu görmelidir. Sessizce güncellenen bir rapor, kullanıcının yanlış bir ekran görüntüsüne
bakıp karar vermesine yol açar.

> **ENGELLEYİCİ ÖN KOŞUL 2 (kod okunarak doğrulandı, 2026-07-27):** Transkripti konuşmacıya
> göre bölmek **zaman damgalı metin** ister, ama bugün saklanan transkript **düz bir string**.
> Whisper zaman damgalı `SttSegment`'ler üretiyor, fakat `TranscriptAssembler.Assemble` bunları
> tek bir metne indirgeyip zaman bilgisini **atıyor** (`Transcript(string Text, ..., double AvgLogProb)`),
> ve `chunks.transcript` sütununa da bu düz metin yazılıyor. Konuşmacı bölgeleriyle hizalanacak
> bir şey kalmıyor. 10b bu zaman damgalarını korumalı ve saklamalıdır.
> **Dikkat:** Whisper'ın segment zamanları **chunk'a göreli** (0'dan başlar); mutlak zamana
> çevirmek için `chunk.Start` eklenmelidir — bu, sessizce yanlış hizalama üretebilecek bir
> tuzaktır ve testle kilitlenmelidir.

> **ENGELLEYİCİ ÖN KOŞUL 1 (kod okunarak doğrulandı, 2026-07-27):** Arka plan geçişi
> **saklanmış sese** ihtiyaç duyar, ama canlı akışta ses **hiç saklanmıyor**.
> `SqliteSessionStore.SaveAudioAsync` yazılmış ve testli, fakat üretim kodunda **hiçbir yerden
> çağrılmıyor** (yalnızca `SqliteSessionStoreTests` kullanıyor). Yani `sessions.audio_path`
> canlı oturumlarda daima `NULL`. 10b'nin **ilk task'ı** bu bağlantıyı kurmak olmalıdır:
> canlı oturumun sesi (master D13 gereği Opus + şifreli, 7 gün retention) kaydedilmeden
> konuşmacı ayrımı canlı modda hiç çalışamaz. Bu, `SpeakerId` ve VAD bölgeleri gibi üçüncü
> "belgede var, kodda yok" kalemidir — sayılmadan plan yapılmamalı.

### Yeni proje ve sözleşmeler

Yeni Infrastructure dilimi: **`SentimentAnalysis.Infrastructure.Diarization`**. sherpa-onnx
bağımlılığı bu projenin içinde kalır, dışarı sızmaz (mimari kural: her Infrastructure dilimi
ayrı proje, native tipler motor sınırında).

Application katmanına **tek** arayüz (S2 revizyonu `ISpeakerTracker`'ı gereksiz kıldı):

```csharp
/// <summary>Tüm kaydı görüp konuşmacı bölgeleri üretir (sherpa offline diarization).</summary>
public interface IDiarizer
{
    Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(float[] mono16k, CancellationToken ct);
}
```

Core'a eklenenler:

```csharp
/// <summary>Oturum-içi anonim konuşmacı kimliği. Toplantılar arası TAŞINMAZ (S4).</summary>
public readonly record struct SpeakerId(int Index)
{
    public static readonly SpeakerId Unknown = new(-1);
    public string DefaultLabel => Index < 0 ? "Bilinmeyen" : $"Konuşmacı {(char)('A' + Index)}";
}

public sealed record SpeakerSegment(SpeakerId Speaker, TimeSpan Start, TimeSpan End);

/// <summary>Konuşmacı başına toplantı özeti.</summary>
public sealed record SpeakerSummary(
    SpeakerId Speaker, string? UserGivenName, TimeSpan SpeakingTime, double SpeakingShare,
    Emotion DominantEmotion, ValenceArousal AverageVa, double AverageCongruence);
```

> **S2 revizyonunun sadeleştirdiği yer:** İlk tasarımda diarization canlı hattın içinde,
> chunk başına çalışacaktı; bu yüzden `ChunkResult` bir `SpeakerId` alanı kazanacak ve
> `IChunkProcessor.ProcessAsync` **liste** döndürecekti (bir chunk → konuşmacı başına N sonuç).
> Diarization artık oturum sonrası tek bir geçiş olduğundan **ikisi de gereksiz**:
> `IChunkProcessor` imzası değişmez, `ChunkResult` konuşmacı taşımaz. Konuşmacı bilgisi zaman
> ekseninde ayrı yaşar (`speaker_segments`) ve gösterim/agregasyon anında hizalanır.

10b'de `ChunkResult`'ın kazandığı tek alan **zaman damgalı transkript segmentleri**
(`IReadOnlyList<TranscriptSegment> Segments`, mutlak zamanlı) — hizalamanın hammaddesi.
`MeetingSummary`'ye `IReadOnlyList<SpeakerSummary> Speakers` **10c'de** eklenir.

**`EmotionPipeline`'ın kanal/drenaj/backpressure mantığına hiç dokunulmaz** — 08e'de gerçek
donanımda doğrulanmış o hassas kod korunur.

### Saf, testlenebilir yardımcılar

Bu özelliğin kalitesi ML'de değil burada kazanılır:

- **`SpeakerAligner`** — `SttSegment[]` ↔ `SpeakerSegment[]` zaman örtüşmesiyle eşleşir.
  Kenar durumlar: segment iki konuşmacıya yayılıyorsa **en çok örtüşene** atanır; hiç örtüşme
  yoksa `SpeakerId.Unknown`; örtüşme eşiği altındaysa (S6) `Unknown`.
- **`SpeakerTimeAggregator`** — konuşmacı başına süre, oran ve duygu agregasyonu → `SpeakerSummary`.

> `SpeakerLabelReconciler` ve `OnlineSpeakerClusterer` **kapsamdan çıktı** (S2 revizyonu:
> canlı geçici etiket yok → uzlaştırılacak etiket de yok). Belirgin sadeleşme.

---

## Depolama ve yapılandırma

- **İki yeni tablo** (S2 revizyonundan sonra `chunks.speaker_id` sütunu yerine): diarization
  artık chunk düzeyinde değil, tüm oturum üzerinde çalıştığı için konuşmacı bilgisi chunk'a
  değil zaman eksenine aittir.
  - `transcript_segments(session_id, seq, start_ms, end_ms, text)` — zaman damgalı transkript
    (bkz. ENGELLEYİCİ ÖN KOŞUL 2; bugün yalnızca düz metin saklanıyor).
  - `speaker_segments(session_id, seq, start_ms, end_ms, speaker_index)` — diarization çıktısı;
    `speaker_index = -1` → `Bilinmeyen`.
  - İkisi de `CREATE TABLE IF NOT EXISTS` ile gelir, yani eski veritabanlarında ALTER
    gerekmeden oluşur (08d'deki sütun-ekleme durumundan farkı budur).
- Konuşmacı isimleri (10c) oturum-başı ayrı küçük tabloda tutulur.
- **Ses embedding'i asla diske yazılmaz** (S4). Yalnızca bellekte, oturum süresince.
- `AppConfig`'e additive `DiarizationConfig` (varsayılanlı, master kuralı):

```csharp
public sealed record DiarizationConfig(
    bool Enabled = false,              // S7: opt-in
    double ClusteringThreshold = 0.8,  // 10a'da ÖLÇÜLDÜ: 0,5 altı kat fazla konuşmacı buluyor
    int MaxSpeakers = 8,
    double MinSegmentSeconds = 4.0);   // 10a: bunun altında ayrım ölçülebilir değil
```

Bu değerler **kalibre edilmiştir** — bkz. [10a ölçümü](10a-diarization-spike-olcumu.md).
`ClusteringThreshold = 0.8` sherpa'nın tipik varsayılanı olan 0,5'ten belirgin şekilde
yüksektir; 0,5'te iki kişilik kayıtta **altı** konuşmacı bulunmuştur.

## UI

- **Canlı ekran:** konuşmacı etiketi **gösterilmez** (S2 revizyonu). "Durdur" sonrası rapor
  hemen gelir; arka plan diarization'ının durumu ("konuşmacılar hesaplanıyor…" / "hazır" /
  "başarısız") kullanıcıya görünür biçimde bildirilir.
- **Rapor:** konuşmacı başına özet kartı (süre, oran, baskın duygu, ortalama VA, congruence);
  zaman çizelgesinde konuşmacı renkleri; transkript `Konuşmacı A: …` biçiminde.
- **İsimlendirme:** oturum içinde inline düzenleme; isim yalnızca o oturuma ait.

---

## Alt-proje bölünmesi

Bu iş tek plana sığmaz; üç dilim, her biri kendi plan → uygulama döngüsünü alır:

| # | Dilim | İçerik | Bitiş ölçütü |
|---|---|---|---|
| **10a** | ~~Spike (ölçüm)~~ **TAMAMLANDI 2026-07-27** | Çakışma yok; kalite %92,9; RTF 0,44; eşik 0,8; embedding modeli seçildi | ✅ [Ölçüm raporu](10a-diarization-spike-olcumu.md); S2 revize edildi |
| **10b** | **Diarization + etiketli transkript** | (1) **Canlı oturum sesinin saklanması** (`SaveAudioAsync` bağlantısı — bugün yok, engelleyici); (2) Diarization dilimi + `IDiarizer`; (3) `SpeakerId` + `SpeakerAligner`; (4) arka plan geçişi ve rapor güncelleme akışı; (5) depolama; (6) rapor UI'da etiketli transkript | `dotnet build && dotnet test` yeşil **ve** kullanıcı gerçek toplantıda canlı doğruladı |
| **10c** | **Konuşmacı-başına duygu** | Konuşmacı başına füzyon, `SpeakerSummary`, konuşmacı bazlı rapor/agregasyon | Aynı ölçüt; 10b doğrulanmadan başlamaz |

---

## Riskler

| Risk | Etki | Azaltma |
|---|---|---|
| ~~Tek mikrofon, uzak alan~~ | — | **KAPANDI:** kullanıcının gerçek odasında ölçüldü → %92,9 doğruluk, 0,48 sn sınır sapması. Uzak alan bir engel çıkmadı |
| ~~sherpa-onnx native çakışması~~ | — | **KAPANDI:** iki yükleme sırasında da çakışma yok; üçü aynı süreçte gerçek çıkarım yaptı |
| **Canlı oturum sesi saklanmıyor** — `SaveAudioAsync` üretimde hiç çağrılmıyor | Arka plan geçişi çalışacak veri bulamaz → canlı modda özellik hiç çalışmaz | 10b'nin **ilk task'ı**; master D13 (Opus + şifreli, 7 gün) ile uyumlu kurulur |
| **Arka plan geçişi bittiğinde raporun sessizce değişmesi** | Kullanıcı eski ekran görüntüsüne bakıp karar verir | Durum göstergesi zorunlu; "hesaplanıyor / hazır / başarısız" görünür |
| **3+ konuşmacıda eşik 0,8 doğrulanmadı** — 10a kaydı 2 kişiydi | Kalabalık toplantıda yanlış konuşmacı sayısı | 10b'de 3-5 kişilik kayıtla doğrulanır; gerekirse eşik yeniden kalibre edilir |
| Örtüşen konuşma (10a'da hiç ölçülmedi — kayıtta örtüşme yoktu) | Yanlış konuşmacıya duygu atfı | S6: `Bilinmeyen`, duyguya sayılmaz; 10b'de gerçek örtüşmeli kayıtla sınanmalı |
| RTF 0,44 → uzun toplantıda arka plan işi çok uzun sürer | Konuşmacılar saatlerce "hesaplanıyor" kalır | int8 fayda vermedi (ölçüldü); 10b'de üst süre sınırı + kullanıcıya dürüst bildirim |
| Kalite ölçülmeden "bitti" denmesi (**08e dersi**) | Yeşil test, kırık ürün | 10a ve 10b'ye "kullanıcı canlı doğrulamadan task bitmez" adımı zorunlu |

## Kapsam dışı (YAGNI)

- Toplantılar arası kalıcı ses profili / otomatik kişi tanıma (S4 gereği).
- Örtüşen konuşmada iki konuşmacıyı ayrıştırma (kaynak ayırma).
- Tam eval-09 altyapısı — 10a tek seferlik ölçüm scripti kullanır, kapsam patlamasın diye.

## Açık uçlar

- ~~Eşik kalibrasyonu~~ **KAPANDI:** `ClusteringThreshold = 0.8`, `MinSegmentSeconds = 4.0` (10a).
- ~~Hangi embedding modeli~~ **KAPANDI:** 3D-Speaker ERes2Net seçildi; WeSpeaker CAM++ ölçümde
  hiçbir bölge uzunluğunda ayıramadı ve elendi (10a).
- **Arka plan geçişinin akışı** (ne zaman başlar, iptal edilebilir mi, uygulama kapanırsa ne
  olur, rapor nasıl güncellenir) → 10b'de tasarlanacak; S2 revizyonunun getirdiği yeni soru.
- **3+ konuşmacı ve örtüşen konuşma davranışı** → 10b'de gerçek kayıtla doğrulanacak.
- Diarization kalitesinin (DER) sürekli izlenmesi → eval-09 uygulandığında oraya taşınır.
