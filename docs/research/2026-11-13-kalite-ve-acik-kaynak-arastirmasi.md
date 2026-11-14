**Duygu analizi ve görüşme desteği: kalite araştırması — 13 Kasım 2026**

Bu not, mevcut kodun incelenmesine ve proje geliştiricilerinin depoları, model kartları ve araştırma yayınlarına dayanır. Yeni modeller bu bilgisayarda gerçek Türkçe görüşmeler üzerinde çalıştırılmadı. Yayımlanan sonuçlar, bu projede elde edilmiş sonuçlar değildir. Aşağıdaki mimari ve ürün kararları öneridir; uygulanmış değişiklik değildir.

**Ana değerlendirme**

Sorunu yalnızca daha büyük bir model seçerek çözmek beklenmemeli. Sesin hangi kişiye ait olduğunun belirlenmesi, kelimelerin zamanlanması, duygu çıktılarının yorumlanması, güven hesabı ve önerinin bağlama oturması birbirini etkiliyor. Yanlış kişiye yazılan doğru bir duygu ölçümü bile kullanıcı açısından yanlış sonuçtur.

Önerilen öncelik: ölçüm düzeneği → konuşmacı ve kelime hizalaması → model çıktılarının doğrulanması → duygu kalibrasyonu → bağlama dayalı öneri → görüntü katkısı.

**Mevcut koddan somut bulgular**

| Bulgu | Kod kanıtı | Etkisi ve doğrulama ihtiyacı |
|---|---|---|
| Metin modeli çıktıları softmax ile işleniyor. | `src/SentimentAnalysis.Infrastructure.Emotion/TextVaLookup.cs:36` | Güncel model kartı bağımsız sigmoid kullanan çok etiketli sınıflandırma tarif ediyor. Yerel ONNX dosyasının kaynak revizyonu, etiketleri ve referans çıktıları doğrulanmalı. |
| Varsayılan metin ağırlığı iki duygu ekseninde de sıfır. | `src/SentimentAnalysis.Application/Configuration/AppConfig.cs:5` | Metin hesaplanıyor fakat nihai VA hesabına doğrudan katkı vermiyor. Ağırlığı rastgele artırmak yerine ses/metin/ikisi birlikte karşılaştırılmalı. |
| Prozodi güveni nötr merkezden uzaklıktan türetiliyor. | `src/SentimentAnalysis.Infrastructure.Emotion/ProsodyOutputMapper.cs:30` | Duygunun şiddeti ile ölçümün güvenilirliği birbirine karışıyor. Kazanç artırılınca aynı ham çıktı daha güvenilir sayılıyor. |
| Konuşmacı ataması kişi başına toplam örtüşme yerine tek bölgenin en büyük örtüşmesini seçiyor. | `src/SentimentAnalysis.Application/Diarization/SpeakerAligner.cs:73` | Bir kişi birkaç kısa turda daha çok konuşsa da diğer kişinin tek uzun turu kazanabiliyor. |
| Uzun analiz parçası tek kişiye atanıyor. | `SpeakerAligner.AlignChunks`; `PersonEmotionComposer.Compose` | Bir parçada iki kişi varsa karışık ses/metin duygusu bir kişiye mal edilebilir. Ayrı konuşmacı prozodi geçişi bulunması, bu karışık parça yolunu ortadan kaldırmıyor. |
| Canlı duygu birleşimi en görünür/güvenilir yüzü alıyor. | `src/SentimentAnalysis.Application/Fusion/FaceFusion.cs:14` | Seçilen yüz dinleyene ait olabilir. Rapordaki elle eşleme canlı akışın bu varsayımını düzeltmiyor. |
| Kısa ve az konuşan kümeler eleniyor. | `src/SentimentAnalysis.Application/Diarization/GhostSpeakerFilter.cs` | Hem toplam payı %8 altında hem süresi 6 saniyeden kısa olan gerçek katılımcı da elenebilir. Süre tek başına kimliğin sahte olduğunu kanıtlamaz. |
| Öneriler elle yazılmış etki puanlarından sıralanıyor. | `src/SentimentAnalysis.Application/Decision/DecisionKnowledgeBase.cs` | AHP tutarlılığı ve TOPSIS sıralaması, bir eylemin görüşmeye gerçekten faydalı olduğunu doğrulamaz. |
| İlgi kaybı başını çevirme oranından; düşük enerji konuşma hızı ve arousal'dan hesaplanıyor. | `src/SentimentAnalysis.Application/Decision/IndicatorCalculator.cs:37` | Not alma, düşünme, ekrana bakma ve kişisel konuşma tarzı sorun olarak yorumlanabilir. Gözlenen işaret ile yorumu ayırmak gerekir. |

