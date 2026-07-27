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
