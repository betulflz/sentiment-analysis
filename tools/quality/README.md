**M1 Air kalite pilotu**

Amaç: aynı Türkçe kayıtlar üzerinde transkripsiyon ve konuşmacı ayrımını ölçmek. Python 3.9+ standart kütüphanesi ve mevcut .NET 9 projeleri kullanılır; yeni Python paketi, model veya bulut servisi gerekmez. Komutları depo kökünden çalıştırın.

Bu araç duygu veya öneri doğruluğu ölçmez. İterasyon 1 ile veri hazırlama, kaynak/ses kimliği, arayüzsüz üretim ses hattı ve Markdown raporu eklendi: [İterasyon 1 kılavuzu](ITERATION-1.md). Referansları insan tarafından kontrol edilmiş gerçek kayıtlarla hazırlamak gerekir. `examples/` içindeki örnek skor sentetiktir; pilot şablonu ise doldurulması gereken boş bir plandır.

**Önce araçları doğrulama**

```sh
python3 -m unittest discover -s tools/quality -v
dotnet build tools/SentimentAnalysis.Benchmark -c Release
python3 tools/quality/benchmark.py score \
  --manifest tools/quality/examples/manifest.json \
  --run tools/quality/examples/synthetic-run.json
```

Son örnekte WER 0,25 çıkar: dört referans kelimenin biri değişiktir. Bu değer yalnızca skor aracının kullanım örneğidir.

**Yerel kayıtları hazırlama**

`quality-local/` git tarafından dışlanır. Kendi ses, referans ve çıktı dosyalarınızı burada veya depo dışında tutun. Tahmin JSON'ları ve hata günlükleri özel konuşma içerebilir; kaynak kodla paylaşmayın. Araç dosya yüklemez ve model indirmez; verilen harici motor komutunun kendi davranışını ayrıca kontrol edin.

```sh
mkdir -p quality-local
cp tools/quality/examples/manifest.json quality-local/manifest.json
```

Manifestte `audio` yolu manifest dosyasına göredir. WAV dosyası kullanın. Diğer biçimleri önce dönüştürün; örneğin ffmpeg zaten kuruluysa:

```sh
ffmpeg -i input.m4a -ac 1 -ar 16000 -c:a pcm_s16le quality-local/sample.wav
```

`durationSeconds`, gerçek sesin saniye cinsinden süresidir. Referans `transcript` tam metindir. `speakers` içindeki `start`/`end` saniye cinsinden oturum zamanları; `speaker` ise oturum içi anonim metindir. Üst üste konuşma iki ayrı bölgeyle yazılır. `expectedSpeakers: 0` kişi sayısının modele verilmediği deneydir. Bilinen kişi sayısıyla denemek ayrı bir koşuldur; ayrı manifest ve sonuç klasörü kullanın. Test kümesinde kişinin kimliğini öğrenip geliştirme tarafına taşımayın.

`transcribe` görevi için referans metin, `diarize` görevi için referans konuşmacı bölgeleri gerekir. Referanslar tahminlerden üretilmemeli. İlk pilotta 20–30 kısa kayıt ve farklı konuşmacılarla başlanabilir; bunun istatistiksel olarak genellenebilir doğruluk kanıtı olmadığı unutulmamalı.

**Mevcut sherpa konuşmacı bileşenini çalıştırma**

Aşağıdaki JSON argv içindeki model klasörünü yerel konumunuzla değiştirin. Sonuç klasörü mevcut olmamalı; eski koşuların üzerine yazılmaz. Yer tutucular shell'de çalıştırılmaz, argv öğelerine doğrudan yerleştirilir.

```sh
python3 tools/quality/benchmark.py run \
  --manifest quality-local/manifest.json \
  --engine sherpa-current-unknown-count \
  --task diarize \
  --out quality-local/sherpa-run-01 \
  --timeout 600 \
  --memory-limit-mib 4096 \
  --command '["dotnet","tools/SentimentAnalysis.Benchmark/bin/Release/net9.0/SentimentAnalysis.Benchmark.dll","diarize","--input","{audio}","--output","{output}","--models","/Users/YOUR_NAME/Library/Application Support/SentimentAnalysis/models","--speakers","{speakers}"]'

python3 tools/quality/benchmark.py score \
  --manifest quality-local/manifest.json \
  --run quality-local/sherpa-run-01/run.json
```

Beklenen mevcut dosyalar: `sherpa-onnx-pyannote-segmentation-3-0/model.onnx` ve `speaker-embedding.onnx`. Bunlar yoksa araç açık hata verir; hiçbir modeli kendiliğinden indirmez. Çıktıda kullanılan dosyaların SHA-256 değerleri ve boyutları bulunur.

Bu bileşen koşusu ham sherpa çıktısını ölçer; uygulamanın kısa kümeleri eleyen `GhostSpeakerFilter` adımını uygulamaz. Böylece gerçek motor ile sonradan yapılan temizliğin etkisi karışmaz. Tam uygulama karşılaştırması için ayrı adaptör gerekir.

**Mevcut Whisper bileşenini çalıştırma**

Yukarıdaki komutta `--task transcribe`, motor komutunda `transcribe`, farklı bir `--engine` ve yeni bir `--out` klasörü kullanın. Motor komutuna `--threads 2 --model large-v3-turbo` eklenebilir. `--threads` yalnız Whisper'a uygulanır; 1–4 arası kabul edilir. Varsayılan dil Türkçe, model dosyası `ggml-large-v3-turbo-q5_0.bin`.

