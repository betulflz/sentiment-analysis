using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Canlı analiz görünümü VM'i. <see cref="IProgress{ChunkResult}"/> olarak pipeline'a verilir;
/// gelen her chunk çizelgeye + güncel VA/duygu/rozete yansır. Start/Stop koşuyu <see cref="IAnalysisRunner"/>
/// üzerinden yönetir. UI-agnostik (headless test).</summary>
public sealed partial class LiveViewModel : ObservableObject, IProgress<ChunkResult>, ICaptureWarnings
{
    private readonly IAnalysisRunner _runner;
    private readonly IAudioFilePicker? _picker;
    private readonly ISelectedAudio? _selected;
    private readonly double _temperature;
    private readonly double? _neutralRadius;
    private readonly ICaptureModeState? _captureMode;
    private readonly IDiarizationState? _diarization;
    private CancellationTokenSource? _cts;
    private Task? _run;

    public ObservableCollection<ChunkRowVm> Timeline { get; } = new();

    /// <summary>Karar paneli (11d): öneri kartı, profil, kaydırıcılar, göstergeler.</summary>
    public DecisionPanelViewModel Decision { get; }

    /// <summary>Önizlemedeki yüz kutuları (kare pikselinde).</summary>
    public ObservableCollection<FaceBoxVm> PreviewFaces { get; } = new();

    /// <summary>Önizlenen son kare; <c>null</c> = görüntü yok (önizleme gizlenir).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPreview)), NotifyPropertyChangedFor(nameof(ShowSharedPreview))] private VideoFrame? _previewFrame;

    public bool HasPreview => PreviewFrame is not null;

    private IReadOnlyList<(int Index, FaceBox Box)>? _previewBoxes;

    /// <summary>Her profilde yüz başına paneller (görünen yüzler); yüz yoksa boş kalır.</summary>
    public ObservableCollection<FacePanelVm> FacePanels { get; } = new();

    // Panel nesneleri yüz numarasına göre yaşar: yüz kaybolup dönünce ad ve göstergeler korunur.
    private readonly Dictionary<int, FacePanelVm> _panelsByFace = new();

    private readonly List<ChunkResult> _runChunks = new();

    /// <summary>Bu koşuda raporlanan parçalar (yüzsüz değerleriyle); rapor, konuşmacıya özel duyguyu bunlardan kurar.</summary>
    public IReadOnlyList<ChunkResult> RunChunks => _runChunks;

    /// <summary>Kullanıcının panellere yazdığı adlar (yüz numarası → ad); varsayılan "Yüz N" olanlar yok.</summary>
    public IReadOnlyDictionary<int, string> UserFaceNames => _panelsByFace.Values
        .Where(p => !string.IsNullOrWhiteSpace(p.Name) && p.Name.Trim() != p.DefaultName)
        .ToDictionary(p => p.Index, p => p.Name.Trim());

    public bool ShowFacePanels => FacePanels.Count > 0;

    /// <summary>Tek ortak kare: yüz görünene kadar.</summary>
    public bool ShowSharedPreview => HasPreview && !ShowFacePanels;

    /// <summary>Panel ızgarasının sütun sayısı: 1–2 yüz yan yana, sonra kareye yakın ızgara.</summary>
    public int FacePanelColumns => Math.Max(1, (int)Math.Ceiling(Math.Sqrt(FacePanels.Count)));

    private FacePanelVm PanelFor(int index) =>
        _panelsByFace.TryGetValue(index, out var p) ? p : _panelsByFace[index] = new FacePanelVm(index);

    /// <summary>Yeni koşuda adlar ve göstergeler sıfırlanır (yüz numaraları koşular arasında kişi değildir).</summary>
    public void ResetFacePanels()
    {
        _panelsByFace.Clear();
        _runChunks.Clear();
        _previewBoxes = null;   // sonraki yenileme, kutular aynı nesne olsa da panelleri yeniden kurar
        SyncFacePanels(Array.Empty<(int, FaceBox)>());
    }

