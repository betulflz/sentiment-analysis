# Kelime zamanıyla konuşmacı ataması — deney (İt.2), 14 Kasım 2026

**Karar: uygulamaya eklenmedi.** Ölçülen kazanç yok denecek kadar küçük; DTW seçeneği yerel çökme yaratıyor.

## Soru

Bugün uzun bir Whisper segmenti iki kişiye yayılırsa kelimeler konuşma sürelerine **orantılı** bölünür
(`SpeakerAligner.AlignSplitting`, eşit konuşma hızı varsayımı). Whisper'ın token zaman damgalarıyla her
kelimeyi kendi zamanındaki konuşmacıya yazmak doğru kişi oranını artırır mı?

## Yöntem

- Aynı transkript (large-v3-turbo q5_0, beam 5, 30 sn parça) ve aynı konuşmacı bölgeleri (uygulamanın
  pencereli ayrımı) üzerinde iki atama karşılaştırıldı:
  - **A** — bugünkü orantılı bölme (C# mantığının birebir Python kopyası).
  - **B** — her kelime, orta noktasına en yakın konuşmacı bölgesine (≤1 sn); tek/iki kelimelik ara
    yumuşatmalı ve yumuşatmasız; 0,5 ve 2 sn sınırlı varyantlar.
- Doğru cevap: kullanıcının söz sırası başına verdiği metin. Referans ve tahmin kelimeleri metin olarak
  hizalanır; eşleşen her kelimenin konuşanı karşılaştırılır (zamanlar yaklaşık olduğundan zamana değil
  metne dayanır). Etiket eşlemesi en iyi permütasyonla yapılır.
- Kayıtlar: `semsi-01` (116 sn röportaj, 218 kelime), `pilot-01` (46 sn dizi sahnesi, hızlı söz alma,
  105 kelime). İkisi de geliştirme kaydıdır; bağımsız test değildir.

## Sonuç

| Kayıt | A bugünkü | B kelime zamanı | B + yumuşatma |
|---|---|---|---|
| semsi-01 | %100,0 | %100,0 | %100,0 |
| pilot-01 | %98,0 | %99,0 | %99,0 |

(Eşleşen kelime: 214/218 ve 98/105.) 0,5 sn sınırı Şemsi'de 6 kelimeyi "bilinmiyor" bıraktı (%97,7).

**DTW zaman damgaları** (`UseDtwTimeStamps`, `LargeV3Turbo` başlıkları): Şemsi'de çalıştı, Muğla
kaydında (150 sn) whisper.cpp **yerel olarak çöktü** (exit 134); kısa son parça atılınca ve DTW belleği
1 GB'a çıkarılınca da çöktü. Üretimde uygulamayı kapatabileceği için elendi.

## Yorum

Konuşmacı bölgeleri doğru olduğunda segment sınırları zaten söz sıralarına yakın düşüyor; orantılı bölme
yeterli. Hatanın asıl kaynağı hizalama değil konuşmacı ayrımının kendisi (benzer sesler, kısa katılım,
örtüşme). Kelime zamanını eklemek veri şemasını (kelime zamanlarının saklanması) değiştirirdi; bu kazanç
için değmez.

Yeniden değerlendirme koşulu: örtüşmeli ya da çok hızlı söz alan etiketli kayıtlarda A'nın %95'in altına
düştüğü görülürse. Araç: `scratchpad/.../wordts` (Whisper.net token zamanları → JSON) ve
`evalwords.py` (bu belgedeki tablo); git dışında.

## Yan bulgu (uygulandı)

Aynı ölçümde Whisper'ın sessizlikte "Altyazı M.K." uydurduğu görüldü (30 sn sessiz kayıt ve Muğla
kaydının son 0,9 sn'si). `WhisperHallucinationFilter` bu kalıpları segmentin tamamıysa atar (commit
`0b9cabb`); gerçek modelde sessiz kayıt artık boş metin veriyor.
