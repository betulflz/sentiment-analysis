# 10a Diarization Spike Ölçümü

> Tarih: 2026-07-27 · Donanım: MacBook Air M1, 8 GB, macOS 26.3.1 · .NET 9.0.2 · osx-arm64
> · sherpa-onnx **1.13.4** · Microsoft.ML.OnnxRuntime 1.28.0 · Whisper.net 1.9.1

## Sonuç

**GEÇTİ (kayıtla)** — sherpa-onnx bu projede çalışıyor ve nihai diarization kalitesi iyi
(%92,9 doğruluk), ancak **hız canlı senaryoda kabul edilemez** (RTF 0,44) ve **canlı anlık
etiketleme güvenilir değil** (kısa bölgelerde ayrım yok). Bu iki bulgu S2 kararını değiştirdi.

## Kayıt

Kullanıcının gerçek ortamında, odadaki mikrofonla (kulaklık değil) alınmış Türkçe sohbet:
**120,57 sn, 2 konuşmacı, 15'er saniyelik sırayla 8 tur.** Opus/ogg → `afconvert` ile
16 kHz mono PCM16 WAV. Bayt muhasebesi birebir tutuyor (120,5735 × 16000 × 2 = 3.858.352).

Referans doğru (`referans.txt`) kullanıcı tarafından verildi — doğruluk ölçümü buna dayanıyor.

---

## 1. Native çakışma — YOK

Üç native runtime aynı süreçte, her iki yükleme sırasıyla:

| Yükleme sırası | Microsoft.ML.OnnxRuntime | sherpa-onnx | Whisper.net | Süreç | RSS |
|---|---|---|---|---|---|
| ORT → sherpa → whisper | ✅ (3 girdi) | ✅ (512 boyut) | ✅ | çökme yok | 208 MB |
| sherpa → ORT → whisper | ✅ | ✅ | ✅ | çökme yok | 262 MB |

**Yükleme sırası fark etmiyor** — 10b'de DI kayıt sırası için özel bir kısıt gerekmez.

### Yüklemek yetmez: gerçek çıkarım da doğrulandı

Sembol/sürüm çakışmaları çoğu zaman ilk gerçek çıkarımda patlar. Üçü de aynı süreçte, aynı
ses üzerinde gerçekten çalıştırıldı:

| Motor | Çıkarım | Sonuç |
|---|---|---|
| sherpa-onnx | ilk 15 sn'den embedding | 512 boyut, L2 norm 19,333 (sıfır/NaN değil) |
| Microsoft.ML.OnnxRuntime | Silero VAD | konuşma olasılığı 0,0151 (kaydın ilk 512 örneği = sessizlik) |
| Whisper.net | ilk 15 sn transkripsiyon (tr) | 3,0 sn'de doğru Türkçe metin üretti |

Gözlenen en yüksek RSS: **289 MB** — M1 8 GB için rahat.

**Sonuç: master D3/D4 escape-hatch'ine (saf-ONNX / Python sidecar) gerek yok; S3 kararı ayakta.**

---

## 2. Offline diarization doğruluğu

### Eşik taraması (gerçek konuşmacı sayısı: 2)

| `Clustering.Threshold` | 0,3 | 0,4 | 0,5 | 0,6 | 0,7 | **0,8** | **0,9** | 1,0 |
|---|---|---|---|---|---|---|---|---|
| Bulunan konuşmacı | 11 | 7 | 6 | 4 | 4 | **2** | **2** | 1 |
| Segment sayısı | 18 | 17 | 16 | 13 | 13 | 12 | 12 | 6 |

sherpa'nın tipik varsayılanı olan 0,5 **altı kat fazla** konuşmacı buluyor. Doğru bölge
**0,8–0,9**. Bu değer 10b'nin varsayılanı olmalı.

### Threshold 0,8'de doğruluk (referansla hesaplandı, gözle değil)

| Ölçüm | Sonuç |
|---|---|
| Konuşmacı sayısı | **2 / 2 doğru** |
| Doğru konuşmacıya atanan süre | **%92,9** (105,5 / 113,5 sn) |
| Konuşma kapsama oranı | %94,6 (kalan = sessizlik/geçiş, konuşma yok sayıldı) |
| Sınır isabeti (ortalama mutlak sapma) | **0,48 sn** |

Referans geçişi → en yakın tespit sınırı: 15→15,40 · 30→29,98 · 45→44,68 · 60→60,39 ·
75→74,94 · 90→**92,03** · 105→105,11. Yedi geçişin altısı ±0,4 sn içinde; yalnızca 90.
saniyedeki geçiş 2,03 sn geç yakalandı.

