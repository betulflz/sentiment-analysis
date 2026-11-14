# 12a — Ses örneğiyle tanıtma (enrollment) tabanlı "Yöneten / Karşı taraf" ayrımı: spike ölçümü

**Tarih:** 2026-11-12 · **Durum:** ölçüldü, karar kapısı GEÇİLDİ (hedef ≥ %85, ölçülen %93–95)
**Araç (git dışı):** `scratchpad/enroll-spike/` (etiketler `labels.txt`, ses uygulamanın şifre çözme yoluyla bellekte okunur)

## Soru
Canlı öneri motoru mikrofondaki iki sesi ayıramıyor; görüşme sonrası çalışan pyannote ayrımı da karşılıklı
konuşmada 13–17 sn'lik tek parçalar üretiyor. Görüşmeyi yöneten kişinin birkaç saniyelik ses örneğine
benzerlikle, **kümeleme ve bölütleme olmadan**, kısa pencereler "Yöneten / Karşı taraf" diye etiketlenebilir mi?
Canlı çalışacak kadar hızlı mı?

## Yöntem
- **Veri:** 2026-11-12'de kaydedilmiş 4 iki kişilik oturum (mülakat, satış, toplantı canlandırması, bağırarak
  tartışılan kızgın müşteri sahnesi). Etiketler transkript içeriğinden *kimin konuştuğu kesin olan* aralıklara
  verildi (25 aralık, kenarlar ~0,3 sn içeri çekildi).
- **Deneme:** her oturumda iki kişi sırayla "yöneten" sayılır; o kişinin en uzun aralığı (4,7–14 sn) tanıtma
  örneği olur, kalan tüm aralıklar test edilir. Test aralıkları L uzunluğunda, 0,5 sn kaydırmalı pencerelere bölünür.
- **Karar:** pencere embedding'i ile tanıtma embedding'i arasındaki kosinüs benzerliği ≥ eşik ise "Yöneten".
  "Yumuşak" = 3 komşu pencerenin ortancası. "Uyarlamalı" = karşı taraf prototipi çevrimiçi biriktirilir.
- **Ölçüt:** dengeli doğruluk (iki sınıf doğruluğunun ortalaması). Eşik tüm verinin tek eşiğidir;
  **LOSO** satırında her oturumun eşiği diğer üç oturumdan seçilir (iyimserlik kontrolü).
- **Taban çizgisi:** oturum sırasında saklanan pyannote bölgeleri, aynı pencerelerde.

## Sonuçlar

| Yöntem | Pencere | Dengeli doğruluk | LOSO | Kızgın sahne (6339) | Süre / pencere |
|---|---|---|---|---|---|
| pyannote (bugünkü) | 1,5 sn | %73 | — | %50 | (oturum sonrası, RTF 0,44) |
| Tanıtma + wespeaker resnet34 (bugünkü model) | 2,0 sn | %92 | — | %54 | 66 ms |
| Tanıtma + 3D-Speaker ERes2NetV2 | 2,0 sn | %94 | — | %74 | 141 ms |
| Tanıtma + 3D-Speaker CAM++ (zh-en) | 1,5 sn | %95 | %93 | %88 | 20 ms |
| Tanıtma + 3D-Speaker CAM++ (zh-en) | 2,0 sn | %95 | %94 | %83 | 26 ms |
| **Tanıtma + NeMo TitaNet-small** | **1,5 sn** | **%95** | **%95** | **%88** | **16 ms** |
| Tanıtma + NeMo TitaNet-small | 2,0 sn | %96 | %95 | %84 | 21 ms |

Ayrıntı (yumuşatılmış, tek eşik): TitaNet-small 1,5 sn → 17ea %100, 73d4 %100, 4a6a %91, 6339 %88.
Benzerlik ortalamaları: aynı kişi 0,55 / farklı kişi 0,21 (TitaNet-small, 1,5 sn) — bugünkü modelde 0,72 / 0,42.

## Bulgular
1. **Tanıtma yöntemi pyannote'u açıkça geçiyor** (%73 → %95). pyannote'un çöktüğü iki oturumda (toplantı ve kızgın
   sahne, ikisi de %50 = hep tek kişi) tanıtma %88–91.
2. **Bugünkü embedding modeli (resnet34) yükseltilmiş, öfkeli seste çöküyor** (%54). Öneri motoru için en kritik
   an tam da bu olduğu için model değişmeli. CAM++ ve TitaNet-small öfkeli seste %84–88 tutuyor.
3. **Canlı çalışabilir:** TitaNet-small 1,5 sn pencerede 16 ms (M1 Air, 2 iş parçacığı). 0,5 sn'de bir pencere
   ≈ tek çekirdeğin %3'ü. Gecikme: pencere 1,5 sn + yumuşatma 0,5 sn.
4. Uyarlamalı (karşı taraf prototipi) ek kazanç sağlamadı — basit eşik yeterli.

## Sınırlar (rapora girmeli)
- Küçük veri: 4 oturum, ~5 dk etiketli konuşma, aynı 3–4 kişi (kadın sesleri). 3+ kişili toplantı ölçülmedi.
- Tanıtma örneği aynı görüşmenin içinden alındı. Üretimde ayrı, sakin bir örnek alınacak; ses tonu farkı
  (sakin örnek, öfkeli konuşma) ek kayıp yaratabilir. 6339'daki %88 bunun kısmi bir göstergesi.
- Etiket sınırları transkript zamanlarından geliyor (±0,5 sn). 0,5 sn'lik tek kelimelik cevaplar ("22", "Tokat")
  ve üst üste konuşma ölçülmedi; 1,5 sn pencere bunları kaçırır.
- Eşik 4 oturumda ayarlandı (LOSO ile kontrol edildi, fark ≤ 2 puan).

## Karar ve sonraki adım
- Model: **NeMo TitaNet-small** (40 MB, sherpa-onnx `SpeakerEmbeddingExtractor` ile aynı API), pencere 1,5 sn,
  kaydırma 0,5 sn, 3'lü ortanca yumuşatma, eşik **0,41** (ilk değer, kalibre edilecek).
- Görüşme başında "Önce siz 5–8 sn konuşun" tanıtma adımı.
- Canlı hatta pencere etiketi → öneri motoru yalnız karşı tarafın sesini kullanır, ayrıca canlı konuşma
  dengesi göstergesi (K7) eklenir. Görüşme sonrası pyannote yalnız 3+ kişili toplantıda kişileri ayırmak için kalır.
