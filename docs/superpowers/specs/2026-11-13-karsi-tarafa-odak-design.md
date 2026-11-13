# Karar desteği yalnız karşı tarafa odaklansın — tasarım

Tarih: 13 Kasım 2026 · Dal: `11-karar-destek` · Durum: kullanıcı onayı bekliyor (builder check yapıldı; "Bizim kararımız" işaretli maddeler geliştirici kararı)
Önceki iş: 12. aşama (`specs/subprojects/12a-tanitma-tabanli-konusmaci-spike.md`; SpeakerRoleTracker, ChunkResult.Roles/Counterpart, TalkMeter).

## Amaç ve neden

Uygulama bir karar destek sistemidir: satışta satıcıya, mülakatta mülakatçıya, toplantıda toplantı yöneticisine
karar desteği verir. Kararın konusu **karşı taraftır** (müşteri, aday, katılımcılar). Kullanıcının fikri:
görüşmeyi yönetenin duygusunu tartmak gereksizdir ve sinyali bulandırır; analiz yalnız karşı tarafa odaklanmalı.

Bugün: canlı modda "Sesimi tanıt" yapılmışsa göstergeler zaten karşı taraftan hesaplanıyor (12. aşama).
Yöneten hâlâ karışıyor: (1) tanıtma yapılmadan başlatılan canlı görüşme, (2) dosya modu (rol ayrımı yok),
(3) rapordaki duygu özeti/zaman çizelgesi karışık sesten, (4) raporda yönetenin duygu kartı.

## Hedefler / hedef olmayanlar / anti-hedefler

- **Hedef:** her görüşmede yöneten bilinsin; duygu ve göstergeler yalnız karşı taraftan; yönetenin yalnız
  **davranışı** (konuşma payı, konuşma hızı) karar girdisi olarak kalsın.
- **Hedef değil:** konuşmacı ayrımı motorunu değiştirmek (İterasyon 2 işi); duygu modellerini değiştirmek;
  3+ kişili toplantıda katılımcıları tek tek ayırmak (karşı taraf = yönetenin dışındaki herkes, birlikte).
- **Anti-hedef:** rol bilinmediği halde karışık sonucu "karşı taraf" diye göstermek; yönetenin sesini
  karşı tarafın duygusuymuş gibi yazmak.

## Kullanıcı kararları

1. **Canlı mod (mikrofon / kamera): tanıtma zorunlu** — "Sesimi tanıt" yapılmadan Başlat devre dışı.
2. **Dosya modu: önce konuşmacı ayrımı → "Siz hangisiniz?" → analiz** (1. yol). Tanıtmanın yerini bu seçim alır;
   analiz 12. aşamanın karşı taraf hattıyla çalışır, Whisper bir kez çalışır.
3. Kimlik seçimi **ses dinleterek**: her konuşmacı için "Dinle" düğmesi ~5 sn'lik sesini çalar.
4. **Gizlilik:** dosya modunda konuşmacı ayrımı "Konuşmacı ayırımı" ayarından **bağımsız olarak her zaman** yapılır;
   çözülmüş ses **yalnız bellekte** tutulur, diske yazılmaz, oturum sonrası kişi kartları da hesaplandıktan sonra bırakılır.
   (Dosya zaten kullanıcının diskinde; ek saklama yok.) Ayar kapalıyken canlı modun davranışı değişmez.

## Yaklaşımlar (dosya modu)

| Yol | Özet | Karar |
|---|---|---|
| **1. Önce ayır, sor, sonra analiz et** | Dosya seçilince diarization → seçim → rol bilgili analiz | **Seçildi**: canlıyla aynı mantık/kod, tek Whisper, rapor baştan temiz |
| 2. Analiz et, sonra sor, yeniden hesapla | Raporda seçim → karşı taraf bölümleri için modeller yeniden | Modeller iki kez yüklenir (~2–3 GB), iki tür rapor |
| 3. Yalnız raporu düzelt | Seçimden sonra takip kararı ve kişi kartları | Pencere göstergeleri karışık kalır |

## Tasarım