Bu incelemede küçük, sentetik hesaplarla üç davranış gösterildi; bunlar gerçek model deneyi değildir:

- 10 saniyede A kişisi 3 ayrı turda toplam 6 saniye, B toplam 4 saniye konuştuğunda mevcut en büyük tek örtüşme mantığı B'yi seçebiliyor.
- İki etiketin logiti 4, diğer dokuzunun -4 olduğunda softmax bu ikisine yaklaşık 0,499; bağımsız sigmoid yaklaşık 0,982 veriyor. Çok etiketli modelin anlamı değişiyor.
- Aynı ham prozodi değerlerinde kazanç 1'den 2'ye çıkarıldığında mevcut güven hesabı 0,3'ten 0,6'ya çıkabiliyor. Ek kanıt gelmeden güven artıyor.

Metin modelinin güncel kartı Türkçeyi desteklenen diller arasında gösteriyor; eğitim verisi sentetik. Bu, gerçek Türkçe toplantılarda doğruluk ölçümü yerine geçmez. Kaynak: [tabularisai model kartı](https://huggingface.co/tabularisai/multilingual-emotion-classification).

Mevcut ses modeli MSP-Podcast üzerinde eğitilmiş, kartında İngilizce olarak etiketlenmiş bir boyutsal duygu modeli. Türkçede çalışmayacağı sonucu çıkarılamaz; Türkçe başarısı ayrıca ölçülmelidir. Kart araştırma kullanımı ve CC-BY-NC-SA-4.0 koşullarını belirtiyor. Kaynak: [audEERING model kartı](https://huggingface.co/audeering/wav2vec2-large-robust-12-ft-emotion-msp-dim).

**İncelenen açık kaynak projeler ve adaylar**

1. **pyannote.audio / Community-1 — konuşmacı ayrımı için ilk referans.** Community-1 tam bir diarization hattıdır; mevcut sherpa sarmalayıcısında bir pyannote segmentasyon modelinin bulunması aynı hattın kullanıldığı anlamına gelmez. Geliştiricinin karşılaştırmasında AMI-SDM DER değeri 3.1'de %22,7, Community-1'de %19,9. DER hata oranıdır, düşük olması iyidir. Model yerelde çalışabilir; ilk erişimde Hugging Face koşulları/token gerekir. Model lisansı CC-BY-4.0, araç kodu MIT. [Model ve ölçüm](https://huggingface.co/pyannote/speaker-diarization-community-1), [kod](https://github.com/pyannote/pyannote-audio).

2. **NVIDIA Nemotron-3-Diarization — canlı ve dosya analizi için güçlü ikinci aday.** Sekiz kişiye kadar akışlı ve çevrimdışı ayrım yapıyor. Üretici tablosunda DIHARD III tam sette, 30,4 saniye tampon ayarında Sortformer v2.1 %19,09 DER, Nemotron %12,73. Bu iki satır aynı protokolün karşılaştırmasıdır; Türkçe başarı garantisi değildir. OpenMDW-1.1 lisanslı açık ağırlıklı model. [Model kartı ve ölçümler](https://huggingface.co/nvidia/Nemotron-3-Diarization).

3. **NeMo-Speech.cpp — .NET ile entegrasyon için önemli seçenek.** Nemotron ve Sortformer için yerel C++ çalışma ortamı, CLI, C ABI ve CPU/Metal/Vulkan/CUDA derleme yolları sunuyor. Bu nedenle Python servisi zorunlu kabul edilmemeli. Ancak her modelin her arka uçtaki doğruluğu, belleği ve hızı hedef Mac'te ölçülmeli; ASR için yayımlanmış hızlar diarization hızı diye kullanılmamalı. [Resmî depo](https://github.com/NVIDIA/NeMo-Speech.cpp).

4. **WhisperX — kelime zamanlama ve konuşmacı–metin eşleştirme referansı.** Konuşmayı yazıya çevirme, forced alignment ve diarization adımlarını birleştiriyor. Türkçe için `mpoyraz/wav2vec2-xls-r-300m-cv7-turkish` varsayılan hizalama modeli tanımlı. Kendi belgesi üst üste konuşmanın ve konuşmacı ayrımının kusursuz olmadığını belirtiyor. Mevcut uygulamada özellikle kelime düzeyinde zamanlama yaklaşımı örnek alınmalı. [Depo](https://github.com/m-bain/whisperX), [Türkçe hizalama tanımı](https://github.com/m-bain/whisperX/blob/main/whisperx/alignment.py).

5. **Qwen3-ASR — Türkçe transkripsiyon için karşılaştırma adayı.** 0.6B/1.7B ASR modelleri Türkçeyi destekliyor. Aynı ailedeki ForcedAligner'ın desteklediği 11 dil arasında Türkçe yok. Transkripsiyon desteğiyle hizalama desteği karıştırılmamalı. Whisper turbo ve large-v3'e karşı kendi kayıtlarımızda kelime hatası, gecikme ve bellek ölçülmeden motor değişimi önerilmiyor. [Resmî depo ve dil tablosu](https://github.com/QwenLM/Qwen3-ASR).

6. **emotion2vec+ — ses duygusu için deney adayı.** Base ve large sürümleri karşılaştırılabilir. Large sürüm dokuz sınıf ve yaklaşık 300M parametre; pseudo-label verilerle genişletilmiş eğitim kullanıyor. Karttaki geniş genelleme iddiası, bu uygulamanın Türkçe görüşmelerinde doğrulanmış bir sonuç değildir. Kendi sınıfları doğrudan mevcut sekiz duygu etiketine zorlanmamalı; model lisansı ayrıca kontrol edilmeli. [Resmî kod](https://github.com/ddlBoJack/emotion2vec), [model kartı](https://huggingface.co/emotion2vec/emotion2vec_plus_large).

7. **EmoBox ve MERBench/MERTools — ölçüm ve füzyon araştırması için.** EmoBox, 32 veri kümesi ve 14 dil üzerinde duygu karşılaştırmaları sunuyor; Türkçe TurEV-DB de listede. TurEV-DB küçük, oyunculuk temelli bir veri kümesi; gerçek toplantı testinin yerini tutmaz. MERTools farklı kanalların katkısını ve gürültü dayanımını araştırmak için uygun kaynak. MER veri kümelerinin araştırma koşulları var. [EmoBox](https://github.com/emo-box/emobox), [MERTools](https://github.com/zeroQiaoba/MERTools).

8. **LibreFace / OpenFace — yüz davranışı için.** Yüz kas hareketleri, baş yönü ve bakış gibi ölçülebilir işaretlere odaklanmak için incelenmeli. LibreFace C# ve Python yolları sunuyor; USC araştırma lisansına sahip. Ürüne doğrudan kopyalanabilir, kısıtsız ticari bileşen varsayılmamalı. [LibreFace](https://github.com/ihp-lab/LibreFace), [OpenFace](https://github.com/TadasBaltrusaitis/OpenFace).

9. **TalkNet-ASD — görüntüde kimin konuştuğunu bulmak için.** Ses–dudak hareketi eşzamanlılığını kullanır. AVA testinde yayımlanan mAP 90,8; bu bir duygu doğruluğu veya Türkçe toplantı doğruluğu değildir. Doğal toplantı videolarında aktarım ve görüntü kalitesi ayrıca sınanmalı. Mevcut en görünür yüz varsayımına araştırılabilir alternatif. [Resmî depo](https://github.com/TaoRuijie/TalkNet-ASD).

10. **Meetily — ürün ve kullanım akışı referansı.** Yerel kayıt, transkripsiyon, model yönetimi, yeniden işleme ve özet akışları incelenebilir. Depo MIT; README bazı konuşmacı tanıma özelliklerini Pro sürüme ayırıyor. Bu nedenle tüm tanıtılan özelliklerin açık kaynak sürümde bulunduğu varsayılmamalı. Duygu veya görüşme tavsiyelerinin doğruluğuna dair bir referans olarak kullanılmıyor. [Resmî depo](https://github.com/Zackriya-Solutions/meetily).

11. **Diart — düşük gecikmeli ayrım için alternatif araştırma.** Örtüşmeyi dikkate alan çevrimiçi kümeleme kullanıyor; 0,5–5 saniye gecikme aralığını inceliyor. Mevcut model/sürüm uyumluluğu ayrıca kontrol edilmeli. İlk karşılaştırmada Community-1 ve Nemotron öncelikli, Diart ikinci tur adayı. [Resmî depo](https://github.com/juanmc2005/diart).

Popülerlik için erişim anında GitHub yaklaşık 25,2 bin WhisperX, 10,8 bin pyannote.audio ve 32,7 bin Meetily yıldızı gösteriyordu. Bu sayılar kalite ölçümü değildir; kullanım yaygınlığını belirtir.

**Önerilen çalışma biçimi**

Avalonia/.NET arayüzü ve mevcut katman sınırları korunabilir. Deneyleri aynı kayıtlar üzerinde bağımsız motorlarla yapabilen bir değerlendirme katmanı eklenmesi öneriliyor. Önce Python ile referans sonuç üretmek kolay olabilir; başarılı motorun ürüne C ABI, CLI veya yalnız yerel çalışan ayrı süreç olarak bağlanması daha sonra seçilebilir. Tüm modeli ONNX'e taşıma zorunluluğu araştırmayı gereksiz yere kısıtlar. C++ çalışma ortamına geçmek de tek başına doğruluğu artırmaz.

Ses yakalamada mikrofon ve karşı tarafın sistem sesi ayrı elde edilebiliyorsa kaynak kimliği korunmalı. Ayrı kanalları başta mono olarak karıştırıp sonra yeniden ayırmaya çalışmamak iyi bir tasarım hedefidir. Aynı odadaki tek mikrofon senaryosunda ise diarization gerekir. Gürültü giderme özellikle prozodi sinyallerini değiştirebileceğinden, ASR için işlenmiş ve duygu için orijinal/denetlenmiş ses yolları ayrı karşılaştırılmalı.

Canlı görünüm hızlı ve geçici sonuç göstermeli. Oturum sonunda daha fazla bağlamla yeniden işleme nihai raporu oluşturmalı. Konuşmacı dönüşleri ve kelimeler zamanlandıktan sonra kişi başına duygu hesaplanmalı. Üst üste konuşmada karışık ses bir kişiye kesin duygu olarak yazılmamalı. Yüz eşlemesi yoksa ses ve yüz ayrı gösterilmeli.

İlk duygu ürününde üç geniş sınıf (olumlu/nötr/olumsuz), enerji eğilimi ve belirsizlik öneriliyor. Sekiz duygu ayrımı ancak etiketli testte faydası gösterilirse eklenmeli. Üç sınıf otomatik olarak daha doğru kabul edilmeyecek; sekiz sınıfla aynı test setinde kıyaslanacak. Nötr sonuç düşük güven anlamına gelmemeli. Güven, held-out veride gerçek hatayla ilişkilendirilmeli; doğruluk–kapsam eğrisi raporlanmalı.

Yüz ifadesinden iç duyguyu doğrudan çıkarmanın bağlam ve kişiler arasında ciddi sınırları var. Bu yüzden “başını çevirdi” ile “ilgisi kayboldu” ayrı çıktılar olmalı. [Bilimsel inceleme](https://pmc.ncbi.nlm.nih.gov/articles/PMC6640856/).

**Öneri motoru için değişiklik önerisi**

Öneriler üç bilgi grubuna dayanmalı: görüşmenin amacı ve roller; son konuşma turlarının içeriği; güvenilir ve süreklilik gösteren davranış ölçümleri. “Olumsuz ses → empati kur” gibi tek adımlı ilişki yetersizdir. Kullanıcının ihtiyacına göre örnek çıktı:

> Karşı taraf teslim tarihini iki kez sordu ve net bir tarih verilmedi. Teslim gününü ve gecikme ihtimalini açıklayın.

Bu, önerilen ürün davranışıdır; doğrulanmış bir danışmanlık modeli değildir. İlk aşamada az sayıda, açık kanıtı olan kural; ikinci aşamada son konuşma bağlamından yapılandırılmış olay çıkaran yerel dil modeli karşılaştırılmalı. Her öneri zaman damgalı kanıt, hedef kişi/rol ve önerilmeme seçeneği taşımalı. Dil modeli olay çıkarma ve ifade üretiminde kullanılabilir; ham duygu ölçümünü veya eylemin gerçekten faydalı olduğunun kanıtını üstlenmemeli.

Yapılandırılmış JSON çıktısı teknik olarak sınırlandırılabilir; bu yalnız biçimi denetler, doğruluğu garanti etmez. [llama.cpp gramer/JSON desteği](https://github.com/ggml-org/llama.cpp/blob/master/grammars/README.md).

AHP/TOPSIS gerekiyorsa doğrulanmış olaylar için aday eylemleri sıralayan son katman olarak kalabilir. Etki puanlarını faydalılık etiketleriyle ölçmeden tabloları büyütmek önerilmiyor. Öneri çeşitliliği de sırf ekran değişsin diye yapılmamalı; aynı sorun sürüyorsa tavsiyeyi döndürmek yerine yeni kanıt veya kullanıcı geri bildirimi beklenmeli.

**İlk deney paketi ve karar ölçütleri**

Başlangıç önerisi: farklı konuşmacılardan yaklaşık 20–30 kısa kayıt ve toplam 2–3 saat hedef alan Türkçe pilot küme. Bu miktar model eğitmeye veya genellenebilir başarı iddiasına yetmez; belirgin hata kaynaklarını bulmaya yarar. Aynı kişinin veya örtüşen ses parçalarının geliştirme ve test tarafına sızması engellenmeli. Sessizlik, benzer sesler, kısa katılım, gürültü, karşılıklı hızlı konuşma, örtüşme ve farklı mikrofonlar bulunmalı.

| Deney | Karşılaştırma | Raporlanacak sonuç |
|---|---|---|
| Konuşmacı ayrımı | Mevcut sherpa; Community-1; Nemotron | DER, speaker confusion, kişi sayısı hatası, gecikme, RAM; bilinen/bilinmeyen kişi sayısı ayrı |
| Yazıya çevirme | Mevcut turbo; Whisper large-v3; Qwen3-ASR | Türkçe WER/CER, özel isim/sayı hataları, kelime zamanlama hatası, gerçek zaman oranı |
| Ses duygusu | Mevcut ham ve kazançlı çıktı; emotion2vec+ base/large | Macro-F1/UAR; VA için CCC/MAE; konuşmacı bazında hata; belirsiz bırakılan örnek oranı |
| Metin işleme | Referans tokenizer/model ile mevcut ONNX | Token ID, etiket sırası, ham logit eşliği; sigmoid/softmax etkisi; tek cümle/bağlam farkı |
| Füzyon | Yalnız ses; yalnız metin; ses+metin; eşleşmiş yüz eklenmiş | Her kanalın gerçekten katkısı ve yanlış kişi aktarımı; kalibrasyon; doğruluk–kapsam |
| Öneri | Mevcut TOPSIS; kanıtlı kurallar; kurallar+bağlam modeli | Kör insan değerlendirmesi: ilgililik, uygulanabilirlik, yanlış uyarı/saat, kaçırılan olay, uydurma kanıt |

Konuşmacı ölçümü için [pyannote-metrics](https://github.com/pyannote/pyannote-metrics) kullanılabilir. DER karşılaştırmalarında aynı referans, collar ve overlap kuralları kullanılmalı. NVIDIA ve pyannote tablolarındaki farklı veri/etiket protokollerini doğrudan bir lig tablosuna koymamak gerekir.

İlk pilotta bileşen hatasını ayırmak için “doğru transkript verilince”, “doğru konuşmacı sınırı verilince”, “görüntü kapalıyken” deneyleri yapılmalı. Böylece öneri kötüyse bunun metin hatası mı, kişi hatası mı, duygu hatası mı, karar kuralı mı olduğu görülebilir.

Üretime geçiş eşikleri mevcut başlangıç ölçümü ve kullanım amacı görüldükten sonra belirlenmeli. Bu araştırmadan “yeni modelle %95 doğruluk” gibi bir sayı çıkarılamaz. İlk somut teslimat, aynı Türkçe kayıtlar üzerinde mevcut sistemle iki alternatifin karşılaştırıldığı yeniden üretilebilir kalite raporu olmalıdır.
