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

**Ek deney: metin modelini sıkıştırma (int8 dinamik kuantizasyon)**

Araç: `onnxruntime` 1.32 `quantize_dynamic` (Python, geçici sanal ortam; uygulama bağımlılığı değil). Karşılaştırma: aynı tokenizer, 60 elle yazılmış Türkçe cümle (mülakat/satış/toplantı dili, farklı duygular), referans = mevcut fp32 model. **Bu doğruluk değil, fp32 ile aynılık ölçümüdür.**

| Sürüm | Dosya | Tek başına tepe ayak izi | Baskın duygu fp32 ile aynı | Etiket kümesi (sigmoid ≥0,5) aynı | Valans farkı en büyük |
|---|---:|---:|---:|---:|---:|
| fp32 (mevcut) | 1,11 GB | 2,67 GB | – | – | – |
| MatMul+Gemm int8 | 0,86 GB | ölçülmedi | ölçülmedi | ölçülmedi | ölçülmedi |
| MatMul+Gemm+Gather int8 | 0,28 GB | 0,70 GB | 50/60 | 44/60 | 0,63 |
| aynısı, kanal başına ölçek | 0,28 GB | 0,70 GB | 53/60 | 47/60 | 0,58 |
| **yalnız Gather (kelime tablosu) int8** | **0,54 GB** | **0,93 GB** | **57/60** | **60/60** | **0,02** |

Tam ölçekli sıkıştırma sonuçları belirgin biçimde nötre kaydırıyor ("Harika, sonunda anlaştık, çok sevindim": sevinç → nötr); kabul edilmedi. Yalnız kelime tablosunu sıkıştırmak çıktıyı pratikte değiştirmiyor (farklı baskın duygu çıkan 3 cümlede iki duygu zaten başa baş; valans farkı ≤0,02).

**Tam hatta doğrulama (231 sn, aynı ses):** yalnız-Gather modeliyle tepe ayak izi **4,75 → 3,42 GB** (−1,33 GB; tek başına ölçümden beklenen −1,75 GB'tan az), RSS 2,27 → 2,33 GB, süre 101 → 95 sn, swap 0. Transkript, takip kararı ve karar göstergeleri aynı; metin valansı farkı ≤0,0006, metin güveni farkı ≤0,006.

Sınırlar: tek kayıt + 60 cümle; Türkçe doğruluk ölçülmedi (fp32'nin kendisi de ölçülmedi). Arayüzün kendisinde (uygulama + konuşmacı ayrımı) ölçüm yapılmadı.
