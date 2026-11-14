# Alt-Proje 11a Uygulama Planı — Görüntü Spike'ı (Ölçüm)

> **Agentic worker'lar için:** GEREKLİ ALT-BECERİ: Bu planı task-task uygulamak için
> `superpowers:subagent-driven-development` (önerilen) veya `superpowers:executing-plans`
> kullanın. Adımlar takip için checkbox (`- [ ]`) sözdizimi kullanır.

**Hedef:** Görüntü kanalının bu makinede gerçekten çalışıp çalışmadığını **ürün kodu yazmadan
önce ölçmek**: ffmpeg ile kare alma (dosya + kamera), kamera izni, YuNet ve FER+ ONNX
kontratları ve M1 üzerindeki hız/bellek bütçesi. 11b'ye GEÇ/GEÇME kararlarını sayılara dayandırmak.

**Architecture:** Spike: ürün kodu üretmez. `scratchpad/video-spike/` altında geçici bir .NET
konsol projesi (repoya girmez) ölçümleri yapar; kalıcı tek çıktı ölçüm raporudur. Modeller
mevcut model klasörüne iner ve 11b'de kullanılır.

**Tech Stack:** .NET 9 · `Microsoft.ML.OnnxRuntime` 1.28.0 (projedeki sürüm) · ffmpeg (Homebrew) ·
YuNet `face_detection_yunet_2023mar.onnx` (OpenCV Zoo) · FER+ `emotion-ferplus-8.onnx` (ONNX Model Zoo) ·
macOS arm64 (MacBook Air M1, 8 GB)

**Spec:** [`2026-10-21-canli-gorusme-karar-destek-design.md`](../specs/2026-10-21-canli-gorusme-karar-destek-design.md) §5, §6.2, §7 satır 0

## Global Kısıtlar

- **Ürün koduna DOKUNULMAZ** (`src/`, `tests/`). Task 1'de mevcut test sayısı kaydedilir, Task 6'da aynı çıkmalı.
- Spike `scratchpad/video-spike/` altında yaşar (`.gitignore` kapsamında) ve Task 6'da silinir.
- **Ölçülen sayılar rapora AYNEN yazılır**; beklenen değer değil. Kötüyse kötü yazılır.
- **Her ölçüm bir karşılaştırmadır (08d dersi):** yüzlü vs yüzsüz kare, düz bakış vs dönük baş,
  gülümseme vs nötr vs kaşlar çatık, izin verildi vs reddedildi. Tek koşuya ve mutlak eşiğe güvenilmez.
