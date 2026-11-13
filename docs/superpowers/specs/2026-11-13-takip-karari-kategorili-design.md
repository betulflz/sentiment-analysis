# Görüşme sonu takip kararı — kategorili ekran (kısa tasarım)

Tarih: 13 Kasım 2026 · Dal: `11-karar-destek` · Durum: kullanıcı onayı bekliyor

## Amaç ve neden

Kullanıcı denemede görüşme sonu önerilerini anlamsız buldu. Mülakatta yalnız 3 genel seçenek var
("Ek / teknik görüşme", "Görüşmeyi tekrarla", "Değerlendirmeye geç"). Hedef: gerçek iş hayatındaki
takip adımlarına benzeyen, kategorilere ayrılmış, gerekçeli öneriler. Kullanıcının mülakat listesi
**örnektir**; kesin "işe al / reddet" kararı verilmez (2026-10-21 kararı: sistem kişi hakkında hüküm vermez).

## Hedefler / hedef olmayanlar

- Hedef: her profilde ~8 yumuşak dilli takip seçeneği, 4 ortak kategori, ekranda **önerilen yön + ilk 3 seçenek + kısa neden**.
- Hedef değil: işe al / reddet / maaş / dil seviyesi / bütçe gibi sistemin ölçemediği konular; canlı öneriler (LiveAction) değişmez; etki puanlarının kalibrasyonu (ayrı iş).
- Anti-hedef: kesin hüküm gibi okunan metin; ölçülmeyen bir şeye dayanıyormuş gibi gerekçe.

## Yaklaşımlar (kullanıcı **B**'yi seçti)

| | Ne | Neden seçilmedi / seçildi |
|---|---|---|
| A | Yalnız seçenek havuzunu genişlet | Ekran pek değişmez |
| **B** | Kategorili ekran: yön + ilk 3 seçenek + neden; AHP/TOPSIS aynen | **Seçildi** — anlamlı, ders gereği karar motoru korunur |
| C | B + görüşmecinin kendi kararını işaretleyip kaydetmesi | Veritabanı değişikliği, en büyük iş; sonraya |

## Tasarım

**Seçenekler** (`FollowUpAction`). Veritabanında **sayı** olarak saklandığı için mevcut 10 değerin sırası
değişmez, hiçbiri silinmez; yeniler **sona eklenir**. Mevcut değerlerin bazılarının ekrandaki metni yumuşatılır
(eski raporlar da yeni metni gösterir; anlam aynı).

| Kategori | Mülakat | Satış | Toplantı |
|---|---|---|---|
| **İlerletin** | Bir sonraki aşamaya davet etmeyi değerlendirin (`DegerlendirmeyeGec`, metin değişir) · Referans kontrolüne geçmeyi düşünün *(yeni)* | Teklif / fiyat önerisi gönderin (`TeklifGonder`) · Deneme sürümü ya da demo önerin *(yeni)* | Kararları ve sorumluları yazan özet e-postası gönderin (`KararEpostasi`, metin değişir) |
| **Netleştirin** | Ek teknik değerlendirme / örnek ödev isteyin (`EkGorusme`, metin değişir) · İK'dan ya da başka bir uzmandan ikinci görüş alın *(yeni)* · Netleşmeyen konular için kısa ikinci görüşme *(yeni)* | İtirazları yanıtlayan bilgi ve örnekler gönderin (`ItirazBilgisi`) · Karar vericiyle ikinci görüşme ayarlayın (`IkinciGorusme`, metin değişir) · Teknik ekiple ayrıntılı demo planlayın *(yeni)* | Takip toplantısı planlayın (`TakipToplantisi`) · Açık konular için küçük çalışma grubu kurun *(yeni)* · Az konuşanlardan yazılı görüş isteyin *(yeni)* |
| **Bekleyin** | Diğer adaylarla karşılaştırıp kararı sonra verin *(yeni)* | Acele etmeyin, bir hafta sonra kısa hatırlatma yapın (`Beklet`, metin değişir) · Bilgilendirme listesine alın *(yeni)* | Kararı erteleyin, eksik bilgiyi toplayın *(yeni)* |
| **İlişkiyi gözetin** | Görüşmeyi daha rahat bir ortamda tekrarlayın (`GorusmeyiTekrarla`) | Gergin geçti: önce kısa bir telefonla ilişkiyi onarın *(yeni)* | Gerginlik yaşanan kişiyle birebir görüşün (`BireBirGorusme`) |

**Seçim mantığı** (`FollowUpAdvisor`, motor aynı):
- Sakin görüşme (tüm göstergeler eşik altı) → kural yolu: yön **İlerletin**, profilin "İlerletin" seçenekleri puansız listelenir (bugünkü `PositiveFollowUp` ilk sırada).
- Mülakatta düşük veri kalitesi → kural yolu: **İlişkiyi gözetin / Görüşmeyi tekrarla** (bugünkü gibi).
- Diğer durumlar → profilin İlerletin dışındaki seçenekleri TOPSIS ile sıralanır; **yön = 1. seçeneğin kategorisi**; ilk 3 seçenek kendi "en çok etkilediği sorunlar" gerekçesiyle gösterilir. Tam sıralama ve duyarlılık analizi bugünkü yerinde kalır.
- Yeni seçeneklerin 6 kriterdeki etki puanları geliştiricinin tahminidir (mevcutlar gibi), kodda "kalibre değil" notuyla.

**Ekran** (rapordaki "Görüşmeden sonra ne yapmalı?" kartı):

```
Görüşmeden sonra ne yapmalı?
Profil: Mülakat · AHP CR = 0,04
[ Önerilen yön: Netleştirin ]
Ek teknik değerlendirme veya örnek ödev isteyin          ← büyük yazı (bugünkü BestText)
Neden: görüşmede gerilim ve söz–his çelişkisi belirgindi.

İlk 3 seçenek
 1. Ek teknik değerlendirme / örnek ödev     %71   gerilim, çelişki
 2. Netleşmeyen konular için kısa görüşme    %64   çelişki
 3. Daha rahat ortamda tekrarlayın           %58   gerilim
ⓘ Bu bir öneridir; kararı siz verirsiniz. Sistem yalnız ses tonu, yüz ifadesi ve konuşma payını ölçer.
```

**Değişmeyenler:** veritabanı şeması, kaydırıcılar, duyarlılık analizi, canlı öneriler.

**Testler:** mevcut 10 enum değerinin sayısal karşılığı sabit (eski rapor koruması); her profil seçeneğinin etki puanı ve kategorisi var; profil başına 8 seçenek; kural yolları (sakin → İlerletin, düşük kalite → tekrarla); yön = 1. seçeneğin kategorisi; ilk 3 en fazla 3 satır; ViewModel yön/neden metni.

## Geliştiriciye bırakılanlar

Etki puanı sayıları, kategori kimliği adlandırması, "neden" metninin tam kalıbı, ekrandaki stil ayrıntıları (mevcut kart stiline uyar).
