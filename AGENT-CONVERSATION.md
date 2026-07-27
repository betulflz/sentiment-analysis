ME:

bir proje fikrim var ve bu proje için bana güzel bir planlama dokümanları tasarım anlatım vs oluşturacaksın başta. Benimle iyice her noktayı kapatana kadar brainstorm yaparak. Hatta belki kodların istekeletini de yazarız çünkü işimiz bittiğinde projeyi dokümanlar ve kodlar ile başka agent'a vereceğim, ancak human-first dokümanalr olsa daha iyi. Projemiz ses kayıtları, canlı konuşmalardan duygu analizi yapmak. 2 adet model olacak. ilk modelimiz stt yaparak oluşan metnin duygusunu sadece text bazında analiz ederek bir skor üretecek, ikinci modelimiz ise ses frekansları ve belki metin'e de bakarak hangi kelime hangi frekansa gelmiş vs, frekansa ses hertz'lerine yoğunluğa değerlere bakarak (tıpkı spotify müzikleri duygulara göre ayrım yaparken kullandığı algoritma gibi) bir analiz skoru üretecek ve sonunda bu oluşan skoru ve metrikleri güzel bir algoritma ile merge ederek nihai bir sonuç elde edeceğiz. İki modelde sadece skor output vermek zorunda değil, birden fazla metrik vermesi daha iyi olabilir nihai sonuç için. Nihai sonuçta da 5 yada 10 tane artık modelimiz ve sistemiz hangi modelleri destekliyorsa ona göre bir duygu ortaya çıkacak, mutlu, hüzünlü vs vs gibi. Burada teknik zorluklara girmek istiyorum, sadece ses kayıtlarını sisteme yükle sonuç al şeklinde çalışmayacak, teams toplantılarını dinleyebilen bir agent gibi de çalışacak sistem. Ayrıca yönetici tarafından aldığım bildirimle bu bir web uygulaması olmayacak ve dotnet ile geliştirilecek, bu konuda da yardım et fikirler sun. Bir de burada modeller de çok önemli, ses kayıtı gibi kişisel veriler olduğundan bir api kullanmak sıkınıt olur llm için, onun yerine lokal'de kurulabilecek bir agent lazım ve whisper'ın düşük modellerinden birini kullandığımda yetersiz sonuç aldım. Model seçimimiz de önemli olacak. Ayrıca optimizasyon ve depolama da çok kritik. cpu gpu optimizasyonu zaten lokalde llm yükünü çekeceğimizden çok önemli olacak. Ayrıca daha önce başka stajyerlerin oluşturduğu kodları bilgisayarımda "/Users/ilkaygokbudak/Downloads/ses-duygu-analizi" dizininde bulabilirisn, stajyer kodu olduğundan yetersiz olabilir ve python ile geliştirilmiş. sadece proje hedef ve vizyonunu anlamak için yardımcı olabilir. Biz olaya profesyonel yaklaşacağız. clean bir kod yazımı ve dokümantasyonu önemli bir yazım olacak. Bu konuda brainstorm'a başlayalım. Çok uzun bir brainstorm yapalım, şuanda kod yazma hedefimiz yok.

AGENT:

superpowers:brainstorming skill
Harika bir proje. Uzun ve derinlemesine bir brainstorm yapacağız. Önce mevcut bağlamı anlamak için stajyerlerin kodlarına ve çalışma dizinine bakayım.

Bash List reference and current project dirs
IN
echo "=== Downloads/ses-duygu-analizi ===" && ls -la /Users/ilkaygokbudak/Downloads/ses-duygu-analizi 2>/dev/null | head -50; echo; echo "=== current dir ==="; ls -la /Users/ilkaygokbudak/Development/Projects/sentiment-analysis 2>/dev/null | head -30

OUT
=== Downloads/ses-duygu-analizi ===
total 72
drwxr-xr-x@ 18 ilkaygokbudak  staff   576 Jul 23 14:54 .
drwx------@ 77 ilkaygokbudak  staff  2464 Jul 24 10:34 ..
-rw-r--r--@  1 ilkaygokbudak  staff  6148 Jul 22 22:11 .DS_Store
drwxr-xr-x@ 18 ilkaygokbudak  staff   576 Jul 23 16:50 .git
drwxr-xr-x@  3 ilkaygokbudak  staff    96 Jul 23 08:50 .github
-rw-r--r--@  1 ilkaygokbudak  staff    50 Jul 23 11:22 .gitignore
drwxr-xr-x@  6 ilkaygokbudak  staff   192 Jul 23 08:58 .pytest_cache
drwxr-xr-x@  5 ilkaygokbudak  staff   160 Jul 23 08:56 .ruff_cache
drwxr-xr-x@  3 ilkaygokbudak  staff    96 Jul 23 08:49 .superpowers
drwxr-xr-x@  7 ilkaygokbudak  staff   224 Jul 23 09:07 .venv
-rw-r--r--@  1 ilkaygokbudak  staff  8225 Jul 23 14:54 README.md
drwxr-xr-x@ 13 ilkaygokbudak  staff   416 Jul 23 14:50 app
drwxr-xr-x@  3 ilkaygokbudak  staff    96 Jul 22 23:39 docs
drwxr-xr-x@ 10 ilkaygokbudak  staff   320 Jul 23 15:55 eval
-rw-r--r--@  1 ilkaygokbudak  staff   281 Jul 23 09:45 pyproject.toml
-rw-r--r--@  1 ilkaygokbudak  staff   112 Jul 23 09:12 requirements-dev.txt
-rw-r--r--@  1 ilkaygokbudak  staff   191 Jul 23 14:46 requirements.txt
drwxr-xr-x@ 18 ilkaygokbudak  staff   576 Jul 23 14:50 tests

=== current dir ===
total 0
drwxr-xr-x   2 ilkaygokbudak  staff    64 Jul 24 10:37 .
drwxr-xr-x  75 ilkaygokbudak  staff  2400 Jul 24 10:37 ..
Bash Read reference README and app structure
IN
cd /Users/ilkaygokbudak/Downloads/ses-duygu-analizi && echo "=== README ===" && cat README.md; echo; echo "=== app tree ==="; find app -maxdepth 2 -type f | head -60; echo; echo "=== requirements ==="; cat requirements.txt

OUT
=== README ===
# Ses Duygu Analizi

Bir ses dosyası yükleyip (mp3/wav/m4a) **veya tarayıcıdan mikrofonla kayıt
alıp** iki farklı yöntemle duygu analizi yapan, ardından iki sonucu tek bir
nihai karara indirgeyen FastAPI uygulaması. Türkçe ve İngilizce konuşma
desteklenir: Whisper konuşulan dili algılar, İngilizce ise metin Türkçeye
çevrilir ve duygu analizi çeviri üzerinden yapılır.

1. **Metin tabanlı (Pipeline A):** Whisper (`openai/whisper-base`) ile
   Türkçe transkripsiyon → çok dilli zero-shot duygu sınıflandırma
   (`MoritzLaurer/mDeBERTa-v3-base-mnli-xnli`).
2. **Ses tabanlı (Pipeline B):** Ham ses dalga formu üzerinde doğrudan
   konuşma duygu tanıma
   (`SeaBenSea/hubert-large-turkish-speech-emotion-recognition`).
3. **Füzyon:** İki dağılım, girdi kalitesi (transkript uzunluğu, ses süresi,
   konuşma yoğunluğu) ve model kesinliği (entropi) ile ağırlıklandırılıp tek
   bir nihai sonuca indirgenir. Karar, Türkçe bir açıklama cümlesiyle birlikte
   sunulur.

Duygu etiketleri: `mutlu`, `üzgün`, `kızgın`, `nötr` — kararsız durumlarda
`belirsiz`.

Mimari ve teslimat kararlarının gerekçeleri için:
[`docs/superpowers/specs/`](docs/superpowers/specs) ve
[`docs/superpowers/plans/`](docs/superpowers/plans).

## Gereksinimler

- Python 3.12 (3.14 gibi çok yeni sürümlerde `torch` wheel'i henüz
  bulunmayabilir — bu depo 3.12 ile test edildi)
- ~3.5 GB boş disk alanı (ilk çalıştırmada modeller HuggingFace Hub'dan
  indirilir ve önbelleğe alınır; SER modeli tek başına ~1.2 GB, çeviri
  modeli ~470 MB ekler)
- İnternet bağlantısı (yalnızca ilk model indirmesi için)

## Kurulum

```bash
python3.12 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
```

## Çalıştırma

```bash
source .venv/bin/activate
uvicorn app.main:app --reload
```

Tarayıcıda `http://127.0.0.1:8000` adresini açın. İlk istek, modellerin
belleğe yüklenmesi nedeniyle (uygulama açılışında bir kere) birkaç dakika
sürebilir; sonraki istekler daha hızlıdır.

## Mikrofonla kayıt

Arayüzdeki "Mikrofonla kaydet" sekmesi tarayıcıdan doğrudan kayıt alır.
Ses, tarayıcıda 16 kHz mono WAV olarak kodlanıp gönderilir; sunucuda ek bir
bağımlılık (ffmpeg vb.) gerekmez.

Tarayıcılar mikrofon erişimine yalnızca **güvenli bağlamda** izin verir.
`localhost` üzerinde sorunsuz çalışır; uygulama ağ üzerinden servis edilecekse
HTTPS gerekir, aksi halde mikrofon sekmesi devre dışı kalır ve dosya yükleme
kullanılmalıdır.

Kayıt üst sınırı 60 saniyedir ve süre dolduğunda otomatik durur.

## API

### `POST /analyze`

`multipart/form-data`, alan adı `file`. Kısıtlar: mp3/wav/m4a, maks 15 MB,
maks 60 saniye.

```bash
curl -F "file=@ornek.mp3" http://127.0.0.1:8000/analyze
```

Örnek yanıt:

```json
{
  "filename": "ornek.mp3",
  "duration_seconds": 12.4,
  "final": {
    "emotion": { "label": "mutlu", "score": 0.78 },
    "all_scores": [{ "label": "mutlu", "score": 0.78 }, "..."],
    "agreement": true,
    "confidence_level": "yüksek",
    "weights": { "text": 0.62, "audio": 0.38 },
    "explanation": "Her iki yöntem de 'mutlu' sonucunu verdi (metin %83, ses %71). Güven düzeyi: yüksek."
  },
  "text_pipeline": {
    "transcript": "...",
    "translation": null,
    "detected_language": "tr",
    "emotion": { "label": "mutlu", "score": 0.83 },
    "all_scores": [{ "label": "mutlu", "score": 0.83 }, "..."]
  },
  "audio_pipeline": { "...": "..." }
}
```

Geçersiz/çok büyük/çok uzun dosyalarda `400`, işleme hatasında `500` döner.

## Kapsam ve Bilinen Sınırlamalar

- Kalıcılık yok — sonuçlar veritabanında saklanmaz, her istek bağımsızdır.
- Kimlik doğrulama yok — yalnızca yerel/güvenilir ağda çalıştırılmak üzere
  tasarlandı.
- Rate limiting yok — MVP tek kullanıcı senaryosu için yeterli; üretime
  taşınmadan önce eklenmelidir.
- Duygu etiketi kümesi 4 sınıfla sınırlıdır (`mutlu`, `üzgün`, `kızgın`,
  `nötr`). Türkçe eğitilmiş SER modelleri ve etiketli Türkçe veri setleri bu
  dört duygu üzerine kuruludur; `korkmuş`, `şaşkın`, `tiksinmiş` gibi
  etiketler ölçülebilir biçimde desteklenemediği için kapsam dışıdır.
- Ses tabanlı model TurEV-DB (oyunculu Türkçe konuşma) ile eğitilmiştir;
  kendiliğinden konuşmada başarım daha düşük olabilir.
- SER checkpoint'i (`SeaBenSea/hubert-large-turkish-speech-emotion-recognition`)
  transformers'ın standart `pipeline()` fonksiyonuyla yüklenemez: özel bir
  `HubertForSpeechClassification` mimarisiyle kaydedilmiştir ve `pipeline()`
  eğitilmiş sınıflandırma başlığını sessizce atıp rastgele başlatır (çıktı
  gürültü olur). Bu yüzden `app/ml.py` içinde checkpoint'in mimarisine uyan
  özel bir model sınıfı ve `SpeechEmotionClassifier` sarmalayıcısı
  bulunur — model değiştirilecekse bu tuzağa dikkat edilmelidir.
- Yalnızca Türkçe ve İngilizce konuşma desteklenir; başka bir dil
  algılanırsa istek 400 ile reddedilir.
- İngilizce kayıtlarda duygu analizi Türkçe çeviri üzerinden yapılır.
  Makine çevirisi tonu düzleştirebilir: örneğin "I'm really upset"
  ifadesi "çok üzgünüm" olarak çevrildiğinde öfke tonu kaybolur ve
  sınıflandırıcı `kızgın` yerine `üzgün` görür.

## Geliştirme

```bash
pip install -r requirements-dev.txt
ruff check .
pytest
```

Test paketinde 81 test vardır. `yavas` işaretli 2 test gerçek model
indirmesi gerektirdiği için varsayılan koşudan dışlanır; çalıştırmak için
`pytest -m yavas` kullanılır.

## Değerlendirme

```bash
python -m eval.prepare_dataset --etiket-basina 100   # alt kümeyi indir
python -m eval.evaluate_ser                          # SER adaylarını karşılaştır
python -m eval.evaluate_fusion --ornek-sayisi 200    # füzyon kazancını ölç
```

İndirilen ses dosyaları ve manifest `eval/data/` altına yazılır (git'e
dahil değildir); ölçüm raporları ise `eval/raporlar/` altına yazılır ve
git'e dahildir. Füzyon değerlendirmesi tek bir "doğruluk" sayısı vermez:
katı doğruluk (tüm örnekler; füzyonun döndürdüğü `belirsiz` yanlış sayılır)
ve kararlı örneklerdeki doğruluk + kapsam oranı (füzyonun `belirsiz`
dönmediği örneklerin oranı) ayrı ayrı raporlanır.

## Kaynaklar ve lisanslar

