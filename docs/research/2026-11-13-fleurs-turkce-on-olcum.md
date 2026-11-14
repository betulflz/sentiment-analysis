**Türkçe transkripsiyon ön ölçümü — 13 Kasım 2026**

Bu gerçek model koşusudur; görüşme, konuşmacı ayrımı veya duygu değerlendirmesi değildir. Sonuçlar yalnız aşağıdaki altı dış veri örneğini kapsar.

**Veri ve yöntem**

[Google FLEURS](https://huggingface.co/datasets/google/fleurs) Türkçe `tr_tr/test` alt kümesinin ilk 6 satırı: toplam 74,28 saniye, normalizasyon sonrası 104 referans kelime. Veri lisansı CC-BY-4.0; yayınlanmış transkriptler kullanıldı, bu oturumda insan tarafından yeniden dinlenip doğrulanmadı. Konuşmacı kimlikleri bilinmediğinden katılımcı ayrımı iddiası yok. Bu örnekler üzerinde model ayarı veya eğitim yapılmadı.

Apple M1, 8 GB; aynı .NET 9 Debug CLI, Türkçe zorlaması, iki Whisper iş parçacığı, 30 saniye örtüşmesiz bölümleme, VAD kapalı. Her kayıt ayrı süreçte ve modeller sırayla çalıştırıldı. Sesler ffmpeg ile 16 kHz mono PCM16 biçimine çevrildi. Kaynak çalışma ortamı sürümü ve ses/model SHA-256 değerleri yerel koşu JSON dosyalarında saklandı. Model dosyaları [whisper.cpp model deposundan](https://huggingface.co/ggerganov/whisper.cpp/tree/main), MIT lisans bilgisiyle alındı; indirme boyutları ve yayınlanan LFS SHA-256 değerleri doğrulandı.

**Gözlenen sonuç**

| Model | Kelime hatası / 104 | WER | CER | Toplam işlem (sn) | Kayıt başına medyan (sn) | Tepe süreç RSS (MiB) |
|---|---:|---:|---:|---:|---:|---:|
| base q5_1 (üçüncü koşu) | 41 | %39,42 | %9,36 | 6,571 | 1,211 | 215,3 |
| small q5_1 | 28 | %26,92 | %6,15 | 10,366 | 1,794 | 401,9 |
| large-v3-turbo q5_0 | 9 | %8,65 | %2,41 | 27,190 | 4,394 | 753,0 |

WER/CER hata oranıdır; düşük değer iyidir. Buradan genel bir doğruluk yüzdesi türetilmemeli. Kayıt uzunlukları farklıdır; kayıt başına medyan canlı güncelleme gecikmesi anlamına gelmez. Peak RSS yalnız motor sürecinin ölçümüdür; bütün uygulamanın, işletim sisteminin veya GPU bellek ayırmalarının toplamı değildir. İşlem süresine süreç açılışı, model hash okuma/yükleme ve çıktı yazma dahildir.

**Başlangıç maliyeti ve ölçüm sınırları**

İlk base koşusunun ilk kaydı 23,771 saniye, altı kaydın toplamı 28,453 saniye sürdü. Aynı model sonraki iki koşuda aynı 41 kelime hatasını verdi. İkinci koşu model indirmesiyle çakıştığından hız karşılaştırmasının ana satırına alınmadı; ham sonuç korunuyor. Tablodaki base üçüncü koşudur: indirmeler bittikten sonra çalıştırıldı. Small ve turbo ilk kendi model koşularıdır; işletim sistemi ve yerel çalışma ortamı önbellekleri temizlenmedi. Modellerin sırası randomize edilmedi, güç/termal durum denetlenmedi. Bu nedenle süreler kontrollü bir hız üstünlüğü iddiası değildir. İlk başlatmadaki farkın nedeni ayrıca ayrıştırılmadı.

Altı kısa okuma kaydı; gürültülü, örtüşen veya çok kişili doğal görüşmeleri temsil etmez. Özel isim, sayı ve Türkçe biçimbilim hataları için daha geniş, insan denetimli bir küme gerekir. WER/CER mevcut araçtaki Türkçe harf, noktalama ve boşluk normalizasyonuyla hesaplandı.

**Karar**

Bu örneklerde mevcut large-v3-turbo, iki küçük alternatife göre daha az kelime hatası verdi. Yaklaşık 753 MiB tepe süreç RSS ile bu cihazda çalıştı. Sırf modeli küçültmek bu örneklerde kaliteyi düşürüyor; uygulamanın varsayılanı bu deney yüzünden değiştirilmedi. Tam üretim hattının bellek ve doğruluğu ayrıca ölçülecek. Konuşmacı ayrımı, uzun parça–kişi eşlemesi ve duygu/öneri aşamalarındaki şüpheler bu transkripsiyon sonucu ile doğrulanmış veya çürütülmüş değildir.

**Tekrar üretim kimliği**

- Veri dönüştürülmüş Parquet revizyonu: `168de341b3db6859a9bac1c50a2ef5e3b47647e0`; `tr_tr/test/0000.parquet`, satırlar 0–5.
- whisper.cpp model depo revizyonu: `5359861c739e955e79d9a303bcbc70fb988958b1`.
- Yerel manifest: `quality-local/public-turkish-pilot/manifest.json`; kaynak türü public, referans durumu published-not-locally-reviewed, protokol external-read-speech-pilot-v1.
- Koşular: `base-q5_1-t2-01`, `base-q5_1-t2-02`, `base-q5_1-t2-03`, `small-q5_1-t2-01`, `turbo-q5_0-t2-01`.
- Ham JSON, tahminler, günlükler, `comparison.json`, model ve veri dosyaları `quality-local/public-turkish-pilot/` altında, git dışında tutuluyor. Bu klasör ilk kurulumda yaklaşık 1,4 GB disk kullanır; Parquet çıkarmada kullanılan pyarrow sanal ortamı uygulama bağımlılığı değildir.

Örnek skor komutu (depo kökünde):

```sh
python3 tools/quality/benchmark.py score \
  --manifest quality-local/public-turkish-pilot/manifest.json \
  --run quality-local/public-turkish-pilot/turbo-q5_0-t2-01/run.json
```

Yeni koşu için `run` görevine `--task transcribe`, model komutuna `--model base|small|large-v3-turbo`, doğru `--quantization q5_1|q5_0` ve `--threads 2` verilir. Aynı manifest ve ses dosyaları kullanılmalı; çıktı klasörü yeni olmalı. Kaynak veri lisansı ve revizyonu korunmalı. Donanım ve yazılım önbellekleri nedeniyle yeni sürelerin birebir aynı olması beklenmez.

Bu dış pilot, İterasyon 1 için hedeflenen gerçek görüşme başlangıç raporunun yerine geçmez.
