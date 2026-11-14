# Uzun görüşme testi (İt.5) ve öneri karşılaştırma denemesi (İt.4) — 14 Kasım 2026

## 30 dakikalık ses hattı testi

- **Girdi:** semsi-01 + mugla-01 + pilot-01 art arda, 1.836,9 sn (30,6 dk), 16 kHz mono.
- **Yol:** `SentimentAnalysis.Benchmark analyze` — üretim `EmotionPipeline` (Silero VAD, Whisper large-v3-turbo q5_0
  beam 5, ses + metin duygu, füzyon) + karar raporu; Offline mod, 30 sn parça / 5 sn örtüşme, 2 iş parçacığı.
  Avalonia, SQLite, kamera ve oturum sonrası konuşmacı ayrımı **dahil değil**.
- **Cihaz:** MacBook Air M1, 8 GB. Not: koşu sırasında bir süre birim testleri de çalıştı; süreler hafif kötümser.

| Ölçü | Değer |
|---|---|
| Toplam süre | 963,9 sn → genel RTF **0,52** |
| Parça başına işlem (74 parça) | medyan 13,2 sn, en fazla 18,8 sn; 25 sn'lik adıma göre RTF medyan 0,53, en kötü 0,75 |
| İlk 4 / son 4 parça ortalaması | 11,8 sn / 11,7 sn — **zamanla yavaşlama yok** |
| Kapsam | %100; atlanan, düşen, hatalı parça yok (`hasIssues=false`) |
| En fazla RSS | 2,33 GB |
| Tepe bellek ayak izi | 3,61 GB (hedef: 3 GiB normal / 4 GiB tepe → tepe hedefin içinde, normal hedefin biraz üstünde) |

Sonuç: dosya modunda 30 dk görüşme bellek sızıntısı ya da birikme olmadan biter; işlem gerçek zamanın yaklaşık
yarısında. **Ölçülmedi:** canlı mikrofonla 30 dk (canlıda 10 sn parça ve rol ayrımı ek yük getirir), tekrarlı
başlat/durdur, arayüz tepkisi. Bunlar cihazda elle denenmelidir.

## Öneri karşılaştırma denemesi (İt.4)

Aynı üç kayıt mülakat profiliyle (`--profile mulakat`) çalıştırıldı; kanıtlı açık olaylar aynı transkript ve
uygulamanın konuşmacı ayrımıyla üretildi.

- TOPSIS canlı önerisi: 13 pencerenin **13'ünde "Devam et — belirgin bir sorun yok"**.
- Açık olay kuralları: ilk denemede 1 olay, o da **yanlış alarm** ("… kelimeler mi bunlar diye … hatırlıyorum"
  aktarılan iç ses, soru sanıldı). Düzeltildi (`365162f`); sonra 0 olay.
- Bu kayıtlarda gerçekten cevapsız soru, 90 sn+ monolog ya da belirsiz kapanış yok; dolayısıyla iki sistem
  **ayırt edilemiyor**. Kör insan değerlendirmesi için sorunlu anlar içeren kayıt gerekiyor (ör. yanıtı
  geçiştirilen soru, tek kişinin uzun konuşması, "sonra konuşuruz" diye biten görüşme).
- Gözlem: pilot-01 tartışmalı bir sahne olduğu hâlde TOPSIS "Devam et" dedi (duygu güveni düşük → göstergeler
  nötre yakın). Olası bir kaçırma; etiketli duygu verisi olmadan karar verilmedi.
- Form üreteci (git dışında): `scratchpad/.../events` → `quality-local/kor-degerlendirme/form.csv` (kaynak gizli,
  karışık sıra) ve `anahtar.csv`.