### A. Canlı mod — tanıtma zorunlu
- `LiveViewModel.CanStart`: Mikrofon/Kamera modunda geçerli bir tanıtma yoksa `false`. Dosya modunda değişmez.
- Tanıtma yokken Başlat'ın yanında açıklama: "Başlamadan önce 'Sesimi tanıt' ile sesinizi tanıtın — öneriler yalnız karşı tarafa göre hesaplanır."
- "Şüpheli" tanıtma (12. aşamadaki iki-yarı tutarsızlığı) başlatmayı **engellemez**; mevcut uyarı kalır. *(Bizim kararımız: engellemek kullanıcıyı kilitler; rol etiketindeki şüphe zaten gösteriliyor.)*
- "Tanıtmayı sil" → Başlat yeniden devre dışı.
- Tanıtma **imkânsızsa** (ses imzası modeli eksik/bozuk → özellik kapalı): Başlat kilitlenmez; açıklama gösterilir ve
  görüşme karışık yolda başlar, raporda "Yöneten ayrılmadı" notu. *(Bizim kararımız: kullanıcıyı kalıcı kilitlemeyelim.)*
- Görüşme ortasında rol etiketleme hata verirse: etiketsiz kalan konuşmalı parça varsa raporda "Yöneten yalnız kısmen
  ayrıldı" notu. *(Bizim kararımız.)*

### B. Dosya modu — önce ayır, sor, analiz et
Akış (ses ve video dosyası):
1. Kullanıcı dosyayı seçip Başlat'a basar → **"Konuşmacılar ayrılıyor…"** (ilerleme göstergesi, Durdur çalışır).
   Dosya 16 kHz mono'ya çözülür (mevcut dönüştürücü), `IDiarizer` "Kaç kişi konuşacak?" seçimiyle çalışır,
   `GhostSpeakerFilter` uygulanır. Konuşmacı ayrımı modeli bu iş bitince kapatılır (ModelWorkQueue, iş başı model ömrü).
2. **"Siz hangisiniz?"** paneli: her konuşmacı için "Konuşmacı A · 1 dk 20 sn konuştu · [▶ Dinle]".
   Dinle: o konuşmacının en uzun bölgesinden ≤5 sn'lik parça geçici bir WAV'a yazılır ve macOS `afplay` ile çalınır;
   çalma bitince/iptalde dosya silinir. macOS dışında Dinle düğmesi gizlenir (süreler yine görünür).
   Seçenekler: konuşmacılar + **"Hiçbiri / emin değilim"**. Onayla → analiz başlar.
