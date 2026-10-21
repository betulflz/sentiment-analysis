# Canlı Görüşme Karar Destek Sistemi — Tasarım

> **Durum:** Taslak (brainstorm çıktısı) · **Tarih:** 2026-10-21
> **Bağlam:** Karar Destek Sistemleri dersi projesi. Hoca iki şey istedi: (1) **görüntü** eklenmesi,
> (2) projenin bir **karar destek sistemine (KDS)** dönüşmesi. Teslim: ~2 hafta (≈ 2026-11-04);
> çalışan canlı demo + PPTX sunum + tez formatında rapor. Rapor ve sunum bu belgenin kapsamı
> dışındadır ama bu belge onların ana kaynağıdır.
> **Üst belge:** [`2026-07-24-sesli-duygu-analizi-master-design.md`](2026-07-24-sesli-duygu-analizi-master-design.md)
> — oradaki ilkeler (lokal çalışma, dürüst belirsizlik, gizlilik varsayılanı) aynen geçerlidir.

---

## 1. Niyet ve neden

Mevcut sistem sesten "kim, ne, nasıl bir duyguyla konuştu" sorusunu cevaplıyor — bu bir
**analiz aracı**. KDS olması için çıktının bir **kararı** desteklemesi gerekir: bir karar verici,
seçenekler, kriterler, ağırlıklar, seçenekleri sıralayan bir model ve "ya ağırlıklar farklı
olsaydı" sorusu.

**Ürün cümlesi:** Görüşmeyi yöneten kişiye (satışçı, mülakatçı, toplantı yöneticisi), görüşme
SIRASINDA "şu an hangi eylemi yapmalıyım?" ve görüşme SONRASINDA "bu görüşmenin ardından ne
yapmalıyım?" sorularında, sesten + metinden + yüzden ölçülen göstergelere dayanan, gerekçeli ve
ağırlıkları ayarlanabilir öneriler sunan lokal bir masaüstü sistemi.

**Neden bu kurgu (öğrenci-izleme değil):** Kullanıcı öğrenci senaryosundan bilinçli vazgeçti.
Ayrıca AB Yapay Zekâ Yasası eğitim ve iş yerinde duygu tanımayı yasaklar; bu proje bunu raporda
"etik ve yasal sınırlar" olarak işler ve sistemi **kişiyi değil görüşmeyi yöneten kişinin kendi
eylemini** destekleyecek şekilde konumlar (bkz. §4 anti-hedefler).

## 2. Kullanıcı ve karar

| | Görüşme sırasında (canlı) | Görüşme sonrasında (rapor) |
|---|---|---|
| **Karar verici** | Görüşmeyi yöneten kişi | Aynı kişi |
| **Karar** | Şu an hangi eylem? | Takip adımı ne olmalı? |
| **Seçenekler** | Eylem listesi (§6.3) | Profile özgü takip listesi (§6.6) |
| **Sıklık** | Her analiz penceresinde (mevcut chunk süresi) | Görüşme başına bir kez |

**Görüşme profilleri** (üçü de desteklenir, kullanıcı test videolarını kendisi bulacak):
**Satış/Müşteri**, **Mülakat**, **Toplantı**. Profil = kriter ağırlıkları + eylem listesi + takip
listesi. Kullanıcı görüşme başında profili seçer.

## 3. Hedefler

1. **Görüntü kanalı:** Video dosyasından ve kameradan kare alıp yüz bulma, yüz ifadesi (8 duygu)
   ve baş yönü/kameraya bakma göstergesi üretmek.
2. **Üç kaynaklı duygu:** Mevcut metin + ses füzyonuna yüzü üçüncü kaynak olarak eklemek
   (kalite ağırlıklı — yüz görünmüyorsa yüzün ağırlığı düşer, dürüst belirsizlik korunur).
3. **KDS motoru (saf, deterministik, test edilebilir):** göstergeler → ihtiyaç puanları →
   AHP ile kriter ağırlıkları → TOPSIS ile eylem sıralaması → Türkçe gerekçe.
