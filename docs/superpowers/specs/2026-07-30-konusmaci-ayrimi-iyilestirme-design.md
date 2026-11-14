# Konuşmacı Ayrımı İyileştirmesi — Tasarım

**Tarih:** 2026-07-30
**Durum:** Onaylandı, uygulama planı bekliyor
**İlgili:** Alt-proje 10 (10a spike, 10b diarization, 10c konuşmacı-başına duygu)

## Problem

Kullanıcı gözlemi: "sistem rastgele konuşmacı F, bilinmeyen gibi adlar atıyor."

Kod incelemesi bu gözlemin **üç ayrı nedeni** olduğunu gösterdi. İkisi gerçek kusur, biri
yanlış teşhis:

1. **Etiketler zaten harf tabanlı.** `SpeakerId.DefaultLabel` indeksi harfe çevirir
   (0→"Konuşmacı A"). Görülen "Konuşmacı F" rastgele bir ad değil, **indeks 5** demektir —
   yani sistem gerçekten altı küme bulmuştur. Sorun isimlendirmede değil, kümelemededir.

2. **Küme numaraları hiçbir yerde yeniden sıralanmıyor** (gerçek kusur). sherpa-onnx küme
   kimliklerini keyfi verir; ilk konuşana 0 verildiği garanti değildir. Bu yüzden ilk konuşan
   kişi "Konuşmacı F" olarak görünebilir, A–E hiç görünmeden.

3. **Embedding modeli ile eşik uyuşmuyor** (kök neden). `b22616b` (2026-07-29) embedding
   modelini `3dspeaker_speech_eres2net_base_sv_zh-cn` → `wespeaker_en_voxceleb_resnet34_LM`
   olarak değiştirdi ve `ClusteringThreshold`'u 0,8 → 0,5 çekti. Commit'in kendi notu:
   *"eşik, kullanılan embedding modeline özgüdür."* Ancak dosya adı her iki modelde de
   `speaker-embedding.onnx` olduğu için uyuşmazlık **hiçbir hata üretmez**. Geliştirme
   makinesindeki dosya 39,5 MB / 27 Temmuz tarihli — yani hâlâ eski model. Eski model +
   yeni eşik, 10a ölçümüne göre iki kişilik kayıtta altı küme üretir.

Buna ek olarak, konuşmacı-başına duygu analizi bir **yaklaşıklık** üzerine kurulu: 10–30
saniyelik chunk'lar konuşmacılara atanıp ortalanıyor. Gerçek bir diyalogda chunk çoğunlukla
birden fazla kişiyi içerir, hiçbirine %50 örtüşme eşiği yetişmez ve chunk `Unknown`'a düşerek
ölçümden tamamen çıkar — yani en çok karşılıklı konuşulan anlar sistematik olarak eleniyor.

## Kapsam

Dahil: embedding modelinin düzeltilmesi ve eşiğin ölçümle seçilmesi; küme numaralarının
konuşma sırasına göre yeniden verilmesi; konuşmacı-başına duygunun konuşmacının kendi
sesinden ölçülmesi; belirsiz atamaların "Bilinmeyen" yerine işaretli atama olarak
gösterilmesi.