3. Analiz, seçilen konuşmacıyı yöneten, diğerlerini karşı taraf sayan yeni `DiarizationRoleLabeler : IRoleLabeler`
   ile çalışır (12. aşamadaki `roles:` parametresi; ChunkProcessor'ın karşı taraf yolu aynen kullanılır).
   Etiketleme: parçayla örtüşen diarization bölgeleri → `RoleSpan` (seçilen = `Yoneten`, diğerleri = `KarsiTaraf`),
   `Similarity = 1`, `Enrolled = true`, `EnrollmentDoubtful = false`.
4. Görüşme sonrası konuşmacı geçişi **yeniden diarization yapmaz** ve oturum sesini depodan okumaz: 1. adımdaki
   bölgeler ve bellekteki ses yeni bir giriş noktasıyla (ön-hesaplı bölgeler + PCM alan) kişi kartları/satırlar ve
   konuşmacı başına ses tonu için kullanılır; ardından ses bellekten bırakılır. Bölgeler oturuma (konuşmacı bölgeleri
   tablosu) yazılabilir — bunlar ses değildir.
5. Uzunluk sınırı: mevcut `MaxRecordedMinutes` (90 dk) dosya modu ayrımı için de geçerli; daha uzun dosyada
   ilk 90 dk ayrılır, kalan kısım karışık yol + not. *(Bizim kararımız: 8 GB'ta bellek sınırı.)*
6. Video dosyası: ses, ayrım öncesi ffmpeg ile 16 kHz mono'ya çözülür (mevcut dönüştürücü yalnız wav/mp3/m4a/aac).
7. `SpeakerId.Unknown` bölgeleri ve hiçbir bölgenin kapsamadığı konuşma **rolsüz** (`Belirsiz`) kalır, karşı tarafa
   sayılmaz. *(Bizim kararımız: anti-hedef — yönetenin sesi karşı tarafa karışmasın.)*

Kenar durumları:
- Diarization başarısız ya da tek konuşmacı bulduysa → seçim paneli yerine bilgi: "Konuşmacılar ayrılamadı; analiz
  karışık ses üzerinden yapılacak ve raporda öyle belirtilecek." (roles = null; bugünkü davranış + etiket).
- "Hiçbiri / emin değilim" seçilirse → aynı karışık yol + etiket.
- Video dosyasında görüntü kanalı bugünkü gibi çalışır (gerçek hızlı önizleme diarization sonrası başlar).

### C. Rapor — yönetenin duygusu yok, davranışı var
- Rol bilgisi olan oturumlarda **duygu özeti ve duygu payları** (`MeetingAggregator`, bugün `Fused`) karşı tarafın
  sonucundan (`Counterpart`) hesaplanır; karşı tarafın konuşmadığı parçalar özete girmez. (Counterpart saklanmaz; özet
  oturum içinde bellekten üretildiği için depolama değişmez — Geçmiş ekranı raporu yeniden açmıyor.)
- **Gösterge zaman çizelgesi** (karar pencereleri): karşı tarafın sustuğu pencere bugün nötr değerlerle geliyor
  (`IndicatorCalculator`); bu pencereler çizelgede **boşluk** olarak çizilir ve görüşme sonu takip kararının toplamına
  (`FollowUpAdvisor.AggregateMeeting`) ve kritik anlara **girmez**. Bunun için `DecisionWindow`'a bellek içi
  "karşı taraf sustu" bayrağı; saklanırsa yalnız additive alan. *(Bizim kararımız: sessizliği nötr saymak anti-hedefe ters.)*
- **Kişi kartları — yalnız dosya modunda:** rol diarization konuşmacısıyla birebir eşleştiği için yönetenin kartında
  duygu yerine yalnız davranış (konuşma payı, konuşma hızı). Canlı modda kişi kartları (yalnız "Konuşmacı ayırımı" açıksa
  var) bugünkü gibi kalır; canlı rol ↔ diarization konuşmacısı eşlemesi bu işin dışında. *(Bizim kararımız.)*
- Rol bilgisi olmayan oturumlarda rapor başında görünür not: "Yöneten ayrılmadı — sonuçlar iki tarafın karışık sesinden."
- Takip kararı rol bilgili oturumda karşı tarafın göstergelerinden hesaplanır (pencere göstergeleri zaten öyle).

### D. Değişmeyenler
Duygu modelleri, öneri/AHP/TOPSIS motoru, canlı öneri havuzu, veritabanı şeması (ek alan gerekirse yalnız additive),
"Kaç kişi konuşacak?" seçimi, görüntü kanalı.

## Kısıtlar
- 8 GB M1: diarization (≈0,25 GB) ve analiz modelleri **aynı anda** yüklenmez (ModelWorkQueue sırası).
- Bekleme: diarization ölçülen hızla kayıt süresinin ~yarısı (231 sn → 123 sn); kullanıcıya ilerleme gösterilir.
- Gizlilik: Dinle parçası yalnız geçici klasörde, çalma sonrası silinir; ses diske kalıcı yazılmaz (mevcut şifreli oturum deposu hariç).
- Geçerli test komutları: `dotnet build SentimentAnalysis.sln`, `dotnet test SentimentAnalysis.sln`.

## Geliştiriciye bırakılanlar
Panel yerleşimi ve metinlerin tam hali (mevcut kart stiline uyar), Dinle parçasının seçimi (en uzun bölgenin ortası
vb.), ilerleme göstergesinin biçimi, sınıf/dosya adları, mevcut post-session konuşmacı akışına ön-hesaplı bölgeleri
aktarmanın tam yolu.