4. **Canlı ekran:** video önizlemesi, kişi başına göstergeler, en iyi 1–2 öneri kartı (gerekçeli),
   profil seçimi, ağırlık kaydırıcıları (canlı "ya şöyle olsaydı").
5. **Görüşme sonrası rapor:** kritik anlar, verilen öneriler zaman çizelgesi, konuşma dengesi
   (mevcut konuşmacı ayrımından), takip kararı sıralaması ve **duyarlılık analizi**
   (ağırlıklar ±%10–20 oynayınca sıralama değişiyor mu).
6. **Demo güvenliği:** Video dosyası modu birincil ve sağlam; kamera modu ikincil.
7. **Video dosyası gerçek hızda oynar** (kullanıcı kararı): görüntü ve analiz zamanla eşzamanlı
   ilerler, öneri kartı görüşme boyunca değişir. Ses hoparlöre verilmez (sessiz önizleme). Bunun
   için bir oynatma saati / hız ayarlama katmanı gerekir. Mevcut ses dosyası modunun "olabildiğince
   hızlı" davranışı değişmez.
8. **Kamera karşı tarafı çeker** (kullanıcı kararı): canlı modda dizüstü bilgisayar, görüşülen
   kişiye dönük durur (ör. yüz yüze mülakat). Göstergeler karşı tarafı ölçer. Öneriler yine
   görüşmeyi yönetenin eylemi hakkındadır.

## 4. Hedef dışı ve anti-hedefler

**Hedef dışı (bu 2 haftada yok):**
- Zoom/Teams penceresinden ekran yakalama (ScreenCaptureKit — ertelenmiş 05b). Canlı mod =
  dizüstü kamera + mikrofon.
- Yüz ↔ konuşmacı eşleştirme (hangi yüzün konuştuğunu bulma / aktif konuşmacı tespiti).
- Canlı konuşmacı ayrımı. Mevcut diarization yalnızca görüşme sonrası çalışıyor (10b, RTF ~0,44);
  **canlı modda "kim ne kadar konuştu" göstergesi YOK**, yalnızca raporda var.
- Kimlik tanıma / kişiyi oturumlar arası hatırlama.
- Bulut/LLM kullanımı. Windows desteği.
- Model eğitimi/ince ayarı (hazır ONNX modeller kullanılır).

**Anti-hedefler (teknik olarak çalışsa bile başarısızlık sayılır):**
- Sistem bir **kişi hakkında hüküm** verirse: "aday yalan söylüyor", "işe alma", "bu müşteri
  dürüst değil". Öneriler daima görüşmeyi yönetenin **kendi eylemi** hakkındadır; mülakat
  profilinde takip seçenekleri süreç adımlarıdır, işe alım kararı değildir.
- Gerekçesiz öneri. Her öneri, kendisini doğuran en etkili 1–2 göstergeyi Türkçe söylemelidir.
- Veri kalitesi düşükken (yüz yok, ses sessiz) emin görünmek. Güven düşükse kart bunu söyler.
- **Görüntünün diske yazılması.** Kareler bellekte işlenir ve atılır; yalnızca türetilmiş
  göstergeler saklanır (master gizlilik varsayılanı).
- Demo anında takılan/çöken canlı ekran — demo bunun için çok önemli değil ama "çalışır vaziyette
  denenecek"; video dosyası modu her zaman yedek yol olmalı.

## 5. Kısıtlar

- **Donanım:** M1 MacBook Air 8 GB. Whisper turbo zaten ağır; görüntü hattı hafif olmalı
  (hedef ≤ 5 fps alma, analiz 2 fps yeterli). Bütçe spike'ta ölçülür, tahmin edilmez.
- **Yığın:** .NET + Avalonia + ONNX Runtime (mevcut). Yeni harici bağımlılık: **ffmpeg** (brew;
  makinede şu an kurulu değil).
- **Süre:** ~2 hafta. Hızlı iterasyon; saf parçalarda hafif review.
- **Doğrulama kuralı (08e dersi):** UI/native dilimlerde (kamera, canlı ekran) task, kullanıcı
  gerçek donanımda görmeden bitmiş sayılmaz.
