# Sesli Duygu Analizi

Ses kayıtlarından ve canlı konuşmadan duygu analizi yapan, **tamamen yerel** çalışan
cross-platform .NET masaüstü uygulaması.

Hiçbir ses kaydı veya metin cihazı terk etmez. Bulut API'si yoktur, telemetri yoktur —
tüm modeller makinenizde çalışır.

## Ne yapar

Konuşmayı iki bağımsız kanaldan analiz eder ve sonuçları birleştirir:

- **Metin kanalı** — Whisper ile konuşma metne çevrilir, metnin duygusu ölçülür ("ne söylendi")
- **Prozodi kanalı** — sesin tonu, temposu ve enerjisinden duygu ölçülür ("nasıl söylendi")

Her iki model de duyguyu ayrık etiketlerle değil, iki sürekli eksende ifade eder:

| Eksen | Anlamı | Aralık |
|---|---|---|
| **Valence** | olumsuz ↔ olumlu | −1 … +1 |
| **Arousal** | sakin ↔ uyarılmış | 0 … 1 |

Bu boyutsal temsil sistemin iç para birimidir; ayrık duygular yalnızca en sonda,
sunum katmanında üretilir.

### Neden iki model?

Çünkü ikisi çeliştiğinde ortaya çıkan sinyal, tek başına her ikisinden de değerlidir.

Füzyon katmanı **eksen-başına güvenilirlik** ilkesiyle çalışır: arousal ağırlıklı olarak
sesten, valence ağırlıklı olarak metinden gelir — çünkü her model farklı eksende güvenilirdir.
Ağırlıklar sabit değildir; sinyal-gürültü oranı, konuşma süresi, transkript uzunluğu ve
model güvenine göre modüle edilir. Girdi kalitesi düştüğünde sistem yüksek güven iddia etmez.

**Söz-ton uyumsuzluğu birinci sınıf bir çıktıdır.** Metin "harika, çok memnunum" derken ses
gergin ve düz çıkıyorsa, sistem bunu ortalamayla örtmez — işaretler ve sınıflandırır:

| Örüntü | Anlamı |
|---|---|
| `Maskeleme` | olumlu söz, olumsuz ton |
| `OlasiIroni` | olumlu söz, düz/sıkıntılı ton |
| `Saka` | olumsuz söz, olumlu ton |

Nihai valence-arousal noktası 8 duygudan oluşan bir circumplex çarkına haritalanır:
**Coşkulu · Mutlu · Sakin · Sıkılmış · Üzgün · Gergin · Kızgın · Nötr**

## Mimari

Bağımlılıklar içe doğru akar. `Core` hiçbir şeye bağlı değildir; her altyapı dilimi
ayrı bir projedir ve yalnızca kendi native bağımlılığını taşır.

```
Core                       Domain: ValenceArousal, Emotion, EmotionWheel, sonuç kayıtları
  ↑
Application                Füzyon motoru, pipeline orkestrasyonu, arayüzler, AppConfig
  ↑
Infrastructure.Stt         Whisper.net (Metal) — konuşma → metin
Infrastructure.Emotion     ONNX Runtime — prozodi ve metin duygu modelleri
Infrastructure.Audio       WAV çözme, yeniden örnekleme, chunk'lama
Infrastructure.Vad         Silero VAD — sessiz parçaları eler
Infrastructure.Storage     SQLite + AES-256-GCM şifreli ses deposu
  ↑
Desktop                    Avalonia'sız ViewModel'ler (headless test edilebilir)
Desktop.Avalonia           Avalonia UI kabuğu, composition root
```

Native tipler (Whisper, ONNX oturumları, Silero) yalnızca motor sınırında yaşar;
aralarındaki tüm yardımcı mantık saf ve birim testlidir.

### Veri akışı

```
ses kaynağı → chunk'lama → VAD kapısı → ┬→ prozodi modeli ─┐
                                        └→ Whisper → metin modeli ─┴→ füzyon → oturum özeti
```

Chunk'lar sınırlı kapasiteli (bounded) kanallar üzerinden akar; kanal taştığında en eski
chunk düşürülür ve düşen sayısı raporlanır — geri basınç sessizce yutulmaz.

## Gereksinimler

- **.NET 9 SDK** (veya .NET 10 — `Directory.Build.props` içindeki `RollForward=Major` sayesinde
  .NET 9 runtime kurulu olmasa da çalışır)