    private void SyncFacePanels(IReadOnlyList<(int Index, FaceBox Box)> boxes)
    {
        // Her profilde (satış dahil) yüz başına panel: tek ortak kare yalnız yüz görünmeyince kalır.
        var wanted = boxes.OrderBy(b => b.Index).ToArray();

        bool sameSet = wanted.Length == FacePanels.Count
            && wanted.Select(w => w.Index).SequenceEqual(FacePanels.Select(p => p.Index));
        if (!sameSet)
        {
            FacePanels.Clear();
            foreach (var (index, _) in wanted) FacePanels.Add(PanelFor(index));
        }
        foreach (var (index, box) in wanted)
            PanelFor(index).Box = new FaceBoxVm(box.X, box.Y, box.W, box.H, PanelFor(index).DefaultName);

        OnPropertyChanged(nameof(ShowFacePanels));
        OnPropertyChanged(nameof(ShowSharedPreview));
        OnPropertyChanged(nameof(FacePanelColumns));
    }

    /// <summary>Runner'ın son karesini ve yüz kutularını okur (UI zamanlayıcısı ~5 Hz çağırır).
    /// Kare ve kutular değişmediyse hiçbir özellik değişmez.</summary>
    public void RefreshPreview()
    {
        var frame = _runner.LatestFrame;
        var boxes = frame is null ? Array.Empty<(int, FaceBox)>() : _runner.LatestFaces;
        if (!ReferenceEquals(frame, PreviewFrame)) PreviewFrame = frame;
        if (ReferenceEquals(boxes, _previewBoxes)) return;
        _previewBoxes = boxes;
        SyncFacePanels(boxes);
        if (boxes.Count == 0 && PreviewFaces.Count == 0) return;
        PreviewFaces.Clear();
        foreach (var (index, box) in boxes)
            PreviewFaces.Add(new FaceBoxVm(box.X, box.Y, box.W, box.H, $"Yüz {index + 1}"));
    }

    /// <summary>Koşu bittiğinde raised olur; sonucu (Rapor'a devretmek gibi) dinleyicilere iletir.</summary>
    public event Action<MeetingSummary>? Completed;

    /// <summary>Son tamamlanan koşunun oturum kimliği (10b). <c>null</c> = koşu kalıcı bir
    /// oturum üretmedi → konuşmacı ayrımı denenmez.</summary>
    public Guid? LastSessionId { get; private set; }

    /// <summary>"Kaç kişi konuşacak?" seçenekleri; 0 = bilinmiyor (ayrım kişi sayısını kendisi tahmin eder).</summary>
    public IReadOnlyList<SpeakerCountOption> SpeakerCountOptions { get; } =
        new[] { 2, 3, 4, 5 }.Select(n => new SpeakerCountOption(n, $"{n} kişi"))
            .Append(new SpeakerCountOption(0, "Bilmiyorum")).ToArray();

    [ObservableProperty] private SpeakerCountOption _selectedSpeakerCount = null!;

    /// <summary>Görüşme BAŞINDA seçilen kişi sayısı — Durdur sonrası seçim değişse de konuşmacı
    /// ayrımı bu değerle çalışır.</summary>
    public int LastSpeakerCount { get; private set; } = 2;

    // --- 12: "Sesimi tanıt" (canlı Yöneten / Karşı taraf ayrımı) ---
    private readonly IVoiceEnrollment? _enrollment;

    /// <summary>Tanıtma durumu (boş = özellik yok). Kayıt yoksa ne işe yaradığını söyler.</summary>
    [ObservableProperty] private string _enrollmentStatus = string.Empty;

