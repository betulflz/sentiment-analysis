# M1 model kaynak yönetimi

Amaç: analiz bittiğinde yerel model oturumlarını kapatmak; canlı analiz/hazırlık ve görüşme sonrası konuşmacı geçişinin ağır modellerini aynı anda belleğe yüklememek.

Tasarım: uygulama/ayarlar/veritabanı kök sağlayıcıda kalır. Her model işi bağımsız, kapatılabilir bir DI sağlayıcısına sahip olur. Ortak tek işlik kuyruk hazırlıktan temizliğin sonuna kadar tutulur. Çalışan görevin bitmesi beklenmeden native nesneler kapatılmaz. Yüz zaman çizelgesi ve rapor kimliği model nesnelerinden ayrı saklanır. Hazırlık UI dışına taşınır, hazırlık sırasında Durdur kullanılabilir. Uygulama kapanırken iptal → bekleme → model temizliği → kök servis temizliği sırası izlenir.

1. Kuyruk ve analiz sahipliği: başarı, hata, iptal, yeniden başlatma ve bekleyen iş testleri; ardından uygulama.
2. Konuşmacı işi için aynı kuyruğu ve iş başına sahipliği bağla; kapalı özellik hiçbir model oluşturmasın.
3. CompositionRoot model kayıtlarını iş sağlayıcılarına taşı; görüntü modellerini de kapat; kapanış akışını bağla.
4. Sahte modellerle yaşam döngüsü testleri ve mevcut testler; tek MSBuild işçisi, paylaşılmayan derleyici. Gerçek model yükleme/indirme yok.
5. Kod incelemesi ve bulunan hata yollarını düzelt; sonuçları ve ölçülmemiş RSS sınırını belgeleyip bildir.

Sınırlar: native yükleme her zaman anında iptal olmaz. Dispose, işletim sisteminin RSS/swap değerlerinin anında düşeceği garantisi değildir. Bu değişiklik doğruluk artışı olarak sunulmaz; FLEURS ön ölçümü model karşılaştırmasıdır.