- **Dal:** Mevcut `10d-konusmaci-ayrimi` dalının ucundan yeni dal açılır (10d + 08e'nin main'e
  birleşme durumu ilk planlama adımında netleştirilir).

## 6. Nasıl — kararlaştırılanlar

### 6.1 Mimari (raporda Sprague–Carlson KDS yapısına eşlenir)

```
[Video dosyası | Kamera]──ffmpeg──► kareler ─► Yüz analizi (YuNet + FER+) ─┐
[Video dosyası | Mikrofon]────────► ses ─► mevcut hat (VAD, STT, metin, prozodi) ─┤
                                                                     ▼
                                   Pencere göstergeleri (kişi/yüz başına + genel)
                                                                     ▼
            KDS MODEL TABANI: ihtiyaç puanları → AHP ağırlıkları → TOPSIS → gerekçe
                                                                     ▼
            DİYALOG (UI): canlı öneri kartı · profil · kaydırıcılar · rapor · duyarlılık
            VERİ TABANI: SQLite (göstergeler, öneriler, takip kararı) — görüntü YOK
```

| KDS bileşeni | Bu projede |
|---|---|
| Veri yönetimi | Ses/görüntü yakalama + SQLite (mevcut 07 + yeni tablolar) |
| Model yönetimi | Duygu modelleri (ONNX) + KDS motoru (kural + AHP + TOPSIS) |
| Bilgi tabanı | Eylem etki matrisi + profil AHP karşılaştırmaları (uzman bilgisi) |
| Kullanıcı arayüzü | Avalonia canlı ekran, rapor, ayarlar |

### 6.2 Görüntü kanalı

- **Kare alma:** `ffmpeg` alt süreci, `rawvideo rgb24`, sabit boyut (ör. 640×360), düşük fps.
  Video dosyasında aynı dosyadan ses de alınır (mevcut dönüştürücü düzeni). Kamerada
  `-f avfoundation`; ses mevcut `MicrophoneCaptureSource`'tan gelir. Mevcut
  `IExternalProcessRunner` / `AudioToolResolver` deseni izlenir.
- **Yeni soyutlama:** `IVideoCaptureSource` → `IAsyncEnumerable<VideoFrame>` (zaman damgası mutlak,
  sesle aynı eksen). `IAudioCaptureSource`'un iptal sözleşmesi aynen uygulanır (asla bloklanmaz).
- **Yüz bulma:** YuNet ONNX (kutu + 5 nokta). 5 noktadan kaba **baş yönü** (yaw/pitch) ve
  "kameraya dönük mü" türetilir.
- **Yüz ifadesi:** FER+ ONNX (64×64 gri, 8 sınıf: nötr, mutlu, şaşkın, üzgün, kızgın, iğrenmiş,
  korkmuş, küçümseyen). Sınıf olasılıkları sabit Russell koordinatlarıyla **valence-arousal**'a
  çevrilir → mevcut `EmotionWheel` ile uyumlu.
- **Yüz takibi:** Basit kutu örtüşmesi (IoU) / merkez yakınlığı ile kareler arası "Yüz 1, Yüz 2"
  etiketleri, oturum içi. Kimlik yok.
- **Ön işleme** (kırp, gri, yeniden boyutlandır) elle C# ile yapılır; yeni görüntü kütüphanesi
  ancak spike gerektirirse eklenir.

### 6.3 Göstergeler (kriterler) ve eylemler

Her analiz penceresinde, her yüz için ve genel olarak hesaplanır; hepsi 0..1:

