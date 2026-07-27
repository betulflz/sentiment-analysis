using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public LiveViewModel(IAnalysisRunner runner, IAudioFilePicker? picker = null, ISelectedAudio? selected = null)
    {
        _runner = runner;
        _picker = picker;
        _selected = selected;
    }

    public void Report(ChunkResult r)
    {
        var row = new ChunkRowVm(r);
        Timeline.Add(row);
        CurrentEmotion = row.Emotion;
        CurrentValence = row.Valence;
        CurrentArousal = row.Arousal;
        CongruenceBadge = row.Badge;
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

    private bool CanStart() => !IsCapturing && _selected?.Path != null;

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var summary = await _runner.RunAsync(this, ct).ConfigureAwait(true);
            Completed?.Invoke(summary);
        }
        catch (OperationCanceledException) { /* Stop ile iptal — normal */ }
        catch (Exception ex) { CongruenceBadge = "Hata: " + ex.Message; } // sessiz yutma yok (08 deferred fix)
        finally { IsCapturing = false; StartCommand.NotifyCanExecuteChanged(); }
    }

    [RelayCommand]
    private void Stop() => _cts?.Cancel();

    /// <summary>Test yardımcısı: aktif koşu görevini bekler (yoksa hemen döner).</summary>
    public Task WaitForIdleAsync() => _run ?? Task.CompletedTask;
}
