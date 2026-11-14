# 11a — Görüntü Spike Ölçüm Raporu

> **Tarih:** 2026-10-21 · **Plan:** [`2026-10-21-11a-goruntu-spike.md`](../../plans/2026-10-21-11a-goruntu-spike.md)
> · **Tasarım:** [`2026-10-21-canli-gorusme-karar-destek-design.md`](../2026-10-21-canli-gorusme-karar-destek-design.md)
>
> Tüm sayılar bu makinede ölçülmüştür. Beklenen değil, ölçülen değerlerdir.

## 1. Ortam

| | |
|---|---|
| Donanım | MacBook Air M1, 8 GB, macOS (Darwin 25.3) |
| .NET / ORT | net9.0 · Microsoft.ML.OnnxRuntime 1.28.0 · Whisper.net 1.9.1 |
| ffmpeg | 9.0.2_1 (Homebrew), `/opt/homebrew/bin/ffmpeg` |
| YuNet | `face_detection_yunet_2023mar.onnx`, 232 589 B, sha256 `8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4` |
| FER+ | `emotion-ferplus-8.onnx`, 35 040 571 B, sha256 `a2a2ba6a335a3b29c21acb6272f962bd3d47f84952aaffa03b60986e04efa61c` |
| Test videoları | `kendim.mov` (Photo Booth, 1080×720, 40,4 sn; 0–10 düz, 10–20 baş çevirme, 20–30 gülümseme, 30–40 kaş çatma) · `iki-kisi.mp4` (YouTube mülakat simülasyonu, 1280×720 25 fps, 77 sn, iki yüz yan yana). Repoda değil: `scratchpad/test-videolari/` |

Modeller `~/Library/Application Support/SentimentAnalysis/models/` altındadır.

## 2. Dosyadan kare alma

Komut: `ffmpeg -hide_banner -loglevel error [-re] -i <dosya> -vf "fps=5,scale=640:360:force_original_aspect_ratio=decrease,pad=640:360:(ow-iw)/2:(oh-ih)/2" -pix_fmt rgb24 -f rawvideo pipe:1` (kare = 691 200 bayt).

| | `-re` yok | `-re` (gerçek hız) |
|---|---|---|
| Kare sayısı (40,4 sn × 5) | 202 / 202 | 202 / 202 |
| Toplam süre | 1,5 sn | 39,9 sn |
| İlk kare | 183 ms | 154 ms |
| Kare aralığı | — | medyan 199,8 · p95 204,2 · maks 234,9 ms |
| ffmpeg CPU | %145–466 | %18–30 |

- **Ses:** `-ac 1 -ar 16000 -f s16le pipe:1` → 40,363 sn; kare süresi 40,4 sn → fark ≈ 37 ms. Senkron için yeterli.
- **Dikey video:** iki yana siyah bant eklenir, oran korunur (yapay `transpose` ile denendi; telefonun döndürme meta verisi ayrıca denenmedi).
- **Ses yolu olmayan dosya:** ses çıkarma **exit 234**, stderr `Output file does not contain any stream`. Sessizce boş dönmez.
- **Olmayan dosya:** exit 254, stderr `Error opening input: No such file or directory`.
- **PATH'te Homebrew yokken** çıplak `ffmpeg` → `Win32Exception: No such file or directory`.

## 3. Kamera ve izin

- Cihazlar: `[0] FaceTime HD Kamera`, `[1] iPhone Kamerası`, `[2] iPhone Masa Görüntüsü`, `[3] Capture screen 0`. **Sıra Continuity Camera ile değişebilir → kamera adıyla seçilmeli.**
- Varsayılan `yuv420p` desteklenmiyor uyarısı → `-pixel_format nv12` gerekli.
- Komut: `ffmpeg -f avfoundation -pixel_format nv12 -framerate 30 -video_size 1280x720 -i "0:none" -vf "fps=5,scale=640:360" -pix_fmt rgb24 -f rawvideo pipe:1`

