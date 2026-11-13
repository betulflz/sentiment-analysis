**M1 Air bellek ve süre ölçümü — 13 Kasım 2026**

Gerçek model koşusu; doğruluk ölçümü DEĞİLDİR (referans etiket yok). İterasyon 1'in "toplam bellek" maddesine ilk veri.

**Düzen:** Apple M1, 8 GB, macOS 26.3. `tools/SentimentAnalysis.Benchmark` Release, `--threads 2`, her koşu ayrı süreç, `/usr/bin/time -l`. Ses: `scratchpad/test-videolari/iki-kisi.mp4`'ten 16 kHz mono WAV (77 sn; ayrıca 3 kez art arda eklenmiş 231 sn). Dosyalar git dışı `quality-local/memory/`. Uygulama (.app) aynı anda boşta açıktı. Önbellekler temizlenmedi, sıra randomize edilmedi.

**Tam ses hattı (`analyze`: VAD + Whisper turbo + prozodi + metin duygusu + füzyon + karar; arayüz/SQLite/kamera/konuşmacı ayrımı yok)**

| Ses | Süre (sn) | Gerçek zaman oranı | Tepe RSS | Tepe bellek ayak izi (Etkinlik İzleyicisi) | Swap |
|---|---:|---:|---:|---:|---:|
| 77 sn | 35,6 | 0,46 | 2,31 GB | 4,73 GB | 0 |
| 231 sn | 101,1 | 0,44 | 2,27 GB | 4,75 GB | 0 |

Her iki koşu `complete=true`, tüm parçalar "Speech". Bellek kayıt uzunluğuyla büyümüyor → maliyet model yüklemesinden, sızıntı belirtisi yok (yalnız 231 sn'ye kadar denendi).

**Görüşme sonrası konuşmacı ayrımı (`diarize`, sherpa, 2 kişi, 231 sn):** 123,5 sn (oran 0,53), tepe RSS 0,25 GB, ayak izi 0,21 GB.

**Uygulama boşta:** RSS ≈ 0,22 GB.

**Model başına yükleme maliyeti (tek başına, ayrı süreç; ONNX Runtime CPU)**

| Model | Dosya | Tepe RSS | Tepe ayak izi |
|---|---:|---:|---:|
| Metin duygusu `text-emotion-xlmr.onnx` | 1,11 GB | 1,64 GB | **2,67 GB** |
| Whisper turbo q5_0 (transcribe, 77 sn) | 0,57 GB | 0,84 GB | 0,80 GB |
| Prozodi `prosody-msp-dim.onnx` | 0,66 GB | 0,78 GB | 0,74 GB |
| Silero VAD | 2 MB | 0,08 GB | 0,05 GB |
| (boş .NET süreci) | – | 0,06 GB | 0,03 GB |

Toplamlar tam hat ölçümüyle tutarlı (≈4,3 GB + taban ≈ 4,7 GB).

**Yorum**

- Strateji hedefi (normal ~3 GiB, tepe 4 GiB) tam hat ayak izinde **aşılıyor** (4,75 GB). Swap görülmedi; tek başına çalışırken sistem kaldırdı. Tarayıcı vb. açıkken davranış ölçülmedi.
- Tepenin **~%55'i metin duygusu modeli**. Füzyonda metin ağırlığı 0 (`FusionConfig`), ama metin duygusu "söz–his çelişkisi" (uyumsuzluk) hesabında ve 12. aşamanın karşı taraf metninde kullanılıyor; doğrudan kaldırılamaz.
- Ayak izinin RSS'ten büyük olmasının nedeni bu ölçümle ayrıştırılmadı (ONNX yükleme sırasında ağırlık kopyaları / sıkıştırılmış bellek olabilir — doğrulanmadı).
- Model kapatma değişikliğinin (iş başı model ömrü) arayüzdeki etkisi bu ölçümde YOK: CLI her koşuda yeni süreçtir. Arayüzde analiz öncesi/sonrası ölçüm ayrıca yapılmalı.

**Metin modeli çıktı yorumu (İterasyon 3 ön kontrol)**

- Etiket sırası: modelin resmî `config.json` `id2label` sırası (anger…surprise) koddaki `XlmrEmotionLabels.Ordered` ile aynı (2026-07-26'da da doğrulanmıştı).
- `config.json`: `"problem_type": "multi_label_classification"` → bağımsız sigmoid. Kod softmax uyguluyor (`TextVaLookup.cs:36`).
- 6 elle yazılmış Türkçe cümlede gerçek model: açık cümlelerde iki yöntem aynı etiketi veriyor; karışık cümlede ("biraz endişeliyim…") softmax korkuyu 0,16'ya bastırıyor, sigmoid 0,55 ile korkuyu da eşik üstü buluyor (valans −0,11 → −0,28). Bu bir doğruluk ölçümü değil, davranış farkının örneğidir. Ayrıca "Fiyat çok yüksek ama ürün güzel, emin değilim" iki yöntemde de %99 nötr çıktı.

**Tekrar üretim:** komutlar `quality-local/memory/*.log` başında değil; şu biçim: `/usr/bin/time -l dotnet tools/SentimentAnalysis.Benchmark/bin/Release/net9.0/SentimentAnalysis.Benchmark.dll analyze --input <wav> --output <json> --models "<models>" --threads 2`.
