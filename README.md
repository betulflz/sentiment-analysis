# Sesli Duygu Analizi

Ses kayıtlarından ve canlı konuşmadan duygu analizi yapan, **tamamen yerel** çalışan
cross-platform .NET masaüstü uygulaması.

M1 Air üzerinde doğruluk, süre ve bellek karşılaştırması için
[kalite pilotu aracını](tools/quality/README.md) kullanabilirsiniz. Bu araç mevcut Whisper ve
sherpa bileşenlerini ayrı ölçer; gerçek model ve etiketli kayıt gerektirir.

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

### Nötr neden bir merkez değil, bir disk?

Klasik "en yakın merkez" kuralında Nötr çarkın tam ortasında, diğer yedi duygu ise
kenarında oturur. Bu geometri Nötr'e orantısız büyük bir havza verir: arousal 0,5'te
valence −0,33…+0,40 arasındaki *her* nokta Nötr'dür ve valence sıfıra yakınken ses ne
kadar heyecanlı olursa olsun etiket Nötr'den çıkamaz. Sonuç: aynı cümleyi mutlu ya da
üzgün tonla söylemek çıktıyı değiştirmez.

Bunun iki ayrı sebebi vardır ve ikisi de düzeltilmiştir:

- **Prozodi kazancı** (`EmotionConfig.ProsodyValenceGain/ProsodyArousalGain`) — msp-dim'in
  ham çıktısı nötr merkez çevresinde sıkışıktır (valence pratikte −0,3…+0,3). Kazanç,
  merkez etrafında simetrik doğrusal genişletmedir: işareti değil yayılımı düzeltir,
  nötr girdiyi nötr bırakır.
- **Nötr yarıçapı** (`FusionConfig.NeutralRadius`) — Nötr artık uzaklık yarışına girmez.
  Nokta merkez çevresindeki küçük diskin içindeyse Nötr, dışındaysa gerçek yedi duygunun
  en yakınıdır. Aynı kural etiket, ikincil duygu ve yüzde dağılımının tamamında geçerlidir.

Her iki değer de **kalibre edilmemiş başlangıçlardır** (Alt-Proje 09 Türkçe etiketli
veriyle ölçecek); `null`/`1.0` verilerek klasik davranışa dönülebilir.

Ayrıca her chunk, girdi kalitesini iki kanal için ayrı ayrı raporlar
(`FusedResult.AudioReliability` / `TextReliability`) ve bunlar zaman çizelgesinde
görünür — beklenmedik bir sonucun sebebi çoğu zaman ağırlıklarda değil girdi
kalitesindedir (kısa, sessiz veya gürültülü kayıt).

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

- **.NET 9 SDK**
- **macOS** (Apple Silicon önerilir — Whisper Metal ile hızlanır)
- ~2 GB boş disk (model dosyaları için)

Kod cross-platform .NET'tir ve platforma özgü API kullanmaz, ancak **yalnızca macOS'ta
(Apple Silicon) çalıştırılıp doğrulanmıştır** — Windows ve Linux henüz denenmemiştir.
Canlı sistem-sesi yakalama ise tasarım gereği platforma özgüdür.

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

Model dosyaları lisans ve boyut nedeniyle repoya dahil **değildir**. Beş dosya
indirilip şu klasöre konur:

```
macOS:    ~/Library/Application Support/SentimentAnalysis/models/
Windows:  %APPDATA%\SentimentAnalysis\models\
```

