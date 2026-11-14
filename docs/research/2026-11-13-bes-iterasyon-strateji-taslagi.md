**M1 Air için beş iterasyonluk gelişim stratejisi — taslak**

Tarih: 13 Kasım 2026. Durum: kullanıcının değerlendirmesine açık strateji; uygulanmış veya tüm ayrıntıları onaylanmış teknik tasarım değildir. Bu turda uygulama kodu değiştirilmedi.

**Hedef ve başlangıç varsayımı**

Hedef, Türkçe görüşmelerde her gün kullanılabilecek bir yerel yardımcıya yaklaşmak. İlk kapsam varsayımı iki kişilik kayıt ve görüşme sonu raporu; canlı görünüm geçici bilgi sunar. Kullanıcı canlı görüşmeyi veya üç ve daha fazla kişiyi önceliklendirirse veri dağılımı ve ikinci iterasyonun motor önceliği buna göre değişir.

“6–7 seviyesi” tek bir doğruluk yüzdesi olarak tanımlanmıyor. Kullanıcı konuşmacı ve metni büyük ölçüde takip edebilmeli, hatalı satırı düzeltebilmeli, duygu sonucunun belirsizliğini görebilmeli, önerinin dayandığı cümleyi açabilmeli ve 8 GB M1 Air üzerinde görüşmeyi tamamlayabilmeli. Duygu okumada kesinlik veya her koşulda kusursuz kişi ayrımı hedeflenmiyor.

Önceki çalışmada konuşmacı örtüşme hesabı düzeltildi ve bileşen ölçüm aracı eklendi. Bu, hazırlık aşamasıdır. Gerçek Türkçe görüşmeler üzerinde karşılaştırmalı başarı sonucu henüz yoktur.

**Yaklaşım seçenekleri**

| Seçenek | Kazanç | Maliyet | Karar |
|---|---|---|---|
| Yalnız mevcut modellerin eşiklerini ayarlamak | En az entegrasyon, mevcut dağıtım korunur | Model sınırları, yanlış çıktı yorumu ve uzun parça–kişi sorunu sürer | Kontrol grubu ve belirli düzeltmeler için kullan |
| Analiz hattını bütünüyle Python araştırma yığınına taşımak | Hazır araştırma modellerini denemek kolay | Paketleme, açılış, RAM ve native bağımlılık yükü; mevcut uygulamanın yeniden bağlanması | Ürün için başlangıç seçimi değil |
| .NET arayüzünü koruyup analiz motorlarını yerelde değiştirilebilir yapmak | Araştırmada Python, üründe uygun C++/ONNX/.NET yolu seçilebilir | Net veri sözleşmeleri ve süreç yaşam döngüsü gerekir | Önerilen yaklaşım |

Bu yaklaşımda bir araştırma referansının ürünle aynı teknolojiyle çalışması zorunlu değil. Önce güçlü referansla mevcut hatanın ne kadar azaltılabildiği görülür; sonra hedef cihazda uygun çalışma ortamı seçilir. Bir motoru C++ ile çalıştırmak kendi başına doğruluğu artırmaz.

**Açık kaynak kullanım yöntemi**

Her aday için tek sayfalık kayıt tutulur: hangi problemi çözüyor, hangi sürüm/commit ve ağırlık kullanıldı, lisansı, girdi/çıktı sözleşmesi, aynı kayıtlar üzerindeki hata, süre, bellek ve bilinen başarısız örnekler. Kaynak kodu lisansı ile model/veri lisansı ayrı kaydedilir. Lisans ve erişim koşulları doğrulanmadan ürün paketine eklenmez.

Üç farklı kullanım biçimi var: algoritmayı anlamak için kaynak incelemek; karşılaştırma için yerel referans çalıştırmak; ölçüm kazananını ürüne bağlamak. Üçüncü adım ilk ikisinin otomatik sonucu değildir.

