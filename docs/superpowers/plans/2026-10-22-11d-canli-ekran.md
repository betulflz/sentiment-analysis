# Alt-Proje 11d Uygulama Planı — Canlı Ekran (öneri kartı, profil, kaydırıcılar, önizleme)

> **Agentic worker'lar için:** GEREKLİ ALT-BECERİ: `superpowers:subagent-driven-development` (önerilen)
> veya `superpowers:executing-plans`. Adımlar checkbox (`- [ ]`) sözdizimi kullanır.

**Goal:** Canlı görünüme KDS'yi bağlamak: her analiz penceresinde gerekçeli öneri kartı, K1–K6 gösterge
çubukları, yüz başına çubuklar, profil seçimi, ağırlık kaydırıcıları (anında yeniden sıralama) ve yüz
kutulu video önizlemesi.

**Architecture:** Karar akışı yeni, Avalonia'sız `DecisionPanelViewModel`'dedir (Desktop): her
`ChunkResult` + yüz özetleri → `IndicatorCalculator` → `LiveAdvisor` → kart/çubuklar. `LiveViewModel`
paneli sahiplenir ve `Report`/`Start`'ta besler. Önizleme için `IAnalysisRunner`'a additive
`LatestFrame`/`LatestFaces` eklenir; Avalonia katmanı bir zamanlayıcıyla (5 Hz) VM'deki
`RefreshPreview()`'ü çağırır, kareyi bir dönüştürücüyle `WriteableBitmap`'e çevirir.

**Tech Stack:** .NET 9, Avalonia 11, CommunityToolkit.Mvvm, xUnit 2.9.

**Spec:** [`2026-10-21-canli-gorusme-karar-destek-design.md`](../specs/2026-10-21-canli-gorusme-karar-destek-design.md) §3.4, §6.4.6–8, §6.5

## Global Constraints

- `SentimentAnalysis.Desktop` Avalonia'sız ve headless test edilebilir kalır; Avalonia tipleri (bitmap,
  zamanlayıcı, dispatcher) yalnızca `Desktop.Avalonia`'da.
- Görüntü diske yazılmaz, oturum bitince bellekte tutulmaz (spec §4): önizleme yalnızca runner'ın
  o anki karesini gösterir. Koşu bitince kare `null` olur ve önizleme gizlenir.
- Öneri metni kişi hakkında hüküm vermez (spec §4). Metinler `DecisionText`'ten gelir, yeni Türkçe
  karar metni gerekirse orada üretilir.
- Mevcut arayüzlere eklemeler additive'dir (varsayılan gövdeli arayüz üyeleri, opsiyonel parametreler):
  mevcut sahte runner'lar ve kurucu çağrıları değişmez.
- Ses dosyası / mikrofon modunda (yüz yok) kart ve K1–K6 çubukları yine çalışır; yüz çubukları ve
  önizleme görünmez.
- Başlangıç: 619 geçer / 16 atlanır / 0 başarısız. Her task sonunda tüm suite yeşil, 0 uyarı.
- UI dilimi (Task 3) kullanıcı gerçek `.app`'te görmeden bitmiş sayılmaz (08e dersi).

## Review Focus

1. **Kaydırıcıların hepsi 0'a çekilir:** NaN/çökme yok, motor eşit ağırlığa düşer, yüzdeler "%0"
   yerine eşit pay gösterir — Task 1 testi `AllWeightsZero_StillRecommends`.
2. **Kaydırıcı, henüz pencere gelmeden oynatılır:** yeniden sıralama denenmez, kart "—" kalır — Task 1
   testi `WeightChange_BeforeFirstWindow_NoRecommendation`.
3. **Koşu sırasında profil değiştirilmeye çalışılır:** profil kilitlidir (spec: profil görüşme başında
   seçilir). Kaydırıcılar serbesttir — Task 1 testi `Profile_LockedWhileCapturing`.
4. **İkinci oturum:** önceki oturumun kartı, pencere geçmişi, eğilim (K6) serisi ve histerezis durumu
   sızmaz — Task 1 testi `Start_ResetsHistoryAndAdvisor`.
