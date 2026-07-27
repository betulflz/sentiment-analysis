using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Canlı analiz görünümü VM'i. <see cref="IProgress{ChunkResult}"/> olarak pipeline'a verilir;
/// gelen her chunk çizelgeye + güncel VA/duygu/rozete yansır. Start/Stop koşuyu <see cref="IAnalysisRunner"/>
/// üzerinden yönetir. UI-agnostik (headless test).</summary>
public sealed partial class LiveViewModel : ObservableObject, IProgress<ChunkResult>
{
    private readonly IAnalysisRunner _runner;
    private readonly IAudioFilePicker? _picker;
    private readonly ISelectedAudio? _selected;
    private readonly double _temperature;
    private readonly ICaptureModeState? _captureMode;
    private CancellationTokenSource? _cts;
    private Task? _run;

    public ObservableCollection<ChunkRowVm> Timeline { get; } = new();

    /// <summary>Koşu bittiğinde raised olur; sonucu (Rapor'a devretmek gibi) dinleyicilere iletir.</summary>
    public event Action<MeetingSummary>? Completed;

    [ObservableProperty] private string _currentEmotion = "—";
    [ObservableProperty] private double _currentValence;
    [ObservableProperty] private double _currentArousal;
    [ObservableProperty] private string _congruenceBadge = string.Empty;
    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private string _selectedFile = "(dosya seçilmedi)";
    [ObservableProperty] private string _currentConfidence = "—";
    [ObservableProperty] private SourceAnalysisVm _textAnalysis = SourceAnalysisVm.Empty("Metin Analizi");
    [ObservableProperty] private SourceAnalysisVm _audioAnalysis = SourceAnalysisVm.Empty("Ses Analizi");
    [ObservableProperty] private SourceAnalysisVm _finalAnalysis = SourceAnalysisVm.Empty("Nihai Karar");

    /// <summary>Canlı mikrofon mu kullanılsın (aksi halde seçili dosya)?</summary>
    [ObservableProperty] private bool _useMicrophone;

    /// <summary>Koşu sırasında oluşan hatanın (örn. reddedilen mikrofon izni) tam metni.
    /// <see cref="CongruenceBadge"/> kısa bir rozet olduğundan uzun/açıklayıcı hatalar buraya yazılır.</summary>
    [ObservableProperty] private string _captureError = string.Empty;

    public LiveViewModel(IAnalysisRunner runner, IAudioFilePicker? picker = null,
                         ISelectedAudio? selected = null, AppConfig? config = null,
                         ICaptureModeState? captureMode = null)
    {
        _runner = runner;
        _picker = picker;
        _selected = selected;
        _temperature = (config ?? AppConfig.Default).Fusion.DistributionTemperature;
        _captureMode = captureMode;
    }

    partial void OnUseMicrophoneChanged(bool value)
    {
        if (_captureMode is not null)
            _captureMode.Mode = value ? CaptureMode.Mikrofon : CaptureMode.Dosya;
        StartCommand.NotifyCanExecuteChanged();
    }

    public void Report(ChunkResult r)
    {
        var row = new ChunkRowVm(r);
        Timeline.Add(row);
        CurrentEmotion = row.Emotion;
        CurrentValence = row.Valence;
        CurrentArousal = row.Arousal;
        CongruenceBadge = row.Badge;

        // Üç kaynak ayrı ayrı — söz-ton uyumsuzluğu böylece ekranda görünür hale gelir.
        CurrentConfidence = EmotionLocalizer.Confidence(r.Fused.Confidence);
        TextAnalysis  = new SourceAnalysisVm("Metin Analizi", r.Fused.TextVa, _temperature);
        AudioAnalysis = new SourceAnalysisVm("Ses Analizi", r.Fused.ProsodyVa, _temperature);
        FinalAnalysis = new SourceAnalysisVm("Nihai Karar", r.Fused.Combined, _temperature);
    }

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
        if (IsCapturing) return;
        IsCapturing = true;
        StartCommand.NotifyCanExecuteChanged();
        _cts = new CancellationTokenSource();
        _run = RunAsync(_cts.Token);
    }

    private bool CanStart() => !IsCapturing && (UseMicrophone || _selected?.Path != null);

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            CaptureError = string.Empty;
            CongruenceBadge = string.Empty; // önceki koşudan kalan rozet ("Maskeleme" vb.) sarkmasın
            var summary = await _runner.RunAsync(this, ct).ConfigureAwait(true);
            Completed?.Invoke(summary);
        }
        catch (OperationCanceledException) { /* Stop ile iptal — normal */ }
        // Sessiz yutma yok (08 deferred fix): hata uzun/açıklayıcı olabileceğinden (örn. mikrofon
        // izni reddi) küçük rozete değil, tam metni gösterebilen CaptureError'a yazılır.
        catch (Exception ex) { CaptureError = ex.Message; }
        finally { IsCapturing = false; StartCommand.NotifyCanExecuteChanged(); }
    }

    [RelayCommand]
    private void Stop() => _cts?.Cancel();

    /// <summary>Test yardımcısı: aktif koşu görevini bekler (yoksa hemen döner).</summary>
    public Task WaitForIdleAsync() => _run ?? Task.CompletedTask;
}
