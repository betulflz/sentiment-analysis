**İterasyon 1: gerçek başlangıç ölçümünü çalıştırma**

Bu teslimat ölçüm altyapısını tamamlar. İki kişilik görüşme, konuşmacı ayrımı, duygu ve öneri için gerçek başlangıç değerlendirmesi henüz tamamlanmadı. Tam üretim hattının model seti ve insan tarafından kontrol edilmiş görüşme kayıtları gerekli. Ayrıca açık FLEURS verisiyle sınırlı bir Türkçe transkripsiyon pilotu hazırlanmıştır; bu, hedef görüşme değerlendirmesinin yerini tutmaz. Aşağıdaki örneklerdeki yollar yerel dosyalarınızla değiştirilmelidir.

**Teslim edilenler**

- Dosya modunda bounded kuyruk bekler; yavaş işlemci yüzünden eski parçalar düşürülmez. Mikrofon ve gerçek hızda video akışı canlı kuyruğu kullanır.
- Her yakalanan parça için konuşma / konuşma bulunmadı / işlem hatası / kuyruk kaybı / iptal / drenajda atlama / diğer işlenememe durumu kaydedilir. İşleme süresi ve hata türü vardır; bu gözlemde ses, metin, istisna mesajı ve dosya yolu yoktur.
- Girdi süreleri örtüşen aralıkların birleşimidir. %100 girdi kapsamı, duygu doğruluğu değildir. Hatalı parça komşularıyla örtülse bile hata sayısı görünür kalır.
- Rapor ekranında girdi kapsamı ve sorunlar gösterilir. Önceden saklanmış veya gözlenmemiş oturumlar %100 sayılmaz; “ölçülmemiş” görünür. Bu teslimatta gözlem SQLite'a eklenmedi; yeniden açma ve kalıcı kalite geçmişi sonraki ürün iterasyonunda ele alınır.
- `analyze` komutu üretimdeki ses hazırlama, VAD, STT, ses/metin duygu, füzyon, karar ve özet algoritmalarını arayüzsüz çalıştırır. **Avalonia, SQLite, kamera ve isteğe bağlı oturum sonrası konuşmacı geçişi dahil değildir.** Bütün uygulamanın toplam RAM ölçümü olarak sunulamaz.
- Eksik/hatalı analiz JSON'u korunur, `complete=false` ve çıkış kodu 3 verilir. Harness bunu `partial` olarak kaydeder; başarılı analiz gibi skorlamaz. Hiç konuşma penceresi yoksa takip önerisi üretilmez.

**Kayıt ve referans hazırlama**

`examples/pilot-template.json` 8 geliştirme + 4 test kaydının doldurulacak planıdır. Bu dosya gerçek veri içermez ve varsayılan haliyle ölçüme kabul edilmez. Hedef koşullar: temiz ses, benzer sesler, hızlı konuşmacı değişimi, gürültü, örtüşme, kısa konuşan katılımcı ve sessizlik.

```sh
mkdir -p quality-local/audio
cp tools/quality/examples/pilot-template.json quality-local/pilot.json
```

WAV dosyaları 16 kHz mono PCM16 olmalı. `durationSeconds` gerçek süreyi belirtmeli (50 ms tolerans). Metin ve/veya konuşmacı referanslarını insan kontrol etmeli. Referanslar motor çıktısından otomatik doğru kabul edilerek oluşturulmamalı. `participantIds` aynı gerçek kişiyi her kayıtta aynı anonim kimlikle göstermeli; şablondaki kimlikler gerçek katılımcılara göre değiştirilmelidir. `sourceKind`, `real`, `public` veya `synthetic` olabilir; kaynağı dürüstçe belirtin. Public veri için kaynağı, sürümü ve kullanım koşullarını ayrıca kaydedin.

Kontrol edilen her kayıtta `referenceStatus` alanını `reviewed` yapın. `prepare`, bu beyanın gerçek insan denetiminin yerini tutmadığını varsayar; kullanıcı tarafından doğrulanmış kayıtları ve kayıt koşullarını denetler. Yalnız konuşmacı karşılaştırması için `reference.speakers`, metin/analyze için `reference.transcript` yeterli; ikisi de varsa ikisi doğrulanır. Pilot şablonundaki 12 kayıt başlangıçtır, genellenebilir doğruluk iddiasına yeterli değildir.

```sh
python3 tools/quality/benchmark.py prepare \
  --manifest quality-local/pilot.json \
  --out quality-local/frozen.json
```