5. **Kamera/video bitti ya da hiç başlamadı:** önizleme boş kareyle donmuş kalmaz, gizlenir — Task 2
   testi `RefreshPreview_NoFrame_HidesPreview`.

## Dosya Yapısı

| Dosya | Sorumluluk | Task |
|---|---|---|
| `src/SentimentAnalysis.Core/Decision.cs` | + `DecisionWindow` | 1 |
| `src/SentimentAnalysis.Application/Decision/LiveAdvisor.cs` | + `Rerank` (histerezissiz yeniden sıralama) | 1 |
| `src/SentimentAnalysis.Application/Decision/DecisionText.cs` | + `Name(MeetingProfile)` | 1 |
| `src/SentimentAnalysis.Desktop/DecisionPanelViewModel.cs` | Profil, kaydırıcılar, kart, çubuklar, pencere geçmişi | 1 |
| `src/SentimentAnalysis.Desktop/LiveViewModel.cs` | + `Decision` paneli; `Report`/`Start` besler; + önizleme | 1, 2 |
| `src/SentimentAnalysis.Desktop/IAnalysisRunner.cs` | + `LatestFrame`, `LatestFaces` | 2 |
| `src/SentimentAnalysis.Desktop/PreviewPixels.cs` | RGB24 → BGRA (saf) | 2 |
| `src/SentimentAnalysis.Desktop.Avalonia/PipelineAnalysisRunner.cs` | `LatestFrame`/`LatestFaces` ← `_activeVision` | 2 |
| `src/SentimentAnalysis.Desktop.Avalonia/Converters/VideoFrameToBitmapConverter.cs` | `VideoFrame` → `WriteableBitmap` | 3 |
| `src/SentimentAnalysis.Desktop.Avalonia/Views/LiveView.axaml(.cs)` | Önizleme, kart, çubuklar, profil, kaydırıcılar; 5 Hz zamanlayıcı | 3 |
| `src/SentimentAnalysis.Desktop.Avalonia/CompositionRoot.cs` | `LiveViewModel`'e `AppConfig.Decision` geçişi (zaten `config` alıyor; değişiklik gerekmeyebilir) | 3 |

---

### Task 1: Karar paneli (profil, kaydırıcılar, öneri kartı, göstergeler)

**Files:**
- Modify: `src/SentimentAnalysis.Core/Decision.cs`, `src/SentimentAnalysis.Application/Decision/LiveAdvisor.cs`,
  `src/SentimentAnalysis.Application/Decision/DecisionText.cs`, `src/SentimentAnalysis.Desktop/LiveViewModel.cs`
- Create: `src/SentimentAnalysis.Desktop/DecisionPanelViewModel.cs`
- Test: `tests/SentimentAnalysis.Application.Tests/Decision/LiveAdvisorTests.cs`,
  `tests/SentimentAnalysis.Desktop.Tests/DecisionPanelViewModelTests.cs`, `tests/SentimentAnalysis.Desktop.Tests/LiveViewModelTests.cs`

**Interfaces:**
- Consumes (11c/11b, mevcut): `IndicatorCalculator.Compute(ChunkResult, IReadOnlyList<FaceWindowSummary>, IReadOnlyList<double> previousValences, DecisionConfig)`,
  `LiveAdvisor(MeetingProfile, DecisionConfig)`, `LiveAdvisor.Next(Indicators, IReadOnlyList<double>)`, `LiveAdvisor.Reset()`,
  `DecisionKnowledgeBase.DefaultWeights(MeetingProfile)`, `DecisionText.Name(Criterion|LiveAction)`, `DecisionText.Subject(int?)`,
  `EmotionLocalizer.Confidence(Confidence)`, `IAnalysisRunner.FacesFor(ChunkResult)`.