- SER modeli:
  [SeaBenSea/hubert-large-turkish-speech-emotion-recognition](https://huggingface.co/SeaBenSea/hubert-large-turkish-speech-emotion-recognition)
  — Apache 2.0, TurEV-DB ile eğitilmiştir
- Değerlendirme verisi:
  [Codyfederer/tr-combined](https://huggingface.co/datasets/Codyfederer/tr-combined)
  — CC BY 4.0
- Transkripsiyon: [openai/whisper-base](https://huggingface.co/openai/whisper-base)
- Metin sınıflandırma:
  [MoritzLaurer/mDeBERTa-v3-base-mnli-xnli](https://huggingface.co/MoritzLaurer/mDeBERTa-v3-base-mnli-xnli)
- Çeviri: [Helsinki-NLP/opus-mt-tc-big-en-tr](https://huggingface.co/Helsinki-NLP/opus-mt-tc-big-en-tr)
  — CC-BY 4.0

## Proje Yapısı

```
app/
  main.py                 FastAPI uygulaması ve route'lar
  config.py               Sabitler (limitler, model isimleri, füzyon eşikleri)
  schemas.py              Pydantic response modelleri
  ml.py                   Model yükleme (startup'ta bir kez)
  utils.py                Upload/validasyon yardımcıları
  services/
    transcription.py      Pipeline A - Whisper transkripsiyon
    text_emotion.py       Pipeline A - metin duygu sınıflandırma
    audio_emotion.py      Pipeline B - ses duygu sınıflandırma
    quality.py            Girdi kalitesi sinyalleri (saf fonksiyonlar)
    fusion.py             Füzyon kararı ve açıklama (saf fonksiyonlar)
    pipeline.py           İki pipeline + füzyon orkestrasyonu
  templates/index.html    Yükleme/kayıt + sonuç ekranı
  static/
    recorder.js           Mikrofon kaydı ve WAV kodlayıcı
    pcm-worklet.js         AudioWorklet PCM toplayıcı
    script.js, style.css
eval/                     Model ve füzyon değerlendirme scriptleri
tests/                    pytest test paketi
docs/superpowers/         Tasarım dokümanları ve uygulama planları
```

=== app tree ===
app/.DS_Store
app/config.py
app/__init__.py
app/ml.py
app/schemas.py
app/utils.py
app/main.py
app/__pycache__/schemas.cpython-312.pyc
app/__pycache__/main.cpython-312.pyc
app/__pycache__/main.cpython-311.pyc
app/__pycache__/config.cpython-312.pyc
app/__pycache__/utils.cpython-312.pyc
app/__pycache__/ml.cpython-312.pyc
app/__pycache__/__init__.cpython-312.pyc
app/__pycache__/__init__.cpython-311.pyc
app/static/tf-logo.svg
app/static/recorder.js
app/static/script.js
app/static/style.css
app/static/pcm-worklet.js
app/templates/index.html
app/services/transcription.py
app/services/.DS_Store
app/services/text_emotion.py
app/services/audio_emotion.py
app/services/__init__.py
app/services/pipeline.py
app/services/translation.py
app/services/fusion.py
app/services/quality.py

=== requirements ===
fastapi==0.115.6
uvicorn[standard]==0.34.0
python-multipart==0.0.20
jinja2==3.1.5
transformers==4.47.1
torch==2.5.1
soundfile==0.13.0
librosa==0.10.2.post1
numpy==1.26.4
sentencepiece==0.2.2
Shell cwd was reset to /Users/ilkaygokbudak/Development/Projects/sentiment-analysis
Stajyerlerin bıraktığı proje aslında oldukça olgun bir MVP — iki pipeline (Whisper STT + metin duygu, ve HuBERT ses/prozodi duygu), bir füzyon katmanı, hatta bir değerlendirme (eval) altyapısı var. Vizyonu net anladım. Bizim yeni projemiz bundan üç temel noktada ayrışıyor: (1) Python/FastAPI değil .NET, (2) web değil canlı dinleyebilen bir agent, (3) veri hassas olduğu için tamamen lokal modeller.

Önce bir çerçeve notu: 📌 Bu, tek bir spec'e sığmayacak kadar büyük, çok-alt-sistemli bir proje (canlı ses yakalama, STT, iki duygu modeli, füzyon, .NET uygulama iskeleti, depolama, optimizasyon). Brainstorm'u geniş yapıp sonunda bunu bağımsız parçalara ayıracağız ve her parçanın kendi spec'i olacak. Şimdilik kod yok, sadece kafamızdaki her noktayı kapatıyoruz.

Birçok kritik çatal var; onları teker teker ve mantığını açıklayarak soracağım. En temel olandan başlıyorum, çünkü bu diğer her şeyi (optimizasyon, depolama, model barındırma) belirliyor.

Birinci çatal — uygulama form-factor'ü ve platform. "Teams toplantısını dinleyen agent" ifadesi güçlü biçimde Windows'a işaret ediyor ama nasıl paketlendiği her şeyi değiştirir:

AskUserQuestion
OUT
The user answered: "Sistem son kullanıcıda nasıl çalışacak — hangi platform ve hangi form-factor?"="windows service olarak çalışma ihtiyacı doğabillir ancak cross platform first olmalı. bende ve çoğu ekip arkadaşımda macOS var.". Read the answers carefully — they may request clarification, changes, or that you not proceed — and follow what they actually say.
macOS ağırlıklı ekip + "cross-platform first", ama ileride Windows service ihtimali — not aldım. Bu önemli bir sonuç doğuruyor: muhtemelen Avalonia + .NET (tek kod tabanı, macOS/Windows/Linux), ve canlı sistem-sesi yakalama her OS'ta ayrı ele alınacak (macOS'ta ScreenCaptureKit / CoreAudio process-tap veya BlackHole gibi sanal aygıt; Windows'ta WASAPI loopback). Buna capture bölümünde döneceğiz.

İkinci çatal — ve bence mimariyi en çok belirleyen soru — zamansal mod. "Canlı dinleme" iki çok farklı şey olabilir ve optimizasyon/depolama yükü ile teknik zorluk buna göre uçar:

AskUserQuestion
OUT
Your questions have been answered: "Canlı dinlemede duygu sonucu ne zaman üretilmeli — anlık akış mı, yoksa parça parça/sonrasında mı?"="Chunked near-real-time (önerilen)". You can now continue with these answers in mind.
Mükemmel — chunked near-real-time doğru seçim: lokal donanımda gerçekçi, streaming STT'nin cehennemine girmeden "canlı" hissi verir, hem anlık zaman çizelgesi hem toplantı-sonu özet üretir.

Üçüncü çatal — projenin kalbi: modelleri .NET'te nasıl barındıracağız? Buradaki karar optimizasyon, paketleme ve "clean kod" hedefinin hepsini belirliyor. Kritik bir uyarı stajyer README'sinde gizli: kullandıkları Türkçe HuBERT SER modeli özel bir mimariyle kaydedilmiş ve standart araçlarla düzgün yüklenmiyor — bu, "her modeli ONNX'e çeviririz" varsayımını riske atıyor. Seçenekler:

AskUserQuestion
OUT
Your questions have been answered: ".NET içinde lokal modelleri nasıl çalıştıracağız (STT + duygu modelleri)?"="Kararsızım — artılar/eksileri derinleştir". You can now continue with these answers in mind.
Bu kararı hak ettiği derinlikte açalım — çünkü yanlış seçim ya paketlemeyi ya da doğruluğu kalıcı olarak sakatlar. Önce şu kilit içgörü: modelleri sıfırdan yeniden seçiyoruz. Stajyerin ONNX-export acısı, standart olmayan özel bir sınıfla kaydedilmiş bir checkpoint'ten geliyordu. Biz baştan standart mimarili modeller seçersek (ör. Wav2Vec2ForSequenceClassification tabanlı SER) o tuzak tamamen ortadan kalkar. Yani "ONNX riski" aslında bir model-seçim kısıtına dönüşüyor — bunu yönetebiliriz.

Şimdi üç yolu, bu proje için gerçekten önemli olan eksenlerde karşılaştırayım:

Eksen	Hibrit (Whisper.net + ONNX)	Python sidecar (gRPC)	Tam ONNX
macOS GPU	Whisper.cpp → Metal ✅; ONNX → CoreML EP ✅	PyTorch MPS backend ✅	ONNX CoreML EP, Whisper kısmı zayıf
Model uyumluluğu	Standart mimarilerle iyi; egzotik modeller zor	Her PyTorch modeli çalışır ✅	Whisper-ONNX kurulumu kırılgan
Paketleme	Tek .NET runtime, native .dylib/.dll — temiz ✅	.NET + gömülü Python venv/PyInstaller — şişkin, macOS imzalama derdi	Tek runtime ama Whisper varlıkları büyük
Hız (chunked)	whisper.cpp quantize + Metal çok hızlı ✅	PyTorch overhead + IPC serileştirme	Orta
Clean kod	C# uçtan uca, tek dil ✅	İki dil, IPC sözleşmesi bakımı	C# ama dönüşüm scriptleri Python
Başlangıç hızı	Modelleri export etmek ön yatırım ister	Olgun pipeline bugün çalışır ✅	En yavaş kurulum
Önerim — ikisini de dizmemek, ama arayüzle ayırmak: Clean architecture'ın klasik cevabı burada işe yarıyor. Modelleri bir soyutlama arkasına koyarız:


ISpeechToText          → WhisperNetEngine   (Whisper.net, native)
ITextEmotionModel      → OnnxTextEmotion    | PySidecarTextEmotion
IProsodyEmotionModel   → OnnxProsodyEmotion | PySidecarProsodyEmotion
IEmotionFusion         → saf C# algoritma (model değil, matematik)
Böylece hedef mimari = Hibrit (Whisper.net + ONNX), ama her duygu modeli için Python sidecar'ı açık bir yedek/escape-hatch olarak aynı arayüzün ardında tutarız. Pratikte:

STT kesinlikle Whisper.net — lokal STT'nin en iyi yolu, tartışmasız. (Model boyutu ayrı soru; large-v3-turbo'yu konuşacağız.)
Duygu modelleri ONNX-first, ama modeli export edemezsek o tek model sidecar'a düşer — mimari değişmez, sadece o sınıfın implementasyonu değişir.
Füzyon zaten model değil, saf C# matematiği; hiçbir runtime derdi yok, %100 test edilebilir.
Bu yaklaşım "clean", cross-platform, macOS-Metal dostu ve bizi tek bir model yüzünden köşeye sıkıştırmıyor.

Bir sonraki tek soru — bu strateji içinde başlangıç duruşumuz ne olsun:

AskUserQuestion
OUT
The user answered: "Soyutlama aynı kalmak şartıyla, duygu modelleri için başlangıç implementasyonu ne olsun?"="önerine göre gidebiliriz, iyi sonuç alabilmem önemli". Read the answers carefully — they may request clarification, changes, or that you not proceed — and follow what they actually say.
Anlaşıldı — "iyi sonuç" en yüksek öncelik. Bunu bir ilke olarak sabitliyorum: paketleme kolaylığı ile doğruluk çatışırsa doğruluk kazanır (yani bir model gerçekten sidecar'da çok daha iyiyse, o modeli sidecar'da tutarız — mimari buna zaten izin veriyor).

Şimdi işin kalite belirleyen kısmına, somut model seçimine giriyoruz. Ama modeller dilden ayrılamaz, o yüzden önce dil kapsamını netleştirelim. Stajyer TR+EN destekliyordu ve İngilizceyi Türkçeye çevirip analiz ediyordu (README'de bunun tonu düzleştirdiğini kendileri de yazmış — biz bundan kaçınacağız).

AskUserQuestion
OUT
The user answered: "Sistem hangi konuşma dillerini desteklemeli? (Bu, STT ve duygu modeli seçimini doğrudan belirler)"="buda bir başka teknik kısıt çok iyi yere bastın söylemeyi unutmuşum. Şöyle ki projenin ilerki aşamalrında yöneticiler bunun ing desteğini de bekliyor. Eğer sistem kurgulaması başlangıcında dahi çok değişmiyorsa türkçe first gidebiliriz, ancak ilerde bu bize çok borç yaratır diyorsan türkçe ama ingilizce tabanını da hazır olacak şekilde kurgulayabiliriz. Yani şuanki hedef türkçe, ancak ilerde ingilizce de istenecek". Read the answers carefully — they may request clarification, changes, or that you not proceed — and follow what they actually say.
Tam da doğru sezgi — ve iyi haber: İngilizce'yi ileriye hazır tutmanın maliyeti neredeyse sıfır, yeter ki dili baştan birinci-sınıf bir parametre yapalım. Nerede borç var, nerede yok, net ayıralım:

Bedava (zaten çok-dilli, hiç ekstra iş yok):

Whisper STT — large-v3/turbo zaten TR ve EN'i native anlıyor, dil algılamayı kendi yapıyor. İngilizce için tek satır değişmez.
SER / prozodi modeli — tonlama, tempo, enerjiye bakar; dilden bağımsızdır.
Füzyon matematiği, ses yakalama, chunking, depolama — hepsi dil-agnostik.
Şimdi küçük yatırım, yok sayılırsa gerçek borç (tek nokta):

Metin duygu modeli. İki temiz yol var: (a) baştan çok-dilli bir metin modeli seçmek, ya da (b) ITextEmotionModel'i dil-başına takılabilir yapmak (tr → TurkishEmotionModel, ileride en → EnglishEmotionModel). İkisinde de İngilizce = "yeni bir implementasyon + doğrulama", yeniden tasarım değil.
Duygu etiketleri dil-bağımsız bir enum olmalı (Happy, Angry...), UI metni ayrı lokalize edilir. Böylece Türkçe/İngilizce aynı çekirdeği paylaşır.
Domain modeline bir DetectedLanguage alanı iliştirmek — bir satır.
Önerim: Türkçe-first ama çok-dilli-hazır mimari. Yani şimdi yalnızca Türkçe'yi tune/valide ederiz, kalite oraya odaklanır; ama arayüz dil-başına takılabilir, dil parametresi baştan boru hattından akar, etiketler dil-bağımsız. Bu, ileride İngilizce için "ekle ve doğrula" borcunu bırakır — "söküp yeniden yap" borcunu değil. Maliyeti: birkaç interface imzasına language eklemek. Buna değer.

AskUserQuestion
OUT
Your questions have been answered: "Dil stratejisi — 'Türkçe-first ama çok-dilli-hazır mimari' önerisini benimseyelim mi?"="Evet, Türkçe-first + çok-dilli-hazır (önerilen)". You can now continue with these answers in mind.
Şimdi somut model seçimi — kaliteyi burada kazanıyoruz. Üç modelimiz var: STT, metin-duygu, ses/prozodi-duygu. STT'den başlayalım çünkü diğer her şeyin girdisi o; STT kötüyse metin modeli de çöp görür ("garbage in, garbage out").

Stajyer whisper-base kullanmış — sizin de yetersiz bulduğunuz düşük model. Türkçe + kalite-öncelikli + macOS'ta Metal (whisper.cpp) bağlamında gerçek seçenekler:

AskUserQuestion
OUT
Your questions have been answered: "STT (konuşma→metin) modeli olarak hangisini varsayılan alalım? (whisper.cpp/GGUF, macOS Metal)"="Whisper large-v3-turbo (önerilen varsayılan)". You can now continue with these answers in mind.
STT çözüldü — large-v3-turbo varsayılan, ileride "derin rapor" için large-v3'ü açık tutarız.

Şimdi çok kritik ve sizin baştan bahsettiğiniz noktaya geliyoruz: nihai duygu kümesi (5-10 duygu). Bunu model seçiminden önce kapatmalıyız, çünkü hedef etiket kümesi her iki modeli de kısıtlıyor. Ve burada dürüst bir teknik gerçek var:

Metin modelleri kolayca çok sayıda duygu verebilir (bazıları 27 duygu). Ama Türkçe ses (SER) modelleri eğitildikleri veriyle sınırlıdır — stajyerin 4 duyguda kalmasının sebebi buydu (TurEV-DB sadece mutlu/üzgün/kızgın/nötr içeriyor). "Korku/şaşkınlık/tiksinti"yi sesten Türkçe'de ölçmek için etiketli veri neredeyse yok.

Burada sizin Spotify benzetmeniz aslında çözümün ta kendisi. Spotify duyguyu ayrık etiketlerle değil, boyutsal olarak tutar: valence (olumluluk) ve energy/arousal (uyarılma). Prozodi (ton, tempo, enerji) doğal olarak bu iki eksene oturur. Bu, asimetrik etiket sorununu da çözer: her iki model de bir valence-arousal uzayında konuşur, en sonda bu uzaydan ayrık duygulara haritalarız.

Üç yaklaşım:

AskUserQuestion
OUT
Your questions have been answered: "Nihai duygu temsili nasıl olsun? (Bu, her iki duygu modelinin çıktısını ve füzyonu belirler)"="Boyutsal çekirdek + ayrık harita (önerilen, Spotify-tarzı)". You can now continue with these answers in mind.
Bu çok tutarlı bir tasarım — ve şimdi çok güçlü bir içgörü açılıyor. Boyutsal temsili seçtiğimiz an, iki modelin rekabet eden değil, birbirini tamamlayan roller aldığını görüyoruz. Ses işleme literatüründe iyi bilinen bir gerçek şu:

Arousal (enerji/uyarılma) sesten çok güvenilir ve dilden bağımsız ölçülür (bağırma, tempo, ses tizliği). Ama valence (olumlu/olumsuz) sesten zayıf ölçülür — sakin bir sesle çok kötü şeyler söylenebilir. Tersine metin, valence'ı çok iyi yakalar (kelimelerin anlamı) ama arousal'ı zayıf (yazıda "bağırma" görünmez).

Bu bize ilkeli bir füzyon hediye ediyor: arousal ← ağırlıkla ses, valence ← ağırlıkla metin. İki modeli aynı şeyi tahmin edip ortalamak yerine, her birini güçlü olduğu eksende dinleriz. Bu, stajyerin naif "iki dağılımı ağırlıkla topla" füzyonundan çok daha sağlam. (Detayını füzyon bölümünde açacağız.)

Bu da model seçimini netleştiriyor: prozodi modeli doğrudan valence-arousal regresyonu yapan bir model olmalı (ayrık etiket değil) — örn. wav2vec2 tabanlı boyutsal SER modelleri VAD çıktısı verir ve standart mimari oldukları için ONNX'e uyumludur.

Önce ayrık duygu çarkını (VA uzayından haritalayacağımız nihai etiketler) somutlaştıralım. Circumplex modeline göre önerim:

AskUserQuestion
OUT
Your questions have been answered: "Valence-Arousal uzayından haritalanacak nihai ayrık duygu kümesi hangisi olsun?"="8 duygu circumplex (önerilen)". You can now continue with these answers in mind.
8 duygu circumplex — kilitlendi. VA uzayını 8 bölgeye bölüp her chunk'ı oraya haritalayacağız.

Şimdi büyük bir kapsam çatalı, ve çıktı zenginliğini doğrudan belirliyor: konuşmacı ayrımı (diarization). Bir Teams toplantısında birden fazla kişi var ve sistem-sesi loopback hepsini tek karışık akışta yakalar. Burada iki dünya var:

Toplam/oda duygusu: Akışı olduğu gibi analiz ederiz — "toplantının genel duygusu şu an gergin". Basit, hafif, gizlilik-dostu (kim söyledi bilinmez).
Konuşmacı-başına duygu: "Ahmet gergin, Ayşe nötr" — kim ne zaman konuştu ayrımı (diarization) + her konuşmacıya duygu. Çok daha zengin ama ayrı bir ağır alt-sistem (pyannote gibi diarization modelleri, çoğu Python-merkezli, ONNX'e zor gelir, CPU/GPU yükü ciddi artar) ve bir gizlilik/rıza boyutu açar (kişi-bazlı duygu profili).
Bu, capture mimarisini, model yükünü ve depolamayı hep birden etkiliyor:

AskUserQuestion
OUT
Your questions have been answered: "Duygu analizi konuşmacı-bazında mı yoksa toplam-akış (oda) bazında mı olsun?"="V1 toplam-akış, mimari konuşmacı-hazır (önerilen)". You can now continue with these answers in mind.
İyi seçim — hafif başla, mimariyi konuşmacı-hazır tut.

Büyük çatalların çoğunu kapattık. Şimdi çıktı zenginliği + füzyon algoritmasını birlikte tasarlayalım (ikisi ayrılmaz), çünkü siz "sadece skor değil, çok metrik" istediniz. Bunu bir tasarım bloğu olarak sunuyorum, sonra onayınızı alıp capture'a geçeceğim.

Çıktı metrikleri (her chunk için)
Her iki model de ham olarak şunu üretir:

valence (−1..+1, sürekli), arousal (0..1, sürekli)
Model kendi güveni: tahmin varyansı / entropi → modelConfidence
Metin modeli ayrıca: ayrık duygu dağılımı (ham), konuşulan dil, transkript kalitesi sinyali (kelime sayısı, whisper avg-logprob).
Prozodi modeli ayrıca: ses kalite sinyalleri (SNR/enerji, konuşma oranı vs sessizlik, süre) — bunlar füzyon ağırlığını besler.

Füzyon sonrası, chunk düzeyi:

valence, arousal (birleştirilmiş) → primaryEmotion + secondaryEmotion (VA çarkından, ikincisi nüans için)
confidence (yüksek/orta/düşük) ve agreement (iki model aynı kadranda mı?)
timestamp aralığı → duygu zaman çizelgesi bundan oluşur
Toplantı düzeyi (bitişte agregasyon):

Baskın duygu, her duyguda geçirilen % süre
Valence trendi (toplantı ısındı mı, soğudu mu?), duygusal oynaklık (volatilite)
Tepe anlar (en yüksek arousal / en düşük valence chunk'ları — "kritik anlar")
Ortalama VA + doğal-dilde Türkçe özet cümlesi
Füzyon algoritması (saf C#, model değil, %100 test edilebilir)
Naif "iki dağılımı topla" yerine eksen-başına, güvenilirlik-ağırlıklı füzyon:


arousal_final = w_a_audio·arousal_audio + w_a_text·arousal_text     (w_a_audio yüksek)
valence_final = w_v_text·valence_text  + w_v_audio·valence_audio     (w_v_text yüksek)
Ağırlıklar sabit değil, girdi kalitesiyle dinamik modüle edilir:

Ses gürültülü / çok kısa / çoğu sessizse → ses ağırlığı düşer
Transkript boş / çok kısa / düşük logprob ise → metin ağırlığı düşer
İki model zıt kadranlarda ve ikisi de düşük güvenliyse → sonuç belirsiz (stajyerin dürüst yaklaşımı; uydurmaktan iyidir)
Bu tasarım: (1) sizin çok-metrik isteğinizi karşılar, (2) Spotify-tarzı boyutsal çekirdeğe oturur, (3) her modeli güçlü olduğu yerde kullanır.

AskUserQuestion
OUT
The user answered: "Çok-metrik çıktı şeması + eksen-başına güvenilirlik-ağırlıklı füzyon tasarımını onaylıyor musunuz?"="metriklerde önerin iyidir. Şöyle düşün ki ağlamaklı bir ses tonuyla "çok mutluyum" diyebilirşm, yada gülerek ve neşeli şekilde "bugün çok üzgünüm" diyebilirim. Bu durumda sonucun ne olacağı da önemli mesela bunu da düşün.". Read the answers carefully — they may request clarification, changes, or that you not proceed — and follow what they actually say.
Çok değerli bir nokta — ve aslında iki-model sisteminin bütün varlık sebebi bu. Tek modelli bir sistem bunu asla yakalayamaz. Bu vaka benim önerdiğim naif "valence hep metinden" kuralını da inceltiyor, iyi ki söylediniz.

Kilit içgörü: uyumsuzluk (incongruence) gürültü değil, birinci sınıf bir sinyaldir. Sözler ve ton çeliştiğinde bu; ironi/sarkazm, duygu maskeleme, ya da karmaşık/karışık duygu demektir — ve toplantıda bunu tespit etmek en değerli çıktılardan biridir ("herkes 'olur' dedi ama ses tonu isteksizdi").

İki vakanızı sistemin nasıl işlemesi gerektiğiyle çözelim:

Girdi	Metin valence	Ses valence	Ses arousal	Doğru yorum
Ağlamaklı "çok mutluyum"	+ (olumlu)	− (ağlama)	orta-yüksek	Maskeleme — gerçek durum olumsuz
Gülerek "çok üzgünüm"	− (olumsuz)	+ (neşe/gülme)	yüksek	Şaka/hafiflik — gerçek durum olumsuz değil
Her iki satırda da ses tonu gerçeği söylüyor, sözler değil. Bu, iyi bilinen "affect leakage" ilkesi: insan sözcükleri kontrol edebilir ama prozodi sızdırır. Dolayısıyla tasarımı şöyle inceltiyorum:

Her chunk'ta valence_text ile valence_audio ıraksamasını ölçeriz.
Uyumlu (agree) → güvenle birleştir, valence metin-ağırlıklı, confidence: yüksek.
Uyumsuz (diverge) → incongruent = true bayrağı + örüntü sınıfı ("olumlu söz / olumsuz ton" = olası maskeleme-ironi; "olumsuz söz / neşeli ton" = olası şaka). Bu durumda nihai valence prozodiye yaslanır (gerçek duygu), ama asla tek cevaba indirgemeyiz — her iki sinyali de + yorumu açıkça raporlarız.
Yeni bir üst-metrik doğar: congruence/authenticity skoru — "söylenen ile hissedilen ne kadar örtüşüyor". Toplantı analizinde altın değerinde.
Tek karar noktası — uyumsuz vakada nihai etiketi nasıl sunalım:

AskUserQuestion
OUT
Your questions have been answered: "Sözler ve ton çeliştiğinde (uyumsuz vaka) sistem nihai duyguyu nasıl sunsun?"="Prozodi-baskın + iki sinyali de göster (önerilen)". You can now continue with these answers in mind.
Mükemmel — bu, ürünü tek-model rakiplerden ayıran çekirdek. Bunu spec'te örnek tablolarıyla sabitleyeceğim.

Şimdi en zorlu teknik parçaya geliyoruz: canlı ses yakalama. "Teams'i dinlemek" demek, uzaktaki katılımcılar dahil sistemden çalan sesi yakalamak demek — sadece mikrofon uzaktakileri kaçırır. Ve bu, macOS'ta Windows'tan çok daha çetrefil. Temiz mimari yine bir arayüz: IAudioCaptureSource, platform-başına implementasyon. Windows tarafı kolay (NAudio + WASAPI loopback, ekstra kurulum yok). Asıl karar macOS:

Yol	Nasıl	Artı	Eksi
ScreenCaptureKit (macOS 13+)	Küçük native Swift yardımcı süreç PCM'i .NET'e akıtır	Sıfır-kurulum, Apple-onaylı, sadece Teams sürecine kapsanabilir, temiz	Native interop katmanı yazmak gerek (ön iş)
CoreAudio process-tap (macOS 14.4+)	Belirli uygulamanın sesini "tap"le	En temiz per-app, sıfır-kurulum	Sadece çok yeni macOS; yine native katman
BlackHole (sanal aygıt)	Kullanıcı ücretsiz sürücü kurar, sesi yönlendirir, biz normal girdi aygıtı gibi okuruz	.NET'te kodlaması en kolay	Kullanıcı kurulumu + ses yönlendirme derdi; sesi "çalar" (multi-output ayarı gerekir); tüm sistem sesi karışır
İki önemli tasarım notu:

Kapsamlama: ScreenCaptureKit/process-tap ile yalnızca Teams uygulamasının sesini yakalayabiliriz — arka planda çalan Spotify vs. karışmaz. BlackHole tüm sistem sesini karıştırır. Bu, doğruluk için ciddi bir avantaj.
Rıza/etik: Toplantı kaydı yasal olarak rıza gerektirir. Ürün, kayıt/dinleme aktifken görünür bir gösterge ve toplantı öncesi onay akışı içermeli. Bunu tasarıma ilke olarak koyacağım.
AskUserQuestion
OUT
Your questions have been answered: "macOS'ta canlı ses yakalama için birincil yol hangisi olsun? (Windows zaten WASAPI loopback ile kolay)"="ScreenCaptureKit + küçük native yardımcı (önerilen)". You can now continue with these answers in mind.
ScreenCaptureKit — profesyonel ve kalıcı yol. Native Swift yardımcı + IAudioCaptureSource soyutlaması.

Şimdi sizin "depolama çok kritik" dediğiniz yere geliyoruz — ve bu hem gizlilik hem disk kararı. Toplantı sesi = son derece hassas kişisel veri. Ne kadarını, ne kadar süre saklayacağımız hem etik hem teknik bir çatal. Boyut gerçeği: 16kHz mono ham WAV ~115 MB/saat; Opus 24kbps ~11 MB/saat (10x tasarruf); sadece metrik+transkript ise ~KB'ler.

Üç politika:

AskUserQuestion
OUT
Your questions have been answered: "Ne saklanacak ve ne kadar süre? (Gizlilik + disk dengesi)"="Katmanlı: metrik kalıcı, ses geçici+şifreli (önerilen)". You can now continue with these answers in mind.
Katmanlı depolama — dengeli seçim. Metrikler kalıcı/küçük, ses Opus+şifreli/7 gün, sonra otomatik temizlik.

Neredeyse tüm çatalları kapattık. Şimdi sizin vurguladığınız optimizasyon ve ardından .NET mimari iskeletini birer tasarım bloğu olarak sunuyorum — bunlar sert çatal değil, onayınızla ilerleyeceğim.

İşleme hattı + optimizasyon (lokal CPU/GPU kritik)
Çekirdek fikir: backpressure'lı, bölmeli (bounded) bir producer/consumer boru hattı (System.Threading.Channels). Canlı yakalama üretici, işleme tüketici; hat geri kalırsa nazikçe degrade eder (chunk atlar / turbo'da kalır), çökmez.


[Capture] → ring buffer → [Chunker 10-30sn] → [VAD kapısı] → ┬→ [STT turbo] → [Metin duygu ONNX]
                                                              └→ [Prozodi VAD-regresyon ONNX]
                                                                         ↓
                                                              [Füzyon (saf C#)] → [Zaman çizelgesi + kalıcılık]
Kilit optimizasyonlar:

VAD kapısı (Silero VAD, ~KB'lik ONNX): sessiz/konuşmasız chunk'ları STT'ye hiç sokma. Hem büyük CPU tasarrufu hem kalite (sessizliği transkribe etmeyiz). Belki en yüksek getirili tek optimizasyon.
Model yaşam döngüsü: modeller açılışta bir kez yüklenir, sıcak tutulur (singleton), asla istek başına yüklenmez.
Quantization: Whisper GGUF q5; duygu modelleri INT8 quantized ONNX. Hız + bellek.
Donanım hızlandırma: macOS → whisper.cpp Metal + ONNX CoreML EP; Windows → CUDA/DirectML.
Bellek disiplini: PCM tamponları ArrayPool<float> ile havuzlanır, GC baskısı düşer — uzun toplantıda kritik.
Degradasyon politikası: canlı hat geri kalırsa turbo'da kal + chunk birleştir; "derin large-v3 geçişi" toplantı bittikten sonra boşta yapılır.
.NET çözüm mimarisi (clean, katmanlı, cross-platform)
Avalonia UI + saf .NET çekirdek. Bağımlılıklar hep içe doğru; çekirdek hiçbir platform/kütüphaneye bağlı değil (test edilebilirlik):


SentimentAnalysis.sln
├─ Core            (domain: Emotion, ValenceArousal, ChunkResult, MeetingSession; saf C#, sıfır bağımlılık)
├─ Application     (arayüzler: ISpeechToText, ITextEmotionModel, IProsodyEmotionModel,
│                   IEmotionFusion, IAudioCaptureSource, ISessionStore; orkestrasyon/pipeline)
├─ Infrastructure
│   ├─ Stt         (WhisperNetEngine)
│   ├─ Emotion     (Onnx* implementasyonları; PySidecar* escape-hatch)
│   ├─ Audio       (WindowsLoopbackCapture, MacScreenCaptureKitCapture + native yardımcı)
│   ├─ Vad         (SileroVadGate)
│   └─ Storage     (SqliteSessionStore, Opus + şifreleme)
├─ Desktop.Avalonia (tepsi UI, canlı gösterge, toplantı geçmişi)
└─ Tests           (Core/Application %100 birim; Infrastructure entegrasyon; eval harness)
Not: stajyerin eval/ altyapısı fikrini koruyoruz — model/füzyon kararlarını Türkçe veri üzerinde ölçmek için ayrı bir değerlendirme projesi (doğruluk + kapsam, keyfi değil ölçülü kararlar).

AskUserQuestion
OUT
Your questions have been answered: "İşleme hattı/optimizasyon + .NET katmanlı mimari iskeletini onaylıyor musunuz?"="Evet, ikisi de iyi". You can now continue with these answers in mind.
Harika — tüm büyük çatalları kapattık. Şimdi brainstorm'un son adımı: bu büyük sistemi bağımsız alt-projelere ayırmak (her biri ileride kendi spec→plan→uygulama döngüsünü alacak) ve teslim dokümanlarının biçimini kararlaştırmak.

Alt-proje ayrışımı ve önerilen inşa sırası
#	Alt-proje	Ne	Neden bu sıra
1	Temel + Domain	Core domain modeli (ValenceArousal, 8-duygu çarkı, ChunkResult, MeetingSession) + tüm arayüzler + DI/config iskeleti	Her şeyin bastığı zemin; saf C#, ML yok
2	Füzyon motoru	Eksen-başına güvenilirlik, uyumsuzluk tespiti, congruence, ayrık harita, toplantı agregasyonu	Saf C#, %100 test edilebilir, ML paketleme derdi olmadan erken kazanç
3	STT motoru	Whisper.net + large-v3-turbo, model yönetimi, Metal/CUDA	Duygu modellerinin girdisi
4	Duygu modelleri	Metin (çok-dilli ONNX) + prozodi (VAD-regresyon ONNX) + ONNX export hattı + sidecar escape-hatch	Çekirdek kalite
5	Ses yakalama	IAudioCaptureSource: Windows WASAPI + macOS ScreenCaptureKit native yardımcı + Silero VAD kapısı	Canlı girdi
6	Pipeline orkestrasyonu	Bounded channels, chunker, backpressure, degradasyon, canlı zaman çizelgesi	3-4-5'i birleştirir
7	Depolama + gizlilik	SQLite, Opus, şifreleme, saklama/otomatik-temizlik	Kalıcılık katmanı
8	Avalonia UI	Tepsi, canlı gösterge, rıza akışı, toplantı geçmişi/rapor	Kullanıcı yüzü
9	Değerlendirme (eval)	Türkçe veri seti, model karşılaştırma, füzyon kalibrasyonu	3-4 ile paralel yürür
Önerilen ilk dikey dilim: #1 → #2 → #3 → #4. Yani önce saf-C# çekirdek + füzyon (paketleme acısı sıfır, test yoğun), sonra STT, sonra duygu modelleri. Ses yakalama (#5) ve UI (#8) sonraki dalga.

Bir sonraki tek soru — teslim dokümanlarının biçimi (siz bunu başka bir agent'a vereceksiniz, "human-first" istediniz):

AskUserQuestion
OUT
Your questions have been answered: "Teslim için dokümanları nasıl yapılandıralım?"="Master tasarım + alt-proje spec taşlakları (önerilen)". You can now continue with these answers in mind.
Tüm kararlar netleşti. Şimdi onaylı tasarımı yazıyorum: bir master tasarım dokümanı + 9 alt-proje spec taşlağı. Önce dizinleri ve master dokümanı oluşturuyorum.

Bash Create docs directory structure
IN
mkdir -p /Users/ilkaygokbudak/Development/Projects/sentiment-analysis/docs/superpowers/specs/subprojects && echo "created"

OUT
created
Write 2026-07-24-sesli-duygu-analizi-master-design.md
326 lines
# Sesli Duygu Analizi — Master Tasarım Dokümanı

> **Durum:** Onaylanmış tasarım (brainstorm çıktısı) · **Tarih:** 2026-07-24
> **Amaç:** Bu doküman, projenin bütününü başka bir geliştirici/agent'ın sıfırdan
> anlayıp inşa edebileceği şekilde anlatır. Kararların yalnızca *ne* olduğunu değil
> *neden* öyle olduğunu da içerir (human-first). Uygulama planı ayrı çıkacaktır;
> bu doküman **kod yazma** değil, **ortak anlayış** dokümanıdır.

---

## 1. Vizyon ve Kapsam

Ses kayıtlarından ve **canlı konuşmalardan** (özellikle Teams toplantıları) duygu
analizi yapan, **tamamen lokal** çalışan bir masaüstü agent'ı. Sistem iki bağımsız
modelin çıktısını ilkeli bir füzyon algoritmasıyla birleştirip nihai bir duygu
sonucu ve zengin metrikler üretir.

**İki model, iki bakış açısı:**

1. **Metin modeli (ne söylendi):** Konuşma → metin (STT) → metnin duygusu.
2. **Prozodi modeli (nasıl söylendi):** Ham ses dalgasının tonu, temposu, enerjisi
   → duygu. Kelimelere değil, sesin *müzikalitesine* bakar (kullanıcının Spotify
   benzetmesi tam olarak budur).

**Neden iki model?** Çünkü insan sözcükleri kontrol edebilir ama ses tonu gerçeği
sızdırır. Ağlamaklı bir sesle "çok mutluyum" veya gülerek "bugün çok üzgünüm"
denebilir. Tek modelli sistem bunu asla yakalayamaz; bizim sistemimizin **varlık
sebebi** bu uyumsuzluğu tespit edip anlamlandırmaktır.

### Bu proje neyi HEDEFLER
- Lokal, gizlilik-öncelikli çalışma (kişisel ses verisi makineden çıkmaz).
- Canlı Teams toplantısını dinleyip near-real-time duygu zaman çizelgesi üretmek.
- Ses dosyası yükleyip analiz etmek.
- Türkçe-öncelikli, İngilizce'ye mimari olarak hazır.
- Çok-metrikli, boyutsal (valence-arousal) çekirdekli zengin çıktı.
- Clean, katmanlı, test edilebilir .NET kodu ve ölçülebilir model kararları.

### Bu proje neyi HEDEFLEMEZ (YAGNI)
- Bulut/API tabanlı LLM kullanımı (gizlilik ihlali).
- Gerçek-zamanlı (kelime kelime) streaming — near-real-time chunk yeterli.
- V1'de konuşmacı-başına duygu (diarization) — mimari hazır, uygulama sonraya.
- V1'de İngilizce tam doğrulama — mimari hazır, tune/valide sonraya.
- Web arayüzü — bu bir masaüstü agent'tır.

---

## 2. Kararlar Kütüğü (rationale ile)

Bu tablo brainstorm'da verilen her büyük kararı ve *nedenini* özetler. Bir tasarım
kararını değiştirmek isteyen herkes önce buraya bakmalı.

| # | Karar | Seçim | Neden |
|---|---|---|---|
| D1 | Platform/form-factor | Cross-platform masaüstü (Avalonia + .NET), macOS-first pratikte; ileride Windows service | Ekip macOS ağırlıklı; tek kod tabanı; Teams sesi lokal yakalanır |
| D2 | Zamansal mod | Chunked near-real-time (10–30 sn pencere) + toplantı-sonu özet | Lokal donanımda gerçekçi; streaming STT karmaşasından kaçınır; yine "canlı" hissi |
| D3 | Model barındırma | Hibrit: **Whisper.net** (STT) + **ONNX Runtime** (duygu), Python sidecar açık escape-hatch | Tek runtime, temiz paketleme, macOS Metal/CoreML; egzotik model gerekirse sidecar |
| D4 | Başlangıç duruşu | ONNX-first; bir model export edilemezse sadece o sidecar'a düşer | Kalite > paketleme kolaylığı; ama mimari değişmez |
| D5 | Dil stratejisi | Türkçe-first tune/valide + çok-dilli-hazır mimari | İngilizce ileride gelecek; STT/SER zaten dil-agnostik, borç minimal |
| D6 | STT modeli | **Whisper large-v3-turbo** (GGUF q5, varsayılan); large-v3 "derin geçiş" için opsiyonel | Yüksek doğruluk/hız dengesi; stajyerin `base`'i yetersizdi |
| D7 | Duygu temsili | **Boyutsal çekirdek (valence-arousal)** + ayrık haritalama | SER'in etiket kısıtını aşar; Spotify-tarzı; iki modeli tamamlayıcı yapar |
| D8 | Ayrık duygu çarkı | **8 duygu circumplex**: Coşkulu, Mutlu, Sakin, Sıkılmış, Üzgün, Gergin, Kızgın, Nötr | VA uzayını dengeli kaplar; toplantı için anlamlı; ne az ne fazla |
| D9 | Konuşmacı ayrımı | V1 toplam-akış (oda), mimari konuşmacı-hazır (`SpeakerId` alanı) | Diarization ağır ve çoğu Python; borcu minimize et |
| D10 | Füzyon | Eksen-başına güvenilirlik-ağırlıklı: arousal←ses, valence←metin; kaliteyle dinamik | Arousal sesten güvenilir, valence metinden güvenilir; naif ortalamadan sağlam |
| D11 | Uyumsuzluk | Prozodi-baskın + iki sinyali de göster + congruence skoru | Affect leakage: ses gerçeği söyler; ama dürüstçe her iki sinyali sun |
| D12 | macOS capture | **ScreenCaptureKit** + küçük native Swift yardımcı süreç | Sıfır-kurulum, Teams sürecine kapsanabilir, profesyonel |
| D13 | Depolama | Katmanlı: metrik+transkript kalıcı; ses Opus+şifreli, 7 gün sonra oto-sil | Yeniden-analize izin ver, sonra gizlilik için temizle; disk dostu |
| D14 | UI | Avalonia (tepsi + minimal pencere) | Cross-platform, tek kod tabanı |
| D15 | Kalite kanıtı | Ayrı `eval` projesi (Türkçe veri, doğruluk + kapsam) | Keyfi değil ölçülü model/füzyon kararları (stajyer yaklaşımını koru) |

---

## 3. Duygu Temsili: Valence-Arousal Çekirdeği

Sistemin **iç para birimi** ayrık etiketler değil, iki sürekli eksendir:

- **Valence** (−1 … +1): olumsuz ↔ olumlu. "Kötü mü iyi mi hissediliyor?"
- **Arousal** (0 … 1): sakin ↔ uyarılmış/enerjik. "Ne kadar yoğun?"

Ayrık 8 duygu, bu 2B uzaydan **en sonda** haritalanır (circumplex modeli):

```
                    Arousal ↑ (yüksek enerji)
                         |
     Gergin/Stresli      |      Coşkulu
        (V− A+)          |       (V+ A+)
                         |
   Kızgın (V−− A+)       |
 ── ─────────────────────●───────────────────── →  Valence
   Valence −             |             Valence +
                         |
       Üzgün (V− A↓)     |      Mutlu (V+ orta-A)
                         |
      Sıkılmış (V− A−)   |    Sakin/Memnun (V+ A−)
                         |
                    Arousal ↓ (düşük enerji)

                (merkez ≈ Nötr)
```

**Neden bu tasarım kritik?** İki eksen, iki modeli **rakip değil tamamlayıcı** yapar:

| Eksen | Güvenilir kaynak | Neden |
|---|---|---|
| **Arousal** | **Ses (prozodi)** | Bağırma, tempo, ses tizliği; dilden bağımsız, güvenilir |
| **Valence** | **Metin** | Kelimelerin anlamı; sesten valence zayıf ölçülür |

Bu, füzyonun temelidir (Bölüm 5).

---

## 4. İki Model

### 4.1 Metin modeli (ne söylendi)
- **Girdi:** STT'den gelen transkript + algılanan dil.
- **Çıktı:** `valence_text`, `arousal_text`, ayrık duygu dağılımı (ham),
  `modelConfidence` (entropi/varyans), transkript kalite sinyali.
- **Model tipi:** Çok-dilli metin duygu/sentiment modeli (ONNX-export edilebilir
  standart mimari — ör. XLM-R tabanlı). Ayrık çıktı VA'ya lookup ile haritalanır,
  ya da doğrudan VA-regresyon veren bir model tercih edilir.
- **Dil:** Şimdi Türkçe tune/valide; İngilizce `ITextEmotionModel`'in yeni bir
  implementasyonu olarak gelir.

### 4.2 Prozodi modeli (nasıl söylendi)
- **Girdi:** Ham 16 kHz mono ses dalgası (chunk).
- **Çıktı:** `valence_audio`, `arousal_audio` (**doğrudan VAD-regresyon**),
  `modelConfidence`, ses kalite sinyalleri (SNR/enerji, konuşma-oranı, süre).
- **Model tipi:** wav2vec2/HuBERT tabanlı **boyutsal (dimensional) SER** — ayrık
  etiket değil, valence-arousal-(dominance) regresyonu veren standart mimari
  (ONNX uyumlu). Bu, stajyerin özel-mimari HuBERT tuzağından **bilinçli kaçıştır**.
- **Not:** Prozodi dil-agnostiktir; İngilizce için değişmez.

> **Uyarı (stajyer README'sinden ders):** Standart olmayan bir sınıfla kaydedilmiş
> SER checkpoint'leri `pipeline()`/naif yükleyicilerle sessizce bozulur (sınıflandırma
> başlığı atılır). **Model seçiminde ONNX-export edilebilirlik bir kabul kriteridir.**

---

## 5. Füzyon Algoritması (saf C#, %100 test edilebilir)

Füzyon bir model değil, **matematiktir** — hiçbir runtime bağımlılığı yok, tam test
edilebilir. Naif "iki dağılımı ağırlıkla topla" yerine **eksen-başına güvenilirlik-
ağırlıklı** yaklaşım:

```
arousal_final = w_a_audio · arousal_audio + w_a_text · arousal_text    (w_a_audio yüksek)
valence_final = w_v_text  · valence_text  + w_v_audio · valence_audio   (w_v_text  yüksek)
```

Ağırlıklar **sabit değil**, girdi kalitesiyle dinamik modüle edilir:
- Ses gürültülü / çok kısa / çoğu sessiz → ses ağırlığı düşer.
- Transkript boş / çok kısa / düşük Whisper avg-logprob → metin ağırlığı düşer.

### 5.1 Uyumsuzluk (incongruence) işleme — sistemin kalbi

Her chunk'ta `valence_text` ile `valence_audio` **ıraksaması** ölçülür.

| Durum | Davranış |
|---|---|
| **Uyumlu** (agree) | Güvenle birleştir; valence metin-ağırlıklı; `confidence: yüksek` |
| **Uyumsuz** (diverge) | `incongruent = true` + örüntü sınıfı; **valence prozodiye yaslanır** (affect leakage); ama **iki sinyal de** raporlanır |
| **İki model zıt kadran + ikisi de düşük güven** | Nihai sonuç `belirsiz` (uydurmaktan dürüsttür) |

**Uyumsuzluk örüntüleri:**

| Girdi örneği | Metin val. | Ses val. | Ses arousal | Yorum | Nihai |
|---|---|---|---|---|---|
| Ağlamaklı "çok mutluyum" | + | − | orta-yüksek | **Maskeleme** | Olumsuz (prozodi), bayraklı |
| Gülerek "çok üzgünüm" | − | + | yüksek | **Şaka/hafiflik** | Olumsuz değil (prozodi), bayraklı |
| Düz tonla "harika, bayıldım" | + | ~0 nötr | düşük | **Olası ironi/sarkazm** | Şüpheli, bayraklı |

**Yeni üst-metrik:** `congruence` (0–1) = söylenen ile hissedilenin örtüşmesi.
Toplantı analizinde altın değerinde ("herkes 'olur' dedi ama ton isteksizdi").

### 5.2 Ayrık haritalama
`valence_final, arousal_final` → 8-duygu çarkında en yakın bölge → `primaryEmotion`;
ikinci en yakın → `secondaryEmotion` (nüans için).

---

## 6. Çıktı Metrikleri

### Chunk düzeyi
- `valence`, `arousal` (birleşik), `primaryEmotion`, `secondaryEmotion`
- `confidence` (yüksek/orta/düşük), `agreement`, `incongruent` (+ örüntü), `congruence`
- `timeRange`, kaynak sinyaller (metin ve ses ham VA + güven)

### Toplantı düzeyi (bitişte agregasyon)
- Baskın duygu, her duyguda geçirilen % süre
- **Valence trendi** (ısındı/soğudu), **duygusal oynaklık** (volatilite)
- **Tepe anlar** (en yüksek arousal / en düşük valence chunk'ları = "kritik anlar")
- Ortalama VA + doğal-dilde Türkçe özet cümlesi
- Ortalama congruence (toplantıda ne kadar "samimi/örtüşük" konuşuldu)

---

## 7. Canlı Ses Yakalama

`IAudioCaptureSource` soyutlaması, platform-başına implementasyon:

| Platform | Yol | Kurulum |
|---|---|---|
| **Windows** | WASAPI loopback (NAudio) | Ekstra yok |
| **macOS** | **ScreenCaptureKit** + küçük native Swift yardımcı süreç → PCM'i .NET'e akıtır | Ekstra yok; sadece ekran-kaydı izni |

**Avantaj:** ScreenCaptureKit/process-tap ile **yalnızca Teams sürecinin** sesi
yakalanabilir → arka plandaki müzik vb. karışmaz (doğruluk kazanımı).

**Rıza/etik ilkesi (zorunlu):** Kayıt/dinleme aktifken **görünür gösterge** ve
toplantı öncesi onay akışı. Kayıt yasal olarak rıza gerektirir; ürün bunu
görünür kılar.

---

## 8. İşleme Hattı ve Optimizasyon

Backpressure'lı, **bounded** producer/consumer hattı (`System.Threading.Channels`):

```
[Capture] → ring buffer → [Chunker 10–30sn] → [VAD kapısı] → ┬→ [STT turbo] → [Metin duygu ONNX]
                                                             └→ [Prozodi VAD-reg. ONNX]
                                                                        ↓
                                                          [Füzyon (saf C#)] → [Zaman çizelgesi + kalıcılık]
```

**Kilit optimizasyonlar:**
- **VAD kapısı (Silero VAD, ~KB ONNX):** sessiz chunk'ları STT'ye hiç sokma —
  en yüksek getirili tek optimizasyon (hem CPU hem kalite).
- **Model yaşam döngüsü:** açılışta bir kez yükle, sıcak tut (singleton).
- **Quantization:** Whisper GGUF q5; duygu modelleri INT8 ONNX.
- **Donanım:** macOS → whisper.cpp **Metal** + ONNX **CoreML EP**; Windows →
  CUDA/DirectML.
- **Bellek disiplini:** PCM tamponları `ArrayPool<float>`; GC baskısı düşer.
- **Degradasyon:** hat geri kalırsa turbo'da kal + chunk birleştir; "derin
  large-v3 geçişi" toplantı bittikten sonra boşta yapılır.

---

## 9. .NET Çözüm Mimarisi

Clean, katmanlı; bağımlılıklar hep **içe** doğru; `Core` hiçbir platform/kütüphaneye
bağlı değil.

```
SentimentAnalysis.sln
├─ Core            (domain: Emotion, ValenceArousal, ChunkResult, MeetingSession; saf C#, sıfır bağımlılık)
├─ Application     (arayüzler: ISpeechToText, ITextEmotionModel, IProsodyEmotionModel,
│                   IEmotionFusion, IAudioCaptureSource, ISessionStore, IVadGate; orkestrasyon/pipeline)
├─ Infrastructure
│   ├─ Stt         (WhisperNetEngine)
│   ├─ Emotion     (Onnx* implementasyonları; PySidecar* escape-hatch)
│   ├─ Audio       (WindowsLoopbackCapture, MacScreenCaptureKitCapture + native yardımcı)
│   ├─ Vad         (SileroVadGate)
│   └─ Storage     (SqliteSessionStore, Opus + şifreleme)
├─ Desktop.Avalonia (tepsi UI, canlı gösterge, rıza akışı, toplantı geçmişi/rapor)
└─ Tests           (Core/Application %100 birim; Infrastructure entegrasyon; eval harness)
```

**Temel arayüzler (sözleşmeler):**

```csharp
public interface ISpeechToText {
    Task<Transcript> TranscribeAsync(AudioChunk chunk, CancellationToken ct);
}
public interface ITextEmotionModel {
    Task<TextEmotion> AnalyzeAsync(Transcript transcript, CancellationToken ct);
}
public interface IProsodyEmotionModel {
    Task<ProsodyEmotion> AnalyzeAsync(AudioChunk chunk, CancellationToken ct);
}
public interface IEmotionFusion {                       // saf, senkron, deterministik
    FusedResult Fuse(TextEmotion text, ProsodyEmotion prosody, QualitySignals quality);
}
public interface IAudioCaptureSource {
    IAsyncEnumerable<AudioFrame> CaptureAsync(CaptureOptions options, CancellationToken ct);
}
public interface IVadGate { bool HasSpeech(AudioChunk chunk); }
public interface ISessionStore { /* kaydet, sorgula, saklama/temizlik */ }
```

---

## 10. Alt-Proje Ayrışımı ve İnşa Sırası

Her alt-proje kendi spec taşlağına sahiptir (`subprojects/`).

| # | Alt-proje | Bağımlı olduğu |
|---|---|---|
| 1 | Temel + Domain | — |
| 2 | Füzyon motoru | 1 |
| 3 | STT motoru | 1 |
| 4 | Duygu modelleri | 1, (girdi için) 3 |
| 5 | Ses yakalama + VAD | 1 |
| 6 | Pipeline orkestrasyonu | 1–5 |
| 7 | Depolama + gizlilik | 1 |
| 8 | Avalonia UI | 1, 6, 7 |
| 9 | Değerlendirme (eval) | 2, 3, 4 (paralel) |

**Önerilen ilk dikey dilim:** 1 → 2 → 3 → 4 (saf-C# çekirdek + füzyon önce; paketleme
acısı yok, test yoğun). Ses yakalama (5) ve UI (8) sonraki dalga.

---

## 11. Riskler ve Açık Sorular

| Risk / soru | Etki | Azaltma |
|---|---|---|
| Türkçe boyutsal (VA) SER modeli bulunamazsa | Prozodi kalitesi | Ayrık Türkçe SER + VA'ya haritalama; ya da İng. eğitilmiş VA modelini prozodi-arousal için kullan (arousal dil-agnostik) |
| Metin VA-regresyon modeli Türkçe zayıfsa | Valence kalitesi | Ayrık duygu modeli + VA lookup; eval ile ölç |
| ScreenCaptureKit native interop karmaşası | macOS capture | İlk BlackHole ile doğrula (aynı arayüz), sonra native'e geç |
| Teams istemcisi (native vs tarayıcı) ses yolu | Yakalama kapsamı | Process kapsamı test edilmeli; gerekirse tüm-sistem fallback |
| Chunk sınırında kesilen kelime/duygu | Doğruluk | Örtüşmeli (overlapping) pencere; cümle sınırına hizalama |
| large-v3-turbo Türkçe doğruluğu yetersiz kalırsa | STT kalite | eval ile ölç; Türkçe fine-tuned aday veya large-v3 |
| Şifreleme anahtarı yönetimi (lokal) | Gizlilik | OS keychain/DPAPI ile anahtar; düz metin anahtar yok |

---

## 12. İlkeler (bu proje boyunca geçerli)

1. **Kalite > paketleme kolaylığı** — bir model gerçekten sidecar'da çok daha iyiyse,
   sidecar'da kalır (mimari buna izin verir).
2. **Dürüstlük > kesinlik gösterisi** — belirsizse `belirsiz` de.
3. **Uyumsuzluk gürültü değil, birinci sınıf sinyaldir.**
4. **Model kararları ölçülür, keyfi verilmez** (eval projesi).
5. **Gizlilik varsayılan** — veri lokal, ses geçici+şifreli, kayıt görünür.
6. **Soyutlama arkasında değiştirilebilirlik** — her model/aygıt bir arayüz ardında.
Master doküman hazır. Şimdi 9 alt-proje spec taşlağını yazıyorum (her biri: hedef, arayüz sözleşmesi, kararlar, kabul kriteri, bağımlılık).

Write 01-temel-domain.md
31 lines
# Alt-Proje 01 — Temel + Domain

> Bağımlılık: yok · İnşa sırası: **1 (ilk)** · Katman: `Core`, `Application`

## Hedef
Tüm sistemin bastığı zemini kurmak: saf-C# domain modeli, tüm arayüz sözleşmeleri,
DI/config iskeleti. Bu alt-projede **hiç ML yok** — sadece tipler ve kontratlar.

## Kapsam
- `Core` domain tipleri:
  - `ValenceArousal` (immutable, `Valence` −1..+1, `Arousal` 0..1; yardımcı: uzaklık, kadran).
  - `Emotion` enum: `Cosakulu, Mutlu, Sakin, Sikilmis, Uzgun, Gergin, Kizgin, Notr` (dil-bağımsız).
  - `EmotionWheel` — VA → en yakın `Emotion` haritalama (saf fonksiyon, bölge merkezleri).
  - `TextEmotion`, `ProsodyEmotion`, `QualitySignals`, `FusedResult`, `ChunkResult`.
  - `MeetingSession` (chunk listesi + toplantı-düzeyi agregatlar), `DetectedLanguage`.
  - `Confidence` (Yuksek/Orta/Dusuk), `IncongruencePattern` enum.
- `Application` arayüzleri: `ISpeechToText, ITextEmotionModel, IProsodyEmotionModel,
  IEmotionFusion, IAudioCaptureSource, IVadGate, ISessionStore`.
- DI kayıt iskeleti (`IServiceCollection` uzantıları) + `AppConfig` (limitler, model
  isimleri, füzyon eşikleri, saklama süresi) — stajyerin `config.py` fikrinin C# karşılığı.

## Kararlar
- Domain immutable ve saf; hiçbir I/O, platform veya kütüphane bağımlılığı yok.
- Duygu etiketleri **enum (dil-bağımsız)**; UI metni ayrı lokalize edilir (D5, D8).
- `DetectedLanguage` domainden akar (çok-dilli-hazır, D5).

## Kabul Kriteri
- `Core` projesi sıfır NuGet bağımlılığıyla derlenir.
- `EmotionWheel` haritalaması ve `ValenceArousal` yardımcıları %100 birim testli.
- Tüm arayüzler XML doc'lu; her biri "ne yapar / nasıl kullanılır / neye bağlı" sorularına cevap verir.
Write 02-fuzyon-motoru.md
36 lines
# Alt-Proje 02 — Füzyon Motoru

> Bağımlılık: 01 · İnşa sırası: **2** · Katman: `Application` (saf C#)

## Hedef
İki modelin çıktısını **eksen-başına güvenilirlik-ağırlıklı** birleştiren, uyumsuzluğu
tespit edip anlamlandıran, ayrık duyguya haritalayan deterministik motor. Model yok,
sadece matematik → **%100 test edilebilir.**

## Sözleşme
```csharp
public interface IEmotionFusion {
    FusedResult Fuse(TextEmotion text, ProsodyEmotion prosody, QualitySignals quality);
}
```

## Kapsam / Algoritma (master §5)
- Eksen-başına ağırlık: `arousal ← ses baskın`, `valence ← metin baskın`.
- Ağırlıklar kalite sinyalleriyle dinamik modüle edilir (gürültü, süre, logprob).
- **Uyumsuzluk tespiti:** `|valence_text − valence_audio|` eşiği → `incongruent`
  bayrağı + `IncongruencePattern` (Maskeleme / Şaka / Olası-ironi).
- Uyumsuz vakada **valence prozodiye yaslanır**, iki sinyal de raporlanır (D11).
- İki model zıt kadran + ikisi de düşük güven → `belirsiz`.
- `congruence` skoru (0–1) üretilir.
- Toplantı-düzeyi agregasyon: baskın duygu, %süre, valence trendi, oynaklık,
  tepe anlar, ortalama congruence.

## Kararlar
- Başlangıç ağırlıkları ve eşikleri `AppConfig`'ten; **kalibrasyon alt-proje 09'da**
  eval seti üzerinde ölçülür (keyfi sabit değil).

## Kabul Kriteri
- Master §5.1 tablosundaki üç örüntü (maskeleme, şaka, ironi) birim testlerle doğrulanır.
- Belirsiz-dönme koşulu testli.
- Saf fonksiyon: aynı girdi → aynı çıktı; hiçbir I/O yok.
Write 03-stt-motoru.md
31 lines
# Alt-Proje 03 — STT Motoru

> Bağımlılık: 01 · İnşa sırası: **3** · Katman: `Infrastructure/Stt`

## Hedef
Whisper large-v3-turbo ile lokal, donanım-hızlandırmalı konuşma→metin.

## Sözleşme
```csharp
public interface ISpeechToText {
    Task<Transcript> TranscribeAsync(AudioChunk chunk, CancellationToken ct);
}
```
`Transcript`: metin, `DetectedLanguage`, `AvgLogProb` (kalite sinyali), segment zamanları.

## Kapsam
- **Whisper.net** (whisper.cpp sarmalayıcı), **GGUF q5 large-v3-turbo** varsayılan.
- Donanım: macOS **Metal**, Windows CUDA/DirectML; runtime seçimi config'ten.
- Model açılışta bir kez yüklenir, sıcak tutulur (singleton).
- `AvgLogProb` düşükse (kalitesiz transkript) → füzyona sinyal.
- Opsiyonel "derin geçiş" için large-v3 ikinci profil (toplantı-sonu, D6).

## Kararlar
- 16 kHz mono float PCM standart girdi formatı.
- Dil algılama Whisper'a bırakılır (çok-dilli-hazır).

## Kabul Kriteri
- Türkçe örnek seste `base`'e göre belirgin daha iyi transkript (eval 09 ile ölçülür).
- macOS'ta Metal ile gerçek-zamanlıdan hızlı (chunk süresi < chunk uzunluğu).
- Model yükleme bir kez; istek başına yükleme yok.
Write 04-duygu-modelleri.md
35 lines
# Alt-Proje 04 — Duygu Modelleri (Metin + Prozodi)

> Bağımlılık: 01, 03 · İnşa sırası: **4** · Katman: `Infrastructure/Emotion`

## Hedef
İki duygu modelini ONNX Runtime ile lokal çalıştırmak; her biri **valence-arousal**
üretir. Sidecar escape-hatch aynı arayüz ardında hazır (D3, D4).

## Sözleşmeler
```csharp
public interface ITextEmotionModel   { Task<TextEmotion>    AnalyzeAsync(Transcript t, CancellationToken ct); }
public interface IProsodyEmotionModel { Task<ProsodyEmotion> AnalyzeAsync(AudioChunk c, CancellationToken ct); }
```

## Kapsam
- **Metin:** çok-dilli metin duygu/sentiment (ONNX). Ayrık çıktı → VA lookup, ya da
  doğrudan VA-regresyon. `modelConfidence` (entropi/varyans). Türkçe tune (D5).
- **Prozodi:** wav2vec2/HuBERT tabanlı **boyutsal (VAD-regresyon)** SER, ONNX.
  Ses kalite sinyalleri (SNR, konuşma-oranı, süre) üretir.
- **ONNX export hattı:** PyTorch→ONNX dönüşüm + INT8 quantization scriptleri (repo'da,
  tekrar üretilebilir).
- **Sidecar sözleşmesi:** `PySidecar*` implementasyonları için gRPC/IPC kontratı
  tanımlı; bir model export edilemezse yalnızca o düşer.

## Kararlar / Uyarılar
- **ONNX-export edilebilirlik bir KABUL KRİTERİDİR** (stajyerin özel-mimari HuBERT
  tuzağından kaçış).
- Prozodi dil-agnostik; İngilizce için değişmez.
- Kalite > paketleme: bir model sidecar'da anlamlı derecede iyiyse orada kalır.

## Kabul Kriteri
- İki model de macOS'ta CoreML EP ile çalışır, VA çıktısı verir.
- Export edilen ONNX çıktısı PyTorch referansıyla tolerans içinde eşleşir (regression test).
- Sidecar fallback en az bir modelle uçtan uca çalışır (kontrat kanıtı).
Write 05-ses-yakalama-vad.md
34 lines
# Alt-Proje 05 — Ses Yakalama + VAD

> Bağımlılık: 01 · İnşa sırası: **5** · Katman: `Infrastructure/Audio`, `Infrastructure/Vad`

## Hedef
Canlı sistem-sesini (Teams) ve dosya girdisini tek arayüz ardında yakalamak; sessizliği
eleyen VAD kapısı.

## Sözleşmeler
```csharp
public interface IAudioCaptureSource {
    IAsyncEnumerable<AudioFrame> CaptureAsync(CaptureOptions options, CancellationToken ct);
}
public interface IVadGate { bool HasSpeech(AudioChunk chunk); }
```

## Kapsam
- **Windows:** WASAPI loopback (NAudio) — ekstra kurulum yok.
- **macOS:** **ScreenCaptureKit** + küçük native **Swift yardımcı süreç**; PCM'i
  .NET'e (stdout/named pipe) akıtır. Yalnızca Teams sürecine kapsanabilir (D12).
- Dosya girdisi (mp3/wav/m4a) için `FileCaptureSource`.
- **Silero VAD** (~KB ONNX) kapısı: sessiz chunk STT'ye hiç girmez.
- Rıza göstergesi için "capture aktif" durumu dışarı verilir (UI dinler).

## Kararlar
- macOS'ta önce **BlackHole ile doğrulanabilir** (aynı arayüz), sonra native
  ScreenCaptureKit'e geçilir — mimari değişmez (risk azaltma).
- 16 kHz mono float PCM'e normalize; ring buffer + chunker (10–30 sn, örtüşmeli).

## Kabul Kriteri
- macOS'ta çalan sistem sesi yakalanır ve VAD sessizliği doğru eler.
- Windows'ta WASAPI loopback ile aynı `AudioFrame` akışı üretilir.
- Native yardımcı süreç çökerse .NET tarafı nazikçe durur (kaynak sızıntısı yok).
Write 06-pipeline-orkestrasyonu.md
23 lines
# Alt-Proje 06 — Pipeline Orkestrasyonu

> Bağımlılık: 01–05 · İnşa sırası: **6** · Katman: `Application`

## Hedef
Yakalama → chunk → VAD → (STT + prozodi paralel) → füzyon → zaman çizelgesi + kalıcılık
akışını backpressure ve degradasyon ile yönetmek.

## Kapsam
- **Bounded** producer/consumer hattı (`System.Threading.Channels`).
- Chunker: 10–30 sn örtüşmeli pencere, cümle sınırına hizalama denemesi.
- STT ve prozodi modelleri **paralel** (ikisi de aynı chunk'ı alır).
- Füzyon senkron; sonuç canlı zaman çizelgesine ve `ISessionStore`'a yazılır.
- **Degradasyon:** hat geri kalırsa turbo'da kal + chunk birleştir; kuyruk taşarsa
  en eski düşük-öncelikli işi düşür (canlı akıcılığı koru).
- **Toplantı-sonu derin geçiş:** boşta, isteğe bağlı large-v3 ile yeniden analiz.
- Toplantı bitişinde agregasyon tetiklenir (füzyon motorunun toplantı-düzeyi kısmı).

## Kabul Kriteri
- Uzun (ör. 1 saat) sentetik akışta bellek sabit kalır (ArrayPool; GC baskısı düşük).
- Yapay yavaşlatmada degradasyon devreye girer, sistem çökmez.
- Chunk sonuçları sırayla ve zaman-damgalı biçimde çizelgeye düşer.
Write 07-depolama-gizlilik.md
29 lines
# Alt-Proje 07 — Depolama + Gizlilik

> Bağımlılık: 01 · İnşa sırası: **7** · Katman: `Infrastructure/Storage`

## Hedef
Katmanlı, gizlilik-öncelikli kalıcılık: metrikler kalıcı, ses geçici+şifreli (D13).

## Sözleşme
```csharp
public interface ISessionStore {
    Task SaveChunkAsync(Guid sessionId, ChunkResult r, CancellationToken ct);
    Task<MeetingSession> GetSessionAsync(Guid id, CancellationToken ct);
    Task PurgeExpiredAudioAsync(CancellationToken ct);   // saklama politikası
    // sorgu/listeleme...
}
```

## Kapsam
- **SQLite** (metrik + transkript + toplantı özeti; küçük, kalıcı).
- Ham ses **Opus** (~24 kbps) ile sıkıştırılıp **disküstü şifreli** tutulur.
- **Saklama:** varsayılan 7 gün sonra ses otomatik silinir; metrik kalıcı.
- **Şifreleme anahtarı** OS keychain/DPAPI'de; düz metin anahtar yok.
- Ses dosyaları DB'den referanslı (yol + şifreleme meta).

## Kabul Kriteri
- `PurgeExpiredAudioAsync` süresi dolan sesi siler, metrikleri korur.
- Şifreli ses dosyası anahtarsız okunamaz.
- 1 saatlik toplantı ses boyutu ~11 MB civarı (Opus doğrulaması).
Write 08-avalonia-ui.md
27 lines
# Alt-Proje 08 — Avalonia UI

> Bağımlılık: 01, 06, 07 · İnşa sırası: **8** · Katman: `Desktop.Avalonia`

## Hedef
Cross-platform tepsi agent'ı + minimal pencere: canlı duygu göstergesi, rıza akışı,
toplantı geçmişi ve raporları.

## Kapsam
- **Sistem tepsisi** ikonu + başlat/durdur; "dinleniyor" görünür göstergesi (rıza).
- **Canlı görünüm:** VA noktası + 8-duygu çarkı, akan zaman çizelgesi, uyumsuzluk/
  congruence rozeti.
- **Toplantı-sonu rapor:** baskın duygu, %süre dağılımı, valence trendi grafiği,
  tepe anlar, Türkçe özet.
- **Geçmiş:** kayıtlı toplantı oturumları listesi (`ISessionStore`).
- **Dosya analizi:** ses dosyası sürükle-bırak → analiz.
- **Ayarlar:** saklama süresi, ses tutma açık/kapalı, STT profili (turbo/derin), dil.
- Duygu etiketleri UI'da **lokalize** (enum → Türkçe metin).

## Kararlar
- MVVM; UI hiçbir ML/IO'ya doğrudan bağlanmaz, `Application` servisleri üzerinden.

## Kabul Kriteri
- macOS ve Windows'ta tepsiden çalışır; capture aktifken gösterge görünür.
- Canlı zaman çizelgesi chunk sonuçlarını gecikmesiz yansıtır.
- Rapor görünümü kayıtlı bir oturumdan yeniden üretilebilir.
Write 09-degerlendirme-eval.md
26 lines
# Alt-Proje 09 — Değerlendirme (Eval)

> Bağımlılık: 02, 03, 04 (paralel yürür) · Katman: ayrı `eval` projesi/araçları

## Hedef
Model ve füzyon kararlarını **ölçmek** — keyfi değil, kanıta dayalı. Stajyerin
`eval/` yaklaşımını koru ve genişlet.

## Kapsam
- **Türkçe değerlendirme veri seti** hazırlığı (etiketli ses + metin alt kümesi).
- **STT değerlendirmesi:** large-v3-turbo vs adaylar (WER/CER, Türkçe).
- **Duygu modeli değerlendirmesi:** metin ve prozodi VA çıktılarının etiketle uyumu.
- **Füzyon kalibrasyonu:** eksen ağırlıkları ve uyumsuzluk eşiklerini veri üzerinde
  ayarla; iki sayı raporla — (a) katı doğruluk (belirsiz = yanlış), (b) kararlı
  örneklerde doğruluk + **kapsam** (belirsiz dönmeme oranı).
- Raporlar repo'ya yazılır (git'e dahil), veri dahil değil.

## Kararlar
- Füzyon eşikleri **burada** kalibre edilir, sonra `AppConfig`'e yansıtılır.
- Değerlendirme tek "doğruluk" sayısına indirgenmez (dürüst raporlama).

## Kabul Kriteri
- Tekrar üretilebilir komutlarla (ör. `prepare`, `evaluate-stt`, `evaluate-fusion`)
  raporlar üretilir.
- Füzyonun naif ortalamaya karşı kazancı sayısal olarak gösterilir.
Tüm dokümanlar yazıldı. Spec öz-incelemesi yaptım: placeholder yok, kararlar master ile alt-projeler arasında tutarlı, her alt-projenin arayüz sözleşmesi ve kabul kriteri net. Bu projeyi gelecekte hatırlamak için kalıcı belleğe de bir not düşüyorum.

Write sesli-duygu-analizi-projesi.md
28 lines
---
name: sesli-duygu-analizi-projesi
description: Lokal .NET sesli duygu analizi agent'ı — mimari ve tasarım kararları
metadata:
  type: project
---

Ses kayıtları + canlı Teams toplantılarından duygu analizi yapan **lokal, cross-platform
.NET (Avalonia) masaüstü agent'ı**. İki model → füzyon. Gizlilik-öncelikli (API yok, veri lokal).

Onaylanmış tasarım: `docs/superpowers/specs/2026-07-24-sesli-duygu-analizi-master-design.md`
+ 9 alt-proje taşlağı (`subprojects/`). Referans (stajyer, Python/FastAPI):
`/Users/ilkaygokbudak/Downloads/ses-duygu-analizi`.

Kilit kararlar (detay master §2 kararlar kütüğü):
- Model barındırma: **Whisper.net (STT) + ONNX Runtime (duygu)**; Python sidecar escape-hatch.
- STT: **large-v3-turbo** (GGUF q5, Metal/CUDA).
- Duygu temsili: **valence-arousal boyutsal çekirdek** → 8-duygu circumplex haritalama.
- Füzyon: eksen-başına güvenilirlik (arousal←ses, valence←metin); **uyumsuzluk = birinci
  sınıf sinyal** (prozodi-baskın + congruence skoru).
- Zamansal: chunked near-real-time. Dil: Türkçe-first, çok-dilli-hazır.
- macOS capture: **ScreenCaptureKit** + native Swift yardımcı. Depolama: metrik kalıcı,
  ses Opus+şifreli 7 gün.
- V1 toplam-akış (diarization mimari-hazır ama sonraya).

Amaç: dokümanlar başka bir agent'a devredilecek (human-first). Sonraki adım: kullanıcı
spec'i inceleyecek, sonra [[writing-plans]] ile uygulama planı.
Bash Check MEMORY.md existence
IN
MEM=/Users/ilkaygokbudak/.claude/projects/-Users-ilkaygokbudak-Development-Projects-sentiment-analysis/memory/MEMORY.md; [ -f "$MEM" ] && echo "exists" || echo "missing"; ls "$MEM" 2>/dev/null

OUT
Exit code 1
missing
Write MEMORY.md
4 lines
# Bellek Dizini

- [Sesli Duygu Analizi Projesi](sesli-duygu-analizi-projesi.md) — lokal .NET duygu analizi agent'ı, mimari kararlar ve tasarım doküman yolları
Brainstorm tamamlandı ve tasarım yazıldı. Özet — kapattığımız her nokta:

Kararlar: Cross-platform Avalonia/.NET · chunked near-real-time · Whisper.net + ONNX (sidecar yedek) · large-v3-turbo · valence-arousal boyutsal çekirdek → 8-duygu circumplex · eksen-başına füzyon + uyumsuzluk/maskeleme tespiti (projenin asıl değeri) · Türkçe-first çok-dilli-hazır · macOS ScreenCaptureKit capture + Silero VAD · katmanlı şifreli depolama · eval ile ölçülü kararlar.

Yazılan dokümanlar:

Master tasarım — vizyon, kararlar kütüğü (rationale), VA çekirdeği, iki model, füzyon+uyumsuzluk, capture, pipeline, mimari, riskler, ilkeler
9 alt-proje spec taşlağı: 01-temel-domain · 02-fuzyon · 03-stt · 04-duygu-modelleri · 05-ses-yakalama-vad · 06-pipeline · 07-depolama-gizlilik · 08-avalonia-ui · 09-degerlendirme-eval
Lütfen dokümanları inceleyin ve değiştirmek istediğiniz bir şey var mı söyleyin. Aklımdaki birkaç açık nokta (sizin de eklemek istedikleriniz olabilir):

Somut model isimleri — Türkçe boyutsal (VA) SER modeli için sağlam bir aday bulmak henüz açık (risk tablosunda var). İsterseniz sonraki turda gerçek HuggingFace adaylarını araştırıp listeye koyabilirim.
Kod iskeletleri — teslim biçiminde "master + spec" seçtiniz; ama isterseniz #1 ve #2 için C# domain/interface iskeletlerini de ekleyebiliriz (kararınız değişirse).
git — bu dizin henüz git deposu değil. Teslim için git init + ilk commit yapmamı ister misiniz?
Onayınızdan sonra sıradaki adım writing-plans ile ilk dikey dilimin (alt-proje 1→2→3→4) uygulama planını çıkarmak olacak — ama önce siz spec'i gözden geçirin.

ME:
spec'ler güzel uygundur

AGENT:
Onay için teşekkürler. Sıradaki adım writing-plans ile uygulama planını çıkarmak. İlk dikey dilime (alt-proje 1→2→3→4) odaklanacağım. Skill'i başlatıyorum.

superpowers:writing-plans skill
writing-plans skill'ini kullanarak uygulama planını oluşturuyorum. Dekompozisyona uygun olarak, skill'in "her plan tek başına çalışan yazılım üretmeli" kuralı gereği alt-proje başına bir plan yazacağım ve ilk dilimden — Alt-proje 01: Temel + Domain — başlıyorum. Bu, saf C#, ML'siz, tamamen test edilebilir ve her şeyin bastığı zemin.

Bash Create plans directory
IN
mkdir -p /Users/ilkaygokbudak/Development/Projects/sentiment-analysis/docs/superpowers/plans && echo ok

OUT
ok
Write 2026-07-24-01-temel-domain.md
665 lines
# Alt-Proje 01: Temel + Domain — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sistemin saf-C# domain modelini, tüm arayüz sözleşmelerini ve DI/config iskeletini kurmak — hiç ML olmadan, tamamen test edilebilir.

**Architecture:** İki proje: `Core` (bağımlılıksız domain tipleri, saf fonksiyonlar) ve `Application` (Core'a bağlı arayüz sözleşmeleri + konfigürasyon + DI iskeleti). Bağımlılıklar hep içe doğru; `Core` hiçbir NuGet paketine bağlı değil. Test: xUnit ile `Core` domain mantığı %100 birim testli.

**Tech Stack:** .NET · C# · xUnit · (Core: sıfır NuGet)

## Global Constraints

- **Hedef framework:** `net9.0` (tüm projeler). Gerekirse ileride net10.0'a taşınabilir; şimdilik net9.0 sabit.
- **`Core` projesi sıfır NuGet bağımlılığı** taşır (yalnızca BCL).
- **Nullable reference types açık** (`<Nullable>enable</Nullable>`) tüm projelerde.
- **Duygu etiketleri dil-bağımsız enum**; UI metni bu katmanda yok.
- Domain tipleri **immutable** (record / readonly record struct).
- Valence aralığı **−1.0 … +1.0**, Arousal aralığı **0.0 … 1.0**, Nötr merkez **(V=0.0, A=0.5)**.
- Enum değerleri master tasarımdaki adlarla birebir: `Cosakulu, Mutlu, Sakin, Sikilmis, Uzgun, Gergin, Kizgin, Notr`.

---

### Task 1: Çözüm iskeleti + `ValenceArousal` değer tipi

**Files:**
- Create: `SentimentAnalysis.sln`
- Create: `src/SentimentAnalysis.Core/SentimentAnalysis.Core.csproj`
- Create: `src/SentimentAnalysis.Core/ValenceArousal.cs`
- Create: `tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj`
- Create: `tests/SentimentAnalysis.Core.Tests/ValenceArousalTests.cs`

**Interfaces:**
- Consumes: —
- Produces: `readonly record struct ValenceArousal(double Valence, double Arousal)` with `static ValenceArousal Clamp(double valence, double arousal)`, `static ValenceArousal Neutral { get; }`, `double DistanceTo(ValenceArousal other)`.

- [ ] **Step 1: Çözüm ve projeleri oluştur**

```bash
cd /Users/ilkaygokbudak/Development/Projects/sentiment-analysis
dotnet new sln -n SentimentAnalysis
dotnet new classlib -n SentimentAnalysis.Core -o src/SentimentAnalysis.Core -f net9.0
dotnet new xunit -n SentimentAnalysis.Core.Tests -o tests/SentimentAnalysis.Core.Tests -f net9.0
rm src/SentimentAnalysis.Core/Class1.cs tests/SentimentAnalysis.Core.Tests/UnitTest1.cs
dotnet sln add src/SentimentAnalysis.Core tests/SentimentAnalysis.Core.Tests
dotnet add tests/SentimentAnalysis.Core.Tests reference src/SentimentAnalysis.Core
```

Ardından `src/SentimentAnalysis.Core/SentimentAnalysis.Core.csproj` içeriğini şununla değiştir:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Başarısız testi yaz**

`tests/SentimentAnalysis.Core.Tests/ValenceArousalTests.cs`:

```csharp
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Core.Tests;

public class ValenceArousalTests
{
    [Fact]
    public void Clamp_KeepsValuesInRange()
    {
        var va = ValenceArousal.Clamp(valence: 2.5, arousal: -0.4);
        Assert.Equal(1.0, va.Valence);
        Assert.Equal(0.0, va.Arousal);
    }

    [Fact]
    public void Clamp_LeavesInRangeValuesUntouched()
    {
        var va = ValenceArousal.Clamp(valence: 0.3, arousal: 0.7);
        Assert.Equal(0.3, va.Valence, precision: 10);
        Assert.Equal(0.7, va.Arousal, precision: 10);
    }

    [Fact]
    public void Neutral_IsCenter()
    {
        Assert.Equal(0.0, ValenceArousal.Neutral.Valence);
        Assert.Equal(0.5, ValenceArousal.Neutral.Arousal);
    }

    [Fact]
    public void DistanceTo_IsEuclidean()
    {
        var a = new ValenceArousal(0.0, 0.0);
        var b = new ValenceArousal(3.0, 4.0);
        Assert.Equal(5.0, a.DistanceTo(b), precision: 10);
    }
}
```

- [ ] **Step 3: Testin başarısız olduğunu doğrula**

Run: `dotnet test tests/SentimentAnalysis.Core.Tests`
Expected: FAIL — `ValenceArousal` tipi bulunamıyor (derleme hatası).

- [ ] **Step 4: Minimal implementasyonu yaz**

`src/SentimentAnalysis.Core/ValenceArousal.cs`:

```csharp
namespace SentimentAnalysis.Core;

/// <summary>
/// Duygunun boyutsal çekirdek temsili. Valence olumsuz↔olumlu (−1..+1),
/// Arousal sakin↔uyarılmış (0..1). Sistemin iç para birimidir.
/// </summary>
public readonly record struct ValenceArousal(double Valence, double Arousal)
{
    /// <summary>Nötr merkez: valence 0, arousal 0.5.</summary>
    public static ValenceArousal Neutral => new(0.0, 0.5);

    /// <summary>Değerleri geçerli aralığa kırparak güvenli bir örnek üretir.</summary>
    public static ValenceArousal Clamp(double valence, double arousal) =>
        new(Math.Clamp(valence, -1.0, 1.0), Math.Clamp(arousal, 0.0, 1.0));

    /// <summary>İki VA noktası arasındaki Öklid uzaklığı (duygu haritalamasında kullanılır).</summary>
    public double DistanceTo(ValenceArousal other)
    {
        var dv = Valence - other.Valence;
        var da = Arousal - other.Arousal;
        return Math.Sqrt(dv * dv + da * da);
    }
}
```

- [ ] **Step 5: Testin geçtiğini doğrula**

Run: `dotnet test tests/SentimentAnalysis.Core.Tests`
Expected: PASS (4 test).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(core): çözüm iskeleti + ValenceArousal değer tipi"
```

---

### Task 2: `Emotion` enum + `EmotionWheel` haritalama

**Files:**
- Create: `src/SentimentAnalysis.Core/Emotion.cs`
- Create: `src/SentimentAnalysis.Core/EmotionWheel.cs`
- Create: `tests/SentimentAnalysis.Core.Tests/EmotionWheelTests.cs`

**Interfaces:**
- Consumes: `ValenceArousal`
- Produces: `enum Emotion { Cosakulu, Mutlu, Sakin, Sikilmis, Uzgun, Gergin, Kizgin, Notr }`; `static class EmotionWheel` with `Emotion Map(ValenceArousal va)` and `(Emotion Primary, Emotion Secondary) MapWithSecondary(ValenceArousal va)`.

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Core.Tests/EmotionWheelTests.cs`:

```csharp
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Core.Tests;

public class EmotionWheelTests
{
    [Theory]
    [InlineData(0.6, 0.9, Emotion.Cosakulu)]   // yüksek valence + yüksek arousal
    [InlineData(0.8, 0.5, Emotion.Mutlu)]      // yüksek valence + orta arousal
    [InlineData(0.6, 0.15, Emotion.Sakin)]     // olumlu + düşük enerji
    [InlineData(-0.4, 0.1, Emotion.Sikilmis)]  // olumsuz + çok düşük enerji
    [InlineData(-0.6, 0.3, Emotion.Uzgun)]     // olumsuz + düşük-orta enerji
    [InlineData(-0.5, 0.8, Emotion.Gergin)]    // olumsuz + yüksek enerji
    [InlineData(-0.8, 0.9, Emotion.Kizgin)]    // çok olumsuz + yüksek enerji
    [InlineData(0.0, 0.5, Emotion.Notr)]       // merkez
    public void Map_ReturnsNearestRegion(double v, double a, Emotion expected)
    {
        Assert.Equal(expected, EmotionWheel.Map(new ValenceArousal(v, a)));
    }

    [Fact]
    public void MapWithSecondary_PrimaryAndSecondaryDiffer()
    {
        var (primary, secondary) = EmotionWheel.MapWithSecondary(new ValenceArousal(0.7, 0.7));
        Assert.NotEqual(primary, secondary);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test tests/SentimentAnalysis.Core.Tests`
Expected: FAIL — `Emotion` / `EmotionWheel` bulunamıyor.

- [ ] **Step 3: `Emotion` enum'ını yaz**

`src/SentimentAnalysis.Core/Emotion.cs`:

```csharp
namespace SentimentAnalysis.Core;

/// <summary>
/// Valence-Arousal uzayından haritalanan dil-bağımsız 8 duygu (circumplex).
/// UI metni bu katmanda YOK; enum → yerelleştirme UI katmanında yapılır.
/// </summary>
public enum Emotion
{
    Cosakulu,  // V+ A+
    Mutlu,     // V+ orta-A
    Sakin,     // V+ A-
    Sikilmis,  // V- A-
    Uzgun,     // V- düşük-A
    Gergin,    // V- yüksek-A
    Kizgin,    // V-- A+
    Notr       // merkez
}
```

- [ ] **Step 4: `EmotionWheel` haritalamayı yaz**

`src/SentimentAnalysis.Core/EmotionWheel.cs`:

```csharp
namespace SentimentAnalysis.Core;

/// <summary>
/// Sürekli Valence-Arousal noktasını en yakın ayrık duyguya haritalar.
/// Her duygunun VA uzayında bir merkez noktası vardır; haritalama en yakın
/// merkeze (Öklid) göre yapılır. Saf, deterministik.
/// </summary>
public static class EmotionWheel
{
    private static readonly (Emotion Emotion, ValenceArousal Center)[] Regions =
    [
        (Emotion.Cosakulu, new ValenceArousal(0.6, 0.9)),
        (Emotion.Mutlu,    new ValenceArousal(0.8, 0.5)),
        (Emotion.Sakin,    new ValenceArousal(0.6, 0.15)),
        (Emotion.Sikilmis, new ValenceArousal(-0.4, 0.1)),
        (Emotion.Uzgun,    new ValenceArousal(-0.6, 0.3)),
        (Emotion.Gergin,   new ValenceArousal(-0.5, 0.8)),
        (Emotion.Kizgin,   new ValenceArousal(-0.8, 0.9)),
        (Emotion.Notr,     new ValenceArousal(0.0, 0.5)),
    ];

    /// <summary>Verilen VA noktasına en yakın duyguyu döndürür.</summary>
    public static Emotion Map(ValenceArousal va) => MapWithSecondary(va).Primary;

    /// <summary>En yakın (primary) ve ikinci en yakın (secondary) duyguyu döndürür.</summary>
    public static (Emotion Primary, Emotion Secondary) MapWithSecondary(ValenceArousal va)
    {
        var ranked = Regions
            .OrderBy(r => va.DistanceTo(r.Center))
            .Select(r => r.Emotion)
            .ToArray();
        return (ranked[0], ranked[1]);
    }
}
```

- [ ] **Step 5: Testin geçtiğini doğrula**

Run: `dotnet test tests/SentimentAnalysis.Core.Tests`
Expected: PASS (tüm testler).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(core): 8-duygu circumplex enum + EmotionWheel haritalama"
```

---

### Task 3: Domain DTO'ları (kayıt tipleri)

**Files:**
- Create: `src/SentimentAnalysis.Core/Signals.cs`
- Create: `src/SentimentAnalysis.Core/Results.cs`
- Create: `tests/SentimentAnalysis.Core.Tests/DomainRecordsTests.cs`

**Interfaces:**
- Consumes: `ValenceArousal`, `Emotion`
- Produces: `enum Confidence`, `enum IncongruencePattern`, `enum DetectedLanguage`; records `AudioChunk`, `Transcript`, `TextEmotion`, `ProsodyEmotion`, `QualitySignals`, `FusedResult`, `ChunkResult`, `MeetingSession`.

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Core.Tests/DomainRecordsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Core.Tests;

public class DomainRecordsTests
{
    [Fact]
    public void FusedResult_CarriesBothSignalsAndCongruence()
    {
        var result = new FusedResult(
            Combined: new ValenceArousal(-0.5, 0.7),
            PrimaryEmotion: Emotion.Gergin,
            SecondaryEmotion: Emotion.Kizgin,
            Confidence: Confidence.Orta,
            Agreement: false,
            Incongruent: true,
            Pattern: IncongruencePattern.Maskeleme,
            Congruence: 0.2,
            TextValence: 0.6,
            AudioValence: -0.6);

        Assert.True(result.Incongruent);
        Assert.Equal(IncongruencePattern.Maskeleme, result.Pattern);
        Assert.Equal(Emotion.Gergin, result.PrimaryEmotion);
    }

    [Fact]
    public void MeetingSession_AggregatesChunks()
    {
        var chunk = new ChunkResult(
            TimeStart: TimeSpan.Zero,
            TimeEnd: TimeSpan.FromSeconds(15),
            Fused: new FusedResult(
                new ValenceArousal(0.8, 0.5), Emotion.Mutlu, Emotion.Sakin,
                Confidence.Yuksek, true, false, IncongruencePattern.Yok, 0.9, 0.8, 0.7),
            Transcript: "merhaba");

        var session = new MeetingSession(
            Id: Guid.NewGuid(),
            StartedAt: DateTimeOffset.UtcNow,
            Language: DetectedLanguage.Turkish,
            Chunks: new List<ChunkResult> { chunk });

        Assert.Single(session.Chunks);
        Assert.Equal(DetectedLanguage.Turkish, session.Language);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `dotnet test tests/SentimentAnalysis.Core.Tests`
Expected: FAIL — tipler bulunamıyor.

- [ ] **Step 3: Enum ve sinyal tiplerini yaz**

`src/SentimentAnalysis.Core/Signals.cs`:

```csharp
namespace SentimentAnalysis.Core;

/// <summary>Nihai sonucun güven düzeyi.</summary>
public enum Confidence { Yuksek, Orta, Dusuk }

/// <summary>Söz-ton uyumsuzluğunun örüntüsü.</summary>
public enum IncongruencePattern { Yok, Maskeleme, Saka, OlasiIroni }

/// <summary>Algılanan konuşma dili (çok-dilli-hazır).</summary>
public enum DetectedLanguage { Turkish, English, Other }

/// <summary>Ham ses parçası: 16 kHz mono float PCM.</summary>
public sealed record AudioChunk(float[] Samples, int SampleRate, TimeSpan Start, TimeSpan End);

/// <summary>STT çıktısı.</summary>
public sealed record Transcript(string Text, DetectedLanguage Language, double AvgLogProb);

/// <summary>Metin modelinin ham çıktısı.</summary>
public sealed record TextEmotion(ValenceArousal Va, double ModelConfidence);

/// <summary>Prozodi modelinin ham çıktısı.</summary>
public sealed record ProsodyEmotion(ValenceArousal Va, double ModelConfidence);

/// <summary>Füzyon ağırlığını besleyen girdi kalite sinyalleri.</summary>
public sealed record QualitySignals(
    double AudioSnr,
    double SpeechRatio,
    TimeSpan Duration,
    int TranscriptWordCount,
    double TranscriptAvgLogProb);
```

- [ ] **Step 4: Sonuç tiplerini yaz**

`src/SentimentAnalysis.Core/Results.cs`:

```csharp
using System.Collections.Generic;

namespace SentimentAnalysis.Core;

/// <summary>Füzyon sonucu (chunk düzeyi). İki ham valence de saydamlık için taşınır.</summary>
public sealed record FusedResult(
    ValenceArousal Combined,
    Emotion PrimaryEmotion,
    Emotion SecondaryEmotion,
    Confidence Confidence,
    bool Agreement,
    bool Incongruent,
    IncongruencePattern Pattern,
    double Congruence,
    double TextValence,
    double AudioValence);

/// <summary>Bir chunk'ın tam sonucu (zaman çizelgesi öğesi).</summary>
public sealed record ChunkResult(
    TimeSpan TimeStart,
    TimeSpan TimeEnd,
    FusedResult Fused,
    string Transcript);

/// <summary>Bir toplantı/oturum: chunk listesi + üst-veriler. Toplantı-düzeyi
/// agregasyon (baskın duygu, trend, oynaklık, tepe anlar) Füzyon motorunda
/// (Alt-proje 02) hesaplanır; burada ham veri tutulur.</summary>
public sealed record MeetingSession(
    Guid Id,
    DateTimeOffset StartedAt,
    DetectedLanguage Language,
    IReadOnlyList<ChunkResult> Chunks);
```

- [ ] **Step 5: Testin geçtiğini doğrula**

Run: `dotnet test tests/SentimentAnalysis.Core.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(core): domain DTO'ları ve sinyal/sonuç tipleri"
```

---

### Task 4: `Application` arayüzleri + `AppConfig` + DI iskeleti

**Files:**
- Create: `src/SentimentAnalysis.Application/SentimentAnalysis.Application.csproj`
- Create: `src/SentimentAnalysis.Application/Abstractions/Interfaces.cs`
- Create: `src/SentimentAnalysis.Application/Configuration/AppConfig.cs`
- Create: `src/SentimentAnalysis.Application/DependencyInjection.cs`
- Create: `tests/SentimentAnalysis.Application.Tests/SentimentAnalysis.Application.Tests.csproj`
- Create: `tests/SentimentAnalysis.Application.Tests/AppConfigTests.cs`

**Interfaces:**
- Consumes: tüm `Core` tipleri
- Produces: `ISpeechToText`, `ITextEmotionModel`, `IProsodyEmotionModel`, `IEmotionFusion`, `IAudioCaptureSource`, `IVadGate`, `ISessionStore`, `AppConfig`, `FusionConfig`, `StorageConfig`.

- [ ] **Step 1: Application projesini oluştur**

```bash
cd /Users/ilkaygokbudak/Development/Projects/sentiment-analysis
dotnet new classlib -n SentimentAnalysis.Application -o src/SentimentAnalysis.Application -f net9.0
rm src/SentimentAnalysis.Application/Class1.cs
dotnet sln add src/SentimentAnalysis.Application
dotnet add src/SentimentAnalysis.Application reference src/SentimentAnalysis.Core
dotnet add src/SentimentAnalysis.Application package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet new xunit -n SentimentAnalysis.Application.Tests -o tests/SentimentAnalysis.Application.Tests -f net9.0
rm tests/SentimentAnalysis.Application.Tests/UnitTest1.cs
dotnet sln add tests/SentimentAnalysis.Application.Tests
dotnet add tests/SentimentAnalysis.Application.Tests reference src/SentimentAnalysis.Application
```

`src/SentimentAnalysis.Application/SentimentAnalysis.Application.csproj` PropertyGroup'una ekle:

```xml
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
```

- [ ] **Step 2: Başarısız testi yaz**

`tests/SentimentAnalysis.Application.Tests/AppConfigTests.cs`:

```csharp
using SentimentAnalysis.Application.Configuration;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class AppConfigTests
{
    [Fact]
    public void Default_HasSaneFusionAndStorageDefaults()
    {
        var config = AppConfig.Default;

        // Arousal ağırlıkla sesten, valence ağırlıkla metinden (master §5).
        Assert.True(config.Fusion.AudioArousalWeight > config.Fusion.TextArousalWeight);
        Assert.True(config.Fusion.TextValenceWeight > config.Fusion.AudioValenceWeight);

        // Ses saklama varsayılanı 7 gün (master §D13).
        Assert.Equal(7, config.Storage.AudioRetentionDays);
    }
}
```

- [ ] **Step 3: Testin başarısız olduğunu doğrula**

Run: `dotnet test tests/SentimentAnalysis.Application.Tests`
Expected: FAIL — `AppConfig` bulunamıyor.

- [ ] **Step 4: `AppConfig`'i yaz**

`src/SentimentAnalysis.Application/Configuration/AppConfig.cs`:

```csharp
namespace SentimentAnalysis.Application.Configuration;

/// <summary>Füzyon başlangıç ağırlıkları ve eşikleri. Nihai değerler
/// Alt-proje 09 (eval) ile kalibre edilip buraya yansıtılır — keyfi sabit değil.</summary>
public sealed record FusionConfig(
    double AudioArousalWeight = 0.75,
    double TextArousalWeight  = 0.25,
    double TextValenceWeight  = 0.70,
    double AudioValenceWeight = 0.30,
    double IncongruenceThreshold = 0.8,   // |valence_text − valence_audio| eşiği
    double LowConfidenceThreshold = 0.35);

/// <summary>Depolama/gizlilik ayarları (master §D13).</summary>
public sealed record StorageConfig(
    int AudioRetentionDays = 7,
    bool KeepAudio = true,
    string OpusBitrate = "24k");

/// <summary>STT profil ayarları (master §D6).</summary>
public sealed record SttConfig(
    string LiveModel = "large-v3-turbo",
    string? DeepModel = "large-v3");

/// <summary>Uygulama genel konfigürasyonu (stajyerin config.py karşılığı).</summary>
public sealed record AppConfig(
    FusionConfig Fusion,
    StorageConfig Storage,
    SttConfig Stt)
{
    public static AppConfig Default => new(
        Fusion: new FusionConfig(),
        Storage: new StorageConfig(),
        Stt: new SttConfig());
}
```

- [ ] **Step 5: Testin geçtiğini doğrula**

Run: `dotnet test tests/SentimentAnalysis.Application.Tests`
Expected: PASS.

- [ ] **Step 6: Arayüz sözleşmelerini yaz**

`src/SentimentAnalysis.Application/Abstractions/Interfaces.cs`:

```csharp
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Abstractions;

/// <summary>Konuşma → metin (Whisper.net; Alt-proje 03).</summary>
public interface ISpeechToText
{
    Task<Transcript> TranscribeAsync(AudioChunk chunk, CancellationToken ct);
}

/// <summary>Metnin duygusu → valence-arousal (ONNX; Alt-proje 04).
/// Dil-başına takılabilir (çok-dilli-hazır).</summary>
public interface ITextEmotionModel
{
    Task<TextEmotion> AnalyzeAsync(Transcript transcript, CancellationToken ct);
}

/// <summary>Sesin prozodisi → valence-arousal (VAD-regresyon ONNX; Alt-proje 04).</summary>
public interface IProsodyEmotionModel
{
    Task<ProsodyEmotion> AnalyzeAsync(AudioChunk chunk, CancellationToken ct);
}

/// <summary>İki modeli birleştiren saf, deterministik füzyon (Alt-proje 02).</summary>
public interface IEmotionFusion
{
    FusedResult Fuse(TextEmotion text, ProsodyEmotion prosody, QualitySignals quality);
}

/// <summary>Canlı sistem-sesi / dosya yakalama (Alt-proje 05).</summary>
public interface IAudioCaptureSource
{
    IAsyncEnumerable<AudioChunk> CaptureAsync(CaptureOptions options, CancellationToken ct);
}

/// <summary>Yakalama seçenekleri.</summary>
public sealed record CaptureOptions(TimeSpan ChunkLength, TimeSpan Overlap);

/// <summary>Sessiz chunk'ları eleyen VAD kapısı (Silero; Alt-proje 05).</summary>
public interface IVadGate
{
    bool HasSpeech(AudioChunk chunk);
}

/// <summary>Oturum kalıcılığı ve saklama politikası (Alt-proje 07).</summary>
public interface ISessionStore
{
    Task SaveChunkAsync(Guid sessionId, ChunkResult result, CancellationToken ct);
    Task<MeetingSession?> GetSessionAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken ct);
    Task PurgeExpiredAudioAsync(CancellationToken ct);
}
```

- [ ] **Step 7: DI iskeletini yaz**

`src/SentimentAnalysis.Application/DependencyInjection.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Application;

/// <summary>Application katmanı DI kayıt noktası. Infrastructure implementasyonları
/// (Alt-projeler 02–07) kendi uzantı metotlarıyla eklenir.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationCore(
        this IServiceCollection services, AppConfig? config = null)
    {
        services.AddSingleton(config ?? AppConfig.Default);
        // Somut servisler (ISpeechToText, IEmotionFusion, ...) Infrastructure
        // katmanında ayrı AddXxx() uzantılarıyla kaydedilecek.
        return services;
    }
}
```

- [ ] **Step 8: Tüm çözümün derlendiğini ve testlerin geçtiğini doğrula**

Run: `dotnet build && dotnet test`
Expected: Build succeeded; tüm testler PASS.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(application): arayüz sözleşmeleri, AppConfig ve DI iskeleti"
```

---

## Self-Review (plan yazarı tarafından)

- **Spec coverage:** Alt-proje 01 spec'inin her maddesi bir task'a bağlı — ValenceArousal/Emotion/EmotionWheel (Task 1–2), DTO'lar ve `DetectedLanguage` (Task 3), tüm arayüzler + `AppConfig` + DI (Task 4). Boşluk yok.
- **Placeholder scan:** TODO/TBD yok; her kod adımı tam içerik taşıyor.
- **Type consistency:** `ValenceArousal`, `Emotion`, `FusedResult`, `ChunkResult`, `MeetingSession`, `Transcript`, `AudioChunk` adları ve alanları task'lar arası tutarlı; testlerdeki kullanımla implementasyon eşleşiyor. `AppConfig.Fusion` ağırlık isimleri test ve implementasyonda birebir.
- Not: Toplantı-düzeyi agregasyon (baskın duygu/trend/oynaklık) bilinçli olarak **Alt-proje 02 (Füzyon)** planına bırakıldı; burada yalnızca veri tipi (`MeetingSession.Chunks`) var — master §5.2 ile tutarlı.
İlk plan hazır: 2026-07-24-01-temel-domain.md — 4 task, bite-sized TDD adımları, tam C# kodu ve xUnit testleriyle. Bir sonraki agent bunu adım adım uygulayabilir. Öz-incelemeyi de yaptım (spec kapsamı, placeholder, tip tutarlılığı — hepsi temiz).

Şimdi sizin teslim hedefinizi hatırlayarak yol ayrımı. Skill normalde "planı hemen çalıştıralım mı?" diye sorar, ama sizin şu anki amacınız kod değil, başka bir agent'a verilecek dokümanlar. Üç makul yol var:

1. Kalan erken planları da yazayım (Alt-proje 02, 03, 04) (teslim için önerilen) — İlk dikey dilimin (Temel→Füzyon→STT→Duygu modelleri) tamamı bite-sized planlara dökülür; agent'a en eksiksiz paket. Not: 03/04 gerçek model isimleri gerektirir; önce onları araştırıp risk tablosunu somutlaştırmam iyi olur.

2. Bu planı şimdi çalıştıralım — Alt-proje 01'i bu oturumda (subagent-driven ya da inline) uygulayıp çalışan Core + Application iskeletini üretelim.

3. Burada duralım — Master tasarım + 9 spec + ilk plan teslim için yeterli; gerisini alacak agent kendi planlarını yazsın.

Ayrıca ufak bir bekleyen: bu dizin henüz git deposu değil. Teslim için git init + tüm dokümanları ilk commit'e almamı ister misiniz?

Hangi yolla devam edelim?

ME:
tamam git deposuna gerek yok. İlk planı uygulamaya geçerek temel iskelete başlaylım.

AGENT:
Plan 1'i inline uyguluyoruz. git istemediğiniz için commit adımlarını atlayıp yerine derleme+test doğrulaması yapacağım. Süreci doğru yürütmek için executing-plans skill'ini açıyorum.

superpowers:executing-plans skill
I'm using the executing-plans skill to implement this plan.

git kullanmayacağımız için worktree/branch adımları ve commit'ler geçerli değil — bunları atlıyorum, doğrulama olarak dotnet build/dotnet test kullanacağım. Önce ortamı ve planı gözden geçireyim.

Bash Check dotnet SDK availability and version
IN
which dotnet && dotnet --version && dotnet --list-sdks

OUT
/usr/local/share/dotnet/dotnet
10.0.101
9.0.102 [/usr/local/share/dotnet/sdk]
9.0.302 [/usr/local/share/dotnet/sdk]
10.0.101 [/usr/local/share/dotnet/sdk]
dotnet 10 ve 9 SDK'ları mevcut; plan net9.0 hedefliyor, sorun yok. Plan sağlam ve nettir; endişem yok. İlerlemeyi takip için todo listesi kuruyorum, sonra Task 1'den başlıyorum.

Update Todos

Task 1: Çözüm iskeleti + ValenceArousal değer tipi

Task 2: Emotion enum + EmotionWheel haritalama

Task 3: Domain DTO'ları (kayıt tipleri)

Task 4: Application arayüzleri + AppConfig + DI iskeleti
Task 1 — çözüm ve projeleri oluşturuyorum (Step 1).

Bash Create solution, Core and test projects
IN
cd /Users/ilkaygokbudak/Development/Projects/sentiment-analysis
dotnet new sln -n SentimentAnalysis
dotnet new classlib -n SentimentAnalysis.Core -o src/SentimentAnalysis.Core -f net9.0
dotnet new xunit -n SentimentAnalysis.Core.Tests -o tests/SentimentAnalysis.Core.Tests -f net9.0
rm src/SentimentAnalysis.Core/Class1.cs tests/SentimentAnalysis.Core.Tests/UnitTest1.cs
dotnet sln add src/SentimentAnalysis.Core tests/SentimentAnalysis.Core.Tests
dotnet add tests/SentimentAnalysis.Core.Tests reference src/SentimentAnalysis.Core
echo "=== DONE ==="

OUT
The template "Solution File" was created successfully.

The template "Class Library" was created successfully.

Processing post-creation actions...
Restoring /Users/ilkaygokbudak/Development/Projects/sentiment-analysis/src/SentimentAnalysis.Core/SentimentAnalysis.Core.csproj:
  Determining projects to restore...
  Restored /Users/ilkaygokbudak/Development/Projects/sentiment-analysis/src/SentimentAnalysis.Core/SentimentAnalysis.Core.csproj (in 840 ms).
Restore succeeded.


The template "xUnit Test Project" was created successfully.

Processing post-creation actions...
Restoring /Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj:
  Determining projects to restore...
  Restored /Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj (in 170 ms).
Restore succeeded.


Project `src/SentimentAnalysis.Core/SentimentAnalysis.Core.csproj` added to the solution.
Project `tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj` added to the solution.
Reference `..\..\src\SentimentAnalysis.Core\SentimentAnalysis.Core.csproj` added to the project.
=== DONE ===
Projeler oluştu. Şimdi Core csproj'u planda belirtilen içerikle güncelliyorum.

Read SentimentAnalysis.Core.csproj
csproj zaten istenen içerikte. Step 2 — başarısız testi yazıyorum.

Write ValenceArousalTests.cs
39 lines
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Core.Tests;

public class ValenceArousalTests
{
    [Fact]
    public void Clamp_KeepsValuesInRange()
    {
        var va = ValenceArousal.Clamp(valence: 2.5, arousal: -0.4);
        Assert.Equal(1.0, va.Valence);
        Assert.Equal(0.0, va.Arousal);
    }

    [Fact]
    public void Clamp_LeavesInRangeValuesUntouched()
    {
        var va = ValenceArousal.Clamp(valence: 0.3, arousal: 0.7);
        Assert.Equal(0.3, va.Valence, precision: 10);
        Assert.Equal(0.7, va.Arousal, precision: 10);
    }

    [Fact]
    public void Neutral_IsCenter()
    {
        Assert.Equal(0.0, ValenceArousal.Neutral.Valence);
        Assert.Equal(0.5, ValenceArousal.Neutral.Arousal);
    }

    [Fact]
    public void DistanceTo_IsEuclidean()
    {
        var a = new ValenceArousal(0.0, 0.0);
        var b = new ValenceArousal(3.0, 4.0);
        Assert.Equal(5.0, a.DistanceTo(b), precision: 10);
    }
}
Bash Run tests, expect compile failure
IN
dotnet test tests/SentimentAnalysis.Core.Tests 2>&1 | tail -15

OUT
  Determining projects to restore...
  Restored /Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj (in 161 ms).
  1 of 2 projects are up-to-date for restore.
  SentimentAnalysis.Core -> /Users/ilkaygokbudak/Development/Projects/sentiment-analysis/src/SentimentAnalysis.Core/bin/Debug/net9.0/SentimentAnalysis.Core.dll
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/ValenceArousalTests.cs(11,18): error CS0103: The name 'ValenceArousal' does not exist in the current context [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/ValenceArousalTests.cs(19,18): error CS0103: The name 'ValenceArousal' does not exist in the current context [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/ValenceArousalTests.cs(27,27): error CS0103: The name 'ValenceArousal' does not exist in the current context [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/ValenceArousalTests.cs(28,27): error CS0103: The name 'ValenceArousal' does not exist in the current context [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/ValenceArousalTests.cs(34,21): error CS0246: The type or namespace name 'ValenceArousal' could not be found (are you missing a using directive or an assembly reference?) [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/ValenceArousalTests.cs(35,21): error CS0246: The type or namespace name 'ValenceArousal' could not be found (are you missing a using directive or an assembly reference?) [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
Beklendiği gibi FAIL (tip yok). Step 4 — implementasyonu yazıyorum.

Write ValenceArousal.cs
24 lines
namespace SentimentAnalysis.Core;

/// <summary>
/// Duygunun boyutsal çekirdek temsili. Valence olumsuz↔olumlu (−1..+1),
/// Arousal sakin↔uyarılmış (0..1). Sistemin iç para birimidir.
/// </summary>
public readonly record struct ValenceArousal(double Valence, double Arousal)
{
    /// <summary>Nötr merkez: valence 0, arousal 0.5.</summary>
    public static ValenceArousal Neutral => new(0.0, 0.5);

    /// <summary>Değerleri geçerli aralığa kırparak güvenli bir örnek üretir.</summary>
    public static ValenceArousal Clamp(double valence, double arousal) =>
        new(Math.Clamp(valence, -1.0, 1.0), Math.Clamp(arousal, 0.0, 1.0));

    /// <summary>İki VA noktası arasındaki Öklid uzaklığı (duygu haritalamasında kullanılır).</summary>
    public double DistanceTo(ValenceArousal other)
    {
        var dv = Valence - other.Valence;
        var da = Arousal - other.Arousal;
        return Math.Sqrt(dv * dv + da * da);
    }
}
Bash Run tests, expect pass
IN
dotnet test tests/SentimentAnalysis.Core.Tests 2>&1 | tail -8

OUT
  SentimentAnalysis.Core.Tests -> /Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/bin/Debug/net9.0/SentimentAnalysis.Core.Tests.dll
Test run for /Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/bin/Debug/net9.0/SentimentAnalysis.Core.Tests.dll (.NETCoreApp,Version=v9.0)
VSTest version 18.0.1 (arm64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 5 ms - SentimentAnalysis.Core.Tests.dll (net9.0)
4/4 test PASS. Task 1 tamam (git commit atlandı). Task 2'ye geçiyorum.

Update Todos

Task 1: Çözüm iskeleti + ValenceArousal değer tipi

Task 2: Emotion enum + EmotionWheel haritalama

Task 3: Domain DTO'ları (kayıt tipleri)

Task 4: Application arayüzleri + AppConfig + DI iskeleti
Write EmotionWheelTests.cs
29 lines
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Core.Tests;

public class EmotionWheelTests
{
    [Theory]
    [InlineData(0.6, 0.9, Emotion.Cosakulu)]   // yüksek valence + yüksek arousal
    [InlineData(0.8, 0.5, Emotion.Mutlu)]      // yüksek valence + orta arousal
    [InlineData(0.6, 0.15, Emotion.Sakin)]     // olumlu + düşük enerji
    [InlineData(-0.4, 0.1, Emotion.Sikilmis)]  // olumsuz + çok düşük enerji
    [InlineData(-0.6, 0.3, Emotion.Uzgun)]     // olumsuz + düşük-orta enerji
    [InlineData(-0.5, 0.8, Emotion.Gergin)]    // olumsuz + yüksek enerji
    [InlineData(-0.8, 0.9, Emotion.Kizgin)]    // çok olumsuz + yüksek enerji
    [InlineData(0.0, 0.5, Emotion.Notr)]       // merkez
    public void Map_ReturnsNearestRegion(double v, double a, Emotion expected)
    {
        Assert.Equal(expected, EmotionWheel.Map(new ValenceArousal(v, a)));
    }

    [Fact]
    public void MapWithSecondary_PrimaryAndSecondaryDiffer()
    {
        var (primary, secondary) = EmotionWheel.MapWithSecondary(new ValenceArousal(0.7, 0.7));
        Assert.NotEqual(primary, secondary);
    }
}
Bash Run tests, expect fail (Emotion/EmotionWheel missing)
IN
dotnet test tests/SentimentAnalysis.Core.Tests 2>&1 | tail -6

OUT
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/EmotionWheelTests.cs(11,28): error CS0103: The name 'Emotion' does not exist in the current context [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/EmotionWheelTests.cs(12,28): error CS0103: The name 'Emotion' does not exist in the current context [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/EmotionWheelTests.cs(13,28): error CS0103: The name 'Emotion' does not exist in the current context [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/EmotionWheelTests.cs(14,28): error CS0103: The name 'Emotion' does not exist in the current context [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/EmotionWheelTests.cs(15,28): error CS0103: The name 'Emotion' does not exist in the current context [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/EmotionWheelTests.cs(16,27): error CS0103: The name 'Emotion' does not exist in the current context [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
FAIL — beklendiği gibi. Emotion enum ve EmotionWheel'i yazıyorum.

Write Emotion.cs
18 lines
namespace SentimentAnalysis.Core;

/// <summary>
/// Valence-Arousal uzayından haritalanan dil-bağımsız 8 duygu (circumplex).
/// UI metni bu katmanda YOK; enum → yerelleştirme UI katmanında yapılır.
/// </summary>
public enum Emotion
{
    Cosakulu,  // V+ A+
    Mutlu,     // V+ orta-A
    Sakin,     // V+ A-
    Sikilmis,  // V- A-
    Uzgun,     // V- düşük-A
    Gergin,    // V- yüksek-A
    Kizgin,    // V-- A+
    Notr       // merkez
}
Write EmotionWheel.cs
35 lines
namespace SentimentAnalysis.Core;

/// <summary>
/// Sürekli Valence-Arousal noktasını en yakın ayrık duyguya haritalar.
/// Her duygunun VA uzayında bir merkez noktası vardır; haritalama en yakın
/// merkeze (Öklid) göre yapılır. Saf, deterministik.
/// </summary>
public static class EmotionWheel
{
    private static readonly (Emotion Emotion, ValenceArousal Center)[] Regions =
    [
        (Emotion.Cosakulu, new ValenceArousal(0.6, 0.9)),
        (Emotion.Mutlu,    new ValenceArousal(0.8, 0.5)),
        (Emotion.Sakin,    new ValenceArousal(0.6, 0.15)),
        (Emotion.Sikilmis, new ValenceArousal(-0.4, 0.1)),
        (Emotion.Uzgun,    new ValenceArousal(-0.6, 0.3)),
        (Emotion.Gergin,   new ValenceArousal(-0.5, 0.8)),
        (Emotion.Kizgin,   new ValenceArousal(-0.8, 0.9)),
        (Emotion.Notr,     new ValenceArousal(0.0, 0.5)),
    ];

    /// <summary>Verilen VA noktasına en yakın duyguyu döndürür.</summary>
    public static Emotion Map(ValenceArousal va) => MapWithSecondary(va).Primary;

    /// <summary>En yakın (primary) ve ikinci en yakın (secondary) duyguyu döndürür.</summary>
    public static (Emotion Primary, Emotion Secondary) MapWithSecondary(ValenceArousal va)
    {
        var ranked = Regions
            .OrderBy(r => va.DistanceTo(r.Center))
            .Select(r => r.Emotion)
            .ToArray();
        return (ranked[0], ranked[1]);
    }
}
Bash Run tests, expect all pass
IN
dotnet test tests/SentimentAnalysis.Core.Tests 2>&1 | tail -4

OUT
Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 18 ms - SentimentAnalysis.Core.Tests.dll (net9.0)
13/13 PASS. Task 2 tamam. Task 3 — domain DTO'ları.

Update Todos

Task 1: Çözüm iskeleti + ValenceArousal değer tipi

Task 2: Emotion enum + EmotionWheel haritalama

Task 3: Domain DTO'ları (kayıt tipleri)

Task 4: Application arayüzleri + AppConfig + DI iskeleti
Write DomainRecordsTests.cs
51 lines
using System;
using System.Collections.Generic;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Core.Tests;

public class DomainRecordsTests
{
    [Fact]
    public void FusedResult_CarriesBothSignalsAndCongruence()
    {
        var result = new FusedResult(
            Combined: new ValenceArousal(-0.5, 0.7),
            PrimaryEmotion: Emotion.Gergin,
            SecondaryEmotion: Emotion.Kizgin,
            Confidence: Confidence.Orta,
            Agreement: false,
            Incongruent: true,
            Pattern: IncongruencePattern.Maskeleme,
            Congruence: 0.2,
            TextValence: 0.6,
            AudioValence: -0.6);

        Assert.True(result.Incongruent);
        Assert.Equal(IncongruencePattern.Maskeleme, result.Pattern);
        Assert.Equal(Emotion.Gergin, result.PrimaryEmotion);
    }

    [Fact]
    public void MeetingSession_AggregatesChunks()
    {
        var chunk = new ChunkResult(
            TimeStart: TimeSpan.Zero,
            TimeEnd: TimeSpan.FromSeconds(15),
            Fused: new FusedResult(
                new ValenceArousal(0.8, 0.5), Emotion.Mutlu, Emotion.Sakin,
                Confidence.Yuksek, true, false, IncongruencePattern.Yok, 0.9, 0.8, 0.7),
            Transcript: "merhaba");

        var session = new MeetingSession(
            Id: Guid.NewGuid(),
            StartedAt: DateTimeOffset.UtcNow,
            Language: DetectedLanguage.Turkish,
            Chunks: new List<ChunkResult> { chunk });

        Assert.Single(session.Chunks);
        Assert.Equal(DetectedLanguage.Turkish, session.Language);
    }
}
Bash Run tests, expect fail (types missing)
IN
dotnet test tests/SentimentAnalysis.Core.Tests 2>&1 | grep -E "error CS|Passed!|Failed!" | head -8

OUT
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/DomainRecordsTests.cs(13,26): error CS0246: The type or namespace name 'FusedResult' could not be found (are you missing a using directive or an assembly reference?) [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/DomainRecordsTests.cs(17,25): error CS0103: The name 'Confidence' does not exist in the current context [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/DomainRecordsTests.cs(20,22): error CS0103: The name 'IncongruencePattern' does not exist in the current context [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/DomainRecordsTests.cs(26,22): error CS0103: The name 'IncongruencePattern' does not exist in the current context [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/DomainRecordsTests.cs(33,25): error CS0246: The type or namespace name 'ChunkResult' could not be found (are you missing a using directive or an assembly reference?) [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/DomainRecordsTests.cs(36,24): error CS0246: The type or namespace name 'FusedResult' could not be found (are you missing a using directive or an assembly reference?) [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/DomainRecordsTests.cs(38,17): error CS0103: The name 'Confidence' does not exist in the current context [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/DomainRecordsTests.cs(38,49): error CS0103: The name 'IncongruencePattern' does not exist in the current context [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Core.Tests/SentimentAnalysis.Core.Tests.csproj]
FAIL — beklendiği gibi. Signals.cs ve Results.cs'i yazıyorum.

Write Signals.cs
31 lines
namespace SentimentAnalysis.Core;

/// <summary>Nihai sonucun güven düzeyi.</summary>
public enum Confidence { Yuksek, Orta, Dusuk }

/// <summary>Söz-ton uyumsuzluğunun örüntüsü.</summary>
public enum IncongruencePattern { Yok, Maskeleme, Saka, OlasiIroni }

/// <summary>Algılanan konuşma dili (çok-dilli-hazır).</summary>
public enum DetectedLanguage { Turkish, English, Other }

/// <summary>Ham ses parçası: 16 kHz mono float PCM.</summary>
public sealed record AudioChunk(float[] Samples, int SampleRate, TimeSpan Start, TimeSpan End);

/// <summary>STT çıktısı.</summary>
public sealed record Transcript(string Text, DetectedLanguage Language, double AvgLogProb);

/// <summary>Metin modelinin ham çıktısı.</summary>
public sealed record TextEmotion(ValenceArousal Va, double ModelConfidence);

/// <summary>Prozodi modelinin ham çıktısı.</summary>
public sealed record ProsodyEmotion(ValenceArousal Va, double ModelConfidence);

/// <summary>Füzyon ağırlığını besleyen girdi kalite sinyalleri.</summary>
public sealed record QualitySignals(
    double AudioSnr,
    double SpeechRatio,
    TimeSpan Duration,
    int TranscriptWordCount,
    double TranscriptAvgLogProb);
Write Results.cs
33 lines
using System.Collections.Generic;

namespace SentimentAnalysis.Core;

/// <summary>Füzyon sonucu (chunk düzeyi). İki ham valence de saydamlık için taşınır.</summary>
public sealed record FusedResult(
    ValenceArousal Combined,
    Emotion PrimaryEmotion,
    Emotion SecondaryEmotion,
    Confidence Confidence,
    bool Agreement,
    bool Incongruent,
    IncongruencePattern Pattern,
    double Congruence,
    double TextValence,
    double AudioValence);

/// <summary>Bir chunk'ın tam sonucu (zaman çizelgesi öğesi).</summary>
public sealed record ChunkResult(
    TimeSpan TimeStart,
    TimeSpan TimeEnd,
    FusedResult Fused,
    string Transcript);

/// <summary>Bir toplantı/oturum: chunk listesi + üst-veriler. Toplantı-düzeyi
/// agregasyon (baskın duygu, trend, oynaklık, tepe anlar) Füzyon motorunda
/// (Alt-proje 02) hesaplanır; burada ham veri tutulur.</summary>
public sealed record MeetingSession(
    Guid Id,
    DateTimeOffset StartedAt,
    DetectedLanguage Language,
    IReadOnlyList<ChunkResult> Chunks);
Bash Run tests, expect all pass
IN
dotnet test tests/SentimentAnalysis.Core.Tests 2>&1 | grep -E "error CS|Passed!|Failed!" | head -5

OUT
Passed!  - Failed:     0, Passed:    15, Skipped:     0, Total:    15, Duration: 20 ms - SentimentAnalysis.Core.Tests.dll (net9.0)
15/15 PASS. Task 3 tamam. Task 4 — Application katmanı.

Update Todos

Task 1: Çözüm iskeleti + ValenceArousal değer tipi

Task 2: Emotion enum + EmotionWheel haritalama

Task 3: Domain DTO'ları (kayıt tipleri)

Task 4: Application arayüzleri + AppConfig + DI iskeleti
Bash Create Application project and its test project
IN
cd /Users/ilkaygokbudak/Development/Projects/sentiment-analysis
dotnet new classlib -n SentimentAnalysis.Application -o src/SentimentAnalysis.Application -f net9.0
rm src/SentimentAnalysis.Application/Class1.cs
dotnet sln add src/SentimentAnalysis.Application
dotnet add src/SentimentAnalysis.Application reference src/SentimentAnalysis.Core
dotnet add src/SentimentAnalysis.Application package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet new xunit -n SentimentAnalysis.Application.Tests -o tests/SentimentAnalysis.Application.Tests -f net9.0
rm tests/SentimentAnalysis.Application.Tests/UnitTest1.cs
dotnet sln add tests/SentimentAnalysis.Application.Tests
dotnet add tests/SentimentAnalysis.Application.Tests reference src/SentimentAnalysis.Application
echo "=== DONE ==="

OUT
The template "Class Library" was created successfully.

Processing post-creation actions...
Restoring /Users/ilkaygokbudak/Development/Projects/sentiment-analysis/src/SentimentAnalysis.Application/SentimentAnalysis.Application.csproj:
  Determining projects to restore...
  Restored /Users/ilkaygokbudak/Development/Projects/sentiment-analysis/src/SentimentAnalysis.Application/SentimentAnalysis.Application.csproj (in 99 ms).
Restore succeeded.


Project `src/SentimentAnalysis.Application/SentimentAnalysis.Application.csproj` added to the solution.
Reference `..\SentimentAnalysis.Core\SentimentAnalysis.Core.csproj` added to the project.
info : X.509 certificate chain validation will use the fallback certificate bundle at '/usr/local/share/dotnet/sdk/10.0.101/trustedroots/codesignctl.pem'.
info : X.509 certificate chain validation will use the fallback certificate bundle at '/usr/local/share/dotnet/sdk/10.0.101/trustedroots/timestampctl.pem'.
info : Adding PackageReference for package 'Microsoft.Extensions.DependencyInjection.Abstractions' into project '/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/src/SentimentAnalysis.Application/SentimentAnalysis.Application.csproj'.
info :   GET https://api.nuget.org/v3/registration5-gz-semver2/microsoft.extensions.dependencyinjection.abstractions/index.json
info :   OK https://api.nuget.org/v3/registration5-gz-semver2/microsoft.extensions.dependencyinjection.abstractions/index.json 354ms
info :   GET https://api.nuget.org/v3/registration5-gz-semver2/microsoft.extensions.dependencyinjection.abstractions/page/1.0.0-rc1-final/3.1.26.json
info :   OK https://api.nuget.org/v3/registration5-gz-semver2/microsoft.extensions.dependencyinjection.abstractions/page/1.0.0-rc1-final/3.1.26.json 172ms
info :   GET https://api.nuget.org/v3/registration5-gz-semver2/microsoft.extensions.dependencyinjection.abstractions/page/3.1.27/9.0.3.json
info :   OK https://api.nuget.org/v3/registration5-gz-semver2/microsoft.extensions.dependencyinjection.abstractions/page/3.1.27/9.0.3.json 172ms
info :   GET https://api.nuget.org/v3/registration5-gz-semver2/microsoft.extensions.dependencyinjection.abstractions/page/9.0.4/11.0.0-preview.6.26359.118.json
info :   OK https://api.nuget.org/v3/registration5-gz-semver2/microsoft.extensions.dependencyinjection.abstractions/page/9.0.4/11.0.0-preview.6.26359.118.json 174ms
info : Restoring packages for /Users/ilkaygokbudak/Development/Projects/sentiment-analysis/src/SentimentAnalysis.Application/SentimentAnalysis.Application.csproj...
info :   CACHE https://api.nuget.org/v3/vulnerabilities/index.json
info :   CACHE https://api.nuget.org/v3-vulnerabilities/2026.07.24.05.37.22/vulnerability.base.json
info :   CACHE https://api.nuget.org/v3-vulnerabilities/2026.07.24.05.37.22/2026.07.24.05.37.22/vulnerability.update.json
info : Package 'Microsoft.Extensions.DependencyInjection.Abstractions' is compatible with all the specified frameworks in project '/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/src/SentimentAnalysis.Application/SentimentAnalysis.Application.csproj'.
info : PackageReference for package 'Microsoft.Extensions.DependencyInjection.Abstractions' version '10.0.10' added to file '/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/src/SentimentAnalysis.Application/SentimentAnalysis.Application.csproj'.
info : Writing assets file to disk. Path: /Users/ilkaygokbudak/Development/Projects/sentiment-analysis/src/SentimentAnalysis.Application/obj/project.assets.json
log  : Restored /Users/ilkaygokbudak/Development/Projects/sentiment-analysis/src/SentimentAnalysis.Application/SentimentAnalysis.Application.csproj (in 102 ms).
The template "xUnit Test Project" was created successfully.

Processing post-creation actions...
Restoring /Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Application.Tests/SentimentAnalysis.Application.Tests.csproj:
  Determining projects to restore...
  Restored /Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Application.Tests/SentimentAnalysis.Application.Tests.csproj (in 155 ms).
Restore succeeded.


Project `tests/SentimentAnalysis.Application.Tests/SentimentAnalysis.Application.Tests.csproj` added to the solution.
Reference `..\..\src\SentimentAnalysis.Application\SentimentAnalysis.Application.csproj` added to the project.
=== DONE ===
Application projeleri oluştu (DI paketi 10.0.10 çekildi, net9 ile uyumlu). csproj'a Nullable/ImplicitUsings ekliyorum.

Read SentimentAnalysis.Application.csproj
csproj zaten Nullable/ImplicitUsings içeriyor — değişiklik gerekmiyor. Step 2 — AppConfig testini yazıyorum.

Write AppConfigTests.cs
21 lines
using SentimentAnalysis.Application.Configuration;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class AppConfigTests
{
    [Fact]
    public void Default_HasSaneFusionAndStorageDefaults()
    {
        var config = AppConfig.Default;

        // Arousal ağırlıkla sesten, valence ağırlıkla metinden (master §5).
        Assert.True(config.Fusion.AudioArousalWeight > config.Fusion.TextArousalWeight);
        Assert.True(config.Fusion.TextValenceWeight > config.Fusion.AudioValenceWeight);

        // Ses saklama varsayılanı 7 gün (master §D13).
        Assert.Equal(7, config.Storage.AudioRetentionDays);
    }
}
Bash Run Application tests, expect fail
IN
dotnet test tests/SentimentAnalysis.Application.Tests 2>&1 | grep -E "error CS|Passed!|Failed!" | head -4

OUT
/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Application.Tests/AppConfigTests.cs(1,37): error CS0234: The type or namespace name 'Configuration' does not exist in the namespace 'SentimentAnalysis.Application' (are you missing an assembly reference?) [/Users/ilkaygokbudak/Development/Projects/sentiment-analysis/tests/SentimentAnalysis.Application.Tests/SentimentAnalysis.Application.Tests.csproj]
FAIL — beklendiği gibi. AppConfig'i yazıyorum (Step 4).

Write AppConfig.cs
35 lines
namespace SentimentAnalysis.Application.Configuration;

/// <summary>Füzyon başlangıç ağırlıkları ve eşikleri. Nihai değerler
/// Alt-proje 09 (eval) ile kalibre edilip buraya yansıtılır — keyfi sabit değil.</summary>
public sealed record FusionConfig(
    double AudioArousalWeight = 0.75,
    double TextArousalWeight  = 0.25,
    double TextValenceWeight  = 0.70,
    double AudioValenceWeight = 0.30,
    double IncongruenceThreshold = 0.8,   // |valence_text − valence_audio| eşiği
    double LowConfidenceThreshold = 0.35);

/// <summary>Depolama/gizlilik ayarları (master §D13).</summary>
public sealed record StorageConfig(
    int AudioRetentionDays = 7,
    bool KeepAudio = true,
    string OpusBitrate = "24k");

/// <summary>STT profil ayarları (master §D6).</summary>
public sealed record SttConfig(
    string LiveModel = "large-v3-turbo",
    string? DeepModel = "large-v3");

/// <summary>Uygulama genel konfigürasyonu (stajyerin config.py karşılığı).</summary>
public sealed record AppConfig(
    FusionConfig Fusion,
    StorageConfig Storage,
    SttConfig Stt)
{
    public static AppConfig Default => new(
        Fusion: new FusionConfig(),
        Storage: new StorageConfig(),
        Stt: new SttConfig());
}
Bash Run Application tests, expect pass
IN
dotnet test tests/SentimentAnalysis.Application.Tests 2>&1 | grep -E "error CS|Passed!|Failed!" | head -4

OUT
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 3 ms - SentimentAnalysis.Application.Tests.dll (net9.0)
AppConfig testi PASS. Step 6-7 — arayüz sözleşmeleri ve DI iskeletini yazıyorum.

Write Interfaces.cs
53 lines
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Abstractions;

/// <summary>Konuşma → metin (Whisper.net; Alt-proje 03).</summary>
public interface ISpeechToText
{
    Task<Transcript> TranscribeAsync(AudioChunk chunk, CancellationToken ct);
}

/// <summary>Metnin duygusu → valence-arousal (ONNX; Alt-proje 04).
/// Dil-başına takılabilir (çok-dilli-hazır).</summary>
public interface ITextEmotionModel
{
    Task<TextEmotion> AnalyzeAsync(Transcript transcript, CancellationToken ct);
}

/// <summary>Sesin prozodisi → valence-arousal (VAD-regresyon ONNX; Alt-proje 04).</summary>
public interface IProsodyEmotionModel
{
    Task<ProsodyEmotion> AnalyzeAsync(AudioChunk chunk, CancellationToken ct);
}

/// <summary>İki modeli birleştiren saf, deterministik füzyon (Alt-proje 02).</summary>
public interface IEmotionFusion
{
    FusedResult Fuse(TextEmotion text, ProsodyEmotion prosody, QualitySignals quality);
}

/// <summary>Canlı sistem-sesi / dosya yakalama (Alt-proje 05).</summary>
public interface IAudioCaptureSource
{
    IAsyncEnumerable<AudioChunk> CaptureAsync(CaptureOptions options, CancellationToken ct);
}

/// <summary>Yakalama seçenekleri.</summary>
public sealed record CaptureOptions(TimeSpan ChunkLength, TimeSpan Overlap);

/// <summary>Sessiz chunk'ları eleyen VAD kapısı (Silero; Alt-proje 05).</summary>
public interface IVadGate
{
    bool HasSpeech(AudioChunk chunk);
}

/// <summary>Oturum kalıcılığı ve saklama politikası (Alt-proje 07).</summary>
public interface ISessionStore
{
    Task SaveChunkAsync(Guid sessionId, ChunkResult result, CancellationToken ct);
    Task<MeetingSession?> GetSessionAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken ct);
    Task PurgeExpiredAudioAsync(CancellationToken ct);
}
Write DependencyInjection.cs
19 lines
using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Application;

/// <summary>Application katmanı DI kayıt noktası. Infrastructure implementasyonları
/// (Alt-projeler 02–07) kendi uzantı metotlarıyla eklenir.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationCore(
        this IServiceCollection services, AppConfig? config = null)
    {
        services.AddSingleton(config ?? AppConfig.Default);
        // Somut servisler (ISpeechToText, IEmotionFusion, ...) Infrastructure
        // katmanında ayrı AddXxx() uzantılarıyla kaydedilecek.
        return services;
    }
}
Step 8 — tüm çözümün derlendiğini ve tüm testlerin geçtiğini doğruluyorum.

Bash Build whole solution and run all tests
IN
dotnet build 2>&1 | grep -E "error|Build succeeded|warning" | head; echo "=== TEST ==="; dotnet test 2>&1 | grep -E "Passed!|Failed!|error" | head

OUT
Build succeeded.
=== TEST ===
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 5 ms - SentimentAnalysis.Application.Tests.dll (net9.0)
Passed!  - Failed:     0, Passed:    15, Skipped:     0, Total:    15, Duration: 22 ms - SentimentAnalysis.Core.Tests.dll (net9.0)
Tüm testler geçti: 16/16 (15 Core + 1 Application). Task 4 tamam.

Update Todos

Task 1: Çözüm iskeleti + ValenceArousal değer tipi

Task 2: Emotion enum + EmotionWheel haritalama

Task 3: Domain DTO'ları (kayıt tipleri)

Task 4: Application arayüzleri + AppConfig + DI iskeleti
Bash List created source files
IN
find src tests -name "*.cs" | sort

OUT
src/SentimentAnalysis.Application/Abstractions/Interfaces.cs
src/SentimentAnalysis.Application/Configuration/AppConfig.cs
src/SentimentAnalysis.Application/DependencyInjection.cs
src/SentimentAnalysis.Application/obj/Debug/net9.0/.NETCoreApp,Version=v9.0.AssemblyAttributes.cs
src/SentimentAnalysis.Application/obj/Debug/net9.0/SentimentAnalysis.Application.AssemblyInfo.cs
src/SentimentAnalysis.Application/obj/Debug/net9.0/SentimentAnalysis.Application.GlobalUsings.g.cs
src/SentimentAnalysis.Core/Emotion.cs
src/SentimentAnalysis.Core/EmotionWheel.cs
src/SentimentAnalysis.Core/Results.cs
src/SentimentAnalysis.Core/Signals.cs
src/SentimentAnalysis.Core/ValenceArousal.cs
src/SentimentAnalysis.Core/obj/Debug/net9.0/.NETCoreApp,Version=v9.0.AssemblyAttributes.cs
src/SentimentAnalysis.Core/obj/Debug/net9.0/SentimentAnalysis.Core.AssemblyInfo.cs
src/SentimentAnalysis.Core/obj/Debug/net9.0/SentimentAnalysis.Core.GlobalUsings.g.cs
tests/SentimentAnalysis.Application.Tests/AppConfigTests.cs
tests/SentimentAnalysis.Application.Tests/obj/Debug/net9.0/.NETCoreApp,Version=v9.0.AssemblyAttributes.cs
tests/SentimentAnalysis.Application.Tests/obj/Debug/net9.0/SentimentAnalysis.Application.Tests.AssemblyInfo.cs
tests/SentimentAnalysis.Application.Tests/obj/Debug/net9.0/SentimentAnalysis.Application.Tests.GlobalUsings.g.cs
tests/SentimentAnalysis.Core.Tests/DomainRecordsTests.cs
tests/SentimentAnalysis.Core.Tests/EmotionWheelTests.cs
tests/SentimentAnalysis.Core.Tests/ValenceArousalTests.cs
tests/SentimentAnalysis.Core.Tests/obj/Debug/net9.0/.NETCoreApp,Version=v9.0.AssemblyAttributes.cs
tests/SentimentAnalysis.Core.Tests/obj/Debug/net9.0/SentimentAnalysis.Core.Tests.AssemblyInfo.cs
tests/SentimentAnalysis.Core.Tests/obj/Debug/net9.0/SentimentAnalysis.Core.Tests.GlobalUsings.g.cs
Alt-proje 01 tamamen bitti. Planın 4 task'ı da uygulandı, doğrulandı. git istemediğiniz için commit adımları atlandı; onların yerine her task'ta dotnet test ile doğrulama yaptım.

Ne inşa edildi
Çözüm: SentimentAnalysis.sln — 2 kaynak + 2 test projesi, hepsi net9.0.

SentimentAnalysis.Core (sıfır NuGet, saf domain):

ValenceArousal.cs — boyutsal çekirdek (clamp, Neutral, Öklid uzaklık)
Emotion.cs — dil-bağımsız 8-duygu enum
EmotionWheel.cs — VA → en yakın duygu haritalama (primary + secondary)
Signals.cs + Results.cs — AudioChunk, Transcript, TextEmotion, ProsodyEmotion, QualitySignals, FusedResult, ChunkResult, MeetingSession
SentimentAnalysis.Application (Core'a bağlı sözleşmeler):

Interfaces.cs — 7 arayüz (ISpeechToText, ITextEmotionModel, IProsodyEmotionModel, IEmotionFusion, IAudioCaptureSource, IVadGate, ISessionStore)
AppConfig.cs — füzyon ağırlıkları/eşikleri + depolama + STT ayarları
DependencyInjection.cs — DI kayıt iskeleti
Doğrulama: dotnet build → succeeded · dotnet test → 16/16 PASS (15 Core + 1 Application).

Kabul kriterleri karşılandı: Core sıfır NuGet ile derleniyor, EmotionWheel/ValenceArousal %100 testli, tüm arayüzler XML doc'lu.

Sıradaki adım
Bu, ilk dikey dilimin (1→2→3→4) temelini oluşturdu. Devam için seçenekler:

Alt-proje 02 (Füzyon motoru) planını yaz — bu iskeletin üstüne saf-C# füzyon algoritması; yine ML'siz, test-yoğun, hemen uygulanabilir.
03/04 (STT + duygu modelleri) için önce gerçek model isimlerini araştır — Türkçe boyutsal SER ve çok-dilli metin modeli adaylarını somutlaştırıp risk tablosunu kapat.
Şimdilik dur — temel iskelet hazır, gerisi devralınacak.
Hangisiyle ilerleyelim?