# Karşı tarafa odak — uygulama planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Her görüşmede yöneten bilinsin; duygu/göstergeler yalnız karşı taraftan, yönetenin yalnız davranışı (konuşma payı, hız) karar girdisi olsun — canlıda tanıtma zorunlu, dosyada önce ayrım + "Siz hangisiniz?".

**Architecture:** Canlıda mevcut `SpeakerRoleTracker` (12. aşama). Dosyada: ffmpeg ile bellekte 16 kHz mono çözme → `IDiarizer` + `GhostSpeakerFilter` (ModelWorkQueue'da kendi model oturumu) → kullanıcı seçimi → seçimden kurulan `DiarizationRoleLabeler : IRoleLabeler` paylaşılan `IFileRoleSelection` üzerinden `PipelineAnalysisRunner`'a; oturum sonrası geçiş ön-hesaplı bölgeler + bellekteki PCM ile çalışır. Rapor `Counterpart`'ı ve "karşı taraf sustu" bayrağını kullanır.

**Tech Stack:** .NET 9, Avalonia, CommunityToolkit.Mvvm, xUnit, sherpa-onnx (mevcut), ffmpeg (mevcut, `IAudioToolResolver`), macOS `afplay`.

**Spec:** `docs/superpowers/specs/2026-11-13-karsi-tarafa-odak-design.md`

## Global Constraints

- Dosya modunda ayrım "Konuşmacı ayırımı" ayarından bağımsız her zaman çalışır; çözülmüş ses **yalnız bellekte**, diske yazılmaz (Dinle'nin geçici WAV'ı çalma bitince/iptalde silinir).
- Ayrım uzunluk sınırı `Diarization.MaxRecordedMinutes` (90 dk); aşan kısım rolsüz.
- `SpeakerId.Unknown` ve bölgesiz konuşma rolsüz (`Belirsiz`) — karşı tarafa sayılmaz.
- Diarization modeli ile analiz modelleri aynı anda yüklenmez (mevcut `ModelWorkQueue`).
- Tanıtma imkânsızsa (ses imzası modeli yok) canlı Başlat kilitlenmez; karışık yol + "Yöneten ayrılmadı".
- Metinler: Başlat ipucu `Başlamadan önce 'Sesimi tanıt' ile sesinizi tanıtın — öneriler yalnız karşı tarafa göre hesaplanır.`; karışık not `Yöneten ayrılmadı — sonuçlar iki tarafın karışık sesinden.`; kısmi not `Yöneten yalnız kısmen ayrıldı — bazı bölümler karışık sesten.`; ayrım başarısız `Konuşmacılar ayrılamadı; analiz karışık ses üzerinden yapılacak ve raporda öyle belirtilecek.`
- Veritabanı şeması değişmez (yeni alanlar bellek içi). Duygu modelleri, öneri motoru, canlı öneri havuzu değişmez.
- Her görev sonunda kullanıcı onayı olmadan commit edilmez ve sonraki göreve geçilmez (kullanıcı kuralı).
- Testler: `dotnet build SentimentAnalysis.sln` 0 uyarı/0 hata; `dotnet test SentimentAnalysis.sln` 0 başarısız.

## Review Focus

1. **Bayat dosya rolü:** ikinci dosya ya da canlı moda geçişte önceki dosyanın seçimi kullanılmamalı → Task 5 testi (dosya değişince/koşu bitince seçim temizlenir).
2. **Hazırlık sırasında Durdur:** ayrım iptal edilir, model oturumu kapanır, analiz başlamaz, PCM bırakılır → Task 5 testi.
3. **Tek konuşmacı / ayrım hatası / "Emin değilim":** analiz karışık yolda başlar, rapor notu görünür → Task 5 + Task 7 testleri.
4. **Dinle iptali:** ikinci Dinle ya da panel kapanınca önceki çalma durur ve geçici dosya silinir → Task 4 testi.
5. **Karşı tarafın hiç konuşmadığı görüşme:** özet boş-ama-çökmeyen, takip kararı sessiz pencereleri saymaz → Task 7 testi.

---

### Task 1: Canlıda tanıtma zorunlu

**Files:** Modify `src/SentimentAnalysis.Desktop/IVoiceEnrollment.cs`, `src/SentimentAnalysis.Desktop/LiveViewModel.cs:150-200,396`, `src/SentimentAnalysis.Desktop.Avalonia/MicrophoneVoiceEnrollment.cs`, `src/SentimentAnalysis.Desktop.Avalonia/Views/LiveView.axaml` (ipucu satırı); Test `tests/SentimentAnalysis.Desktop.Tests/VoiceEnrollmentTests.cs`.

**Interfaces — Produces:** `bool IVoiceEnrollment.IsAvailable { get; }` (varsayılan arayüz üyesi `=> true`); `MicrophoneVoiceEnrollment.IsAvailable` = `config.Role.Enabled` ve ses imzası model dosyası var (`ISpeakerRoleModelProvider`). `LiveViewModel.StartHint : string` (boş ya da Global Constraints'teki ipucu).

- [ ] Step 1 — failing testler: `Mikrofon_TanitmaYok_BaslatKapali` (`IsAvailable=true, IsEnrolled=false` → `StartCommand.CanExecute == false`, `StartHint` = ipucu); `Mikrofon_TanitmaVar_BaslatAcik`; `Kamera_TanitmaYok_BaslatKapali`; `Dosya_TanitmaGerekmez`; `TanitmaImkansiz_BaslatAcik` (`IsAvailable=false` → açık, ipucu boş); `TanitmaSilinince_BaslatKapanir`; `TanitmaBitince_CanExecuteChanged` (EnrollCommand sonrası `CanExecuteChanged` tetiklenir); `SupheliTanitma_BaslatAcik`.
- [ ] Step 2 — `dotnet test tests/SentimentAnalysis.Desktop.Tests --filter VoiceEnrollmentTests` → FAIL.
- [ ] Step 3 — `CanStart`'a koşul: canlı modda `_enrollment is { IsAvailable: true, IsEnrolled: false }` ise false; mod değişimi, Clear, Enroll sonunda `StartCommand.NotifyCanExecuteChanged()` + `OnPropertyChanged(nameof(StartHint))`. XAML'de Başlat altında `StartHint` (boşsa gizli).
- [ ] Step 4 — filtreli test PASS; tam takım yeşil.
- [ ] Step 5 — kullanıcı onayıyla commit `feat(odak): canlıda ses tanıtması zorunlu`.

### Task 2: Diarization bölgelerinden rol etiketi

**Files:** Create `src/SentimentAnalysis.Application/Diarization/DiarizationRoleLabeler.cs`; Test `tests/SentimentAnalysis.Application.Tests/Diarization/DiarizationRoleLabelerTests.cs`.

**Interfaces — Produces:** `public sealed class DiarizationRoleLabeler(IReadOnlyList<SpeakerSegment> segments, SpeakerId host) : IRoleLabeler` — `Label(AudioChunk c)`: `[c.Start, c.End)` ile kesişen her bölge kırpılarak `RoleSpan` (host → `Yoneten`, `SpeakerId.Unknown` → atlanır, diğer → `KarsiTaraf`, `Similarity = 1`); `new RoleTimeline(spans, Enrolled: true, EnrollmentDoubtful: false)`; `Reset()` no-op.

- [ ] Step 1 — failing testler: `HostYoneten_DigerleriKarsiTaraf`; `Unknown_Atlanir`; `ParcaSinirindaKirpilir` (bölge 8–14 sn, parça 10–20 → span 10–14); `KesisimYok_BosAmaEnrolled` (`Spans` boş, `Enrolled == true`); `UcKisi_HostDisindakilerKarsiTaraf`.
- [ ] Step 2 — `--filter DiarizationRoleLabelerTests` → FAIL. Step 3 — uygula. Step 4 — PASS + tam takım. Step 5 — onayla commit `feat(odak): diarization bölgelerinden rol etiketi`.

### Task 3: Dosyayı bellekte çöz + ön ayrım işi

**Files:** Create `src/SentimentAnalysis.Infrastructure.Audio/FfmpegAudioFileDecoder.cs`, `src/SentimentAnalysis.Application/Diarization/SpeakerPreparation.cs`, `src/SentimentAnalysis.Desktop/ResourceManagedSpeakerPreparation.cs`; Modify `src/SentimentAnalysis.Application/Abstractions/Interfaces.cs` (yeni arayüz), `src/SentimentAnalysis.Desktop.Avalonia/ModelSessions.cs` (`CreatePreparation`), `CompositionRoot.cs`; Tests `tests/SentimentAnalysis.Application.Tests/Diarization/SpeakerPreparationTests.cs`, `tests/SentimentAnalysis.Desktop.Tests/SpeakerPreparationLifetimeTests.cs`, `tests/SentimentAnalysis.Infrastructure.Audio.Tests/FfmpegAudioFileDecoderTests.cs`.

**Interfaces — Produces:**
- `public interface IAudioFileDecoder { Task<float[]> DecodeMono16kAsync(string path, TimeSpan maxDuration, CancellationToken ct); }` — ffmpeg `-i <path> -t <max> -ac 1 -ar 16000 -f f32le pipe:1` (`IAudioToolResolver`, `ArgumentList`); ses/video aynı yol.
- `public sealed record SpeakerChoice(SpeakerId Speaker, double TalkSeconds, TimeSpan SampleStart, TimeSpan SampleEnd);` — örnek: en uzun bölgenin ortasından ≤5 sn.
- `public sealed record SpeakerPreparationResult(DiarizationOutcome Outcome, IReadOnlyList<SpeakerSegment> Segments, IReadOnlyList<SpeakerChoice> Choices, float[] Audio, bool Truncated);` — `Choices` Unknown içermez, konuşma süresine göre azalan.
- `public sealed class SpeakerPreparation(IDiarizer diarizer, AppConfig config)` — `Task<SpeakerPreparationResult> RunAsync(float[] mono16k, int expectedSpeakers, bool truncated, CancellationToken ct)`; diarize + `GhostSpeakerFilter`; diarizer hatası → `Failed`; <2 gerçek konuşmacı → `Outcome = Completed`, `Choices.Count < 2` (çağıran karışık yola düşer).
- `public interface ISpeakerPreparation { Task<SpeakerPreparationResult> PrepareAsync(string path, int expectedSpeakers, CancellationToken ct); }` (Desktop) — `ResourceManagedSpeakerPreparation`: çözme + `ModelWorkQueue` içinde `ModelSessions.CreatePreparation` (yalnız sherpa diarization) ile `SpeakerPreparation`, iş bitince/hatada/iptalde oturum kapanır.

- [ ] Step 1 — failing testler: `IkiKonusmaci_SecenekleriSureSirali` (sahte diarizer); `Unknown_SeceneklereGirmez`; `Ornek_EnUzunBolgeOrtasi_En5Sn`; `TekKonusmaci_IkidenAzSecenek`; `DiarizerHatasi_Failed`; `Iptal_Firlatir` (OperationCanceledException); lifetime: `Basari_HataVeIptalde_OturumKapanir` (mevcut `ModelLifetimeTests` deseni); decoder: `FfmpegYok_AnlasilirHata` ve env-gated gerçek dosya testi (`AUDIO_DECODE_TEST_FILE`).
- [ ] Step 2 — ilgili `--filter` → FAIL. Step 3 — uygula; kök sağlayıcıya model kaydı YOK (yalnız iş sağlayıcısı). Step 4 — PASS + tam takım. Step 5 — onayla commit `feat(odak): dosyayı bellekte çöz ve konuşmacıları önceden ayır`.

### Task 4: Konuşmacı sesini dinletme

**Files:** Create `src/SentimentAnalysis.Desktop/ISpeakerSamplePlayer.cs`, `src/SentimentAnalysis.Desktop.Avalonia/AfplaySamplePlayer.cs`; Modify `CompositionRoot.cs`; Test `tests/SentimentAnalysis.Desktop.Avalonia.Tests/AfplaySamplePlayerTests.cs`.

**Interfaces — Produces:** `public interface ISpeakerSamplePlayer { bool IsSupported { get; } Task PlayAsync(float[] mono16k, CancellationToken ct); }`. `AfplaySamplePlayer(IExternalProcessRunner runner, IRuntimePlatform platform)`: `IsSupported` yalnız macOS; geçici klasöre PCM16 WAV yazar, `afplay <dosya>` çalıştırır, `finally` içinde dosyayı siler; yeni çağrı öncekini iptal eder.

- [ ] Step 1 — failing testler (sahte process runner): `Calma_SonrasiDosyaSilinir`; `Iptal_DosyaSilinir_SurecDurur`; `IkinciCagri_OncekiniIptalEder`; `MacDisi_Desteklenmez`.
- [ ] Step 2–4 — FAIL → uygula → PASS + tam takım. Step 5 — onayla commit `feat(odak): konuşmacı ses örneğini dinletme`.

### Task 5: Dosya modu akışı — "Siz hangisiniz?"

**Files:** Create `src/SentimentAnalysis.Desktop/IFileRoleSelection.cs` (+ `FileRoleSelection`), `src/SentimentAnalysis.Desktop/HostChoiceViewModel.cs`; Modify `src/SentimentAnalysis.Desktop/LiveViewModel.cs` (Start/RunAsync), `src/SentimentAnalysis.Desktop.Avalonia/PipelineAnalysisRunner.cs:169`, `ModelSessions.cs`, `CompositionRoot.cs`, `Views/LiveView.axaml`; Test `tests/SentimentAnalysis.Desktop.Tests/FileHostChoiceTests.cs`.

**Interfaces:**
- Consumes: Task 2 `DiarizationRoleLabeler`, Task 3 `ISpeakerPreparation`/`SpeakerPreparationResult`, Task 4 `ISpeakerSamplePlayer`.
- Produces: `public interface IFileRoleSelection { IRoleLabeler? Roles { get; } SpeakerPreparationResult? Prepared { get; } SpeakerId? Host { get; } void Set(SpeakerPreparationResult prepared, SpeakerId host); void SetMixed(); void Clear(); }` (singleton). `HostChoiceViewModel`: `Rows : IReadOnlyList<HostChoiceRowVm>` (`Label` "Konuşmacı A", `TalkText` "1 dk 20 sn konuştu", `PlayCommand`, `CanPlay`), `SelectedRow`, `NotSure` seçeneği, `ConfirmCommand`. `LiveViewModel`: `IsPreparingSpeakers`, `HostChoice : HostChoiceViewModel?`, `PreparationStatus : string`.
- `PipelineAnalysisRunner`: `roles:` = canlı modda `_roles`, Dosya modunda `fileRoleSelection.Roles` (null → karışık).

Akış: Dosya modunda Başlat → `IsPreparingSpeakers` + "Konuşmacılar ayrılıyor…" → `PrepareAsync` → `Choices.Count >= 2` ise `HostChoice` gösterilir; değilse `SetMixed()` + başarısız metni ve analiz başlar → Onayla: `Set(prepared, host)` (ya da "Emin değilim" → `SetMixed()`) → mevcut `RunAsync`. Koşu bitince ve dosya değişince/moda geçişte `Clear()`; `Prepared.Audio` Task 6'ya kadar tutulur.

- [ ] Step 1 — failing testler (sahte preparation/player/runner): `Dosya_Baslat_OnceHazirlikSonraSecim`; `Secim_RollerRunneraGider` (runner'ın gördüğü `IFileRoleSelection.Roles` host'u Yoneten etiketler); `EminDegilim_KarisikYol`; `TekKonusmaci_SecimYok_KarisikYolVeMesaj`; `Hazirlikta_Durdur_AnalizBaslamaz_SecimTemiz`; `DosyaDegisince_SecimTemizlenir`; `CanliModaGecince_SecimTemizlenir`; `Dinle_OynaticiyaDogruParca` (örnek aralığı PCM'den kesilir); `OynaticiDesteklenmez_DinleGizli`.
- [ ] Step 2–4 — FAIL → uygula (XAML: Başlat kartının altında panel, mevcut kart stili) → PASS + tam takım.
- [ ] Step 5 — onayla commit `feat(odak): dosya modunda 'Siz hangisiniz?' ve rol bilgili analiz`.

### Task 6: Oturum sonrası geçiş ön-hesaplı bölgelerle

**Files:** Modify `src/SentimentAnalysis.Application/Diarization/SpeakerAnalysisService.cs` (+ `ISpeakerAnalysis`), `src/SentimentAnalysis.Application/Diarization/ResourceManagedSpeakerAnalysis.cs`, `src/SentimentAnalysis.Desktop/MainViewModel.cs:98-130`; Tests `tests/SentimentAnalysis.Application.Tests/Diarization/SpeakerAnalysisServiceTests.cs`, `tests/SentimentAnalysis.Desktop.Tests/MainViewModelSpeakerWiringTests.cs`.

**Interfaces — Produces:** `Task<SpeakerAnalysisResult> ISpeakerAnalysis.RunPrecomputedAsync(Guid sessionId, IReadOnlyList<SpeakerSegment> segments, float[] mono16k, CancellationToken ct)` — diarizer ÇAĞIRMAZ, oturum sesini OKUMAZ; bölgeleri `IDiarizationStore.SaveSpeakerSegmentsAsync` ile yazar, transkript/chunk'ları okur, prozodiyi verilen PCM'den hesaplar; `Enabled` ayarından bağımsız çalışır. `MainViewModel`: `IFileRoleSelection.Prepared` doluysa bunu çağırır (ayar kapalı olsa da), bitince `IFileRoleSelection.Clear()` (PCM bırakılır).

- [ ] Step 1 — failing testler: `Precomputed_DiarizerCagrilmaz_SesOkunmaz`; `Precomputed_BolgelerKaydedilir`; `Precomputed_ProzodiVerilenSestenHesaplanir`; MainViewModel: `DosyaRolu_AyarKapaliyken_DePrecomputedCalisir`; `Bitince_SecimTemizlenir`.
- [ ] Step 2–4 — FAIL → uygula → PASS + tam takım. Step 5 — onayla commit `feat(odak): oturum sonrası konuşmacı geçişi ön-hesaplı bölgelerle`.

### Task 7: Rapor — karşı taraf özeti, sessiz pencereler, notlar, yönetenin kartı

**Files:** Modify `src/SentimentAnalysis.Application/Fusion/MeetingAggregator.cs:20-75`, `src/SentimentAnalysis.Core/Decision.cs:105` (`DecisionWindow`), `src/SentimentAnalysis.Desktop/DecisionPanelViewModel.cs:139`, `src/SentimentAnalysis.Desktop/FollowUpPanelViewModel.cs:90`, `src/SentimentAnalysis.Application/Decision/DecisionReport.cs` (kritik anlar), `src/SentimentAnalysis.Desktop/ReportViewModel.cs` (zaman çizelgesi, not, yönetenin kartı), `Views/ReportView.axaml`; Tests `tests/SentimentAnalysis.Application.Tests/MeetingAggregatorTests.cs`, `tests/SentimentAnalysis.Desktop.Tests/ReportViewModelTests.cs`, `FollowUpPanelViewModelTests.cs`.

**Interfaces — Produces:**
- `MeetingAggregator`: chunk'ların herhangi birinde `Roles is { Enrolled: true }` varsa her chunk için `Counterpart` kullanılır, `Counterpart == null` olan chunk özete girmez (hepsi null → boş özet yolu).
- `DecisionWindow.CounterpartSilent : bool { get; init; }` (bellek içi; depoya yazılmaz) — `DecisionPanelViewModel` pencere eklerken `chunk.Roles is { Enrolled: true } && chunk.Counterpart is null`.
- `FollowUpPanelViewModel.Load`: `AggregateMeeting`'e yalnız `!CounterpartSilent` pencereler (hepsi sessizse mevcut boş yol). Kritik anlar sessiz pencereleri atlar. Zaman çizelgesi `IndicatorRowVm` değerinde sessiz pencere `double.NaN` → çizimde boşluk.
- `ReportViewModel.RoleNotice : string` — rolsüz oturum → karışık notu; bazı konuşmalı chunk'lar rolsüz → kısmi notu; tam → boş.
- Dosya modu (host biliniyorsa) yönetenin kişi kartı: `SpeakerEmotionVm.IsHost = true`, duygu alanları gizli, `HostBehaviorText` = "Görüşmeyi yöneten (siz) · konuşma payı %62 · dakikada 140 kelime".

- [ ] Step 1 — failing testler: aggregator `Roller_VarsaCounterpartKullanilir`, `KarsiTarafSustu_OzeteGirmez`, `HepsiSessiz_BosOzet`, `RolYok_FusedKullanilir` (bugünkü davranış); follow-up `SessizPencereler_TakipToplaminaGirmez`; report `Zamanlama_SessizPencereNaN`, `RolNotu_Karisik_Kismi_Tam`, `YonetenKarti_DuygusuzDavranisli`; `DecisionWindow_CounterpartSilent_DepoyaYazilmaz` (store round-trip sonrası false — bilerek).
- [ ] Step 2–4 — FAIL → uygula → PASS + tam takım; `./build-macos-app.sh` ile .app; kullanıcı canlı dener (canlı: tanıtmasız Başlat kapalı; dosya: ayrım → Dinle → seçim → rapor notları ve yönetenin kartı).
- [ ] Step 5 — onayla commit `feat(odak): rapor karşı tarafa göre; yönetenin kartında yalnız davranış`.
