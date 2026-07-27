# Alt-Proje 05 — Ses Yakalama + VAD

> Bağımlılık: 01 · İnşa sırası: **5** · Katman: `Infrastructure/Audio`, `Infrastructure/Vad`
> **Durum:** Brainstorm + Silero VAD spike ile somutlaştırıldı (2026-07-25). Bu tur
> **test edilebilir çekirdek** (dosya→chunk hattı + Silero VAD kapısı) inşa edilir;
> **native canlı yakalama** (ScreenCaptureKit/WASAPI) `IAudioCaptureSource` arkasında
> **yalnızca kontrat** olarak kalır, implementasyon 05b/sonraya ertelenir (04'te sidecar
> gibi). Risk azaltma: 04a/04b felsefesi — native tipler sınırda, saf çekirdek unit-testli.

## Hedef
Canlı sistem-sesini (Teams) ve dosya girdisini tek arayüz ardında yakalamak; sessizliği
eleyen VAD kapısı. **Bu tur:** dosya girdisi + VAD; canlı yakalama kontratı.

## Sözleşmeler (Alt-Proje 01'de YERLEŞİK — değişmez)
```csharp
// AudioFrame DEĞİL AudioChunk: 01'de yerleşmiş Core tipi; 03/04 bunu tüketiyor (karar 2026-07-25).
public interface IAudioCaptureSource {
    IAsyncEnumerable<AudioChunk> CaptureAsync(CaptureOptions options, CancellationToken ct);
}
public interface IVadGate { bool HasSpeech(AudioChunk chunk); }
public sealed record CaptureOptions(TimeSpan ChunkLength, TimeSpan Overlap);
public sealed record AudioChunk(float[] Samples, int SampleRate, TimeSpan Start, TimeSpan End); // 16 kHz mono float
```

## Kapsam (bu tur)
- **Dosya girdisi:** `FileCaptureSource : IAudioCaptureSource`. **WAV native** (PCM16/PCM32/
  IEEE-float32; mono/stereo). mp3/m4a **ertelenir** → aynı arayüz ardında `NotSupportedException`
  + aksiyon-alınabilir mesaj (cross-platform decode bağımlılığı sonra eklenir).
- **16 kHz mono float PCM'e normalize:** stereo→mono downmix + lineer resample (saf `AudioPreprocessor`).
- **Ring buffer + chunker:** `AudioChunker` (ChunkLength 10–30 sn, örtüşmeli Overlap; saf, deterministik).
- **Silero VAD** (~2.3 MB ONNX) kapısı: sessiz chunk STT'ye hiç girmez (06 pipeline kullanır).
- **Rıza göstergesi:** capture-aktif durumu YAGNI → minimal (`FileCaptureSource.IsCapturing` benzeri);
  zengin status/rıza yüzeyi UI'a (08) ve pipeline'a (06) ertelenir.

## Kapsam DIŞI (bu tur, bilinçli — dokümante)
- **Native canlı yakalama:** macOS ScreenCaptureKit + Swift yardımcı süreç (PCM'i stdout/named
  pipe ile .NET'e akıtır; Teams-only kapsama D12); Windows WASAPI loopback (NAudio). `IAudioCaptureSource`
  kontratı hazır → `LiveCaptureSource` 05b'de. macOS'ta önce BlackHole ile doğrulanabilir (aynı arayüz).
- **mp3/m4a decode** (cross-platform decoder spike'ı gerektirir).
- Native yardımcı süreç çökme/kaynak-sızıntı yönetimi → 05b (kontrat kabul kriteri olarak kalır).

## Karar: Silero VAD kontratı (SPIKE İLE DOĞRULANDI 2026-07-25, .NET ONNX Runtime)
`silero_vad.onnx` v5 (~2.3 MB) gerçek .NET ORT ile ölçüldü:
```
INPUTS   input  float32 [batch, samples]   → [1, 512]  (16 kHz: 512-örnek/32ms pencere; 8 kHz: 256)
         state  float32 [2, batch, 128]     → [2, 1, 128], sıfır ile başla (LSTM state)
         sr     int64   scalar              → 16000
OUTPUTS  output float32 [batch, 1]          → konuşma olasılığı 0..1
         stateN float32 [2, 1, 128]         → SONRAKİ çağrının `state`'i (zincirlenir)
```
- **Ölçülen davranış:** sessizlik prob≈0.0006, beyaz gürültü≈0.002, saf 300Hz ton≈0.0005 —
  hepsi düşük (doğru; hiçbiri insan sesi değil). State çağrılar arası doğru zincirlendi.
- **`HasSpeech(AudioChunk)` reçetesi:** chunk (16 kHz mono) → `VadWindower` ile ardışık 512-örnek
  pencereler (tail < 512 ise **sıfır-pad**); her pencere Silero'dan geçer, `state` chunk içinde
  zincirlenir (**her `HasSpeech` çağrısında sıfırdan** — chunk'lar bağımsız yargılanır); **max
  pencere prob ≥ eşik (varsayılan 0.5)** ise konuşma var. Eşik `VadConfig` (additive), eval-09 kalibre eder.
- **Model kaynağı:** `snakers4/silero-vad` reposu `src/silero_vad/data/silero_vad.onnx`. Session-özel;
  entegrasyon testi `VAD_MODEL_PATH` env yoksa `Xunit.SkippableFact`/`Skip.If` ile atlanır (04 örüntüsü).

## Önerilen mimari (04a/04b'yi izler)
- **`Infrastructure.Audio`** (ONNX'siz): saf `WavDecoder`, `AudioPreprocessor` (mono downmix +
  16k lineer resample), `AudioChunker` (örtüşmeli); `FileCaptureSource : IAudioCaptureSource`
  (WAV decode→preprocess→chunk→`IAsyncEnumerable`); `AddAudioCapture()` DI.
- **`Infrastructure.Vad`** (ONNX): saf `VadWindower`; `SileroVadGate : IVadGate, IDisposable`
  (singleton InferenceSession, sıcak; init/run kilitleri + `ObjectDisposedException` guard —
  emotion motorlarının dispose disiplini); `VadConfig` (model dosyası + eşik, AppConfig'e additive);
  model sağlayıcı 04a `IEmotionModelProvider` gibi bir dosya-çözücü (veya ortak arayüz);
  `AddSileroVad()` DI.
- Native tipler (InferenceSession) motor sınırında; `WavDecoder`/`AudioPreprocessor`/`AudioChunker`/
  `VadWindower` saf + tam unit-testli (modelsiz).

## Kabul Kriteri (bu tur)
- WAV dosyası (herhangi rate/kanal) → 16 kHz mono AudioChunk akışı (doğru chunk uzunluğu + örtüşme).
- `SileroVadGate` sessizlik chunk'ında `HasSpeech == false`; gerçek-konuşma WAV'ında `true`
  (env-gated: `VAD_MODEL_PATH` + `VAD_SAMPLE_SPEECH_WAV`).
- mp3/m4a `FileCaptureSource`'ta net `NotSupportedException` (ertelenmiş — kontrat sağlam).
- ONNX session dispose'ta kaynak sızıntısı yok (kilitli dispose).
- Saf çekirdek modelsiz `dotnet test`'te yeşil; gerçek-model testi env yoksa Skip.

## Kabul Kriteri (05b — ertelenmiş, kontrat olarak korunur)
- macOS'ta çalan sistem sesi yakalanır ve VAD sessizliği doğru eler.
- Windows'ta WASAPI loopback ile aynı `AudioChunk` akışı üretilir.
- Native yardımcı süreç çökerse .NET tarafı nazikçe durur (kaynak sızıntısı yok).