Hariç: ayar kalıcılığı (08b'nin işi); konuşmacıya kullanıcı tarafından isim verilmesi;
toplantılar arası konuşmacı kimliği (gizlilik gereği kalıcı ses parmak izi saklanmaz —
spec 10 S4).

## Tasarım

### 1. Embedding modeli ve eşik

**Yapılacak:** `wespeaker_en_voxceleb_resnet34_LM.onnx` indirilip `speaker-embedding.onnx`
adıyla models dizinine konur. Ardından `scratchpad/diar-spike/toplanti.wav` (bilinen: iki
konuşmacı) üzerinde `ClusteringThreshold` 0,3 / 0,4 / 0,5 / 0,6 taranır ve tam iki küme veren
aralığın ortası seçilir. Sonuç, mevcut ölçüm notlarının yanına yazılır.

**Sessiz uyuşmazlığa karşı koruma:** `FileDiarizationModelProvider`, embedding dosyasının
boyutunu beklenen aralıkta doğrular ve dışındaysa hangi modelin beklendiğini söyleyen
anlaşılır bir hata verir. Gerekçe: bu hatanın maliyeti, yanlış sonucun sessizce üretilmesiydi.
Boyut kontrolü kriptografik bir garanti değildir; amacı kaza eseri yanlış dosyayı yakalamaktır,
kötü niyetli değişikliği değil.

### 2. Konuşma sırasına göre yeniden numaralandırma

Yeni saf yardımcı: `SpeakerRenumberer` (Application/Diarization).

- Girdi: `IReadOnlyList<SpeakerSegment>`.
- Her farklı özgün küme kimliği, **ilk konuşma anına** göre sırayla 0, 1, 2… ile eşlenir.
- Çıktı: aynı sayıda ve aynı sırada segment, yeni kimliklerle. Boşluk olmaz; ilk konuşan A.
- `SpeakerId.Unknown` (-1) olduğu gibi geçer — yeniden numaralandırma konusu değildir.
- Deterministiktir: aynı girdi her zaman aynı çıktıyı verir.

**Nerede çalışır:** `DiarizationJob.RunAsync` içinde, `DiarizeAsync` sonrası ve
`SaveSpeakerSegmentsAsync` öncesi. Böylece kalıcı veri zaten kanonik hâlde saklanır ve
okuyan her yüzey (rapor, duygu özeti) tutarlı numaralar görür.

### 3. Konuşmacı-başına duygu: kendi sesinden ölçüm

Yeni bileşen: `SpeakerProsodyAnalyzer` (Application/Diarization).

- Girdi: oturumun saklanmış PCM sesi + yeniden numaralandırılmış konuşmacı bölgeleri.
- Her konuşmacı için, **kendi bölgelerinin sesi** kesilip `IProsodyEmotionModel`'e verilir.
- **Bölge seçimi (verimlilik):** 1 saniyeden kısa bölgeler atlanır (prozodi için yetersiz
  bağlam). Kalanlar süreye göre azalan sırada alınır ve konuşmacı başına toplam **60 saniyeyi**
  aşmayacak şekilde durulur. Gerekçe: prozodi ortalaması için 60 saniye fazlasıyla yeterlidir;
  tüm sesi işlemek doğruluk kazandırmadan süreyi katlar.
- Bölge sonuçları **süre-ağırlıklı** ortalanır (uzun bölge daha çok söz hakkı).
- Çıktı, konuşmacı başına: VA + analiz edilen toplam saniye + kullanılan bölge sayısı.
  Analiz edilen süre saydamlık için taşınır: az sese dayanan bir ortalamaya fazla
  güvenilmemelidir.
- Hiç uygun bölgesi olmayan konuşmacı için duygu üretilmez (uydurulmaz); rapor bunu
  "yeterli ses yok" olarak gösterir.

**Nerede çalışır:** diarization ile aynı oturum-sonrası arka plan geçişinde. Canlı hat
etkilenmez.

**Değişen davranış:** `SpeakerEmotionAggregator`'ın chunk-ortalaması yaklaşımı, konuşmacı
**ses** duygusu için yerini bu ölçüme bırakır. Metin duygusu konuşmacı başına hizalanmış
transkript segmentlerinden gelmeye devam eder (metin zaten cümle bazında konuşmacıya atanır).

### 4. Belirsiz atama

`SpeakerAligner` artık üç durumu ayırır:

| Durum | Sonuç |
|---|---|
| Örtüşme oranı ≥ eşik | Konuşmacı atanır, **kesin** |
| 0 < örtüşme oranı < eşik | En çok örtüşen konuşmacı atanır, **belirsiz** işaretlenir |
| Hiç örtüşme yok | `Unknown` — aday bile yoktur, uydurma yapılmaz |

Dönüş tipi bir kayda dönüşür (`SpeakerAssignment`: segment + konuşmacı + belirsiz bayrağı),
böylece bayrak çağıranlar arasında sessizce kaybolamaz.

`SpeakerLineBuilder`: ardışık segmentler yalnızca **aynı konuşmacı ve aynı kesinlik**
durumundaysa tek satırda birleşir. Aksi hâlde emin bir cümle belirsiz bir cümleyle aynı
satıra karışır ve satırın hangi kısmına güvenileceği kaybolurdu.

UI (`ReportView`): belirsiz satır soluk renkte ve "?" rozetiyle gösterilir. "Bilinmeyen"
etiketi yalnızca gerçekten aday olmayan satırlarda görünür.

## Test yaklaşımı

Her parça saf birim testiyle korunur (TDD; önce kırmızı):

- **Renumberer:** keyfi kimlikler (ör. {3, 7, 1}) konuşma sırasına göre {0, 1, 2}'ye eşlenir;
  boşluk kalmaz; `Unknown` değişmeden geçer; aynı girdi aynı çıktıyı verir.
- **Aligner:** eşik altı örtüşme → atanır **ve** belirsiz; sıfır örtüşme → `Unknown`; eşik
  üstü → kesin. Segment sayısı ve sırası korunur.
- **LineBuilder:** aynı konuşmacının kesin ve belirsiz cümleleri **birleşmez**; aynı
  kesinlikteki ardışık cümleler birleşir.
- **SpeakerProsodyAnalyzer:** bölge seçimi (1 sn altı elenir, 60 sn bütçesi aşılmaz, uzun
  bölge önceliklidir) sahte bir prozodi modeliyle test edilir; süre-ağırlıklı ortalama
  doğrulanır; uygun bölgesi olmayan konuşmacı için sonuç üretilmediği doğrulanır.
- **Model koruması:** beklenen boyut aralığı dışındaki dosya, hangi modelin beklendiğini
  söyleyen anlaşılır bir hata üretir.

Eşik seçimi bir birim testiyle değil, gerçek kayıtla **ölçülerek** yapılır ve ölçüm sonucu
koda not düşülür (projenin 10a'daki yöntemi: kararlar ölçülür, keyfi verilmez).

## Riskler

- **Eşik ölçümü tek kayda dayanıyor.** `toplanti.wav` iki konuşmacılı tek bir örnektir; seçilen
  eşik bu kayda aşırı uyabilir. Ölçüm notu bunu açıkça belirtir ve eşik config'te kalır.
- **60 saniyelik bütçe kalibre edilmemiştir.** Başlangıç değeridir; eval-09 ölçebilir.
- **Belirsiz atama, yanlış alıntı riskini sıfırlamaz.** İşaretleme bunu görünür kılar ama
  ortadan kaldırmaz; UI'nın rozeti göstermemesi hâlinde risk sessizleşir.