| Dosya | Kaynak | Ne işe yarar |
|---|---|---|
| `ggml-large-v3-turbo-q5_0.bin` | [ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp) | Konuşma → metin |
| `prosody-msp-dim.onnx` | [audeering/wav2vec2-...-msp-dim](https://huggingface.co/audeering/wav2vec2-large-robust-12-ft-emotion-msp-dim) (ONNX export'u Zenodo'da; `model.onnx` bu adla yeniden adlandırılır) | Ses tonu → valence-arousal |
| `text-emotion-xlmr.onnx` | [tabularisai/multilingual-emotion-classification](https://huggingface.co/tabularisai/multilingual-emotion-classification) (HF Optimum ile ONNX'e export edilir) | Metin → duygu |
| `sentencepiece.bpe.model` | [xlm-roberta-base](https://huggingface.co/xlm-roberta-base) | Metin modelinin tokenizer'ı |
| `silero_vad.onnx` | [snakers4/silero-vad](https://github.com/snakers4/silero-vad) (`src/silero_vad/data/`) | Konuşma var mı tespiti |

> **Not:** Metin modelinin ONNX export'u için `optimum` 2.x kullanıyorsanız ONNX exporter
> ayrı bir pakettedir — `pip install optimum optimum-onnx` gerekir.

Model konumları `AppConfig` üzerinden değiştirilebilir (`SttConfig.ModelDirectory`,
`EmotionConfig.ModelDirectory`, `VadConfig.ModelDirectory`).

## Testler

```bash
dotnet test
```

**193 test geçer, 6 test atlanır.** Atlanan altı test gerçek model dosyaları gerektirir.
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

Bu testler süs değildir: iki gerçek hatayı yakaladılar — metin modelinin etiket sırasının
yanlış olması ve Silero VAD'ın 64 örnekli context penceresini beslemediğimiz için gerçek
konuşmada bile sıfıra yakın olasılık üretmesi. Model uçları gerçek veriyle doğrulanır,
varsayımla değil.

## Yerel model kaynakları

Masaüstü uygulamasında modeller her analiz için oluşturulur; analiz tamamlanınca, hata verince veya durdurma işlemi bitince kapatılır. Sonraki analiz modelleri yeniden yükler. Canlı analiz/hazırlık ve görüşme sonrası konuşmacı analizi ortak tek işlik kuyruğu kullanır; birinin model temizliği bitmeden diğeri model açmaz. Görüntü modelleri de aynı analiz ömrüne bağlıdır.

Hazırlık UI iş parçacığının dışında çalışır ve hazırlık sırasında **Durdur** kullanılabilir. Devam eden bazı native yükleme/çıkarım çağrıları hemen kesilemediğinden güvenli kapanış bu çağrının dönmesini bekleyebilir. Uygulamadan çıkışta çalışan işler iptal edilir, bitmeleri beklenir ve servisler kapatılır. Native oturumların kapatılması, macOS bellek/swap göstergelerinin anında düşeceğini garanti etmez.

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
| 08d Yüzdelik dağılım, üçlü görünüm, canlı mikrofon | 🔜 planlanıyor |
| 09 Değerlendirme (eval) | 🔜 |

Sonraki turlara ertelenenler: canlı sistem-sesi yakalama (ScreenCaptureKit/WASAPI),
Opus ses sıkıştırma, OS keychain entegrasyonu, zengin UI görselleştirme (8-duygu çarkı,
trend grafiği).

### Kalibre edilmemiş olan

Füzyon ağırlıkları, VAD eşiği ve metin duygu → valence-arousal tablosu şu an **makul
başlangıç değerleridir, ölçülmüş değildir.** Etiketli Türkçe veri üzerinde kalibrasyon
alt-proje 09'un konusudur. Değerlendirme tek bir "doğruluk" sayısına indirgenmeyecek;
katı doğruluk ve kararlı örneklerdeki doğruluk + kapsam oranı ayrı ayrı raporlanacaktır.

Bu proje kesinlik gösterisi yerine dürüstlüğü tercih eder: sistem emin olmadığında
"emin değilim" der.

## Dokümantasyon

- [`docs/superpowers/specs/`](docs/superpowers/specs/) — master tasarım (kararlar kütüğü,
  riskler, ilkeler) ve alt-proje spec'leri
  
- [`docs/superpowers/plans/`](docs/superpowers/plans/) — uygulanmış TDD planları; her planın
  sonundaki "Post-Review Düzeltmeleri" bölümü uygulama sırasında alınan kararları anlatır
- [`AGENT-CONVERSATION.md`](AGENT-CONVERSATION.md) — projenin tasarlandığı ilk planlama
  konuşmasının tam kaydı; kararların **neden** öyle alındığının kaynağı

## Teknoloji

.NET 9 · Avalonia 11 · Whisper.net · ONNX Runtime · Silero VAD · Microsoft.Data.Sqlite ·
CommunityToolkit.Mvvm · xUnit