**Uzak alan, tek mikrofon, iki Türkçe konuşmacı için bu iyi bir sonuçtur.**

### Hız ve bellek — asıl sorun

| Ölçüm | Değer |
|---|---|
| 120,6 sn ses işleme süresi | **53,6 sn** |
| **RTF** | **0,444** |
| RSS | ~390 MB |

Bunun "Durdur → nihai geçiş" tasarımına maliyeti:

| Toplantı süresi | Durdur sonrası bekleme |
|---|---|
| 10 dk | ~4,5 dk |
| 30 dk | ~13 dk |
| 60 dk | ~27 dk |

### int8 segmentation modeli hızı ÇÖZMÜYOR

| Segmentation modeli | RTF | Doğruluk | Segment |
|---|---|---|---|
| `model.onnx` (fp32) | 0,444 | %92,9 | 12 |
| `model.int8.onnx` | 0,440 | %91,0 | 13 |

Hız kazancı yok, doğruluk biraz düşük. **Darboğaz segmentation değil, embedding çıkarımıdır.**
fp32 kullanılır.

---

## 3. Online kümeleme kalibrasyonu ve embedding modeli seçimi

Yöntem: her referans bölgesinden bir embedding çıkarılır; tüm çiftlerin kosinüs benzerliği
**aynı-konuşmacı** ve **farklı-konuşmacı** diye ikiye ayrılır. İki dağılım arasındaki BOŞLUK
(`aynı_min − farklı_maks`) pozitifse ayrım mümkündür. Mutlak eşiğe bakmak yerine karşılaştırma
kurulur (08d dersi).

| Embedding modeli | Bölge | AYNI (min) | FARKLI (maks) | BOŞLUK | Ayrılabilir mi |
|---|---|---|---|---|---|
| **3D-Speaker ERes2Net** | 1 sn | 0,097 | 0,300 | −0,203 | ✗ |
| **3D-Speaker ERes2Net** | 2 sn | 0,131 | 0,250 | −0,119 | ✗ |
| **3D-Speaker ERes2Net** | 4 sn | 0,413 | 0,387 | **+0,026** | ✓ (kıl payı), eşik ≈0,400 |
| **3D-Speaker ERes2Net** | tam (15 sn) | 0,679 | 0,403 | **+0,276** | ✓ eşik ≈**0,541** |
| WeSpeaker CAM++ | 1 sn | 0,637 | 0,922 | −0,285 | ✗ |
| WeSpeaker CAM++ | 2 sn | 0,722 | 0,897 | −0,176 | ✗ |
| WeSpeaker CAM++ | 4 sn | 0,798 | 0,908 | −0,111 | ✗ |
| WeSpeaker CAM++ | tam (15 sn) | 0,904 | 0,951 | −0,047 | ✗ |

**Seçilen model: 3D-Speaker ERes2Net** (`3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx`,
~38 MB, 512 boyut). CAM++ **hiçbir bölge uzunluğunda ayıramadı** — farklı konuşmacılara bile
0,9+ benzerlik verip tüm sesleri aynı gösteriyor; dosyası silindi.

> İlginç not: kazanan model Çince veriyle (3D-Speaker), kaybeden İngilizce VoxCeleb ile
> eğitilmiş. Eğitim dilinin Türkçe'ye yakınlığı belirleyici değilmiş — bu tam da tahmin
> yerine ölçmenin (master §12.4) karşılığını verdiği yer.

**Kalibre edilmiş değerler:**
- `SimilarityThreshold` = **0,54** (uzun bölge) / 0,40 (4 sn bölge)
- `MinSegmentSeconds` = **4,0** — bunun altında ayrım ölçülebilir değil

---

## Tasarıma etkisi: S2 DEĞİŞTİ

Ölçümler S2'nin ("canlı anlık geçici etiket + Durdur'da nihai düzeltme") **her iki yarısını da**
zorladı:

- **Canlı anlık etiket:** ayrım ancak >4 sn bölgelerde ve kıl payı marjla mümkün. Gerçek
  sohbette "evet / tamam / katılıyorum" gibi kısa dönüşler yaygındır; bunlar ya yanlış
  etiketlenir ya `Bilinmeyen` kalır. Üstelik ölçüm **kolay senaryoda** (düzenli 15 sn turlar)
  yapıldı ve marj yine de dardı.
- **Sonda düzeltme:** RTF 0,44 → 30 dk toplantıda ~13 dk donma. 08e'de tam bu yüzden drenaj
  sınırı konmuştu.

