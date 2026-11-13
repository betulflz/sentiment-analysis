**İterasyon 1 — ölçüm ve görünür hata uygulama planı**

Dayanak: `docs/research/2026-11-13-bes-iterasyon-strateji-taslagi.md`, İterasyon 1; kullanıcı bu iterasyona başlamayı onayladı.

Amaç: sessizlik/başarı/hata/kuyruk kaybı/iptal durumlarını ayırmak, dosyaları eksiksiz işlemek, aynı veri ve kaynak sürümüyle tekrar üretilebilir bir ses→duygu→karar→özet koşusu oluşturmak. Kamera, yeni motor seçimi ve duygu/öneri kalibrasyonu bu iterasyonun dışında.

- [x] **Pipeline gözlemi:** `Core/AnalysisDiagnostics.cs` içinde `ChunkDisposition`, `ChunkObservation`, `AnalysisDiagnostics` ekle. `Application/Pipeline/PipelineJournal.cs` yakalanan parçaları ve sonuçlarını thread-safe kaydeder; süreleri aralık birleşimiyle hesaplar. Testler: örtüşme iki kez sayılmaz; sessizlik ve hata ayrıdır; iptal edilen parça görünür; zenginleştirme hatası saklanır; her koşu sıfırlanır.
- [x] **Eksiksiz dosya akışı:** `EmotionPipeline.RunAsync` sonuna varsayılanı canlı olan `PipelineExecutionMode` ekle. Dosyada bounded Wait; canlıda DropOldest. Üretici hata/iptal sonrası kapatılır, deadlock oluşmaz. Test: kapasite 1 ve yavaş işlemciyle dosyada kayıp 0; canlı taşması kaydedilir; depo hatası üreticiyi geride bırakmaz.
- [x] **Rapora aktarım:** `MeetingSummary.Diagnostics` additive alanı; `ReportViewModel.AnalysisQualityNotice` durum metni; `ReportView.axaml` mevcut özet kartında bir satır. Bilinmeyen eski rapor kapsamı %100 sayılmaz. `PipelineAnalysisRunner` ses dosyası modunda dosya stratejisini seçer. Arayüz düzeni değiştirilmez.
- [x] **Gerçek ses-rapor koşusu:** Benchmark `analyze` komutu üretimdeki ChunkProcessor/Fusion/Pipeline/Aggregator/Decision motorlarını çağırır; modeller önceden doğrulanır; SQLite, kamera ve Avalonia kullanılmaz ve bu sınır JSON'da belirtilir. Model hash'leri, ayarlar, gözlem kayıtları ve başarısızlıklar çıktıdadır. Modelle doğrulanamayan örnek başarı sayılmaz. Testler model gerektirmeyen bağımlılıklar ve CLI sözleşmesiyle yapılır.
- [x] **Veri kökeni ve ayrımı:** Python araçlarına `prepare` ve `report` ekle. `prepare`: gerçek WAV süresi, SHA-256, geliştirme/test ve anonim katılımcı gruplarını doğrular; aynı kişinin iki gruba sızmasını reddeder. Koşu ses hash'lerini ve git sürüm/durumunu kaydeder; skor değişmiş sesi reddeder. Karma küme raporunda test/geliştirme skorları ayrılır. Sentetik veri açıkça etiketlenir.
- [x] **Doğrulama:** önce başarısız regresyon testleri, sonra uygulama; .NET/Python testleri, çözüm derlemesi, modelsiz hata koşuları, sentetik rapor örneği ve bağımsız kod incelemesi. Gerçek kayıt/model yoksa başlangıç kalite raporu tamamlandı denmez; hazır ölçüm ile gerçek ölçüm ayrılır.

Komutlar: `dotnet test SentimentAnalysis.sln --nologo --verbosity quiet`, `python3 -m unittest discover -s tools/quality -v`, `dotnet build SentimentAnalysis.sln --no-restore --nologo --verbosity quiet`.

Not: Önceki uncommitted çalışmalar korunur. Gerçek kullanıcı sesi, referans metinleri veya model dosyaları git'e eklenmez. Kod ve araçlar bu oturumda uygulanır; yeni onay döngüsü gerektiren yayın/deploy işlemi yoktur.

**Doğrulama — 13 Kasım 2026:** 843 .NET testi geçti, 16 model/ortam testi atlandı; 25 Python testi geçti. Dosya kuyruğu, kapsama, iptal/drenaj, depo hatası, veri kimliği, ayrım sızıntısı ve kısmi analiz regresyonları doğrulandı. Üç CLI görevi eksik modelde çıkış 2 verdi, tahmin üretmedi. Sentetik prepare→run→report akışı denendi; gerçek model başarısı değildir. Bağımsız incelemedeki önemli bulgular giderildi.

**Açık kalan gerçek ölçüm:**

- [ ] Tam üretim model setini ve gerçek görüşme kayıtlarının konumlarını belirle.
- [ ] Pilot referanslarını insan denetiminden geçir; geliştirme/test ayrımını dondur.
- [ ] M1 Air üzerinde mevcut motorlarla başlangıç hata/süre/bellek raporunu üret.

Bu üç adım olmadan İterasyon 1'in gerçek veri değerlendirmesi tamamlandı sayılmaz. Araçlar ve kılavuz `tools/quality/ITERATION-1.md` içinde hazır.

**Devam çalışması — dış transkripsiyon pilotu:** FLEURS Türkçe test verisinin 6 satırı ve yayınlanmış referanslarıyla base q5_1, small q5_1 ve mevcut large-v3-turbo q5_0 gerçekten çalıştırıldı. Dosya boyutları ve model/veri SHA-256 değerleri doğrulandı; varlıklar git dışındaki `quality-local/public-turkish-pilot/` klasöründe. Bu, insan denetimli iki kişilik görüşme testi değildir. Ölçüm sonuçları `docs/research/2026-11-13-fleurs-turkce-on-olcum.md` dosyasında. Varsayılan model değiştirilmedi. Eklenen doctor ve kuantizasyon seçimi test edildi; güncel sonuç 845 .NET testi geçti, 16 atlandı, 27 Python testi geçti.
