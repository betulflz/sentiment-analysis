# Alt-Proje 06 — Pipeline Orkestrasyonu

> Bağımlılık: 01–05 · İnşa sırası: **6** · Katman: `Application`
> **Durum:** Brainstorm ile somutlaştırıldı (2026-07-25). Bu tur **çekirdek pipeline +
> backpressure**; ileri özellikler (cümle-hizalama, degradasyon heuristiği, derin geçiş)
> **06b'ye ertelenir**. Yeni kütüphane/model yok → spike gerekmez (`System.Threading.Channels`
> standart; tüm bileşenler 02–05'te mevcut). Kalıcılık: in-memory store (gerçek SQLite → 07).

## Hedef
Yakalama → chunk → VAD → (STT + prozodi paralel) → füzyon → zaman çizelgesi + kalıcılık
akışını backpressure ile yönetmek. Tüm ağır bileşenler mevcut; **yeni olan orkestrasyon**.

## Kapsam (bu tur)
- **Bounded** producer/consumer hattı (`System.Threading.Channels`, `FullMode=DropOldest`).
- **Per-chunk DAG:** VAD kapısı → (prozodi ∥ STT→metin-duygu) → QualitySignals → füzyon → `ChunkResult`.
- STT ve prozodi **paralel** (`Task.WhenAll`, ikisi de aynı chunk'ı alır); metin-duygu STT'den sonra
  (transcript'e bağlı). Chunk'lar **tek consumer**'da sırayla → sonuçlar sıralı + zaman-damgalı.
- **`QualitySignalsBuilder` (saf, YENİ):** chunk(ses)+transcript → `QualitySignals` (04a'nın 06'ya
  bıraktığı iş). SNR/konuşma-oranı basit ses-enerji tabanlı (kalibrasyon 09); süre chunk'tan;
  kelime sayısı + avg-logprob transcript'ten.
- **Backpressure:** bounded kanal geri-basınç verir; taşarsa en eski chunk düşer (canlı akıcılık korunur).
- **Timeline:** pipeline `ChunkResult`'ları bellekte biriktirir; canlı UI için `IProgress<ChunkResult>`
  callback'i (08 dinler) + snapshot `IReadOnlyList<ChunkResult>`.
- **Kalıcılık:** enjekte edilen `ISessionStore`'a yazılır; bu tur `InMemorySessionStore` sağlanır.
- **Toplantı sonu:** capture akışı bitince/iptal → `MeetingSession` → `IMeetingAggregator.Aggregate` → `MeetingSummary`.
- **Hata yönetimi:** chunk'ta model hatası → logla + o chunk'ı atla (pipeline çökmez); `CancellationToken` akar.

## Kapsam DIŞI (bu tur, bilinçli — spec ile uyumlu, 06b/sonra)
- Cümle-sınırı hizalama (chunker'a); turbo→chunk-birleştirme degradasyon heuristiği; yapay-yavaşlatma
  senaryoları; toplantı-sonu large-v3 **derin geçiş** (yeniden analiz); ArrayPool ses-tampon optimizasyonu.
- Gerçek `ISessionStore` (SQLite/dosya, retention/purge) → **Alt-Proje 07**.

## Bileşenler (hepsi `Application`)
- `QualitySignalsBuilder` (saf) → `QualitySignals`.
- `ChunkProcessor` → per-chunk DAG (VAD, paralel STT/prozodi, metin, quality, füzyon → `ChunkResult`).
  Tüm bağımlılıklar arayüz (`IVadGate`, `ISpeechToText`, `IProsodyEmotionModel`, `ITextEmotionModel`,
  `IEmotionFusion`) → fake'lerle tam test.
- `EmotionPipeline` → bounded `Channel<AudioChunk>` orkestrasyonu; `RunAsync(IAudioCaptureSource, CaptureOptions,
  sessionId, IProgress<ChunkResult>?, ct)`; consumer chunk'ları `ChunkProcessor`'dan geçirir, timeline'a +
  `ISessionStore`'a yazar; sonda `IMeetingAggregator` ile `MeetingSummary` döndürür.
- `InMemorySessionStore : ISessionStore` → oturum chunk'larını bellekte biriktirir; `PurgeExpiredAudioAsync`
  no-op (gerçek retention 07).
- `PipelineConfig` (AppConfig'e additive) → kanal kapasitesi, (ileride) worker sayısı.

## Mevcut tipler (tüketilir, değişmez)
```csharp
public sealed record ChunkResult(TimeSpan TimeStart, TimeSpan TimeEnd, FusedResult Fused, string Transcript);
public sealed record MeetingSession(Guid Id, DateTimeOffset StartedAt, DetectedLanguage Language, IReadOnlyList<ChunkResult> Chunks);
public sealed record QualitySignals(double AudioSnr, double SpeechRatio, TimeSpan Duration, int TranscriptWordCount, double TranscriptAvgLogProb);
public interface IEmotionFusion { FusedResult Fuse(TextEmotion text, ProsodyEmotion prosody, QualitySignals quality); }
public interface IMeetingAggregator { MeetingSummary Aggregate(MeetingSession session); }
public interface ISessionStore { Task SaveChunkAsync(Guid, ChunkResult, CancellationToken); Task<MeetingSession?> GetSessionAsync(Guid, CancellationToken); Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken); Task PurgeExpiredAudioAsync(CancellationToken); }
```

## Kabul Kriteri (bu tur)
- Uzun sentetik akışta (çok sayıda chunk) bounded kanal bellek sabit tutar; consumer geride kalırsa
  en eski chunk düşer (drop sayısı gözlemlenebilir); pipeline çökmez.
- Chunk sonuçları **sırayla ve zaman-damgalı** timeline'a + store'a düşer.
- VAD sessiz chunk'ı eler (timeline'a düşmez).
- Bir chunk'ta bileşen hatası pipeline'ı çökertmez (o chunk atlanır, akış sürer).
- Toplantı sonu `MeetingSummary` üretilir.
- Tüm testler fake bileşenlerle modelsiz yeşil.

## Kabul Kriteri (06b/sonra — ertelenmiş)
- Yapay yavaşlatmada turbo→chunk-birleştirme degradasyonu devreye girer.
- 1 saatlik gerçek akışta GC baskısı düşük (ArrayPool).
- Toplantı-sonu derin geçiş (large-v3) ile yeniden analiz.

## EK — Sözleşme Değişikliği (2026-07-27, Alt-proje 08e düzeltme turu)

Bu spec'in orijinal metni (yukarısı, değiştirilmedi) `EmotionPipeline.RunAsync`'in iptalde
(`CancellationToken`) `OperationCanceledException` fırlattığını varsayıyordu — bu artık DOĞRU
DEĞİL. Kayıt için:

- **Eski sözleşme:** `ct` iptal edildiğinde `RunAsync` `OperationCanceledException` fırlatırdı;
  o ana kadar toplanan zaman çizelgesi çağırana hiç ulaşmazdı.
- **Yeni sözleşme:** `ct` iptali artık bir HATA değil, normal bir bitiştir. `RunAsync` üreticiyi
  (kaynağı) durdurur, kanalda kalan (henüz işlenmemiş) chunk'ları SINIRLI bir bütçe içinde
  (en fazla `MaxDrainedChunksAfterStop`, en fazla `DrainTimeBudget` süre — bkz.
  `EmotionPipeline.cs`) drene edip normal biçimde bir `MeetingSummary` ile döner. Drenaj
  sınırı/bütçesi yüzünden işlenemeyen chunk'lar sessizce kaybolmaz — `DrainSkippedCount` ile
  sayılıp raporlanır.
- **Neden değişti:** Alt-proje 08e (canlı mikrofon) "Durdur" akışını gerçek donanımla ölçtü:
  kullanıcı "Durdur"a bastığında sert bir iptal, o ana kadar yakalanan TÜM sesi (ve üretilen
  chunk'ları) atıyordu — kullanıcı "Durdur, bana bir rapor ver" beklerken boş/yarım bir ekranla
  karşılaşıyordu. Bu, canlı mikrofonun asıl var oluş nedeniyle (§ Hedef) doğrudan çelişiyordu.
- **Korunan:** Chunk-başına hata izolasyonu (bir chunk'ın işleme hatası hattı çökertmez, o chunk
  atlanır) DEĞİŞMEDİ — hem normal akışta hem drenaj sırasında geçerliliğini sürdürür.

Bu not, `Application/Pipeline/` katmanının bu turda (plan'ın Global Constraints'inde "dokunulmaz"
denmesine rağmen) haklı bir sebeple değiştirildiğini gelecekteki okuyucular için kayıt altına alır.