Transkripsiyon bileşeni sesi 30 saniyelik örtüşmesiz parçalarla **sırayla ve tamamen** işler; VAD ve canlı pipeline kuyruğu yoktur. Bu nedenle UI'daki uçtan uca analizle aynı deney değildir. Tüm adaylar aynı bölümleme/VAD koşulunda karşılaştırılmalı veya fark raporda belirtilmeli. Çözümün mevcut WAV çözme ve yeniden örnekleme kodu kullanılır. Girdi boyutu 256 MiB ile sınırlandırılmıştır.

**Ölçümlerin anlamı**

- WER: toplam kelime düzenleme hatası / toplam referans kelime. Kayıt yüzdelerinin ortalaması alınmaz. Türkçe `İ/i`, `I/ı` dönüşümü, Unicode NFC, küçük harf ve noktalama yerine boşluk uygulanır. Sayılar sözcüklere çevrilmez; kesme işareti de ayırıcıdır.
- CER: aynı normalizasyondan sonra tek boşlukla birleştirilmiş metnin karakter hatasıdır; boşluklar dahildir. Referans boşsa oran `null`; eklenen kelime/karakter hataları toplamda korunur.
- DER: kaçırılan konuşma + fazladan konuşma + yanlış kişi süresi / referans kişi-saniyesi. `collar=0`, üst üste konuşma dahil. Aynı kişinin yinelenen/örtüşen bölgeleri bir kez sayılır. Anonim kişi isimleri optimal biçimde eşleştirilir; A yerine 1 yazmak hata sayılmaz. Referans konuşma yoksa oran `null`, yanlış alarm süresi yine raporlanır. Pilot en fazla 8 referans ve 32 tahmini konuşmacı destekler.
- `wallSeconds`: yeni sürecin başlangıcından çıktı doğrulamaya kadar toplam süre. Model yükleme, dosya hash hesaplama, ses hazırlama ve JSON yazma dahildir. İşletim sistemi dosya önbelleği temizlenmez; bu donanım düzeyinde tamamen soğuk başlangıç değildir.
- `realTimeFactor`: işlem süresi / ses süresi. 0,5: on dakikalık kayıt beş dakikada işlenmiş. Canlı kullanımın gecikmesini tek başına kanıtlamaz.
- `peakRssBytes`: macOS `/usr/bin/time -l` tarafından ölçülen süreç tepe RSS'i (bayt). Motorların aynı süreçte yüklenmesi gerekir; uzak sunucu veya önceden çalışan model servisi ölçülmez. macOS dışında bu ilk sürüm `null` verir.
- `memoryWithinBudget`: varsayılan 4096 MiB ile ölçülen tepe belleğin karşılaştırması. **Bu sert bellek sınırı değildir; süreç bu değerde öldürülmez.** 8 GB cihazda bütün sistemin boş belleği anlamına gelmez. Diğer uygulamalar ve işletim sistemi ayrıca yer kaplar. Model ancak doğruluk ve donanım ölçümü birlikte uygun olduğunda seçilir.

Her motor ve her kayıt ayrı süreçte, tek seferde bir tane çalışır. Zaman aşımında macOS'ta süreç grubu kapatılır. Hatalı, eksik veya fazla kayıt bulunan koşu toplu skorlanmaz; başarısız kayıtları çıkarıp doğruluğu yükseltmek engellenir. Manifestin ve ses dosyalarının SHA-256 değerleri saklanır; referans veya ses değişirse eski koşu reddedilir. Koşu sırasında değişen ses de reddedilir.

Her karşılaştırmada aynı kayıtlar, referanslar, normalizasyon, kişi sayısı bilgisi, ses ön işleme ve model revizyonları kullanılmalı. Bilinen/bilinmeyen kişi sayısı; soğuk süreç/ısınmış süreç; ham motor/tam uygulama sonuçları ayrı raporlanmalı. Test çalıştırırken ağır uygulamaları kapatıp güç/batarya koşullarını kaydedin; art arda koşularda ısınma sırasını değiştirerek tekrar ölçün.

**Alternatif motor bağlama**

Araç modelden bağımsızdır. Verilen komut `--output` yoluna UTF-8 JSON üretmeli; süreç başına tek kayıt işlemeli. Transkripsiyon çıktısı `{"transcript":"...","durationSeconds":10}`, konuşmacı çıktısı `{"speakers":[{"speaker":"A","start":0,"end":10}],"durationSeconds":10}`. İkinci alan olan `durationSeconds` isteğe bağlıdır, varsa manifestle 50 ms içinde uyuşmalıdır. Duygu modeli veya toplu GPU servisi bu ölçüme eklenmez.

Community-1 / Nemotron adaptörleri ve gerçek Türkçe kayıt ölçümleri bu ilk teslimata dahil değildir. Önce yerel model/kayıt konumları ve insan referansları sağlanmalı; sonra aynı sözleşmeye bağlanıp M1'de denenmelidir. Mevcut uygulamanın duygu ağırlıkları, sigmoid yorumu, yüz eşleştirmesi ve öneri algoritması bu teslimatta değiştirilmedi.
