# Alt-Proje 09 — Değerlendirme (Eval)

> Bağımlılık: 02, 03, 04 (paralel yürür) · Katman: ayrı `eval` projesi/araçları

## Hedef
Model ve füzyon kararlarını **ölçmek** — keyfi değil, kanıta dayalı. Stajyerin
`eval/` yaklaşımını koru ve genişlet.

## Kapsam
- **Türkçe değerlendirme veri seti** hazırlığı (etiketli ses + metin alt kümesi).
- **STT değerlendirmesi:** large-v3-turbo vs adaylar (WER/CER, Türkçe).
- **Duygu modeli değerlendirmesi:** metin ve prozodi VA çıktılarının etiketle uyumu.
- **Füzyon kalibrasyonu:** eksen ağırlıkları ve uyumsuzluk eşiklerini veri üzerinde
  ayarla; iki sayı raporla — (a) katı doğruluk (belirsiz = yanlış), (b) kararlı
  örneklerde doğruluk + **kapsam** (belirsiz dönmeme oranı).
- Raporlar repo'ya yazılır (git'e dahil), veri dahil değil.

## Kararlar
- Füzyon eşikleri **burada** kalibre edilir, sonra `AppConfig`'e yansıtılır.
- Değerlendirme tek "doğruluk" sayısına indirgenmez (dürüst raporlama).

## Kabul Kriteri
- Tekrar üretilebilir komutlarla (ör. `prepare`, `evaluate-stt`, `evaluate-fusion`)
  raporlar üretilir.
- Füzyonun naif ortalamaya karşı kazancı sayısal olarak gösterilir.
