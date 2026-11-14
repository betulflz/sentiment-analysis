# Konuşmacı ayrımı kümeleme eşiği — ölçüm kaydı (2026-07-30)

## Koşullar

- **Embedding modeli:** `wespeaker_en_voxceleb_resnet34_LM.onnx`
  (indirilen dosya: 26.530.550 bayt / ~25,3 MB — `FileDiarizationModelProvider`'ın
  15.000.000–35.000.000 bayt aralığına giriyor)
- **İndirme kaynağı:** `github.com/k2-fsa/sherpa-onnx` sürüm etiketi
  `speaker-recongition-models` (NOT: GitHub'daki etiket adında yazım hatası var —
  "recognition" değil "recongition"; `speaker-recognition-models` 404 döner)
- **Segmentation modeli:** değişmedi, `sherpa-onnx-pyannote-segmentation-3-0/model.onnx`
- **Kayıt:** `scratchpad/diar-spike/toplanti.wav` — 16 kHz mono, 1.929.176 örnek (120,6 sn)
- **Bilinen gerçek:** kayıtta 2 konuşmacı var
- **Ölçüm koşucusu:** `tests/SentimentAnalysis.Infrastructure.Diarization.Tests/ThresholdSweepTests.cs`
  (`[SkippableFact]`, `DIAR_SWEEP_WAV` ortam değişkeni ile kapılı, normal test koşusunda çalışmaz)
- **Komut:**
  ```
  DIAR_SWEEP_WAV="/Users/betulfiliz/sentiment-analysis/scratchpad/diar-spike/toplanti.wav" \
    dotnet test tests/SentimentAnalysis.Infrastructure.Diarization.Tests/SentimentAnalysis.Infrastructure.Diarization.Tests.csproj \
    --filter "FullyQualifiedName~ThresholdSweepTests" --logger "console;verbosity=detailed"
  ```
- **Tarih:** 2026-07-30

## Sonuç tablosu

| Kayıt | Gerçek konuşmacı sayısı | Eşik | Bulunan küme sayısı | Toplam konuşma süresi |
|---|---|---|---|---|
| toplanti.wav | 2 | 0,3 | **3** | 113,7 sn |
| toplanti.wav | 2 | 0,4 | **2** | 113,8 sn |
| toplanti.wav | 2 | 0,5 | **1** | 113,7 sn |
| toplanti.wav | 2 | 0,6 | **1** | 113,7 sn |

## Seçilen değer: `ClusteringThreshold = 0.4`

**Gerekçe:** Taranan dört değer arasında yalnızca **0,4** tam 2 küme veriyor. 0,3 fazla
konuşmacı buluyor (3), 0,5 ve 0,6 tek kümeye çöküyor. Bu, kod içindeki önceki (ölçülmeden
yazılmış) yorumun iddia ettiği "0,3–0,5 aralığı tam 2 küme buluyor" ifadesiyle ÇELİŞİYOR —
o yorum tahminî yazılmıştı, bu ölçüm onu düzeltiyor.

**Önemli sınırlama — TEK KAYDA DAYALI ÖLÇÜM:** Bu sonuç yalnızca `toplanti.wav` adlı TEK bir
kayda dayanır. Dört nokta (0,3/0,4/0,5/0,6) arasında yalnızca biri (0,4) doğru sonucu
veriyor; 0,35 veya 0,45 gibi ara değerlerin de çalışıp çalışmadığı ölçülmedi (daha ince bir
tarama yapılmadı). Dolayısıyla 0,4 civarındaki gerçek çalışma aralığının sınırları bilinmiyor
— yalnızca 0,4'ün kendisinin bu kayıtla çalıştığı bilinen bir gerçek. Farklı akustik koşullarda
(gürültü, konuşmacı benzerliği, kayıt cihazı) bu değerin yine 2 küme vereceği garanti değildir.
Daha fazla gerçek kayıtla doğrulama yapılmadan bu değer geniş bir güvenle genellenemez.