- Produces:
```csharp
// Core/Decision.cs — 11e (rapor/depolama) tüketir
public sealed record DecisionWindow(TimeSpan Start, TimeSpan End, Indicators Indicators, Recommendation Recommendation);

// LiveAdvisor: kullanıcı ağırlığı değiştirdi → histerezis atlanır, sonuç mevcut öneri olur (Reset + Next).
public Recommendation Rerank(Indicators indicators, IReadOnlyList<double> weights);

// DecisionText
public static string Name(MeetingProfile p); // "Satış / müşteri", "Mülakat", "Toplantı"

// Desktop
public sealed partial class DecisionPanelViewModel : ObservableObject
{
    public DecisionPanelViewModel(DecisionConfig cfg);
    public IReadOnlyList<MeetingProfile> Profiles { get; }          // Satis, Mulakat, Toplanti
    public MeetingProfile SelectedProfile { get; set; }             // değişince kaydırıcılar profilin AHP ağırlıklarına döner
    public bool IsLocked { get; set; }                              // koşu sırasında true; SelectedProfile set'i yok sayılır
    public IReadOnlyList<WeightSliderVm> Weights { get; }           // 6 adet, Criterion sırası
    public IRelayCommand ResetWeightsCommand { get; }               // profilin AHP ağırlıklarına dön
    public string ActionText { get; }                                // "—" başlangıçta
    public string ReasonText { get; }
    public string ConfidenceText { get; }                            // "Güven: Orta"
    public string AlternativeText { get; }                           // "Alternatif: Konuyu değiştir" | ""
    public bool IsLowConfidence { get; }
    public IReadOnlyList<IndicatorBarVm> Indicators { get; }         // 6 adet, son pencere
    public ObservableCollection<FaceBarsVm> Faces { get; }           // son penceredeki yüzler
    public IReadOnlyList<DecisionWindow> History { get; }            // 11e için
    public void Start();                                             // geçmiş, eğilim serisi, kart, advisor sıfırlanır; profil advisor'a uygulanır
    public void OnWindow(ChunkResult chunk, IReadOnlyList<FaceWindowSummary> faces);
    public IReadOnlyList<double> CurrentWeights { get; }             // kaydırıcı değerleri (normalize edilmemiş)
}
public sealed partial class WeightSliderVm : ObservableObject     // Name = DecisionText.Name(c) ilk harfi büyük
{ public Criterion Criterion { get; } public string Name { get; } public double Value { get; set; } /* 0..1 */ public string ShareText { get; } /* "%23" normalize pay */ }
public sealed record IndicatorBarVm(string Name, double Value, string Subject); // Subject: "Yüz 2" | "" (genel)
public sealed record FaceBarsVm(string Label, double LookAway, double Negative, double Smile);
// LiveViewModel
public DecisionPanelViewModel Decision { get; }                    // kurucu: config.Decision ile
```

