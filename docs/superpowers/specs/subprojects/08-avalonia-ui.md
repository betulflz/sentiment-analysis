# Alt-Proje 08 — Avalonia UI

> Bağımlılık: 01, 06, 07 · İnşa sırası: **8** · Katman: `Desktop`
> **Durum:** Brainstorm ile somutlaştırıldı (2026-07-25). Bu tur **test edilebilir MVVM çekirdek +
> minimal shell**; zengin görselleştirme (8-duygu çarkı, trend grafiği), dosya sürükle-bırak, cilalı
> stil → **08b'ye ertelenir**. **Dürüstlük notu:** ViewModel katmanı TDD ile headless test edilir;
> Avalonia View'ların görsel QA'sı kullanıcıdadır (bu ortamda render doğrulanamaz).

## Hedef
Cross-platform tepsi agent'ı + minimal pencere: canlı duygu göstergesi, rıza akışı, toplantı geçmişi
ve raporları. **Bu tur:** test edilebilir sunum mantığı (ViewModels) + fonksiyonel minimal UI + tepsi.

## Mimari (MVVM, üç proje — test edilebilirlik için)
- **`SentimentAnalysis.Desktop`** (kütüphane, Avalonia'sız): ViewModel'ler + `EmotionLocalizer`.
  Bağımlılıklar: `CommunityToolkit.Mvvm` (INotifyPropertyChanged/RelayCommand — UI-framework-agnostik,
  headless test edilebilir) + `SentimentAnalysis.Application`. **UI hiçbir ML/IO'ya doğrudan bağlanmaz**
  (master kararı) — yalnızca Application soyutlamaları (`EmotionPipeline`, `ISessionStore`,
  `IMeetingAggregator`, `AppConfig`).
- **`SentimentAnalysis.Desktop.Avalonia`** (app): App.axaml + View'lar (.axaml) + tepsi ikonu + DI
  composition root (tüm `AddXxx()` uzantıları). `Desktop` + Infrastructure dilimleri + Avalonia'ya bağlı.
  Unit-testli DEĞİL (görsel).
- **`SentimentAnalysis.Desktop.Tests`**: ViewModel + localizer testleri (Avalonia bağımlılığı YOK).

## Kapsam (bu tur)
- **ViewModel katmanı (test edilebilir çekirdek):**
  - `EmotionLocalizer` (saf): `Emotion`/`Confidence`/`IncongruencePattern` enum → Türkçe metin.
  - `LiveViewModel`: `IProgress<ChunkResult>` ile canlı çizelge (`ObservableCollection<ChunkRowVm>`) +
    güncel VA + son baskın duygu (lokalize) + congruence/uyumsuzluk rozeti + `IsCapturing`. `StartCommand`/
    `StopCommand` → `EmotionPipeline.RunAsync` (kaynak enjekte; canlı sistem-yakalama 05b'ye bağlı → bu tur
    dosya-kaynak). UI-thread marshalling `IProgress` ile.
  - `ReportViewModel`: bir `MeetingSession` → `IMeetingAggregator.Aggregate` → `MeetingSummary` alanlarını
    (baskın duygu, %süre dağılımı, valence trend değeri, oynaklık, tepe anlar, ortalama congruence, Türkçe özet)
    lokalize sunar.
  - `HistoryViewModel`: `ISessionStore.ListSessionsAsync` → oturum listesi (özet satırları).
  - `SettingsViewModel`: retention gün, ses-tutma, STT profil (turbo/derin), dil; mevcut `AppConfig`'ten
    okur, `with { }` ile yeni `AppConfig` üretir (kaydetme delege/callback ile).
  - `MainViewModel`: alt-VM'leri barındırır + basit navigasyon durumu.
- **Minimal Avalonia shell:** App + MainWindow, fonksiyonel View'lar (VM'lere bağlı), **tepsi ikonu
  start/stop + "dinleniyor" görünür göstergesi** (rıza). Derlenir + macOS'ta çalışır. frontend-design
  ilkeleriyle sade ama kasıtlı (şablonvari değil).
- **Lokalizasyon:** duygu/güven/uyumsuzluk etiketleri UI'da Türkçe (`EmotionLocalizer`).

## Kapsam DIŞI (bu tur, bilinçli — 08b)
- 8-duygu çarkı çizimi + valence trend grafiği (özel çizim/lib); dosya sürükle-bırak analizi; cilalı
  görsel stil/tema; canlı sistem-sesi yakalama akışı (05b — bu tur dosya-kaynakla LiveViewModel test edilir);
  Windows tepsi doğrulaması (kod cross-platform, görsel QA Windows'ta ayrıca).

## Kararlar
- MVVM; UI → yalnızca `Application` servisleri (ML/IO'ya doğrudan erişim yok).
- `CommunityToolkit.Mvvm` (`ObservableObject`/`RelayCommand`) — ViewModel'ler UI-framework-agnostik → headless test.
- ViewModel task'ları TDD (fake `EmotionPipeline`/`ISessionStore`/`IMeetingAggregator` + `IProgress` besleme);
  View/shell fonksiyonel, görsel QA kullanıcıda.

## Kabul Kriteri (bu tur)
- `EmotionLocalizer` 8 duygu + güven + uyumsuzluk örüntülerini Türkçe'ye çevirir (unit).
- `LiveViewModel` `IProgress<ChunkResult>.Report` çağrılarını çizelgeye + güncel VA/duygu/rozete yansıtır;
  Start/Stop `IsCapturing`'i doğru yönetir (fake pipeline ile unit).
- `ReportViewModel` kayıtlı bir oturumdan rapor alanlarını (baskın duygu, %süre, trend, tepe anlar, özet) üretir.
- `HistoryViewModel` `ISessionStore`'dan oturumları listeler.
- `SettingsViewModel` ayar değişimini `AppConfig`'e (with) doğru yansıtır.
- App macOS'ta tepsiden çalışır + capture aktifken gösterge görünür (**görsel QA**).
- Tüm ViewModel testleri headless yeşil (Avalonia render gerektirmez).

## Kabul Kriteri (08b — ertelenmiş)
- 8-duygu çarkı + valence trend grafiği canlı yansır; dosya sürükle-bırak analizi; Windows tepsi doğrulaması.