- **Görüntü diske yazılmaz** — spike'ta bile yalnızca sayılar ve gerektiğinde tek bir hata ayıklama
  karesi (`scratchpad/` içinde, Task 6'da silinir).
- Modeller `~/Library/Application Support/SentimentAnalysis/models/` altına iner.
- Kullanıcı tercihi: her task sonunda DUR, sonucu kısaca bildir, onay al.

## Review Focus

1. **Kamera izni reddedildiğinde** ffmpeg hata mı verir, yoksa siyah/donuk kare mi akıtır? (Mikrofonda
   izin reddi SESSİZDİ.) Beklenen: rapor hangisi olduğunu ve nasıl tespit edileceğini söyler — Task 3.
2. **ffmpeg kurulu değilken / yol bulunamazken** davranış — 11b'de anlamlı hata mesajı için gereken
   bilgi (Homebrew yolu `/opt/homebrew/bin/ffmpeg`, `.app` içinden PATH görünmez) — Task 2.
3. **Video dosyasında ses yolu yoksa veya video çözünürlüğü/en-boy oranı farklıysa** (dikey telefon
   videosu) kare boyutu ve harf kutusu davranışı — Task 2.
4. **Küçük/uzak yüz** (ızgara görünümlü toplantı videosu): YuNet'in kaç piksel altında yüzü kaçırdığı — Task 4.
5. **Whisper aynı anda çalışırken** görüntü hattının bellek tepe noktası (8 GB'ta takas) — Task 5.

## Kullanıcıdan istenen girdi (Task 2 öncesi)

- `scratchpad/video-spike/kendim.mp4` — dizüstü kamerasıyla ~40 sn: 10 sn düz bakış + nötr yüz,
  10 sn başı sağa/sola çevirme, 10 sn gülümseme, 10 sn kaşları çatma. Hangi saniyede ne yapıldığı
  `scratchpad/video-spike/referans.txt` içine yazılır (`başlangıç_sn bitiş_sn etiket`).
- `scratchpad/video-spike/iki-kisi.mp4` — internetten bulunmuş, 2+ kişinin göründüğü 1–2 dk'lık bir
  görüşme/mülakat videosu (üç profilden biri; ızgara görünüm tercih edilir).

## Dosya Yapısı

| Dosya | Sorumluluk | Task |
|---|---|---|
| `scratchpad/video-spike/VideoSpike/VideoSpike.csproj` | Geçici konsol projesi | 2 |
| `scratchpad/video-spike/VideoSpike/Program.cs` | Ölçüm modları (`args[0]`: `file`, `camera`, `yunet`, `ferplus`, `budget`) | 2–5 |
| `scratchpad/video-spike/CamProbe.app` | Kamera izni için minimal imzalı bundle | 3 |
| `docs/superpowers/specs/subprojects/11a-goruntu-spike-olcumu.md` | **Kalıcı ölçüm raporu** | 6 |

---

### Task 1: Başlangıç durumu ve kurulum

- [ ] **Step 1:** `dotnet test` çalıştır; geçer/atlanır/başarısız/uyarı sayılarını not et (Task 6'da karşılaştırılacak).
- [ ] **Step 2:** `brew install ffmpeg`; `ffmpeg -version` ilk satırını ve tam yolunu (`which ffmpeg`) not et.
- [ ] **Step 3:** Modelleri indir:
  - `https://github.com/opencv/opencv_zoo/raw/main/models/face_detection_yunet/face_detection_yunet_2023mar.onnx`
  - `https://github.com/onnx/models/raw/main/validated/vision/body_analysis/emotion_ferplus/model/emotion-ferplus-8.onnx`
  Dosya boyutlarını ve SHA-256'larını not et (rapora girer). İndirilen dosya HTML/LFS işaretçisi
  çıkarsa (birkaç KB) `?raw=true` / `media.githubusercontent.com` yolunu dene.
- [ ] **Step 4:** Kullanıcıdan iki videoyu iste; gelmeden Task 2'ye geçme.

### Task 2: Dosyadan kare alma

- [ ] **Step 1:** `VideoSpike` konsol projesini kur (net9.0, OnnxRuntime 1.28.0).
- [ ] **Step 2:** `file` modu: ffmpeg'i alt süreç olarak başlat ve stdout'tan kare oku:
  `ffmpeg -hide_banner -loglevel error [-re] -i <dosya> -vf "fps=5,scale=640:360:force_original_aspect_ratio=decrease,pad=640:360:(ow-iw)/2:(oh-ih)/2" -pix_fmt rgb24 -f rawvideo pipe:1`
  Kare boyutu = 640·360·3 bayt; tam kare dolana kadar oku.
  İki koşu ölç: `-re` ile (gerçek hız — spec §3.7) ve `-re` olmadan.
- [ ] **Step 3:** Ölç ve not et: ilk kare gecikmesi, ortalama kare aralığı (`-re` ile 200 ms ± ne kadar),
  toplam kare sayısı vs `video süresi × 5`, ffmpeg süreci CPU %'si (`ps -o %cpu`), dikey bir videoda
  (kullanıcının telefonundan veya `kendim.mp4` döndürülerek) harf kutusu doğru mu.
- [ ] **Step 4:** Aynı dosyadan sesi çıkar: `ffmpeg -i <dosya> -ac 1 -ar 16000 -f s16le pipe:1`. Ses
  süresi ile kare sayısından türeyen süre arasındaki fark (ms) — senkron için esas. Ses yolu olmayan
  bir dosyada (`-an` ile üretilmiş kopya) ffmpeg'in çıkış kodu ve stderr'i.
- [ ] **Step 5:** ffmpeg yokken (`PATH`'ten çıkarılmış ortamda) `Process.Start` hatası ne; `.app`
  içinden çalışırken PATH'te `/opt/homebrew/bin` görünüyor mu (Task 3'teki bundle'da da kontrol).
- [ ] **Step 6:** DUR — sonuçları bildir.

### Task 3: Kamera ve izin

- [ ] **Step 1:** `ffmpeg -f avfoundation -list_devices true -i ""` çıktısından kamera indeksini not et.
- [ ] **Step 2:** `camera` modu: `ffmpeg -f avfoundation -framerate 30 -video_size 1280x720 -i "0:none" -vf "fps=5,scale=640:360" -pix_fmt rgb24 -f rawvideo pipe:1`. Önce terminalden çalıştır (izin terminale atfedilir).
- [ ] **Step 3:** `CamProbe.app` kur: spike'ın publish çıktısı + `Info.plist` (`NSCameraUsageDescription`,
  `CFBundleIdentifier` = `com.sentimentanalysis.camprobe`) + ad-hoc `codesign`
  (`build-macos-app.sh` deseni). `open` ile başlat, sonucu bir log dosyasına yazsın.
  Sorular: izin istemi çıkıyor mu, izin **uygulamaya** mı atfediliyor (ffmpeg alt sürecine geçiyor mu)?
- [ ] **Step 4:** Karşılaştırma: Sistem Ayarları'ndan izni kaldır (`tccutil reset Camera com.sentimentanalysis.camprobe`),
  reddet, tekrar çalıştır. Hata mı, siyah kare mi (kare ortalama parlaklığı ~0), donuk kare mi
  (ardışık karelerin farkı ~0)? Tespit yöntemini öner.
- [ ] **Step 5:** Kamera açılış gecikmesi (ilk kareye kadar ms) ve CPU %'si.
- [ ] **Step 6:** Karar notu: ffmpeg alt süreci kamerada çalışmıyorsa → AVFoundation P/Invoke maliyeti
  kabaca ne / kamera modu kesilsin mi (spec §7). DUR — bildir.

### Task 4: YuNet ve FER+ kontratları

- [ ] **Step 1:** Her iki model için girdi/çıktı adlarını, şekillerini ve tiplerini `InferenceSession.InputMetadata/OutputMetadata`'dan yazdır. YuNet girdisi sabit mi (640×640) yoksa dinamik mi?
- [ ] **Step 2:** `yunet` modu: kareyi model girdisine uyarla (gerekiyorsa 640×640'a sığdır, BGR sırası,
  0–255 float, NCHW). Çıktıyı çöz — OpenCV `FaceDetectorYN` mantığı (doğrulanacak):
  ```
  her adım s ∈ {8,16,32}, ızgara (r,c) için:
    skor = sqrt(clamp(cls)·clamp(obj))
    cx=(c+bbox0)·s  cy=(r+bbox1)·s  w=exp(bbox2)·s  h=exp(bbox3)·s
    nokta_k = ((c+kps[2k])·s, (r+kps[2k+1])·s), k=0..4 (sağ göz, sol göz, burun, ağız sağ, ağız sol)
  skor > 0.6 → NMS (IoU 0.3)
  ```
  Doğrulama: bir karede kutu + 5 noktayı çizip `scratchpad/`'e tek bir PNG yaz ve gözle kontrol et.
- [ ] **Step 3:** Karşılaştırmalar (`referans.txt`'e göre): yüzlü vs yüzsüz karelerde tespit oranı;
  düz bakış vs dönük baş için 5 noktadan kaba yaw (`(burun_x − göz_orta_x) / göz_arası`) dağılımı —
  iki grup ayrışıyor mu, önerilen eşik; `iki-kisi.mp4`'te tespit edilen yüz sayısı ve tespit edilen
  en küçük yüz yüksekliği (piksel).
- [ ] **Step 4:** `ferplus` modu: yüz kutusunu %10–20 büyüt, kırp, gri (`0.299R+0.587G+0.114B`), 64×64,
  değer aralığı 0–255 (FER+ normalize ETMEZ — doğrulanacak), girdi `[1,1,64,64]`. Çıktı 8 logit → softmax.
  Sınıf sırası: nötr, mutlu, şaşkın, üzgün, kızgın, iğrenmiş, korkmuş, küçümseyen.
- [ ] **Step 5:** Karşılaştırma: nötr / gülümseme / kaşlar çatık bölümlerinde ortalama olasılık
  tablosu (bölüm × sınıf). Gülümseme bölümünde "mutlu" baskın mı; kaşlar çatıkta kızgın/üzgün
  nötrden ayrışıyor mu? Ayrışmıyorsa açıkça yaz (yüz kanalının ağırlığını 11b'de düşürmek gerekir).
- [ ] **Step 6:** Kare başına süre: YuNet ms, FER+ ms/yüz (ilk çağrı ayrı; sonraki 50 çağrının
  medyanı ve p95'i). DUR — bildir.

### Task 5: Birleşik bütçe

- [ ] **Step 1:** `budget` modu: `iki-kisi.mp4`'ü `-re` ile 5 fps al, karelerin her 2'sinden birini
  (2,5 fps) YuNet + FER+ ile işle; 60 sn koş. Kare gecikmesi birikiyor mu (işlenen kare zaman
  damgası − duvar saati farkının eğilimi)?
- [ ] **Step 2:** Aynı koşuyu, **eş zamanlı** olarak uygulamada (veya mevcut 10a/03 spike tarzında
  Whisper turbo ile) bir ses dosyası analiz edilirken tekrarla. Toplam süreç belleği (WorkingSet64
  ölçüm noktalarının maksimumu — macOS'ta `PeakWorkingSet64` 0 döner), `memory_pressure` durumu,
  görüntü hattının gecikme eğilimi, Whisper chunk süresinin ne kadar uzadığı.
- [ ] **Step 3:** Öneri: analiz fps'i (1 / 2 / 2,5), ONNX iş parçacığı sayısı (`IntraOpNumThreads`).
  DUR — bildir.

### Task 6: Rapor, temizlik, commit

- [ ] **Step 1:** `docs/superpowers/specs/subprojects/11a-goruntu-spike-olcumu.md` yaz. Bölümler:
  ortam ve sürümler · ffmpeg kare alma (dosya, `-re`, ses senkronu, hatalar) · kamera ve izin
  (reddedildiğinde ne olur + tespit yöntemi) · YuNet kontratı ve çözümleme (doğrulanmış hali) ·
  FER+ kontratı ve ayrışma tablosu · yaw eşiği · hız/bellek tablosu · **11b için GEÇ/GEÇME kararları**
  (kamera modu, analiz fps'i, yüz kanalının güvenilirliği) · beklenmedik bulgular.
- [ ] **Step 2:** `scratchpad/video-spike/` klasörünü sil (videolar dahil — kullanıcıya önceden sor,
  saklamak isterse taşı). Modeller model klasöründe kalır.
- [ ] **Step 3:** `dotnet test` — Task 1'deki sayılarla aynı.
- [ ] **Step 4:** Commit:
  ```bash
  git add docs/superpowers/specs/subprojects/11a-goruntu-spike-olcumu.md
  git commit -m "docs(11a): görüntü spike ölçüm raporu"
  ```

---

## Sonraki planlar (bu spike'ın raporu yazıldıktan sonra yazılır)

11b görüntü kanalı (video/kamera kaynağı, oynatma saati, yüz analizi, üç kaynaklı füzyon, pencere
göstergeleri) · 11c KDS motoru (AHP, TOPSIS, gerekçe, histerezis, takip kararı, duyarlılık — saf, TDD;
spike'a bağımlı değil, 11b ile paralel yazılabilir) · 11d canlı ekran · 11e rapor ekranı + depolama.