Aynı katılımcı veya aynı ses dosyasının geliştirme ve test taraflarında bulunması reddedilir. Çıktı, dosyaların SHA-256 kimliklerini ve mutlak yerel yollarını içerir. Komşu kesitler farklı dosya hash'i taşıyabileceğinden bunların aynı orijinal kayıttan geldiğini de insan denetlemeli; anonim kişi kimliklerini değiştirerek ayrım aşılmamalı.

**Üretim ses hattını çalıştırma**

Önce gerekli model dosyalarını yükleme veya indirme yapmadan kontrol edebilirsiniz:

```sh
python3 tools/quality/benchmark.py doctor \
  --models '/Users/YOUR_NAME/Library/Application Support/SentimentAnalysis/models' \
  --task analyze
```

Küçük Whisper dosyalarıyla ayrı transkripsiyon deneyi için `--task transcribe --model base --quantization q5_1` kullanılabilir. Bu durumda motor komutuna da `--model base --quantization q5_1` verilmelidir. Desteklenen kuantizasyonlar `q5_0`, `q5_1`, `q8_0`; uygulama varsayılanları değiştirilmez. `doctor` yalnız dosya varlığını denetler, model uyumluluğunu veya kalitesini kanıtlamaz.

```sh
dotnet build tools/SentimentAnalysis.Benchmark -c Release

python3 tools/quality/benchmark.py run \
  --manifest quality-local/frozen.json \
  --engine current-audio-report \
  --task analyze \
  --out quality-local/analyze-01 \
  --timeout 600 \
  --memory-limit-mib 4096 \
  --command '["dotnet","tools/SentimentAnalysis.Benchmark/bin/Release/net9.0/SentimentAnalysis.Benchmark.dll","analyze","--input","{audio}","--output","{output}","--models","/Users/YOUR_NAME/Library/Application Support/SentimentAnalysis/models","--threads","2"]'

python3 tools/quality/benchmark.py report \
  --manifest quality-local/frozen.json \
  --run quality-local/analyze-01/run.json \
  --out quality-local/analyze-01/report.md
```

Gerekli modeller: Whisper `ggml-large-v3-turbo-q5_0.bin`, `silero_vad.onnx`, `prosody-msp-dim.onnx`, `text-emotion-xlmr.onnx`, `sentencepiece.bpe.model`. Dosyalar eksikse açık hata verilir; ağdan indirilmez. Model dosyalarının hash'leri, uygulama ayarları, git revizyonu ve uncommitted kaynak dosyalarının kimliği rapora bağlanır. Dış motor kullanılırsa onun sürümü ve ağırlık kimlikleri ayrıca kaydedilmelidir; harness'in kaynak kimliği harici motorun sürümünü kanıtlamaz.

Bu koşu varsayılan satış profiliyle ve görüntüsüz çalışır. Metin, üretim hattının örtüşmeli parça metinlerinin ham birleşimidir; tekrarlar otomatik temizlenmez. Bu yüzden WER, yalnız Whisper bileşeninin WER'iyle eşit koşullu değildir. Önceki `transcribe` ve `diarize` komutları kontrol koşulları olarak korunur. Doğru transkript/konuşmacı verilmiş duygu kontrol koşulları gerçek veri ve sonraki duygu iterasyonu için hâlâ gereklidir; bu teslimat duygu veya öneri doğruluğunu ölçmez.

**Raporu okuma**

`report.md`, başarısız veya eksik koşularda da kayıt bazında durum, süre, RTF ve tepe RSS'i gösterir; toplu başarı skoru üretmez. Geçerli koşuda geliştirme/test skorları ayrı yazılır. Karışık kümenin toplam sayısı tanısal bilgidir; test kalitesi için `bySplit.test` kullanılmalı. Girdi gözlemleri hata türlerini ve nedenleri gösterir. Referans/manifest veya ses kimliği değişmişse eski sonuçların doğruluk skoru reddedilir. Kaynak kod kimliği karşılaştırma için saklanır; arşivlenmiş tahminler bugünkü kaynak kod farklı diye reddedilmez.

İlk tur sonunda aranacak cevaplar: Hangi kayıt başarısız? Hangi parça sessiz sayıldı? Gerçek konuşma yanlış mı elendi? İşleme hatası var mı? Dosya tam kapsandı mı? Ses hazırlama/metin/kişi ataması hangi hatayı büyütüyor? Modelin ilk yüklemesi dahil süre ve bellek ne kadar? Aynı kayıttaki bileşen sonucu ile ses-rapor sonucu neden farklı?

RAM bütçesi ölçümseldir; sert bellek limiti değildir. İşletim sistemi ve diğer uygulamalar ayrıca yer kaplar. Kaynak sınırlarını aşan model varsayılan yapılmaz. İlk iterasyon gerçek başlangıç raporu olmadan kalite bakımından tamamlanmış sayılmaz.