**Kullanıcı kararı (2026-07-27): S2 revize edildi** →
**canlı ekranda konuşmacı etiketi gösterilmez; Durdur anında rapor bugünkü gibi HEMEN üretilir,
diarization arka planda çalışır ve bitince rapor kendini günceller.**

Bunun sonuçları:
- `ISpeakerTracker`, `OnlineSpeakerClusterer` ve `SpeakerLabelReconciler` **10b kapsamından
  çıkar** (canlı geçiş yok → geçici etiket yok → uzlaştırma yok). Belirgin sadeleşme.
- `SpeechRegionDetector` (Vad dilimi) da gereksizleşir — nihai geçişte segmentasyonu
  pyannote yapıyor.
- Yeni gereksinim: **arka plan işi + rapor güncelleme akışı** ve kullanıcıya görünür durum
  ("konuşmacılar hesaplanıyor…" → "konuşmacılar hazır").
- `SimilarityThreshold` artık online kümeleme için değil, `Clustering.Threshold` olarak
  kullanılır ve değeri **0,8**'dir (0,54 değil — o online kümeleme içindi).

---

## Yakalanan tuzaklar

**1. Türkçe yerel ayarı ölçümü sessizce bozdu.** `float.Parse("0.5")` Türkçe kültürde
**5,00** üretti; eşik absürt yüksek olduğu için her şey tek konuşmacıda birleşti ve ilk koşu
"1 konuşmacı bulundu" dedi. Tek koşuya bakılsaydı **"diarization çalışmıyor"** yanlış hükmüne
varılırdı. Düzeltme: `CultureInfo.InvariantCulture`. 10b'de model/eşik ayarlarını string'den
okuyan her yer bu tuzağa açıktır.

**2. Planın API'si üç yerde yanlıştı** (masa başı araştırmasıyla yazılmıştı):
- `SherpaOnnx.WaveReader` NuGet paketinde **yok** (sherpa'nın dotnet *örneklerinde* kendi
  yazdıkları yardımcı sınıf). Elle 16-bit PCM mono WAV okuyucu yazıldı.
- Yerel fonksiyona `Whisper` adı verilemiyor — `Whisper.net` namespace'iyle çakışıyor (CS0119).
- macOS'ta `Process.PeakWorkingSet64` **her zaman 0 döner**; tepe bellek elle takip edilmeli.

**3. "Yükleme başarılı" yanıltıcı bir kanıttır.** İlk çakışma testi yalnızca üç kütüphaneyi
yüklüyordu. Gerçek çıkarım eklenmeseydi, 10b'de ilk model çağrısında patlayan bir çakışma
"ölçüldü, sorun yok" diye kayıtlara geçmiş olacaktı.

---

## 10b için sonuçlar

| Kalem | Değer / karar |
|---|---|
| Motor | sherpa-onnx 1.13.4 (S3 doğrulandı) |
| Segmentation modeli | `sherpa-onnx-pyannote-segmentation-3-0/model.onnx` (fp32; int8 fayda vermiyor) |
| Embedding modeli | 3D-Speaker ERes2Net 16k (CAM++ elendi) |
| `Clustering.Threshold` | **0,8** (varsayılan 0,5 altı kat fazla konuşmacı buluyor) |
| `MinSegmentSeconds` | 4,0 |
| Beklenen kalite | ~%93 doğruluk, ~0,5 sn sınır sapması (2 kişi, uzak alan) |
| Beklenen maliyet | RTF ~0,44, ~390 MB RSS |
| Canlı etiket | **YOK** (S2 revize) — arka planda hesaplanır, rapor sonradan güncellenir |

## Bu spike'ın cevaplamadığı sorular

- **3'ten fazla konuşmacıda davranış** — kayıt 2 kişiydi. Eşik 0,8'in 3-5 kişide de doğru
  sayıyı bulup bulmadığı **bilinmiyor**; 10b'de daha kalabalık bir kayıtla doğrulanmalı.
- **Örtüşen konuşma** — kayıt protokolü sırayla konuşmayı şart koşuyordu; hiç örtüşme yoktu.
  S6 (`Bilinmeyen` say) ölçülmedi, varsayım olarak kalıyor.
- **Whisper + diarization aynı chunk'ta toplam bütçe** — bileşen hızları ayrı ayrı ölçüldü,
  birlikte çalışırken toplam yük ölçülmedi.
- **Arka plan geçişinin UX'i** — S2 revizyonu yeni bir akış getirdi; 10b'de tasarlanacak.
