# 11e Görüşme Sonrası Rapor — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rapor görünümüne karar destek bölümlerini eklemek (gösterge zaman çizelgesi, verilen öneriler, kritik anlar, konuşma dengesi, ayarlanabilir takip kararı + duyarlılık tablosu) ve oturumun karar verisini SQLite'a yazmak.

**Architecture:** Koşu bitince (`LiveViewModel.Completed`) `DecisionPanelViewModel`'in geçmişi bir **anlık görüntü** (`DecisionSession`) olarak kopyalanır; rapor bu kopyayla çalışır (yeni koşu başlarsa rapor bozulmaz) ve aynı kopya `IDecisionStore` ile saklanır. Göstergeler **yeniden hesaplanmaz** (SpeechVa DB'de yok; yüz çift sayılırdı) — pencere başına saklanır. Raporu türeten mantık saf Application koduna (`DecisionReport`, `TalkBalance`) konur; Desktop VM'leri yalnızca sunar.

**Tech Stack:** .NET, Avalonia, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-21-canli-gorusme-karar-destek-design.md` (§6.6, §6.7; §7 dilim 4–5). Ayrıca 11c/11d planları (`plans/2026-10-21-11c-kds-motoru.md`, `plans/2026-10-22-11d-canli-ekran.md`).

## Kararlar (plan onayında itiraz edilebilir)

- **Rapor veri kaynağı bellekteki anlık görüntüdür**, DB değil. Geçmiş görünümü bugün de raporu yeniden açmıyor; DB yazımı kalıcılık + ileride kullanım içindir (round-trip testiyle doğrulanır).
- **Takip kararı koşu sonunda bir kez saklanır** (görüşme sonu ağırlıklarla). Raporda kaydırıcı oynatmak "ya şöyle olsaydı"dır, DB'ye yazılmaz (sürükleme başına yazım yok).
- **Kritik an** = `ihtiyaç (değer × veri kalitesi)` en yüksek kriterde `≥ InterventionThreshold (0,35)` olan ardışık pencerelerin birleşimi (bölüm). Tepe değere göre en fazla 5 bölüm, zaman sırasıyla gösterilir.
- **Verilen öneriler** = yalnızca öneri DEĞİŞTİĞİ pencereler (ilk pencere dahil) — her pencereyi listelemek gürültü.
- **Konuşma dengesi** konuşmacı bölgelerinin süresinden hesaplanır; `Bilinmeyen` paya girmez.
- AHP tutarlılık oranı (CR) takip panelinde gösterilir (spec §6.4.4 "raporda gösterilir").

## Global Constraints

- Sistem kişi hakkında hüküm vermez; metinler gözlem söyler (spec §4). Mülakat takip seçenekleri süreç adımlarıdır.
- Görüntü/kare saklanmaz; yalnızca türetilmiş göstergeler (spec §4, §6.7).
- Yeni tablolar `CREATE TABLE IF NOT EXISTS` ile — mevcut idempotent şema deseni (`StorageSchema.TablesAddedIn10b` gibi bir `TablesAddedIn11e` listesi).
- Duyarlılık: `Sensitivity.Deltas = {-0.2, -0.1, 0.1, 0.2}` (11c'de var, değişmez).
- Takip seçenekleri ve adları `DecisionKnowledgeBase` / `DecisionText`'ten gelir; yeni metin uydurulmaz.
- `Desktop` projesi Avalonia'sız kalır (headless test); UI güncellemesi `MainViewModel`'in `_dispatch`'i ile.
- `README.md`'deki commit edilmemiş kullanıcı değişikliğine dokunulmaz; `git add` her zaman dosya adlarıyla yapılır (asla `-A`/`.`).
- Her task sonunda `dotnet build && dotnet test` yeşil → **DUR, kullanıcı onayı** → commit. UI task'ında (Task 5) commit ancak kullanıcı gerçek .app'te doğruladıktan sonra.

## Review Focus

1. **Rapor açıkken yeni koşu başlatmak** (`Decision.Start()` geçmişi temizler) → rapor ve takip paneli eski görüşmeyi göstermeye devam etmeli. Test: Task 4.
2. **Yalnız-ses koşusu** (yüz yok, tüm `SourceFace` null, `Faces` boş) → depolama null'ları korur, rapor "Görüşme geneli" der. Test: Task 1, Task 2.
3. **Aynı oturumun iki kez kaydedilmesi** → satırlar çoğalmaz (önce sil, sonra yaz). Test: Task 2.
4. **Takip panelinde tüm kaydırıcılar 0** → motor eşit ağırlığa döner, çökme yok, duyarlılık "aynı" der. Test: Task 3.
5. **Kayıt hatası** (DB kilitli/bozuk) → uygulama çökmez, raporda kısa bir uyarı satırı görünür. Test: Task 4.

---

### Task 1: Saf rapor mantığı — kritik anlar, öneri değişimleri, konuşma dengesi

**Files:**
- Create: `src/SentimentAnalysis.Application/Decision/DecisionReport.cs`
- Create: `src/SentimentAnalysis.Application/Diarization/TalkBalance.cs`
- Modify: `src/SentimentAnalysis.Core/Decision.cs` (yeni record'lar)
- Test: `tests/SentimentAnalysis.Application.Tests/Decision/DecisionReportTests.cs`, `tests/SentimentAnalysis.Application.Tests/Diarization/TalkBalanceTests.cs`

**Interfaces:**
- Produces (Core/Decision.cs):
  - `public sealed record CriticalMoment(TimeSpan Start, TimeSpan End, Criterion Criterion, double Peak, int? Face, LiveAction Shown);` — `Peak` ihtiyaç değeridir (değer × kalite); `Face` = tepe penceresinde o kriterin `SourceFace`'i; `Shown` = tepe penceresinde gösterilen öneri.
  - `public sealed record RecommendationChange(TimeSpan At, LiveAction Action, string Reason, Confidence Confidence);`
  - `DecisionWindow`'a additive üye: `public IReadOnlyList<FaceWindowSummary> Faces { get; init; } = Array.Empty<FaceWindowSummary>();`
  - `public sealed record DecisionSession(MeetingProfile Profile, IReadOnlyList<double> ProfileWeights, IReadOnlyList<double> FinalWeights, IReadOnlyList<DecisionWindow> Windows);`
- Produces (Application):
  - `DecisionReport.CriticalMoments(IReadOnlyList<DecisionWindow> windows, DecisionConfig cfg, int max = 5) -> IReadOnlyList<CriticalMoment>`
  - `DecisionReport.Changes(IReadOnlyList<DecisionWindow> windows) -> IReadOnlyList<RecommendationChange>`
  - `public sealed record TalkShare(SpeakerId Speaker, TimeSpan Duration, double Share);`
  - `TalkBalance.Compute(IReadOnlyList<SpeakerSegment> segments) -> IReadOnlyList<TalkShare>`

- [ ] **Step 1: Failing testleri yaz** (`DecisionReportTests`, test yardımcısı `W(double startSec, double[] values, int?[] faces, double quality, LiveAction shown)` 10 sn'lik pencere üretir):
  - `CriticalMoments_ArdisikEsikUstuPencereler_TekBolumOlur`: değerler (kalite 1) K1 = 0,1 / 0,5 / 0,7 / 0,4 / 0,1 → tek bölüm, `Start=10s`, `End=40s`, `Criterion=Gerilim`, `Peak=0,7`, `Shown` = 3. pencerenin eylemi.
  - `CriticalMoments_IhtiyacKaliteyleCarpilir`: K1 = 0,5, kalite 0,5 → ihtiyaç 0,25 < 0,35 → boş liste.
  - `CriticalMoments_EnFazlaBes_TepeyeGoreSecilir_ZamanSirasiylaDoner`: 7 ayrık bölüm (aralarında sakin pencere), tepeler 0,4..1,0 → 5 bölüm döner, en düşük iki tepe (0,4, 0,5) yok, `Start`'a göre artan.
  - `CriticalMoments_YuzsuzPencere_FaceNull`: `SourceFace` hepsi null → `Face == null`.
  - `Changes_IlkPencereVeDegisimler`: eylemler DevamEt, DevamEt, EmpatiKur, EmpatiKur, DevamEt → 3 kayıt, `At` = 0, 20, 40 sn.
  - `Changes_BosGecmis_BosListe` ve `CriticalMoments_BosGecmis_BosListe`.
  - `TalkBalanceTests`: A 0–30 sn + 60–90 sn, B 30–60 sn, Bilinmeyen 90–100 sn → `[A 60 sn %66,7; B 30 sn %33,3]` (süreye göre azalan, Bilinmeyen yok); `Compute_SifirVeNegatifSureliBolge_YokSayilir`; `Compute_Bos_BosListe`; `Compute_YalnizcaBilinmeyen_BosListe`.

- [ ] **Step 2: Çalıştır, derleme hatasıyla düştüğünü gör**
  Run: `dotnet test tests/SentimentAnalysis.Application.Tests --filter "FullyQualifiedName~DecisionReport|FullyQualifiedName~TalkBalance"`
  Expected: FAIL (tipler yok).

- [ ] **Step 3: Uygula.** Bölüm içindeki tepe, pencere başına `max_j(Values[j] × DataQuality)`'nin en büyüğüdür; eşitlikte ilk pencere ve enum sırasıyla ilk kriter. Seçim tepeye göre azalan (eşitlikte erken olan), sonra `Start`'a göre sıralanır. `TalkBalance`: `End > Start` olan ve `Speaker.Index >= 0` olan bölgelerin süreleri toplanır; pay = süre / toplam; süreye göre azalan, eşitlikte `Index`.

- [ ] **Step 4: Testleri çalıştır** — aynı komut, PASS. Sonra `dotnet build && dotnet test` tamamı yeşil.

- [ ] **Step 5: DUR → onay → commit**
  ```bash
  git add src/SentimentAnalysis.Core/Decision.cs src/SentimentAnalysis.Application/Decision/DecisionReport.cs src/SentimentAnalysis.Application/Diarization/TalkBalance.cs tests/SentimentAnalysis.Application.Tests/Decision/DecisionReportTests.cs tests/SentimentAnalysis.Application.Tests/Diarization/TalkBalanceTests.cs
  git commit -m "feat(11e): rapor mantığı — kritik anlar, öneri değişimleri, konuşma dengesi"
  ```

---

### Task 2: Depolama — karar tabloları ve `IDecisionStore`

**Files:**
- Modify: `src/SentimentAnalysis.Infrastructure.Storage/StorageSchema.cs`
- Modify: `src/SentimentAnalysis.Infrastructure.Storage/SqliteSessionStore.cs`
- Modify: `src/SentimentAnalysis.Infrastructure.Storage/StorageServiceCollectionExtensions.cs`
- Modify: `src/SentimentAnalysis.Application/Abstractions/Interfaces.cs`
- Modify: `src/SentimentAnalysis.Core/Decision.cs`
- Test: `tests/SentimentAnalysis.Infrastructure.Storage.Tests/DecisionStoreTests.cs`, `SchemaMigrationTests.cs` (bir test eklenir), `StorageDiTests.cs` (bir test eklenir)

**Interfaces:**
- Consumes: `DecisionSession`, `DecisionWindow.Faces` (Task 1).
- Produces:
  - Core: `public sealed record DecisionSessionRecord(DecisionSession Session, FollowUpAction FollowUp, string FollowUpReason);`
  - Application: `public interface IDecisionStore { Task SaveDecisionSessionAsync(Guid sessionId, DecisionSessionRecord record, CancellationToken ct); Task<DecisionSessionRecord?> GetDecisionSessionAsync(Guid sessionId, CancellationToken ct); }`
  - `SqliteSessionStore : …, IDecisionStore`; DI: `services.AddSingleton<IDecisionStore>(sp => sp.GetRequiredService<SqliteSessionStore>())`.

**Şema** (`CreateSql`'e eklenir; `StorageSchema.TablesAddedIn11e = ["decision_sessions", "decision_weights", "decision_windows", "window_indicators", "window_faces"]`):

```sql
CREATE TABLE IF NOT EXISTS decision_sessions (
    session_id TEXT PRIMARY KEY, profile INTEGER NOT NULL,
    followup_action INTEGER NOT NULL, followup_reason TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS decision_weights (
    session_id TEXT NOT NULL, kind INTEGER NOT NULL,      -- 0 = profilin AHP ağırlığı, 1 = görüşme sonu kaydırıcı
    criterion INTEGER NOT NULL, weight REAL NOT NULL,
    PRIMARY KEY (session_id, kind, criterion));
CREATE TABLE IF NOT EXISTS decision_windows (            -- pencere + o anda GÖSTERİLEN öneri
    session_id TEXT NOT NULL, seq INTEGER NOT NULL,
    start_ms INTEGER NOT NULL, end_ms INTEGER NOT NULL, data_quality REAL NOT NULL,
    action INTEGER NOT NULL, closeness REAL NOT NULL, confidence INTEGER NOT NULL,
    face INTEGER, top1 INTEGER, top2 INTEGER, reason TEXT NOT NULL,
    PRIMARY KEY (session_id, seq));
CREATE TABLE IF NOT EXISTS window_indicators (
    session_id TEXT NOT NULL, window_seq INTEGER NOT NULL, criterion INTEGER NOT NULL,
    value REAL NOT NULL, source_face INTEGER,
    PRIMARY KEY (session_id, window_seq, criterion));
CREATE TABLE IF NOT EXISTS window_faces (                -- yüz başına özet; kare YOK
    session_id TEXT NOT NULL, window_seq INTEGER NOT NULL, face_index INTEGER NOT NULL,
    visible_ratio REAL NOT NULL, look_away_ratio REAL NOT NULL, valence_shift REAL NOT NULL,
    negative_expression REAL NOT NULL, smile_ratio REAL NOT NULL, reliability REAL NOT NULL,
    PRIMARY KEY (session_id, window_seq, face_index));
```

- [ ] **Step 1: Failing testleri yaz** (`DecisionStoreTests`, `SqliteSessionStoreTests`'teki geçici dizin kurulumu izlenir):
  - `Save_Get_RoundTrip`: 2 pencereli oturum (pencere 1'de 2 yüz, `SourceFace` karışık null/int, `TopCriteria` 2 elemanlı; pencere 2 yüzsüz, `TopCriteria` boş) → `Get` profil, iki ağırlık listesi (6 eleman, 1e-9), pencere zamanları, `Values`, `SourceFace`, `DataQuality`, `Recommendation.Action/Closeness/Confidence/Face/TopCriteria/Reason`, `Faces` alanlarını aynen döndürür; `FollowUp` ve `FollowUpReason` aynı. `Recommendation.Ranking` okunurken **boştur** (saklanmaz — testte belgelenir).
  - `Save_IkiKez_SatirlarCogalmaz`: aynı oturumu farklı içerikle iki kez kaydet → `Get` ikinciyi döndürür, pencere sayısı ikincinin sayısı.
  - `Get_KayitYok_Null`.
  - `Save_BaskaOturumaDokunmaz`: iki oturum kaydet, birini yeniden kaydet → diğeri değişmez.
  - `SchemaMigrationTests.EskiVeritabani_KararTablolariOlusur_KayitCalisir`: mevcut legacy şemayla DB oluştur → store aç → `SaveDecisionSessionAsync` + `Get` çalışır.
  - `StorageDiTests`: `IDecisionStore` aynı `SqliteSessionStore` örneğine çözülür.

- [ ] **Step 2: Çalıştır, düştüğünü gör**
  Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Storage.Tests`
  Expected: FAIL (IDecisionStore yok).

- [ ] **Step 3: Uygula.** `SaveDecisionSessionAsync` tek bir transaction içinde önce beş tablodan `session_id`'nin satırlarını siler, sonra yazar (`SaveSpeakerSegmentsAsync` deseni + transaction). Pencere `seq` = liste indeksi. `TopCriteria` → `top1/top2` (eksikse NULL). Okumada `TopCriteria` NULL olmayanlardan kurulur, `Ranking = Array.Empty`. Sayılar typed parametreyle yazılır (kültürden bağımsız).

- [ ] **Step 4: `dotnet build && dotnet test`** — tamamı yeşil.

- [ ] **Step 5: DUR → onay → commit**
  ```bash
  git add src/SentimentAnalysis.Core/Decision.cs src/SentimentAnalysis.Application/Abstractions/Interfaces.cs src/SentimentAnalysis.Infrastructure.Storage/StorageSchema.cs src/SentimentAnalysis.Infrastructure.Storage/SqliteSessionStore.cs src/SentimentAnalysis.Infrastructure.Storage/StorageServiceCollectionExtensions.cs tests/SentimentAnalysis.Infrastructure.Storage.Tests/DecisionStoreTests.cs tests/SentimentAnalysis.Infrastructure.Storage.Tests/SchemaMigrationTests.cs tests/SentimentAnalysis.Infrastructure.Storage.Tests/StorageDiTests.cs
  git commit -m "feat(11e): karar verisinin depolanması — pencere göstergeleri, öneriler, profil, ağırlıklar, takip kararı"
  ```

---

### Task 3: Rapor VM'leri — takip paneli, zaman çizelgesi, kritik anlar, konuşma dengesi

**Files:**
- Create: `src/SentimentAnalysis.Desktop/FollowUpPanelViewModel.cs`
- Modify: `src/SentimentAnalysis.Desktop/ReportViewModel.cs`
- Test: `tests/SentimentAnalysis.Desktop.Tests/FollowUpPanelViewModelTests.cs`, `tests/SentimentAnalysis.Desktop.Tests/ReportViewModelTests.cs` (testler eklenir)

**Interfaces:**
- Consumes: `DecisionSession`, `DecisionReport.*`, `TalkBalance.Compute` (Task 1); mevcut `FollowUpAdvisor.AggregateMeeting/Decide`, `Sensitivity.Analyze`, `Ahp.Compute`, `WeightSliderVm`, `RankingRowVm`, `IndicatorBarVm`, `DecisionPanelViewModel.Capitalize`.
- Produces:
  - `FollowUpPanelViewModel(DecisionConfig cfg)`:
    - `void Load(DecisionSession session)`; `[ObservableProperty] bool HasDecision` (pencere varsa true).
    - `string ProfileText` ("Profil: Mülakat · AHP tutarlılık oranı CR = 0,01"), `string BestText`, `string ReasonText`.
    - `IReadOnlyList<IndicatorBarVm> MeetingIndicators` (görüşme düzeyi K1..K6).
    - `IReadOnlyList<WeightSliderVm> Weights` (başlangıç = `session.FinalWeights`); değişince yeniden hesaplar.
    - `IReadOnlyList<RankingRowVm> Ranking` (kural yolunda boş).
    - `IReadOnlyList<SensitivityRowVm> Sensitivity`; `string SensitivitySummary`.
    - `FollowUpDecision? Current`; `[RelayCommand] ResetWeights()` → görüşme sonu ağırlıklarına döner.
  - `public sealed record SensitivityRowVm(string Criterion, IReadOnlyList<SensitivityCellVm> Cells);` `public sealed record SensitivityCellVm(string Text, bool Changed);` — `Text` değişmediyse `"aynı"`, değiştiyse yeni en iyi seçeneğin adı.
  - `ReportViewModel`: `FollowUpPanelViewModel FollowUp { get; }`; `void LoadDecisions(DecisionSession session)`; `[ObservableProperty] bool HasDecisionData`; `IReadOnlyList<IndicatorRowVm> IndicatorTimeline`; `string TimelineRange` ("00:00–04:30"); `IReadOnlyList<RecommendationRowVm> Recommendations`; `IReadOnlyList<CriticalMomentVm> CriticalMoments`; `IReadOnlyList<TalkShareVm> TalkBalance`; `[ObservableProperty] string DecisionSaveStatus`.
  - `public sealed record IndicatorRowVm(string Name, IReadOnlyList<double> Values);` `public sealed record RecommendationRowVm(string Time, string Action, string Reason, bool IsLowConfidence);` `public sealed record CriticalMomentVm(string TimeRange, string Text);` `public sealed record TalkShareVm(string Label, double Share, string Text);`

**Sabit metinler:**
- Kritik an: `"{Kriter} %{tepe} — {Subject} · gösterilen öneri: {eylem}"` (ör. "Gerilim %72 — Yüz 2 · gösterilen öneri: Empati kur, sakinleştir"); zaman `mm:ss–mm:ss`.
- Konuşma dengesi: `"{dk} dk {sn} sn · %{pay}"` (60 sn altı: `"{sn} sn · %{pay}"`).
- Duyarlılık özeti: değişen yoksa `"24 senaryonun hepsinde en iyi seçenek aynı kaldı — karar ağırlıklara duyarlı değil."`; varsa `"24 senaryonun {k} tanesinde en iyi seçenek değişti — karar en çok {kriter} ağırlığına duyarlı."` (en çok değişim satırı olan kriter; eşitlikte enum sırası).

- [ ] **Step 1: Failing testleri yaz** (`FollowUpPanelViewModelTests`; yardımcı: gergin göstergeli Satış oturumu, sakin oturum):
  - `Load_GorusmeSonuAgirliklariylaBaslar`: `FinalWeights` profil varsayılanından farklı → kaydırıcılar `FinalWeights`, `Current` = `FollowUpAdvisor.Decide(profile, AggregateMeeting(windows), FinalWeights, cfg)`.
  - `KaydiriciDegisince_KararYenidenHesaplanir`: K3 ağırlığını 0 yap → `Current`/`BestText` beklenen `Decide` sonucuna eşit.
  - `ResetWeights_GorusmeSonuAgirliklarinaDoner`.
  - `Sakin_Gorusme_OlumluTakip_SiralamaBos`: Satış'ta `BestText == "Teklif gönder"`, `Ranking` boş, duyarlılık 24 hücrenin hepsi `"aynı"`.
  - `Duyarlilik_6Satir4Hucre_VeOzet`: gergin oturumda `Sensitivity.Count == 6`, her satır 4 hücre, `Changed` hücreleri `Sensitivity.Analyze` çıktısıyla aynı; özet metni yukarıdaki şablondan.
  - `TumAgirliklarSifir_CokmezEsitAgirlik` (Review Focus 4): tüm kaydırıcılar 0 → `Current` null değil, `BestText` dolu.
  - `ProfileText_CrIcerir`: Mülakat → `"Profil: Mülakat · AHP tutarlılık oranı CR = "` ile başlar.
  - `ReportViewModelTests` ekleri:
    - `LoadDecisions_ZamanCizelgesi6SatirPencereSayisiKadarDeger`, `TimelineRange == "00:00–00:30"` (3 pencere × 10 sn).
    - `LoadDecisions_OneriDegisimleriVeKritikAnlar`: `Recommendations` sayısı `DecisionReport.Changes` ile, `CriticalMoments[0].Text` şablonla eşleşir; yüzsüz oturumda "Görüşme geneli" geçer (Review Focus 2).
    - `LoadDecisions_PencereYok_HasDecisionDataFalse_FollowUpHasDecisionFalse`.
    - `ApplySpeakers_KonusmaDengesiDoldurulur`: A 60 sn / B 30 sn → `TalkBalance[0].Text == "1 dk 0 sn · %67"`.
    - `SetDiarizationOutcome_Basarisiz_KonusmaDengesiTemizlenir`.

- [ ] **Step 2: Çalıştır, düştüğünü gör**
  Run: `dotnet test tests/SentimentAnalysis.Desktop.Tests --filter "FullyQualifiedName~FollowUpPanel|FullyQualifiedName~ReportViewModel"`
  Expected: FAIL.

- [ ] **Step 3: Uygula.** Takip paneli, 11d'deki `DecisionPanelViewModel` kaydırıcı desenini izler (`_loading` bayrağıyla toplu atamada tek hesap). Duyarlılık `Sensitivity.Analyze(CurrentWeights, w => FollowUpAdvisor.Decide(profile, meeting, w, cfg).Best)`. CR `Ahp.Compute(DecisionKnowledgeBase.Get(profile).Pairwise).ConsistencyRatio`, `0,00` biçimi tr-TR. `ReportViewModel` ctor'u mevcut `AppConfig`'ten `Decision` ayarını alır. `TalkBalance` `ApplySpeakers` içinde hesaplanır; etiket `SpeakerId.DefaultLabel`.

- [ ] **Step 4: `dotnet build && dotnet test`** — tamamı yeşil.

- [ ] **Step 5: DUR → onay → commit**
  ```bash
  git add src/SentimentAnalysis.Desktop/FollowUpPanelViewModel.cs src/SentimentAnalysis.Desktop/ReportViewModel.cs tests/SentimentAnalysis.Desktop.Tests/FollowUpPanelViewModelTests.cs tests/SentimentAnalysis.Desktop.Tests/ReportViewModelTests.cs
  git commit -m "feat(11e): rapor VM'leri — takip kararı, duyarlılık, zaman çizelgesi, kritik anlar, konuşma dengesi"
  ```

---

### Task 4: Bağlama — anlık görüntü, rapora aktarma, kayıt (+ 11d minor'ları)

**Files:**
- Modify: `src/SentimentAnalysis.Desktop/DecisionPanelViewModel.cs`
- Modify: `src/SentimentAnalysis.Desktop/LiveViewModel.cs`
- Modify: `src/SentimentAnalysis.Desktop/MainViewModel.cs`
- Modify: `src/SentimentAnalysis.Desktop.Avalonia/CompositionRoot.cs`
- Test: `tests/SentimentAnalysis.Desktop.Tests/MainViewModelDecisionWiringTests.cs` (yeni), `DecisionPanelViewModelTests.cs`, `LiveViewModelTests.cs` (testler eklenir)

**Interfaces:**
- Consumes: `IDecisionStore`, `DecisionSessionRecord` (Task 2); `ReportViewModel.LoadDecisions`, `FollowUp.Current`, `DecisionSaveStatus` (Task 3).
- Produces:
  - `DecisionPanelViewModel.Snapshot() -> DecisionSession` — geçmişin **kopyası**, `ProfileWeights = DefaultWeights(SelectedProfile)`, `FinalWeights = CurrentWeights`. `OnWindow` pencereye `Faces = faces.ToArray()` yazar.
  - `MainViewModel(..., Action<Action>? dispatch = null, IDecisionStore? decisions = null)`; `Task? DecisionSaveTask { get; }`.

- [ ] **Step 1: Failing testleri yaz:**
  - `DecisionPanelViewModelTests.Snapshot_GecmisinKopyasi_StartSonrasiDegismez` (Review Focus 1): iki pencere → snapshot → `Start()` → snapshot'ta hâlâ 2 pencere; `FinalWeights` kaydırıcı değerleri, `Faces` pencereye gelen yüzler.
  - `DecisionPanelViewModelTests.TumAgirliklarSifir_EtkisizNotuYok` (11d minor): tüm kaydırıcılar 0 iken hiçbir `IdleNote` dolu değil (motor eşit ağırlığa döner, hepsi etkili).
  - `LiveViewModelTests.Start_OncekiYuzDahilSatiriniTemizler` (11d minor): yüzlü bir chunk sonrası `Start` → `FaceContributionText` boş.
  - `MainViewModelDecisionWiringTests` (sahte runner `MainViewModelSpeakerWiringTests` deseniyle, sahte `IDecisionStore` çağrıları kaydeder):
    - `KosuBitince_RaporKararVerisiniAlir_VeKaydedilir`: Completed → `Report.HasDecisionData`, store'a `LastSessionId` ile tek çağrı; kayıttaki `FollowUp` = `Report.FollowUp.Current.Best`, pencere sayısı eşit.
    - `RaporAcikkenYeniKosu_RaporEskiGorusmeyiKorur`: Completed → `Live.Decision.Start()` → `Report.IndicatorTimeline` ve `Report.FollowUp.Current` değişmez.
    - `KayitHatasi_Cokmez_UyariGosterir` (Review Focus 5): store fırlatır → `await DecisionSaveTask` hatasız, `Report.DecisionSaveStatus` `"Karar kaydı saklanamadı"` ile başlar.
    - `SessionIdYok_VeyaStoreYok_KayitDenenmez`.

- [ ] **Step 2: Çalıştır, düştüğünü gör** — `dotnet test tests/SentimentAnalysis.Desktop.Tests` → FAIL.

- [ ] **Step 3: Uygula.** `Completed` işleyicisinde mevcut `LoadSummary`'den sonra `Report.LoadDecisions(Live.Decision.Snapshot())`, sonra `DecisionSaveTask = SaveDecisionsAsync(...)` (konuşmacı geçişi gibi arka planda; hata `_dispatch` ile `DecisionSaveStatus = $"Karar kaydı saklanamadı: {ex.Message}"`). Kayıt, `Report.FollowUp.Current` hesaplandıktan sonra alınır (yoksa — pencere yok — kayıt denenmez). `LiveViewModel.Start` `FaceContributionText`'i temizler. `DecisionPanelViewModel.UpdateRanking`'te etkisiz notu yalnızca `Σw > 0` iken yazılır. `CompositionRoot` `sp.GetRequiredService<IDecisionStore>()` geçer.

- [ ] **Step 4: `dotnet build && dotnet test`** — tamamı yeşil.

- [ ] **Step 5: DUR → onay → commit**
  ```bash
  git add src/SentimentAnalysis.Desktop/DecisionPanelViewModel.cs src/SentimentAnalysis.Desktop/LiveViewModel.cs src/SentimentAnalysis.Desktop/MainViewModel.cs src/SentimentAnalysis.Desktop.Avalonia/CompositionRoot.cs tests/SentimentAnalysis.Desktop.Tests/MainViewModelDecisionWiringTests.cs tests/SentimentAnalysis.Desktop.Tests/DecisionPanelViewModelTests.cs tests/SentimentAnalysis.Desktop.Tests/LiveViewModelTests.cs
  git commit -m "feat(11e): koşu sonu karar verisini rapora ve veritabanına aktar"
  ```

---

### Task 5: Rapor ekranı (XAML) + gerçek .app doğrulama kapısı

**Files:**
- Modify: `src/SentimentAnalysis.Desktop.Avalonia/Views/ReportView.axaml`

**Interfaces:**
- Consumes: Task 3'teki tüm `ReportViewModel` / `FollowUpPanelViewModel` özellikleri.

Bölümler (mevcut kart stili: `Border` + `CornerRadius=6` + `Padding=16`), "Toplantı Özeti"nin hemen altına, `HasDecisionData` ile görünür:
1. **Takip Kararı** — `ProfileText`, büyük `BestText`, `ReasonText`; görüşme göstergeleri çubukları; sıralama satırları; kaydırıcılar (LiveView'daki `Decision.Weights` şablonu birebir: ad, `Slider 0..1`, pay, etkisiz notu) + "Görüşme sonu ağırlıklarına dön" (`FollowUp.ResetWeightsCommand`); `DecisionSaveStatus` (boşsa gizli).
2. **Duyarlılık Analizi** — başlık satırı `Kriter | −%20 | −%10 | +%10 | +%20`, 6 satır; `Changed` hücre vurgulu (`SystemAccentColor`, kalın); altında `SensitivitySummary`.
3. **Gösterge Zaman Çizelgesi** — 6 satır: sol 130 px ad, sağda yatay `ItemsControl` (pencere başına 10×14 px hücre, `Opacity = değer`, renk `SystemAccentColor`), yatay kaydırma; altında `TimelineRange`.
4. **Verilen Öneriler** — `Time | Action | Reason`; düşük güvenli satır soluk.
5. **Kritik Anlar** — `TimeRange | Text`; boşsa "Belirgin bir kritik an yok." metni.
6. **Konuşma Dengesi** — "Konuşmacılar" kartının içine, satır başına etiket + `ProgressBar` (0..1) + `Text`; `HasSpeakers` ile görünür.

- [ ] **Step 1: XAML'i yaz, `dotnet build` yeşil.**
- [ ] **Step 2: `dotnet test` tamamı yeşil.**
- [ ] **Step 3: .app'i derle ve aç:**
  ```bash
  osascript -e 'quit app "Sesli Duygu Analizi"'
  ./build-macos-app.sh && open "dist/Sesli Duygu Analizi.app"
  ```
  (`open` açık uygulamayı yeniden başlatmaz — önce kapatma şart.)
- [ ] **Step 4: Kullanıcı doğrulama kapısı.** Kullanıcı `iki-kisi.mp4`'ü kendi kopyasından seçer, Ayarlar'da konuşmacı ayrımı açık, profil seçip birkaç kaydırıcı oynatarak oynatır, Durdur'a basar. Kontrol listesi:
  - Rapor açılır; Takip Kararı kartı dolu, başlangıç kaydırıcıları canlı ekrandaki son değerlerle aynı.
  - Rapordaki kaydırıcı oynayınca karar/sıralama/duyarlılık tablosu anında değişir; "Görüşme sonu ağırlıklarına dön" geri getirir.
  - Zaman çizelgesi, verilen öneriler, kritik anlar mantıklı (canlıda görülen kartlarla örtüşür).
  - Konuşmacılar hazır olunca konuşma dengesi görünür.
  - Canlı'ya dönüp yeni koşu başlatınca rapor eski görüşmeyi göstermeye devam eder.
  - (İsteğe bağlı) `sqlite3 ~/…/sessions.db "select count(*) from decision_windows"` > 0.
- [ ] **Step 5: Onay gelince commit** (gelmezse bulgular düzeltilir, Step 3'e dönülür):
  ```bash
  git add src/SentimentAnalysis.Desktop.Avalonia/Views/ReportView.axaml
  git commit -m "feat(11e): rapor ekranı — takip kararı, duyarlılık, zaman çizelgesi, kritik anlar, konuşma dengesi"
  ```
- [ ] **Step 6: Bitiş.** Tek bir final inceleme (en yetenekli modelle, tüm 11e diff'i), README durum tablosu güncellemesi kullanıcıya **önerilir** (README'deki kullanıcı değişikliği nedeniyle kullanıcı onayı olmadan dokunulmaz), hafıza kaydı güncellenir.