- [WhisperX](https://github.com/m-bain/whisperX): kelime hizalama ve konuşmacıyla birleştirme yaklaşımı; Türkçe hizalama yolu ayrıca sınanır. Tüm Python yığını doğrudan ürüne alınmak zorunda değil.
- [Community-1](https://huggingface.co/pyannote/speaker-diarization-community-1): konuşmacı ayrımı için karşılaştırma referansı; indirme koşulları kabul edildikten sonra yerel kullanım mümkün. Ürün seçimi M1 ölçümüne bağlı.
- [NeMo-Speech.cpp](https://github.com/NVIDIA/NeMo-Speech.cpp): Nemotron/Sortformer deneyi için yerel native çalışma yolu. Metal desteğinin bulunması, seçilen modelin hedef cihazda otomatik olarak hızlı veya doğru olacağı anlamına gelmez.
- [emotion2vec+ base](https://huggingface.co/emotion2vec/emotion2vec_plus_base): yaklaşık 90M parametreli duygu adayı. İlk ses duygusu deneyinde large yerine base denenir; Türkçe görüşme başarısı ayrıca ölçülür.
- [EmoBox](https://github.com/emo-box/emobox): duygu değerlendirmesi ve Türkçe TurEV-DB dahil veri hazırlama referansı. Küçük, oyunculuk temelli veri gerçek toplantıların yerine geçmez.
- [Meetily](https://github.com/Zackriya-Solutions/meetily): kayıt, yeniden işleme ve rapor kullanım akışı referansı. Pro özellikleri açık kaynak sürümde mevcut varsayılmaz.
- [llama.cpp](https://github.com/ggml-org/llama.cpp/blob/master/grammars/README.md): gerekirse dördüncü iterasyonda yerel, yapılandırılmış olay çıkarımı için. JSON biçimini sınırlandırmak içerik doğruluğunu kanıtlamaz.

**M1 Air kaynak politikası**

Asıl ölçüm tüm uygulama ve model alt süreçlerinin toplam yerleşik bellek kullanımı olacak. Mevcut pilotun süreç başına tepe RSS'i başlangıç ölçümüdür; birbirini izleyen süreçlerin ayrı tepeleri eşzamanlı toplam değildir. Araştırma motorlarının rahat çalışması uygulamanın da aynı bellekle çalıştığını göstermez.

Başlangıç mühendislik hedefi: normal akışta uygulama ve model süreçleri için yaklaşık 3 GiB, kısa tepede 4 GiB bütçe. Bunlar ölçülmüş kapasite veya sert sınır değil; ilk gerçek koşudan sonra gözden geçirilecek hedeflerdir. Sistem bellek baskısı, swap değişimi ve arayüz tepkisi de raporlanır. Tek başına 4 GiB altında olmak geçer not değildir.

Canlı seste gerekli modeller tutulur. Tam kayıt konuşmacı ayrımı, ağır kelime hizalama ve varsa dil modeli görüşme sonunda sırayla çalıştırılır; kullanılmayan oturumlar boşaltılır. Bu yaşam döngüsü mevcut singleton yapıda ayrıca tasarlanmalıdır; yalnızca yeni bir config değeri eklemek belleği boşaltmaz. GPU/Metal ve ONNX iş parçacığı ayarları 1/2/4 seçenekleriyle ölçülür; tek bir katmandaki kazanç bütün uygulama için kazanç varsayılmaz.

Her aday önce kısa bir kayıtta kaynak uygunluğu, sonra kalite testi, sonra uzun görüşme testi görür. Bütçeyi aşan aday, ölçümde referans olarak kalabilir; varsayılan motor yapılmaz. Sıkıştırma veya kuantizasyon sonrası doğruluk tekrar ölçülür. Kendi Türkçe etiketli verimiz oluşmadan model eğitimi ilk iş değildir.

**İterasyon 1 — doğru ölçüm ve görünür hata**

Kullanıcıya kazanım: sessizce eksilen veya yanlış anlaşılması kolay sonuçlar görünür olur; sonraki kararlar aynı test üzerinde verilir.

Önce 12 kısa Türkçe kayıtla başlayıp 20–30 kayda genişleyen bir pilot hazırlanır. Aynı kişiler ve aynı kaydın komşu parçaları geliştirme/test taraflarına sızmaz. Temiz/gürültülü ses, benzer sesler, kısa katılım, hızlı sıra değişimi, sessizlik ve örtüşme bulunur. Birden çok insanın duygu/öneri etiketinde anlaşamadığı örnekler ayrıca tutulur; tartışmalı örneğe kesin doğru dayatılmaz.

Mevcut ham bileşen koşusuna ek olarak dosya kaynağından rapora kadar tam uygulama yolu ölçülür. VAD tarafından atlanan sessizlik, işlem hatası, kanal taşması ve durdurma sonrası atlanan parça ayrı nedenlerdir. `EmotionPipeline` bugün bileşen hatasını teşhis günlüğüne yazıp devam edebildiğinden, raporda eksik kapsama ve hata durumu görünmesi gerekir.

Manifest, ses dosyası hash'i, kaynak revizyonu, model hash'i ve çalışma ayarları birlikte kaydedilir. Her kayıt için hem doğru metin hem doğru konuşmacı verilmiş kontrollü deneylerle hatanın hangi aşamada büyüdüğü ayrılır. Önceki aracın WER/CER/DER kapsamı başlangıçtır; duygu, öneri ve tam uygulama kapsamı henüz yoktur.

Çıkış koşulu: tekrar üretilebilir başlangıç raporu; başarısız/eksik örneklerin açık listesi; sabit geliştirme ve dokunulmamış test ayrımı. Bu iterasyonun başarısı model doğruluğunun artmasıyla değil, sonraki karşılaştırmanın güvenilir olmasıyla değerlendirilir.

**İterasyon 2 — doğru konuşmacı, doğru metin, doğru zaman**

Kullanıcıya kazanım: cümlelerin yanlış kişiye yazılması ve kişinin konuşmadığı sesin ona mal edilmesi azalır.

Öncelik, mevcut sherpa ile Community-1 referansını ve kaynak uygunsa Nemotron native adayını karşılaştırmak. İlk taramadan sonra yalnız en iyi iki hat ayrıntılı değerlendirmeye alınır. Kişi sayısı verilmiş ve verilmemiş deneyler ayrı tutulur. Kısa bir katılımcıyı yalnız konuşma süresinden dolayı silen kural yeniden değerlendirilir.

Kelime hizalaması için WhisperX yaklaşımı örnek alınır; böylece kelime/cümle ve konuşmacı dönüşü aynı zaman eksenine gelir. Uzun, birden fazla kişinin bulunduğu parçayı tek kişinin duygusu olarak kullanma yolu kaldırılacak şekilde tasarlanır. Ayrı kanallı kayıtta kaynak kimliği korunur. Üst üste konuşma, tek bir kişiye kesin atamayla gizlenmez.

Transkripsiyonda önce mevcut turbo ile daha küçük bir Whisper seçeneği hız/kalite açısından kıyaslanır. Başka ASR ailesi ancak hata örnekleri mevcut aileyi yetersiz gösteriyorsa eklenir; aynı anda çok sayıda motoru paketlemeyiz.

Önerilen çıkış eşiği: test kümesindeki konuşmacı karışıklığı süresinde başlangıca göre en az %20 göreli azalma; transkripsiyon ve kaynak kullanımında kabul edilemez gerileme olmaması. Bu bir hedef, ölçülmüş sonuç değildir. Başlangıç hatası çok düşükse veya örnek sayısı yetersizse tek yüzde yerine kayıt başına sonuç ve belirsizlik aralığı değerlendirilir.

**İterasyon 3 — daha az iddialı, daha güvenilir duygu**

Kullanıcıya kazanım: nötr sesin düşük güven sayılması, birkaç kişinin duygusunun karışması ve sürekli değişen etiketler azalır.

Önce mevcut ONNX dosyalarının kaynak revizyonu, tokenizer'ı, etiket sırası ve ham logitleri referans uygulamayla doğrulanır. Güncel metin kartındaki bağımsız sigmoid tanımıyla mevcut softmax kodunun uyuşması kontrol edilir; yerel dosyanın semantiği doğrulanmadan otomatik değiştirilmez. [Güncel model kartı](https://huggingface.co/tabularisai/multilingual-emotion-classification).

Mevcut ses modeliyle emotion2vec+ base, kişiye ait temiz konuşma turlarında kıyaslanır. Her modelin kendi çıktı anlamı korunur; kategorik duygu modeli, doğrulanmamış sabit tabloyla doğrudan boyutsal VA modeliymiş gibi ele alınmaz. İlk kullanıcı çıktısı olumlu/nötr/olumsuz eğilim, enerji ve belirsizlik olabilir. Üç sınıfın sekiz sınıftan daha faydalı olduğu ayrıca ölçülür.

Nötrden uzaklığa dayanan güven yerine, ayrılmış kalibrasyon verisinde gerçek hatayla ilişkisi ölçülen bir güven politikası tasarlanır. Kalite, duygu şiddeti ve model belirsizliği ayrı alanlar olur. Yalnız ses, yalnız metin ve ikisi birlikte deneylerinde katkı vermeyen kanal çıkarılabilir. Kararsız parçalar için etiket üretmeme seçeneği bulunur; bunun kapsam kaybı da raporlanır.

Çıkış koşulu: konuşmacısı doğru verilmiş durumda ve uçtan uca durumda ayrı duygu raporu; sınıf başına hata, macro-F1/UAR ve güven–kapsam tablosu. Sonuç yalnızca çok az örnekte tahmin yaparak iyileştirilmemeli. Güvenilir bir duygu kazancı gösterilemiyorsa belirli duygu adları yerine sınırlı eğilim sunulur ve dördüncü iterasyondaki kurallar buna bağımlı yapılmaz.

**İterasyon 4 — konuşma bağlamına dayanan öneri**

Kullanıcıya kazanım: genel tavsiyeler yerine görüşmenin gerçekten ele alınabilecek noktalarına yönelik, kanıtı açılabilen öneriler.

Önce 3–5 açık olay seçilir: tekrar sorulup yanıtlanmayan soru, belirsiz kalan sonraki adım, uzun süre tek kişinin konuşması gibi. Olayın tespit edilmesiyle hangi eylemin faydalı olduğu ayrı doğrulanır. Satış/mülakat/toplantı profilleri ortak ölçümleri paylaşır; ayrı ve geniş üç öneri dünyasını aynı anda geliştirmeyiz. İlk profil kullanım önceliğine göre seçilir.

İlk karşılaştırma mevcut TOPSIS, kanıta dayalı az sayıda kural ve kurallara yardımcı bağlam çıkarımı arasında yapılır. Küçük, kuantize yerel dil modeli ancak Türkçe olay çıkarımında ek fayda sağlıyor ve kaynak bütçesine sığıyorsa eklenir. İlk kullanım görüşme sonu raporu olabilir; sürekli çalışan ikinci ağır model varsayılmaz.

Örnek hedef çıktı: “Karşı taraf teslim tarihini iki kez sordu. Net bir gün verilmedi. Teslim gününü ve gecikme ihtimalini açıklayın.” Yanında kaynak cümleler ve zamanları bulunur. Kanıt yoksa öneri yoktur. Aynı problemi ekranı çeşitlendirmek için farklı tavsiyelerle döndürmek yerine, yeni kanıt veya geri bildirim beklenir.

Çıkış koşulu: kör insan değerlendirmesinde ilgililik ve uygulanabilirlik mevcut sisteme göre iyileşmeli; yanlış uyarı/saat ve kaçırılan olay birlikte raporlanmalı. Başlangıç hedefi gösterilen önerilerin en az %80'inin değerlendiricilerce ilgili bulunmasıdır; öneri üretmeyerek hedefe ulaşmayı engellemek için olay yakalama oranı da izlenir. Test örneğinde kaynağı bulunamayan alıntı kabul edilmez. Bunlar proje hedefleridir, hazır modelin performans iddiası değildir.

**İterasyon 5 — günlük kullanıma uygun ürün**

Kullanıcıya kazanım: başlat/durdur, kayıt açma, yeniden işleme, kişi/metin düzeltme ve raporu tekrar açma akışlarının tamamlanması.

Motor seçimiyle birlikte model oturumlarının açılıp kapanması uygulamaya bağlanır. Dosya modunda eksiksiz işlem; canlı modda gecikme birikmesini denetleyen kuyruk ve görünür eksik kapsam uygulanır. Geçici canlı sonuç ile yeniden hesaplanan rapor ayrılır. Ayarların gerçekten uygulanması, saklama tercihi ve geçmiş raporu açma tamamlanır. Kullanıcı düzeltmesi yalnız bu oturumda kullanılır; ayrı onay ve ayrılmış veri süreci olmadan test kümesi model eğitimine dönüşmez.

30 dakikalık görüşme ve tekrarlı başlat/durdur denemeleri M1 Air'de yapılır. İlk dakikayla son dakikanın bellek, işlem süresi ve gecikmesi karşılaştırılır. Uzun testte yalnız mikro-benchmark'ın hızlı olması yeterli değildir. Canlı işlem için sürekli RTF<1, tercihen 0,7 altında pay hedeflenir; gerçek güncelleme gecikmesi parça birikme süresini de içerir. Örtüşmeli 10 saniyelik parçalarda yalnız işlem süresini söyleyerek “anlık” iddiası yapılmaz.

Görüntü bu aşamada ek değer deneyi olarak ele alınır. Yüz–konuşmacı eşlemesi doğrulanmadan yüz, kişinin ses duygusuna karıştırılmaz. Açık kazanç veya kaynak uygunluğu yoksa kamera kapalı kalır. Kamera eklemek beşinci iterasyonun zorunlu başarı koşulu değildir.

Çıkış koşulu: hedef cihazda baştan sona tamamlanan görüşme; kaynak bütçesine uygun kullanım; görünür hata/eksik kapsam; kayıttan tekrar açılabilen ve kullanıcı tarafından kontrol edilebilen rapor. Sınırlı hedef senaryoda kullanılabilirlik değerlendirmesi yapılır; üç ve daha fazla kişiye genelleme ayrı test gerektirir.

**Her iterasyonun ortak kapanışı**

Her turda kullanıcıya dört somut çıktı sunulur: çalışan küçük bir iyileştirme; aynı kayıtlarda önce/sonra karşılaştırması; neyin hâlâ kötü olduğunun örnekleri; bir sonraki turda tutulacak/çıkarılacak yaklaşımın gerekçesi. Test sayısı bu raporun kalite ölçüsü değildir.

Yeni motor, önceki motorla eşit koşulda karşılaştırılır; model, bölümleme ve son işleme aynı anda değiştirilirse etkileri ayrı deneylerle ayrıştırılır. Duygu ölçümünde doğru transkript/konuşmacı verilmiş kontrol koşulu korunur. Bileşen iyileşmesi tüm uygulamada da görünmeden iş tamamlanmış sayılmaz.

Bir turun hedefi tutmazsa sonraki özelliği üstüne yığmayız. Başarısız aday saklanacak bir varsayılan motor değil, neden elendiği yazılı bir deney olur. Beş iterasyonda ulaşılacak seviye önceden garanti edilemez; hedef, dar kullanımda ölçülmüş ve açıklanabilir bir kalite artışıdır.
