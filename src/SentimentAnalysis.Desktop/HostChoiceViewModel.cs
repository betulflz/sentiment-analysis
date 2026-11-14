using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>"Siz hangisiniz?" paneli (dosya modu, karşı tarafa odak). Kullanıcı bir konuşmacıyı ya da "Emin değilim"i
/// seçip onaylar; <see cref="Result"/> seçilen konuşmacıyla (ya da null = karışık yol) tamamlanır.</summary>
public sealed partial class HostChoiceViewModel : ObservableObject
{
    private readonly SpeakerPreparationResult _prepared;
    private readonly ISpeakerSamplePlayer? _player;
    private readonly TaskCompletionSource<SpeakerId?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _playback;

    public HostChoiceViewModel(SpeakerPreparationResult prepared, ISpeakerSamplePlayer? player)
    {
        _prepared = prepared;
        _player = player;
        Rows = prepared.Choices.Select(c => new HostChoiceRowVm(this, c)).ToArray();
    }

    public IReadOnlyList<HostChoiceRowVm> Rows { get; }
    public bool CanPlay => _player?.IsSupported == true;
    public Task<SpeakerId?> Result => _result.Task;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ConfirmCommand))] private HostChoiceRowVm? _selectedRow;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ConfirmCommand))] private bool _notSure;

    partial void OnSelectedRowChanged(HostChoiceRowVm? value)
    {
        if (value is not null) NotSure = false;
        foreach (var r in Rows) r.RefreshSelected();
    }
    partial void OnNotSureChanged(bool value) { if (value) SelectedRow = null; }

    private bool CanConfirm() => SelectedRow is not null || NotSure;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        StopPlayback();
        _result.TrySetResult(NotSure ? null : SelectedRow?.Choice.Speaker);
    }

    /// <summary>Panel kapanırken (Durdur): çalmayı durdur, sonucu iptal et.</summary>
    public void Cancel()
    {
        StopPlayback();
        _result.TrySetCanceled();
    }

    internal float[] SampleOf(SpeakerChoice c)
    {
        var audio = _prepared.Audio;
        int a = Math.Clamp((int)(c.SampleStart.TotalSeconds * 16000), 0, audio.Length);
        int b = Math.Clamp((int)(c.SampleEnd.TotalSeconds * 16000), a, audio.Length);
        return audio[a..b];
    }

    internal async Task PlayAsync(SpeakerChoice c)
    {
        if (!CanPlay) return;
        StopPlayback();
        var cts = _playback = new CancellationTokenSource();
        await _player!.PlayAsync(SampleOf(c), cts.Token).ConfigureAwait(true);
    }

    private void StopPlayback()
    {
        var p = Interlocked.Exchange(ref _playback, null);
        p?.Cancel();
    }
}

public sealed partial class HostChoiceRowVm(HostChoiceViewModel owner, SpeakerChoice choice) : ObservableObject
{
    public SpeakerChoice Choice { get; } = choice;
    public string Label => Choice.Speaker.DefaultLabel;
    public string TalkText => $"{FormatDuration(Choice.TalkSeconds)} konuştu";
    public bool CanPlay => owner.CanPlay;

    public bool IsSelected
    {
        get => ReferenceEquals(owner.SelectedRow, this);
        set { if (value) owner.SelectedRow = this; }
    }

    internal void RefreshSelected() => OnPropertyChanged(nameof(IsSelected));

    [RelayCommand]
    private Task PlayAsync() => owner.PlayAsync(Choice);

    public static string FormatDuration(double seconds)
    {
        int s = (int)Math.Round(seconds);
        return s >= 60 ? $"{s / 60} dk {s % 60:00} sn" : $"{s} sn";
    }
}