| Durum | Sonuç |
|---|---|
| Terminalden | İlk kare 4 375 ms (kamera ısınması), sonra 200 ms aralık, CPU ~%10 |
| İmzalı `.app` (`NSCameraUsageDescription`) | İzin **uygulamaya** atfedilir; ffmpeg alt süreci kamerayı kullanır. İlk kare 7 505 ms (izin istemi dahil), CPU %7–10 |
| İzin istemi açıkken | ffmpeg **bloklanır**: 20 sn boyunca 0 kare, stderr boş |
| İzin reddedilmişken | **Anında exit 251**, stderr `Failed to create AV capture input device: Cannot use FaceTime HD Kamera` |

Mikrofonun aksine **sessiz siyah kare tuzağı yok**. `.app` `open` ile açıldığında PATH Homebrew'i içeriyordu, ama Finder'dan açılışta bu garanti değil.

## 4. Model kontratları

**YuNet:** girdi `input` float32 `[1,3,640,640]` **sabit** · çıktılar `cls_{8,16,32}` `[1,N,1]`, `obj_*` `[1,N,1]`, `bbox_*` `[1,N,4]`, `kps_*` `[1,N,10]` (N = 6400/1600/400). Doğrulanmış çözümleme:

```
girdi: 640x360 kare, altı sıfırla doldurulmuş 640x640; kanal sırası BGR; değerler 0–255 float; NCHW
her adım s ∈ {8,16,32}, ızgara r,c (cols = 640/s), i = r·cols + c:
  skor = sqrt(clamp01(cls)·clamp01(obj));  skor < 0.6 → at
  cx=(c+bbox0)·s  cy=(r+bbox1)·s  w=exp(bbox2)·s  h=exp(bbox3)·s
  nokta_k = ((c+kps[2k])·s, (r+kps[2k+1])·s); sıra: sağ göz, sol göz, burun, ağız sağ, ağız sol
skora göre azalan NMS, IoU eşiği 0.3
```

Bir karede kutu ve 5 nokta çizilerek gözle doğrulandı.

**FER+:** girdi `Input3` float32 `[1,1,64,64]`, gri `0.299R+0.587G+0.114B`, **0–255, normalize yok** · çıktı `Plus692_Output_0` `[1,8]` logit → softmax. Sınıf sırası: nötr, mutlu, şaşkın, üzgün, kızgın, iğrenmiş, korkmuş, küçümseyen. Kırpma: yüz kutusunun merkezinde, kenarı `max(w,h)·1.15` olan kare, bilinear örnekleme.

## 5. Doğruluk karşılaştırmaları

**Tespit:** `kendim` 202/202 kare; `iki-kisi` 385 karenin her birinde 2 yüz, skor ≥ 0,92. Yüzlerin x konumu sabit (yüz0 131–147, yüz1 432–468) → sol-sağ sıralaması kimlik takibi için yeterli. Görüntü küçültülünce yüz boyu 42/30/21/17 piksele indiğinde hâlâ 2/2 yüz bulundu (skor 0,92 → 0,86). **Tespit sınırına ulaşılamadı; asıl sınır ifade kalitesi.**

**Baş yönü** — `yaw = (burun_x − göz_orta_x) / göz_arası`:

| Bölüm | \|yaw\| medyan | \|yaw\| > 0,2 oranı |
|---|---|---|
| Düz | 0,02 (maks 0,05) | 0,00 |
| Baş çevirme | 0,37 | 0,74 (merkeze dönüş kareleri dahil) |
| Gülümseme | 0,05 | 0,06 |
| Kaş çatma | 0,02 | 0,00 |

→ **Eşik 0,2.**

**FER+ ortalama olasılıklar** (`kendim`):

| Bölüm | nötr | mutlu | üzgün | kızgın | diğerleri |
|---|---|---|---|---|---|
| Düz | .73 | .00 | .26 | .00 | ≤ .01 |
| Baş çevirme | .83 | .00 | .15 | .00 | ≤ .01 |
| Gülümseme | .22 | **.75** | .02 | .00 | ≤ .01 |
| Kaş çatma | .46 | .04 | **.47** | .01 | ≤ .01 |