| Kod | Gösterge | Kaynak |
|---|---|---|
| K1 | **Gerilim** | yüksek arousal + negatif valence (ses + yüz) |
| K2 | **İlgi kaybı** | görünen yüzlerin kameradan dönük olma oranı (yüzün hiç görünmemesi K2'yi artırmaz, yalnızca güveni düşürür — kurgulanmış videolarda kamera geçişleri yüzü gizler) |
| K3 | **Olumsuz duygu** | üç kaynaklı füzyon valence'ı (negatif yön) |
| K4 | **Uyumsuzluk** | söz–ton–yüz çelişkisi (mevcut `Flagged` + yüz–ses farkı) |
| K5 | **Düşük enerji** | düşük arousal + düşük konuşma oranı (VAD) |
| K6 | **Kötüleşme eğilimi** | son N pencerede valence eğimi (negatif) |

**Eylem seçenekleri (canlı):** A1 Devam et · A2 Konuyu değiştir · A3 Açık uçlu / netleştirici
soru sor · A4 Empati kur, sakinleştir · A5 Kısa ara ver · A6 Kapanışa geç. Profil, listeden
eylem çıkarabilir veya isim/metin uyarlayabilir.

### 6.4 KDS modeli

1. **İhtiyaç vektörü** `n_j` = pencerenin K1..K6 değerleri (veri kalitesiyle çarpılmış).
   *(Builder kararı)* **Birden çok yüz:** TOPSIS pencere başına **bir kez** çalışır. Yüze bağlı
   kriterlerde (K1, K2, K3, K4) her kriter için yüzler arasındaki **en yüksek ihtiyaç** alınır
   (en kötü durumdaki kişi kararı sürükler) ve o değeri veren yüz gerekçede adıyla anılır. Ses ve
   metin bir yüze bağlanamadığı için genel katkı olarak her yüzün değerine aynı biçimde eklenir.
   Test tabanı tek ve iki yüzlü videodur; ızgara görünümü aynı kuralla çalışır.
2. **Bilgi tabanı:** sabit **etki matrisi** `E[eylem][kriter]` (0..1; "bu eylem bu sorunu ne kadar
   giderir"). A1 "Devam et" için etki, ihtiyaçlar düşükken yüksek olacak şekilde tanımlanır
   (ör. `1 − n_j`).
3. **Karar matrisi** `x_ij = E_ij · n_j` (A1 için özel tanım).
4. **Ağırlıklar** `w_j`: her profil için önceden doldurulmuş **AHP ikili karşılaştırma matrisi** →
   özvektör ağırlıkları, **tutarlılık oranı CR < 0,10** kontrolü (raporda gösterilir). Kullanıcı
   kaydırıcılarla doğrudan ağırlıkları değiştirebilir (normalize edilir).
5. **TOPSIS:** vektör normalizasyonu → ağırlıklı → ideal / negatif ideal → yakınlık katsayısı
   `C_i` → sıralama. En iyi 1–2 eylem gösterilir.
6. **Gerekçe:** Önerilen eylemin puanına en çok katkı veren 1–2 kriter + kişi + süre, Türkçe
   şablonla ("Yüz 2'de gerilim son 2 dakikada arttı ve ilgi düşük").
7. **Kararlılık:** Öneri her pencerede zıplamasın diye histerezis (yeni öneri, mevcudu belirgin
   bir farkla geçmeli veya art arda 2 pencere önde olmalı).
8. **Güven:** Veri kalitesi düşükse (yüz yok, VAD sessiz) kart "düşük güven" der; A1 varsayılan.

Motor **saf ve deterministiktir** (mevcut Fusion katmanı gibi): girdi → çıktı, I/O yok, TDD ile
yazılır. AHP ve TOPSIS ders kitabı örnekleriyle (bilinen sayısal sonuçlarla) test edilir.

### 6.5 Canlı ekran

Mevcut Canlı görünüme eklenir: video önizlemesi (yüz kutuları ve etiketleriyle), yüz başına
gösterge çubukları, **öneri kartı** (eylem + gerekçe + güven), profil seçimi, ağırlık
kaydırıcıları (değişince sıralama anında yeniden hesaplanır). Kaynak seçimi: Dosya (video) /
Kamera + Mikrofon.

### 6.6 Görüşme sonrası rapor ve takip kararı

Mevcut Rapor görünümüne eklenir:
- Göstergelerin zaman çizelgesi + o anda verilen öneriler + kritik anlar.
- Konuşma dengesi (mevcut konuşmacı ayrımından).
- **Takip kararı:** görüşme düzeyindeki göstergeler üzerinden aynı AHP + TOPSIS motoru,
  profile özgü seçeneklerle. *(Builder kararı)* Başlangıç ağırlıkları, görüşmenin sonundaki
  kaydırıcı değerleridir. Raporda kaydırıcılar yeniden ayarlanabilir ve sıralama anında yeniden
  hesaplanır. Saklanan veriler: profilin AHP ağırlıkları ve görüşme sonundaki ağırlıklar. Seçenekler:
  - Satış: Teklif gönder · İkinci görüşme ayarla · İtirazları ele alan bilgi gönder · Beklet
  - Mülakat (süreç adımları): Değerlendirmeye geç · Ek/teknik görüşme planla · Görüşmeyi tekrarla
    (veri/görüşme kalitesi düşük)
  - Toplantı: Karar e-postası gönder · Birebir görüşme ayarla (gerilimli kişi) · Takip toplantısı
- **Duyarlılık analizi:** her ağırlık ±%10 ve ±%20 değiştirildiğinde en iyi seçenek değişiyor mu
  (tablo); kararın ne kadar sağlam olduğunu gösterir.

### 6.7 Depolama

Yeni SQLite tabloları (mevcut idempotent şema geçişi deseniyle): pencere göstergeleri (yüz
başına), gösterilen öneriler, oturum profili + kullanılan ağırlıklar, takip kararı sonucu.
Görüntü/kare saklanmaz.

## 7. Öncelik ve kesme sırası (2 hafta)

| Sıra | Dilim | Kesilebilir mi? |
|---|---|---|
| 0 | **Spike:** ffmpeg kurulumu, dosya + kamera kare alma, YuNet + FER+ girdi/çıktı kontratı ve M1 hızı, kamera izni (.app bundle, `NSCameraUsageDescription`, ffmpeg alt sürecine izin geçiyor mu) | Hayır |
| 1 | Video dosyası → yüz analizi → pencere göstergeleri, gerçek hızda oynatma saati | Hayır |
| 2 | KDS motoru (AHP, TOPSIS, gerekçe, histerezis) — saf, TDD | Hayır |
| 3 | Canlı ekran: önizleme, öneri kartı, profil, kaydırıcılar | Hayır |
| 4 | Rapor: takip kararı + zaman çizelgesi | Hayır |
| 5 | Duyarlılık analizi tablosu | Evet |
| 6 | Kamera canlı modu | Evet (yedek: video dosyası) |
| 7 | Çoklu yüz takibi (tek yüze düşülebilir) | Evet |

Spike, kamera izninin ffmpeg alt sürecinde çalışmadığını gösterirse kamera yolu AVFoundation
P/Invoke'a geçer ya da kesilir — karar spike sonrası.

## 8. Builder'a bırakılanlar

- Pencere süresi (mevcut chunk süresi mi, ayrı daha kısa bir görüntü penceresi mi).
- Etki matrisinin ve AHP matrislerinin sayısal değerleri (makul, belgelenmiş, CR < 0,10).
- FER+ sınıf → valence-arousal koordinatları; üç kaynaklı füzyonun ağırlık formülü (mevcut
  `QualityWeighting` desenine benzer).
- Histerezis eşikleri, gerekçe şablonlarının dili.
- Görsel tasarım ayrıntıları (mevcut Avalonia stiline uyulur).

## 9. Rapor ve sunum için notlar (kapsam dışı ama besleyen)

Rapor bölümleri bu belgeden çıkar: problem ve KDS tanımı, literatür (çok kriterli karar verme,
AHP, TOPSIS, çok kipli duygu tanıma), mimari (§6.1 tablosu), model (§6.4 formülleri, CR),
uygulama, deney (üç profil için test videoları, duyarlılık), etik/yasal sınırlar (AB YZ Yasası,
gizlilik), kısıtlar ve gelecek çalışma (ekran yakalama, aktif konuşmacı tespiti, kalibrasyon).