- [ ] **Step 1: Failing tests.**
  - `LiveAdvisorTests.Rerank_SkipsHysteresis`: gergin göstergelerle `Next` → A, sonra ağırlıkları B'yi öne
    geçirecek biçimde değiştirip `Next` → hâlâ A (histerezis); aynı girdiyle `Rerank` → B; ardından aynı
    ağırlıklarla `Next` → B (Rerank'ın sonucu mevcut öneri oldu).
  - `DecisionPanelViewModelTests`:
    - `Initial_ShowsDashAndProfileWeights`: `ActionText == "—"`, `Weights.Select(w=>w.Value)` ≈ `DefaultWeights(Satis)`.
    - `SelectProfile_ResetsSlidersToProfileWeights` (Mulakat).
    - `OnWindow_TenseChunk_ShowsInterventionWithReason`: valence −0,8, arousal 0,9, iyi kalite → `ActionText`
      `"Devam et"` değil, `ReasonText` boş değil, `Indicators[0].Value > 0.35`, `History.Count == 1`.
    - `OnWindow_CalmChunk_ShowsDevamEt`.
    - `OnWindow_WithFaces_FillsFaceBars`: iki `FaceWindowSummary` → `Faces` = `"Yüz 1"`, `"Yüz 2"`; değerler aynen.
    - `WeightChange_ReranksImmediately`: gergin pencereden sonra bir kaydırıcıyı 0'a çekince `ActionText`
      aynı çağrıda yeniden hesaplanır ve `LiveAdvisor.Rerank` ile aynı sonucu verir.
    - `WeightChange_BeforeFirstWindow_NoRecommendation` *(Review Focus 2)*.
    - `AllWeightsZero_StillRecommends` *(Review Focus 1)*: NaN yok; `ShareText` her biri `"%17"`.
    - `Profile_LockedWhileCapturing` *(Review Focus 3)*: `IsLocked = true` iken `SelectedProfile` değişmez.
    - `Start_ResetsHistoryAndAdvisor` *(Review Focus 4)*: iki pencere sonra `Start()` → `History` boş, `ActionText == "—"`,
      ilk yeni pencerede K6 = 0 (önceki valence serisi taşınmadı).
  - `LiveViewModelTests.Report_FeedsDecisionPanel`: `FaceRunner` ile `Report` → `vm.Decision.History.Count == 1`, `Faces.Count == 1`.
  - `LiveViewModelTests.Start_LocksProfileAndResetsPanel`: koşu sırasında `Decision.IsLocked`, bitince `false`.
- [ ] **Step 2:** Çalıştır: `dotnet test tests/SentimentAnalysis.Desktop.Tests tests/SentimentAnalysis.Application.Tests --filter "DecisionPanel|Rerank|FeedsDecisionPanel|LocksProfile"`. Expected: derleme hatası (tipler yok).
- [ ] **Step 3:** Uygula. Eğilim serisi: `OnWindow` her pencerede `chunk.Fused.Combined.Valence`'ı listeye ekler;
  `IndicatorCalculator`'a o ana kadarki seri (şimdiki hariç) verilir. Kart `Recommendation`'dan: `ActionText =
  DecisionText.Name(r.Action)`, `AlternativeText` = `Ranking`'te önerilen eylemden farklı ilk seçenek (histerezis yüzünden önerilen eylem her zaman `Ranking[0]` değildir); yoksa boş.
  `ShareText` = değer / Σ (Σ=0 ise 1/6), yüzde tam sayı.
- [ ] **Step 4:** Aynı komut → PASS; ardından `dotnet test` → tümü yeşil, `dotnet build` 0 uyarı.
- [ ] **Step 5:** Commit `feat(11d): karar paneli — öneri kartı, profil ve ağırlık kaydırıcıları`.

### Task 2: Önizleme verisi (son kare + yüz kutuları)

**Files:**
- Modify: `src/SentimentAnalysis.Desktop/IAnalysisRunner.cs`, `src/SentimentAnalysis.Desktop/LiveViewModel.cs`,
  `src/SentimentAnalysis.Desktop.Avalonia/PipelineAnalysisRunner.cs`
- Create: `src/SentimentAnalysis.Desktop/PreviewPixels.cs`
- Test: `tests/SentimentAnalysis.Desktop.Tests/PreviewTests.cs`

**Interfaces:**
- Consumes: `VisionAnalyzer.LatestFrame` (`VideoFrame?`), `VisionAnalyzer.LatestFaces` (`IReadOnlyList<(int Index, FaceBox Box)>`).
- Produces:
```csharp
// IAnalysisRunner — additive
VideoFrame? LatestFrame => null;
IReadOnlyList<(int Index, FaceBox Box)> LatestFaces => Array.Empty<(int, FaceBox)>();

public static class PreviewPixels { public static byte[] ToBgra(VideoFrame frame); } // A = 255

public sealed record FaceBoxVm(double X, double Y, double W, double H, string Label); // kare pikseli
// LiveViewModel
public VideoFrame? PreviewFrame { get; }      // observable
public bool HasPreview { get; }               // PreviewFrame != null
public ObservableCollection<FaceBoxVm> PreviewFaces { get; }
public void RefreshPreview();                 // runner'dan okur; kare aynı nesneyse hiçbir şey değişmez
```

- [ ] **Step 1: Failing tests:** `ToBgra_SwapsChannels` (RGB (10,20,30) → B,G,R,A = 30,20,10,255; uzunluk w·h·4);
  `RefreshPreview_ShowsFrameAndLabels` (sahte runner bir kare + `(1, kutu)` döner → `HasPreview`, `PreviewFaces[0].Label == "Yüz 2"`);
  `RefreshPreview_NoFrame_HidesPreview` *(Review Focus 5)* (önce kare, sonra `null` → `HasPreview == false`, `PreviewFaces` boş);
  `RefreshPreview_SameFrame_NoPropertyChange` (aynı kare nesnesiyle ikinci çağrıda `PropertyChanged` tetiklenmez).
- [ ] **Step 2:** `dotnet test tests/SentimentAnalysis.Desktop.Tests --filter Preview` → derleme hatası.
- [ ] **Step 3:** Uygula. `PipelineAnalysisRunner`: `LatestFrame => _activeVision?.LatestFrame`, `LatestFaces` aynı biçimde (app katmanı, testsiz).
- [ ] **Step 4:** PASS; tüm suite yeşil, 0 uyarı.
- [ ] **Step 5:** Commit `feat(11d): önizleme verisi — son kare ve yüz kutuları`.

### Task 3: Canlı ekran arayüzü + kullanıcı doğrulaması

**Files:**
- Create: `src/SentimentAnalysis.Desktop.Avalonia/Converters/VideoFrameToBitmapConverter.cs`
- Modify: `src/SentimentAnalysis.Desktop.Avalonia/Views/LiveView.axaml`, `LiveView.axaml.cs`

Davranış (app katmanı, testsiz — 08e notu):
- **Üst şerit:** profil `ComboBox` (`Decision.Profiles`, `DecisionText.Name` ile gösterilir; `IsEnabled = !Decision.IsLocked`) mevcut kaynak anahtarlarının yanında.
- **Önizleme** (`IsVisible = HasPreview`): `Viewbox` içinde 640×360 `Grid`: `Image` (Source = `PreviewFrame` + dönüştürücü) ve
  üstünde `ItemsControl`/`Canvas` ile `PreviewFaces` kutuları (ince kenarlık + sol üstte etiket). `Canvas.Left/Top` kare pikselidir; `Viewbox` ölçekler.
- **Öneri kartı:** büyük `ActionText`, altında `ReasonText`, `ConfidenceText`, `AlternativeText`; `IsLowConfidence` iken kart soluk/uyarı rengi.
- **Göstergeler:** `Decision.Indicators` için ad + `ProgressBar` (0..1) + `Subject`; `Decision.Faces` için yüz başına üç küçük çubuk
  (dönük, olumsuz ifade, gülümseme).
- **Kaydırıcılar:** `Decision.Weights` için ad + `Slider` (0..1) + `ShareText`; "Varsayılana dön" düğmesi (`ResetWeightsCommand`). Koşu sırasında da açık.
- **Zamanlayıcı:** `LiveView` görsel ağaca eklenince `DispatcherTimer` (200 ms) `LiveViewModel.RefreshPreview()` çağırır; ağaçtan çıkınca durur.
- Dönüştürücü: `PreviewPixels.ToBgra` → `WriteableBitmap(PixelSize(w,h), Vector(96,96), PixelFormat.Bgra8888, AlphaFormat.Opaque)`; `null` → `null`.

- [ ] **Step 1:** Uygula; `dotnet build` 0 uyarı, `dotnet test` tümü yeşil.
- [ ] **Step 2: Canlı doğrulama — KULLANICI KAPISI.** `./build-macos-app.sh` → `open`. Kullanıcı görür ve onaylar:
  (a) `iki-kisi.mp4` (Mülakat profili): önizleme gerçek hızda akıyor, iki yüzde "Yüz 1/Yüz 2" kutuları; her pencerede kart ve çubuklar güncelleniyor;
  (b) bir kaydırıcıyı çekince kart aynı anda değişebiliyor (ör. "Gerilim" 0'a); "Varsayılana dön" çalışıyor;
  (c) koşu sırasında profil kutusu kilitli; Durdur sonrası önizleme kayboluyor, rapor açılıyor;
  (d) kamera modunda önizleme kendi yüzünü gösteriyor.
  Onay gelmeden commit edilmez.
- [ ] **Step 3:** Commit `feat(11d): canlı ekran — önizleme, öneri kartı, göstergeler ve kaydırıcılar`.