- **macOS** (Apple Silicon — Whisper Metal ile hızlanır) **veya Windows**
- **ffmpeg** — yalnızca WAV dışı formatlar için (macOS'ta yerleşik `afconvert` da kullanılır)
- ~3.5 GB boş disk (model dosyaları için)

**Doğrulanmış platformlar:** macOS (Apple Silicon) ve **Windows 11 / x64** — ikisinde de tüm
testler ve uçtan uca analiz çalıştırılmıştır. Linux denenmemiştir (kod platforma özgü API
kullanmaz; ses dönüştürme ffmpeg üzerinden gider).

Canlı sistem-sesi ve mikrofon yakalama tasarım gereği platforma özgüdür ve henüz eklenmemiştir.

Windows'ta hızlı başlangıç ve sorun giderme için: [`NASIL-TEST-EDILIR.md`](NASIL-TEST-EDILIR.md)

## Kurulum

```bash
git clone https://github.com/betulflz/sentiment-analysis.git
cd sentiment-analysis
dotnet build
dotnet test
```

Model dosyaları olmadan da tüm çözüm derlenir ve testler geçer — gerçek modele ihtiyaç
duyan testler otomatik olarak atlanır (bkz. *Testler*).

## Model dosyaları

Model dosyaları lisans ve boyut nedeniyle repoya dahil **değildir**. Şu klasöre konur:

```
macOS:    ~/Library/Application Support/SentimentAnalysis/models/
Windows:  %APPDATA%\SentimentAnalysis\models\
```

### Otomatik kurulum

```powershell
pwsh tools/fetch-models.ps1            # doğrudan indirilebilenler (~555 MB)
python tools/export_onnx_models.py     # PyTorch → ONNX export (~2.9 GB)
```

Export scripti, her modelin `config.json` **etiket sırasını** C# tarafındaki sabit listeyle
karşılaştırır ve ayrışma varsa durur — yanlış sıra sessizce yanlış duygu üretir. Üretilen
ONNX grafiğinin tensör adları da doğrulanır.

### Dosyalar

| Dosya | Kaynak | Ne işe yarar | Zorunlu |
|---|---|---|---|
| `ggml-large-v3-turbo-q5_0.bin` | [ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp) | Konuşma → metin | ✅ |
| `prosody-msp-dim.onnx` | [audeering/wav2vec2-...-msp-dim](https://huggingface.co/audeering/wav2vec2-large-robust-12-ft-emotion-msp-dim) | Ses tonu → valence-arousal | ✅ |
| `text-emotion-xlmr.onnx` | [tabularisai/multilingual-emotion-classification](https://huggingface.co/tabularisai/multilingual-emotion-classification) | Metin → 11 duygu → VA | ✅ |
| `sentencepiece.bpe.model` | [xlm-roberta-base](https://huggingface.co/xlm-roberta-base) | Metin modelinin tokenizer'ı | ✅ |
| `silero_vad.onnx` | [snakers4/silero-vad](https://github.com/snakers4/silero-vad) | Konuşma var mı tespiti | ✅ |
| `prosody-turkish-ser.onnx` | [SeaBenSea/hubert-large-turkish-ser](https://huggingface.co/SeaBenSea/hubert-large-turkish-speech-emotion-recognition) | **Türkçe'ye özel** prozodi (TurEV-DB) | opsiyonel |

> **Tuzak:** audeering ve SeaBenSea checkpoint'leri standart `AutoModel` sınıflarıyla
> yüklenemez — özel başlık mimarileriyle kaydedilmişlerdir ve `pipeline()` eğitilmiş
> sınıflandırma başlığını **sessizce atıp rastgele başlatır** (çıktı gürültü olur).
> `tools/export_onnx_models.py` her ikisi için doğru mimariyi yeniden tanımlar.

Model konumları `AppConfig` üzerinden değiştirilebilir (`SttConfig.ModelDirectory`,
`EmotionConfig.ModelDirectory`, `VadConfig.ModelDirectory`).

## İki prozodi kanalı

`prosody-turkish-ser.onnx` mevcutsa iki model birlikte çalışır ve VA noktaları
harmanlanır (taban ağırlık × modelin kendi güveni):

| Model | Eğitim verisi | Güçlü yanı |
|---|---|---|
| audeering wav2vec2 | MSP-Podcast (İngilizce ağırlıklı) | Genel amaçlı, doğrudan boyutsal regresyon |
| SeaBenSea HuBERT | **TurEV-DB (Türkçe)** | Türkçe konuşmada dile özel eğitilmiş |

İki model çeliştiğinde birleşik güven düşürülür (anlaşmazlık = belirsizlik). Prozodi, STT
ile paralel çalıştığı için chunk'ın dili henüz bilinmez — bu yüzden dile göre model
seçmek yerine ikisi birden çalıştırılıp harmanlanır.

Ağırlıklar `EmotionConfig.GeneralProsodyWeight` / `TurkishSerWeight` (varsayılan 0.5/0.5,
**kalibre edilmemiş**). Kapatmak için `UseTurkishSer = false`.

## Komut satırı aracı

Grafik arayüz olmadan tam hattı çalıştırır; eval/kalibrasyon araçlarının da temelidir.

```bash
dotnet run --project src/SentimentAnalysis.Cli -- kontrol          # kurulumu doğrula
dotnet run --project src/SentimentAnalysis.Cli -- analiz kayit.mp3 # tam analiz
dotnet run --project src/SentimentAnalysis.Cli -- analiz kayit.wav --json
dotnet run --project src/SentimentAnalysis.Cli -- stt kayit.wav    # teşhis: ham STT + kalite ağırlıkları
```

`stt` komutu, füzyon ağırlıklarının **bileşenlerini** ayrı ayrı yazar (kelime sayısı ×
logprob, SNR × konuşma oranı × süre). Bir kanal sıfır ağırlık aldığında hangi çarpanın
sıfırladığı böyle görülür.

## Testler

```bash
dotnet test
```

**310 test geçer, 7 test atlanır.** Atlanan testler gerçek model dosyaları gerektirir.
Modeller kuruluysa ilgili ortam değişkenlerini vererek onları da çalıştırabilirsiniz:

```bash
export MODELS="$HOME/Library/Application Support/SentimentAnalysis/models"

export WHISPER_MODEL_PATH="$MODELS/ggml-large-v3-turbo-q5_0.bin"
export WHISPER_SAMPLE_WAV=/yol/ornek-turkce.wav        # 16 kHz mono WAV
export PROSODY_MODEL_PATH="$MODELS/prosody-msp-dim.onnx"
export PROSODY_SAMPLE_WAV=/yol/ornek-turkce.wav
export TEXT_MODEL_DIR="$MODELS"
export XLMR_SPM_PATH="$MODELS/sentencepiece.bpe.model"
export VAD_MODEL_PATH="$MODELS/silero_vad.onnx"
export VAD_SAMPLE_SPEECH_WAV=/yol/ornek-turkce.wav

dotnet test
```

Bu testler süs değildir: üç gerçek hatayı yakaladılar — metin modelinin etiket sırasının
yanlış olması, Silero VAD'ın 64 örnekli context penceresini beslemediğimiz için gerçek
konuşmada bile sıfıra yakın olasılık üretmesi, ve aşağıdaki metin-kanalı hatası.
Model uçları gerçek veriyle doğrulanır, varsayımla değil.

### Yakalanan kritik hata: metin kanalı hiç oy kullanmıyordu

`WhisperProcessorBuilder.WithProbabilities()` çağrılmadığı için Whisper.net
`SegmentData.Probability`'yi **0** bırakıyordu. Zincir sessiz ve yıkıcıydı:

```
Probability = 0
  → TranscriptAssembler avg-logprob'u tabana kırpar (log 1e-6 ≈ −13.8)
  → QualityWeighting.TextReliability = 0
  → füzyonda METİN KANALI HİÇ OY KULLANMAZ  (nihai valence = saf prozodi)
  → rAudio·rText = 0 olduğu için güven düzeyi ASLA "Yüksek" olamaz
```

Gerçek bir Türkçe kayıtta ölçülen fark:

| | Önce | Sonra |
|---|---:|---:|
| avg-logprob | −13.8155 | −0.0865 |
| Metin ağırlığı | 0.0000 | 0.9135 |
| Nihai valence | +0.3534 (= saf ses) | +0.0699 (karışım) |

Eski test iddiası (`AvgLogProb < 0`) bunu yakalayamıyordu — −13.8 de sıfırdan küçüktür.
Yeni regresyon testi füzyonun kullandığı eşiğe göre kontrol eder.

`FusedResult` artık her iki kanalın ham çıktısını **ve onlara verilen ağırlığı** taşır
(`TextReliability`, `AudioReliability`, `TextChannelSilent`). Bir kanal susturulduğunda
bu artık hem CLI'da hem arayüzde görünür.

## Gizlilik

- Ses ve metin **hiçbir zaman** ağa çıkmaz; harici API çağrısı yoktur
- Saklanan ses **AES-256-GCM** ile şifrelenir; anahtarsız okunamaz
- Saklama süresi dolan ses otomatik silinir, analiz metrikleri korunur (varsayılan 7 gün)
- Ses saklamayı tamamen kapatmak mümkündür (`StorageConfig.KeepAudio`)

## Proje durumu

| Alt-proje | Durum |
|---|---|
| 01 Temel + Domain | ✅ |
| 02 Füzyon motoru | ✅ |
| 03 STT motoru (Whisper.net + Metal) | ✅ |
| 04 Duygu modelleri (prozodi + metin) | ✅ |
| 05 Ses yakalama + Silero VAD | ✅ |
| 06 Pipeline orkestrasyonu | ✅ |
| 07 Depolama + gizlilik | ✅ |
| 08 Avalonia UI (+ dosya-analiz demo) | ✅ |
| 08d Yüzdelik dağılım + üçlü görünüm | ✅ |
| 08d Canlı mikrofon | 🔜 |
| Windows desteği (ffmpeg, RollForward, CPU sağlayıcı) | ✅ |
| Türkçe'ye özel prozodi kanalı (TurEV-DB) | ✅ |
| CLI aracı (analiz / kontrol / teşhis / JSON) | ✅ |
| 09 Değerlendirme (eval) | 🔜 |

Sonraki turlara ertelenenler: canlı sistem-sesi ve mikrofon yakalama (ScreenCaptureKit/WASAPI),
Opus ses sıkıştırma, OS keychain entegrasyonu, 8-duygu çarkı ve trend grafiği,
GPU hızlandırma (DirectML/CUDA — şu an CPU, RTF ≈ 4×).

### Kalibre edilmemiş olan

Füzyon ağırlıkları, VAD eşiği, metin duygu → VA tablosu, Türkçe SER → VA çapaları,
iki prozodi modelinin harman ağırlıkları ve `DistributionTemperature` şu an **makul
başlangıç değerleridir, ölçülmüş değildir.** Etiketli Türkçe veri üzerinde kalibrasyon
alt-proje 09'un konusudur.

Bilinen bir gözlem: audeering prozodi modeli test kayıtlarında arousal'ı sürekli 0.80–0.88
aralığında raporluyor. ONNX çıktısı PyTorch referansıyla birebir doğrulandı (fark ~3e-06),
yani bu bir uygulama hatası değil modelin karakteristiği — ancak kalibrasyonda ölçek
düzeltmesi gerekebilir. Değerlendirme tek bir "doğruluk" sayısına indirgenmeyecek;
katı doğruluk ve kararlı örneklerdeki doğruluk + kapsam oranı ayrı ayrı raporlanacaktır.

Bu proje kesinlik gösterisi yerine dürüstlüğü tercih eder: sistem emin olmadığında
"emin değilim" der.

## Dokümantasyon

- [`NASIL-TEST-EDILIR.md`](NASIL-TEST-EDILIR.md) — Windows'ta hızlı başlangıç, CLI komutları,
  model kurulumu, sorun giderme
- [`docs/superpowers/specs/`](docs/superpowers/specs/) — master tasarım (kararlar kütüğü,
  riskler, ilkeler) ve alt-proje spec'leri
- [`docs/superpowers/plans/`](docs/superpowers/plans/) — uygulanmış TDD planları; her planın
  sonundaki "Post-Review Düzeltmeleri" bölümü uygulama sırasında alınan kararları anlatır
- [`AGENT-CONVERSATION.md`](AGENT-CONVERSATION.md) — projenin tasarlandığı ilk planlama
  konuşmasının tam kaydı; kararların **neden** öyle alındığının kaynağı

## Teknoloji

.NET 9 · Avalonia 11 · Whisper.net · ONNX Runtime · Silero VAD · Microsoft.Data.Sqlite ·
CommunityToolkit.Mvvm · xUnit