- Gülümseme net ayrışıyor (2 sn'lik pencerelerde mutlu .72–.98).
- Kaş çatma **"kızgın" değil "üzgün"** olarak okunuyor, ayrışma zayıf (düz yüzde .26 → kaş çatmada .47).
- Dinlenme yüzünde **"üzgün" tabanı ~.26** var: kişiye özgü taban çizgisi şart.
- Gerçek mülakat: yüz0 nötr .75 / mutlu .20 (50–60 sn ve 70+ sn gülümsemeler), yüz1 nötr .90. Gerçek görüşmede yüz ifadesi çoğunlukla nötr kalıyor.

## 6. Hız ve bellek

| | Değer |
|---|---|
| YuNet (2 ORT thread) | ilk 38–50 ms · medyan 17 · p95 21 ms |
| FER+ (yüz başına) | ilk 16–19 ms · medyan 11,7 · p95 12,8 ms |
| Kare başına toplam iş (2 yüz, tensör doldurma dahil, `budget`) | medyan 72 · p95 75–77 ms |
| Gerçek hız 2,5 fps analizde gecikme | sabit 0,31 sn (yalnız) / 0,32–0,35 sn (Whisper ile); birikme yok |
| Görüntü hattı belleği | ~135–139 MB |
| Whisper turbo q5, 30 sn chunk (4 thread, Metal) | yalnız 6,7–7,0 sn · görüntüyle birlikte 6,1–6,4 sn (yavaşlama yok) |
| Whisper belleği | ~617–699 MB |
| Sistem boş bellek | %45 → %50 (bellek baskısı yok) |

Whisper ekran kartında, görüntü modelleri işlemcide çalıştığı için birbirlerini yavaşlatmıyorlar. **Ölçülmeyen:** tam uygulamada prozodi + metin ONNX ve diarization da eşzamanlı çalışırken toplam yük.

## 7. 11b için kararlar

| Konu | Karar |
|---|---|
| Video dosyası modu | **GEÇ.** `-re` ile gerçek hız; ses aynı dosyadan ayrı bir ffmpeg süreciyle alınır |
| Kamera modu | **GEÇ.** ffmpeg + avfoundation; AVFoundation P/Invoke gerekmez. `.app` + `NSCameraUsageDescription` zorunlu |
| Kamera seçimi | Ada göre (`FaceTime`), sıraya göre değil; `-pixel_format nv12` |
| İzin hataları | İlk kare için zaman aşımı (≥ 10 sn) + "kamera izni bekleniyor"; exit ≠ 0 ise stderr'den izin hatası gösterilir |
| ffmpeg yolu | Mutlak yol çözümlenir (`/opt/homebrew/bin`, `/usr/local/bin`); bulunamazsa "ffmpeg kurulu değil: brew install ffmpeg" |
| Ses yolu olmayan video | exit 234 → kullanıcıya "bu videoda ses yok" mesajı |
| Analiz hızı | 5 fps al, her 2. kareyi analiz et (2,5 fps); `IntraOpNumThreads = 2` |
| Yüz takibi | Sol-sağ sıralaması + IoU; iki sabit kişide yeterli |
| Baş yönü | \|yaw\| > 0,2 → "kameradan dönük" |
| Yüz ifadesi | Kişi başına **taban çizgisi** (ilk ~10 sn ortalaması) çıkarılarak kullanılır. "Kızgın" sinyali zayıf olduğundan gerilim göstergesinde (K1) yüzün ağırlığı sesten düşük tutulur |
| Küçük yüz | Tespit 17 px'te bile çalışıyor; ifade güveni h < 48 px'te düşürülür (**tahmin**, ölçülmedi) |

## 8. Beklenmedik bulgular

1. **Kamera listesinde `Capture screen 0` var.** ffmpeg avfoundation ile ekran da yakalanabiliyor. Zoom/Teams görüntüsü için ScreenCaptureKit'e gerek kalmayabilir. Kapsam dışı bırakıldı (spec §4), ama ileride çalışılacaklar arasında güçlü bir aday.
2. İzin istemi açıkken ffmpeg sonsuza kadar bekleyebiliyor. `IVideoCaptureSource` için "asla bloklanma" sözleşmesi bu yüzden zaman aşımıyla korunmalı.
3. Gerçek mülakatta yüz ifadesi çok az değişiyor. Karar desteğinde asıl sinyali ses ve metin taşıyacak; yüz, ilgi (yaw) ve olumlu an (gülümseme) için daha güvenilir.

## 9. Ürün koduna etki

Yok: `src/` ve `tests/` değişmedi. Başlangıçta da, spike sonunda da 475 test geçti, 11 atlandı, 0 başarısız.