    /// <summary>Tanıtma kaydı şüpheli (iki ses) ya da başarısız: durum metni uyarı renginde gösterilir.</summary>
    [ObservableProperty] private bool _enrollmentWarning;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(EnrollButtonText)), NotifyPropertyChangedFor(nameof(CanClearEnrollment))]
    [NotifyCanExecuteChangedFor(nameof(EnrollCommand))]
    private bool _isEnrolling;

    /// <summary>Mevcut tanıtma en az bir görüşmede kullanıldı: sonraki görüşmeyi başka biri yönetiyorsa roller
    /// yanlış atanır — kullanıcı uyarılır (güvenlik incelemesi 2026-11-12).</summary>
    private bool _enrollmentUsed;

    /// <summary>"Tanıtmayı sil" görünsün mü: tanıtma var ve kayıt/görüşme sürmüyor.</summary>
    public bool CanClearEnrollment => _enrollment?.IsEnrolled == true && !IsEnrolling && !IsCapturing;

    [RelayCommand]
    private void ClearEnrollment()
    {
        if (_enrollment is null || IsCapturing) return;
        _enrollment.Clear();
        _enrollmentUsed = false;
        EnrollmentWarning = false;
        EnrollmentStatus = "Ses tanıtması silindi. " + IdleEnrollmentText;
        OnPropertyChanged(nameof(EnrollButtonText));
        OnPropertyChanged(nameof(CanClearEnrollment));
    }

    /// <summary>Tanıtma düğmesi yalnız canlı modlarda (mikrofon/kamera): ses dosyasında yöneten kişi kayıtta olmayabilir.</summary>
    public bool ShowEnrollment => _enrollment is not null && !IsFileMode;

    public string EnrollButtonText => IsEnrolling ? "Kaydediliyor…"
        : _enrollment?.IsEnrolled == true ? "Sesimi yeniden tanıt" : "Sesimi tanıt";

    private bool CanEnroll() => _enrollment is not null && !IsEnrolling && !IsCapturing && !IsPreparingModels;

    [RelayCommand(CanExecute = nameof(CanEnroll))]
    private async Task EnrollAsync()
    {
        if (_enrollment is null) return;
        IsEnrolling = true;
        EnrollmentWarning = false;
        StartCommand.NotifyCanExecuteChanged();
        EnrollmentStatus = $"Kaydediliyor: {_enrollment.RecordSeconds:0} saniye boyunca yalnız siz konuşun " +
                           "(ör. kendinizi ve görüşmenin amacını anlatın).";
        try
        {
            var r = await _enrollment.RecordAsync(CancellationToken.None).ConfigureAwait(true);
            EnrollmentWarning = !r.Success || r.Doubtful;
            if (r.Success) _enrollmentUsed = false;
            EnrollmentStatus = r.Success && !r.Doubtful
                ? "Sesiniz tanındı. Görüşmede konuşmalar \"Siz\" ve \"Karşı taraf\" diye ayrılacak."
                : r.Message;
        }
        finally
        {
            IsEnrolling = false;
            StartCommand.NotifyCanExecuteChanged();
        }
    }

    private static string IdleEnrollmentText =>
        "Sesinizi tanıtırsanız öneriler yalnız karşı tarafın sesine göre yapılır ve konuşma payınız ölçülür.";

    [ObservableProperty] private string _currentEmotion = "—";
    [ObservableProperty] private double _currentValence;
    [ObservableProperty] private double _currentArousal;
    [ObservableProperty] private string _congruenceBadge = string.Empty;

    /// <summary>Üç kaynaklı füzyonda yüzün nihai valence'a etkisi (11d); yüz katılmadıysa boş.</summary>
    [ObservableProperty] private string _faceContributionText = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(WaitingForSpeech)), NotifyCanExecuteChangedFor(nameof(EnrollCommand))] private bool _isCapturing;

    private bool _receivedThisRun;

    /// <summary>Koşu sürüyor ama bu koşuda henüz hiç analiz satırı gelmedi (ör. sessiz video: VAD tüm
    /// parçaları eler). Boş ekranın "bozuk" sanılmaması için açıklama gösterilir (11b minor).</summary>
    public bool WaitingForSpeech => IsCapturing && !IsPreparingModels && !IsFinishing && !_receivedThisRun;
    [ObservableProperty] private string _selectedFile = "(dosya seçilmedi)";
    [ObservableProperty] private string _currentConfidence = "—";
    [ObservableProperty] private SourceAnalysisVm _textAnalysis = SourceAnalysisVm.Empty("Sözlere göre");
    [ObservableProperty] private SourceAnalysisVm _audioAnalysis = SourceAnalysisVm.Empty("Ses tonuna göre");
    [ObservableProperty] private SourceAnalysisVm _finalAnalysis = SourceAnalysisVm.Empty("Genel sonuç");

    /// <summary>Canlı mikrofon mu kullanılsın (aksi halde seçili dosya)?</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsFileMode)), NotifyPropertyChangedFor(nameof(ShowEnrollment))] private bool _useMicrophone;

    /// <summary>Kamera modu: mikrofon + kamera (11b). <see cref="UseMicrophone"/> ile karşılıklı dışlayıcı.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsFileMode)), NotifyPropertyChangedFor(nameof(ShowEnrollment))] private bool _useCamera;

    /// <summary>Dosya modu mu (dosya seçici görünür)?</summary>
    public bool IsFileMode => !UseMicrophone && !UseCamera;

    /// <summary>Koşu sırasında oluşan hatanın (örn. reddedilen mikrofon izni) tam metni.
    /// <see cref="CongruenceBadge"/> kısa bir rozet olduğundan uzun/açıklayıcı hatalar buraya yazılır.</summary>
    [ObservableProperty] private string _captureError = string.Empty;

    /// <summary>"Durdur"a basıldı ama koşu henüz bitmedi: motor kanalda kalan chunk'ları drene
    /// edip (Whisper/ONNX ile) işliyor — bu birkaç saniye sürebilir. Kullanıcı bu sürede ekranın
    /// donduğunu/hiçbir şey olmadığını düşünmesin diye ayrı bir durum olarak gösterilir.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(WaitingForSpeech))] private bool _isFinishing;

    /// <summary>Dinleme rozetinin metni: normalde "Dinleniyor", Durdur sonrası (drene/analiz
    /// tamamlanana kadar) "Analiz tamamlanıyor…".</summary>
    [ObservableProperty] private string _captureStatusText = "Dinleniyor";

    /// <summary>Son koşuda kanal taşması veya "Durdur" sonrası drenaj sınırı/bütçesi yüzünden
    /// işlenemeden atlanan chunk sayısı (bkz. <see cref="IAnalysisRunner.SkippedChunkCount"/>,
    /// 08e düzeltme turu Fix 2). Sıfırsa kullanıcıya HİÇBİR ŞEY gösterilmez (bkz. <see cref="SkippedChunksNotice"/>);
    /// canlı mikrofonda düzenli bir durum olduğundan sessizce yutulmak yerine burada görünür kılınır.</summary>
    [ObservableProperty] private int _skippedChunkCount;

    /// <summary>UI'da bağlanacak Türkçe metin: <see cref="SkippedChunkCount"/> sıfırsa boş (şerit
    /// çöker, sessiz kalır) — sıfır değilse kısa bir not (alarm değil, dipnot niteliğinde).</summary>
    [ObservableProperty] private string _skippedChunksNotice = string.Empty;

    /// <summary>"Başlat"a basıldığı ANDA konuşmacı ayrımının bu oturum için açık olup olmadığını
    /// dürüstçe söyler (10c) — sessizce mi çalışıyor bilinmezliği kalmasın. Toggle daha SONRA
    /// açılırsa bu oturumu etkilemez (ses o ana kadar zaten kaydedilmemiştir); bu yüzden anlık
    /// okuma "Kaydet"e bastıktan sonra yeni bir koşu başlatılmasını gerektirir.</summary>
    [ObservableProperty] private string _diarizationHint = string.Empty;

    /// <summary>Modeller hazırlanıyor mu (warm-up)?</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(WaitingForSpeech)), NotifyCanExecuteChangedFor(nameof(EnrollCommand))] private bool _isPreparingModels;

    partial void OnIsPreparingModelsChanged(bool value)
    {
        if (value)
            CaptureStatusText = "Modeller hazırlanıyor...";
        else
            CaptureStatusText = IsFinishing ? "Analiz tamamlanıyor…" : "Dinleniyor";
    }

    partial void OnIsFinishingChanged(bool value) =>
        CaptureStatusText = value ? "Analiz tamamlanıyor…" : "Dinleniyor";

    partial void OnSkippedChunkCountChanged(int value) =>
        SkippedChunksNotice = value > 0
            ? $"{value} ses parçası yetişilemediği için atlandı."
            : string.Empty;

    public LiveViewModel(IAnalysisRunner runner, IAudioFilePicker? picker = null,
                         ISelectedAudio? selected = null, AppConfig? config = null,
                         ICaptureModeState? captureMode = null, IDiarizationState? diarization = null,
                         IVoiceEnrollment? enrollment = null)
    {
        _enrollment = enrollment;
        if (enrollment is not null) _enrollmentStatus = IdleEnrollmentText;
        _selectedSpeakerCount = SpeakerCountOptions[0];
        _runner = runner;
        _picker = picker;
        _selected = selected;
        var fusion = (config ?? AppConfig.Default).Fusion;
        Decision = new DecisionPanelViewModel((config ?? AppConfig.Default).Decision);
        _temperature = fusion.DistributionTemperature;
        _neutralRadius = fusion.NeutralRadius;
        _captureMode = captureMode;
        _diarization = diarization;
        // Profil değişince paneller ve ortak kare yeniden belirlenir.
        Decision.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Decision.SelectedProfile) && _previewBoxes is not null)
                SyncFacePanels(_previewBoxes);
        };
    }

    partial void OnUseMicrophoneChanged(bool value)
    {
        if (value) UseCamera = false;
        SyncCaptureMode();
    }

    partial void OnUseCameraChanged(bool value)
    {
        if (value) UseMicrophone = false;
        SyncCaptureMode();
    }

    private void SyncCaptureMode()
    {
        if (_captureMode is not null)
            _captureMode.Mode = UseCamera ? CaptureMode.Kamera : UseMicrophone ? CaptureMode.Mikrofon : CaptureMode.Dosya;
        StartCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Koşuyu durdurmayan uyarı (ör. görüntü kanalı hatası): ses analizi sürerken gösterilir.</summary>
    public void Warn(string message) =>
        CaptureError = string.IsNullOrEmpty(CaptureError) ? message : $"{CaptureError} {message}";

    public void Report(ChunkResult r)
    {
        var faces = _runner.FacesFor(r);
        _runChunks.Add(r);
        var row = new ChunkRowVm(r, faces);
        if (!_receivedThisRun)
        {
            _receivedThisRun = true;
            OnPropertyChanged(nameof(WaitingForSpeech));
        }
        Decision.OnWindow(r, faces);
        foreach (var f in faces) PanelFor(f.FaceIndex).Apply(f);
        Timeline.Add(row);
        CurrentEmotion = row.Emotion;
        CurrentValence = row.Valence;
        CurrentArousal = row.Arousal;
        CongruenceBadge = row.Badge;
        FaceContributionText = r.Fused.FaceValence is double fv
            ? $"Yüz dahil: konuşma {Signed(r.Fused.SpeechValence)} · yüz {Signed(fv)} → nihai {Signed(r.Fused.Combined.Valence)}"
            : string.Empty;

        // Üç kaynak ayrı ayrı — söz-ton uyumsuzluğu böylece ekranda görünür hale gelir.
        CurrentConfidence = EmotionLocalizer.Confidence(r.Fused.Confidence);
        TextAnalysis  = new SourceAnalysisVm("Sözlere göre", r.Fused.TextVa, _temperature, _neutralRadius);
        AudioAnalysis = new SourceAnalysisVm("Ses tonuna göre", r.Fused.ProsodyVa, _temperature, _neutralRadius);
        FinalAnalysis = new SourceAnalysisVm("Genel sonuç", r.Fused.Combined, _temperature, _neutralRadius);
    }

    private static string Signed(double v) =>
        (v < 0 ? "−" : "+") + Math.Abs(v).ToString("0.00", new System.Globalization.CultureInfo("tr-TR"));

    [RelayCommand]
    private async Task SelectFileAsync()
    {
        if (_picker is null) return;
        var path = await _picker.PickAudioFileAsync().ConfigureAwait(true);
        if (string.IsNullOrEmpty(path)) return;
        if (_selected is not null) _selected.Path = path;
        SelectedFile = System.IO.Path.GetFileName(path);
        StartCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        if (IsCapturing || IsPreparingModels) return;
        _receivedThisRun = false;
        IsCapturing = true;
        Decision.Start();
        ResetFacePanels();
        Decision.IsLocked = true;
        FaceContributionText = string.Empty; // önceki koşunun "Yüz dahil" satırı yeni oturumda sarkmasın
        DiarizationHint = _diarization?.Enabled == true
            ? "Konuşmacı ayrımı bu oturum için AÇIK — ses geçici olarak saklanacak, konuşmacılar Durdur sonrası Rapor'da görünür."
            : "Konuşmacı ayrımı KAPALI (Ayarlar'dan açıp Kaydet'e basarak bir sonraki oturumda etkinleştirebilirsiniz).";
        LastSpeakerCount = SelectedSpeakerCount?.Value ?? 2;
        if (_enrollment?.IsEnrolled == true && !IsFileMode)
        {
            if (_enrollmentUsed)
            {
                EnrollmentWarning = true;
                EnrollmentStatus = "Önceki görüşmenin ses tanıtması kullanılıyor. Bu görüşmeyi başka biri yönetiyorsa " +
                                   "durdurup \"Sesimi yeniden tanıt\" ile tanıtın; aksi halde roller yanlış atanır.";
            }
            _enrollmentUsed = true;
        }
        OnPropertyChanged(nameof(CanClearEnrollment));
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        _cts = new CancellationTokenSource();
        _run = RunAsync(_cts.Token);
    }

    private bool CanStart() => !IsCapturing && !IsPreparingModels && !IsEnrolling && (!IsFileMode || _selected?.Path != null);

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            CaptureError = string.Empty;
            CongruenceBadge = string.Empty; // önceki koşudan kalan rozet ("Maskeleme" vb.) sarkmasın
            SkippedChunkCount = 0; // önceki koşudan kalan sayı sarkmasın

            // Modelleri asenkron olarak warm-up ediyoruz
            IsPreparingModels = true;
            StartCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();

            await _runner.WarmUpAsync(ct).ConfigureAwait(true);

            IsPreparingModels = false;
            StartCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();

            var summary = await _runner.RunAsync(this, ct).ConfigureAwait(true);
            SkippedChunkCount = _runner.SkippedChunkCount;
            LastSessionId = _runner.LastSessionId;   // 10b: arka plan konuşmacı geçişinin hedefi
            
            if (summary.Outcome == AnalysisOutcome.Success)
            {
                Completed?.Invoke(summary);
            }
            else if (summary.Outcome == AnalysisOutcome.NoResult)
            {
                Warn("Analiz sonucu üretilemedi (hiç konuşma/ses algılanmadı).");
            }
            else
            {
                CaptureError = "Analiz başarısız oldu.";
            }
        }
        catch (OperationCanceledException) { /* Stop ile iptal — normal */ }
        // Sessiz yutma yok (08 deferred fix): hata uzun/açıklayıcı olabileceğinden (örn. mikrofon
        // izni reddi veya model yüklenememesi) tam metni gösterebilen CaptureError'a yazılır.
        catch (Exception ex) 
        { 
            CaptureError = ex.Message; 
        }
        finally 
        { 
            IsCapturing = false; 
            OnPropertyChanged(nameof(CanClearEnrollment));
            Decision.IsLocked = false;
            RefreshPreview();
            IsPreparingModels = false;
            IsFinishing = false; 
            StartCommand.NotifyCanExecuteChanged(); 
            StopCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        if (IsCapturing && !IsFinishing)
        {
            IsFinishing = true; 
            StopCommand.NotifyCanExecuteChanged();
        }
        _cts?.Cancel();
    }

    private bool CanStop() => IsCapturing && !IsFinishing;

    /// <summary>Test yardımcısı: aktif koşu görevini bekler (yoksa hemen döner).</summary>
    public Task WaitForIdleAsync() => _run ?? Task.CompletedTask;
}

/// <summary>Kişi sayısı seçeneği: <paramref name="Value"/> 0 ise "Bilmiyorum".</summary>
public sealed record SpeakerCountOption(int Value, string Text);
