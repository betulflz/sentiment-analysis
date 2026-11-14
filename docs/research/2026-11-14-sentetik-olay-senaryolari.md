# Sentetik senaryolarla açık olay testi (İt.4) — 14 Kasım 2026

Gerçek kayıt alınamadığı için, içine bilerek sorun yerleştirilmiş beş Türkçe konuşma macOS `say` (Yelda) ile
üretildi; ikinci konuşmacı ses perdesi düşürülerek elde edildi. Duygu ölçümü için kullanılamaz (sentetik ses
duygusuzdur); yalnız **olay kurallarının** doğru tetiklenip tetiklenmediğini sınar.

Hat: Whisper large-v3-turbo (gerçek model) → `SpeakerAligner.AlignSplitting` → `OpenEventDetector`.
Konuşmacı bölgeleri iki yoldan verildi: **gerçek** (üretim sırasında bilinen zamanlar) ve **uygulama** (pencereli ayrım, 2 kişi).

| Senaryo | Beklenen | Gerçek konuşmacıyla | Uygulama ayrımıyla |
|---|---|---|---|
| S1 — soru soruldu, karşı taraf **geçiştirdi** | (insan: 1 cevapsız soru) | 0 | 0 |
| S2 — 104 sn tek kişi konuşması | 1 uzun tek kişi | **1 ✓** | 0 ✗ |
| S3 — somut sonraki adım olmadan kapanış | 1 belirsiz sonraki adım | **1 ✓** | **1 ✓** |
| S4 — kontrol: sorular cevaplı, net sonraki adım | 0 | **0 ✓** | **0 ✓** |
| S5 — soru soruldu, karşı taraf 15 sn+ konuşmadı | 1 cevapsız soru | **1 ✓** | **1 ✓** |

## Yorum

- Kurallar tasarlandıkları durumların hepsini yakaladı, kontrolde yanlış alarm yok (4/4).
- **S1 bilinen sınır:** kural "karşı taraf konuştu mu?" diye bakar; cevabın soruyu karşılayıp karşılamadığını
  anlamaz. Geçiştirilen soru kaçar. Bunu yakalamak anlam çıkarımı ister (ör. yerel dil modeli; strateji
  belgesinde İt.4'ün koşullu adayı). Bilinçli olarak eklenmedi.
- **S2'deki uygulama kaçırması sentetik sese özgü:** perdesi düşürülmüş aynı TTS sesi ses imzası modeli için
  ayrı kişi değil (otomatik modda tek kişi bulundu); 2 kişiye zorlanınca kümeleme monoloğu ikiye bölüyor.
  Kontrol: gerçek Şemsi kaydı zorunlu 2 kişiyle kusursuz ayrıldı (A 31 sn / B 64,5 sn, sınırlar referansla
  uyumlu). Bu yüzden uygulama sütunu gerçek konuşma için ölçü sayılmamalı.
- Gözlenen risk (gerçek veriyle doğrulanmadı): sesler gerçekten ayırt edilemeyecek kadar benzerse, zorunlu kişi
  sayısı belirsizlik yerine sırayla değişen etiketler üretir.

Üretim betiği: `tools/quality/sentetik_senaryo.py` (senaryo metinleri içinde); çıktılar git dışında.
