# Nasıl Test Edilir (Windows)

Bu dosya, projeyi bu makinede çalıştırmak için gereken her şeyi anlatır.
Kurulum **zaten yapılmış** durumda — aşağıdakiler doğrulama ve günlük kullanım içindir.

## Hızlı başlangıç

```powershell
cd C:\Users\bahadır\Downloads\sentiment-analysis-main\sentiment-analysis-main

# 1. Kurulumu doğrula (modeller yerinde mi, hangi prozodi kanalları aktif)
dotnet run --project src\SentimentAnalysis.Cli -- kontrol

# 2. Bir ses dosyasını analiz et
dotnet run --project src\SentimentAnalysis.Cli -- analiz "C:\yol\kayit.mp3"

# 3. Masaüstü uygulamasını aç
dotnet run --project src\SentimentAnalysis.Desktop.Avalonia
```

Hazır test dosyaları: `..\test-sesleri\turkce-kisa.wav` (11 sn) ve `turkce-ornek.wav` (46 sn).

## CLI komutları

| Komut | Ne yapar |
|---|---|
| `sa kontrol` | Model dosyaları, platform, aktif prozodi kanalları |
| `sa analiz <dosya>` | Tam analiz — chunk çizelgesi + oturum özeti |
| `sa analiz <dosya> --json` | Makine okunur çıktı (eval araçları için) |
| `sa stt <dosya>` | **Teşhis**: ham STT çıktısı + kalite ağırlıklarının bileşenleri |

Seçenekler: `--dil auto|tr|en` · `--chunk <sn>` · `--ortusme <sn>` · `--vad-esik <0..1>`

> `sa` yerine `dotnet run --project src\SentimentAnalysis.Cli --` yazılır.
> Kısayol için: `dotnet build -c Release` sonrası
> `src\SentimentAnalysis.Cli\bin\Release\net9.0\sa.exe`.

## Testler

```powershell
dotnet test
```

**310 test geçer.** Gerçek model dosyası gerektiren 7 test, ortam değişkenleri verilmezse
atlanır. Hepsini çalıştırmak için:

```powershell
$M = "$env:APPDATA\SentimentAnalysis\models"
$W = "C:\Users\bahadır\Downloads\sentiment-analysis-main\test-sesleri\turkce-kisa.wav"

$env:WHISPER_MODEL_PATH  = "$M\ggml-large-v3-turbo-q5_0.bin"
$env:WHISPER_SAMPLE_WAV  = $W
$env:PROSODY_MODEL_PATH  = "$M\prosody-msp-dim.onnx"
$env:PROSODY_SAMPLE_WAV  = $W
$env:TEXT_MODEL_DIR      = $M
$env:XLMR_SPM_PATH       = "$M\sentencepiece.bpe.model"
$env:VAD_MODEL_PATH      = "$M\silero_vad.onnx"
$env:VAD_SAMPLE_SPEECH_WAV = $W

dotnet test
```

## Model dosyaları

Konum: `%APPDATA%\SentimentAnalysis\models\`

| Dosya | Boyut | Rol | Zorunlu |
|---|---:|---|---|
| `ggml-large-v3-turbo-q5_0.bin` | 547 MB | Whisper STT | ✅ |
| `prosody-msp-dim.onnx` | 631 MB | Prozodi → VA (çok dilli) | ✅ |
| `text-emotion-xlmr.onnx` | 1061 MB | Metin → 11 duygu → VA | ✅ |
| `sentencepiece.bpe.model` | 5 MB | XLM-R tokenizer | ✅ |
| `silero_vad.onnx` | 2 MB | Konuşma tespiti | ✅ |
| `prosody-turkish-ser.onnx` | 1208 MB | **Türkçe SER** (TurEV-DB) | opsiyonel |

Yeniden kurmak gerekirse:

```powershell
pwsh tools\fetch-models.ps1                        # doğrudan indirilenler
python tools\export_onnx_models.py                 # PyTorch → ONNX export
python tools\export_onnx_models.py --only turkish-ser   # yalnızca Türkçe SER
```

Export scripti, modelin `config.json` etiket sırasını C# tarafındaki sabit listeyle
karşılaştırır ve ayrışma varsa **durur** — yanlış sıra sessizce yanlış duygu üretirdi.

## Ortam gereksinimleri

| Bileşen | Durum | Not |
|---|---|---|
| .NET SDK | 10.0.302 ✅ | Proje net9.0 hedefler; `Directory.Build.props` içindeki `RollForward=Major` ile .NET 10 runtime'da çalışır |
| ffmpeg | ✅ | WAV dışı formatları (mp3/m4a/mp4…) dönüştürmek için. Yoksa: `winget install Gyan.FFmpeg` |
| Python | 3.12 ✅ | Yalnızca ONNX export için gerekir; analiz için gerekmez |
| Disk | ~3.5 GB | Model dosyaları |

## Prozodi kanalları

`prosody-turkish-ser.onnx` mevcutsa **iki prozodi modeli birlikte** çalışır:

- **audeering wav2vec2** (MSP-Podcast) — genel amaçlı, boyutsal (arousal/dominance/valence)
- **SeaBenSea HuBERT** (TurEV-DB) — Türkçe'ye özel, 4 sınıf → VA'ya haritalanır

VA noktaları, taban ağırlık × modelin kendi güveni ile harmanlanır. İkisi çeliştiğinde
birleşik güven düşer. Ağırlıklar `AppConfig.Emotion.GeneralProsodyWeight` /
`TurkishSerWeight` ile ayarlanır (varsayılan 0.5 / 0.5, **kalibre edilmemiş**).

Kapatmak için: `EmotionConfig.UseTurkishSer = false`.

## Performans

CPU'da gerçek-zaman katsayısı (RTF) ~**4×** — 45 saniyelik kayıt ~3 dakika sürer.
İlk chunk en yavaşıdır (modeller belleğe yüklenir). Bu makinede CUDA mevcut ancak
ONNX Runtime CPU sağlayıcısı kullanılıyor; hızlandırma için `Microsoft.ML.OnnxRuntime.DirectML`
veya `.Gpu` paketine geçilebilir (henüz yapılmadı).

## Sorun giderme

**"You must install or update .NET"** — `Directory.Build.props` silinmiş olabilir; ya onu geri
koyun ya da `$env:DOTNET_ROLL_FORWARD="Major"` verin.

**"ffmpeg bulunamadı"** — `winget install Gyan.FFmpeg`, sonra terminali yeniden açın.
Alternatif: dosyayı elle 16 kHz mono WAV'a çevirin.

**"Konuşma içeren hiçbir bölüm bulunamadı"** — VAD tüm chunk'ları sessiz saydı.
`--vad-esik 0.3` ile eşiği düşürün.

**Metin kanalı "ağırlık 0" gösteriyor** — `sa stt <dosya>` ile bileşenlere bakın.
`logprob 0.000` görüyorsanız Whisper olasılıkları gelmiyordur
(`WhisperSpeechToText` içinde `WithProbabilities()` çağrısı duruyor mu?).

## Orijinal proje

Değişiklik yapılmamış kopya: `..\sentiment-analysis-ORIJINAL-YEDEK\`
Karşılaştırma için: `robocopy` veya bir diff aracıyla iki klasörü karşılaştırın.
