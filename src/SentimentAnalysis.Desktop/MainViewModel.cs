using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;

namespace SentimentAnalysis.Desktop;

/// <summary>Kök VM: alt görünümleri barındırır + basit navigasyon.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISpeakerAnalysis? _speakers;
    private readonly AppConfig? _config;
    private readonly Action<Action> _dispatch;

    public LiveViewModel Live { get; }
    public ReportViewModel Report { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }

    /// <summary>Arka plan konuşmacı geçişinin görevi — testlerin bekleyebilmesi için açıkta.
    /// Üretimde kimse beklemez (rapor hemen gelir, konuşmacılar sonra düşer).</summary>
    public Task? SpeakerAnalysisTask { get; private set; }

    [ObservableProperty] private object _currentView;

    /// <param name="speakers">Konuşmacı ayrımı servisi (10b). <c>null</c> ise özellik hiç
    /// denenmez ve rapor bugünküyle bire bir aynı davranır.</param>
    /// <param name="dispatch">UI güncellemelerini doğru iş parçacığına taşıyan eylem. Varsayılan
    /// satır-içi çalıştırmadır (testler); Avalonia katmanı <c>Dispatcher.UIThread.Post</c> geçer.
    /// Bu, 08e'de yaşanan "arka plan iş parçacığından UI koleksiyonuna yazma" hatasının
    /// tekrarlanmaması için EXPLICIT tutulur — <c>Desktop</c> projesi Avalonia'sız kalmalıdır.</param>
    public MainViewModel(LiveViewModel live, ReportViewModel report, HistoryViewModel history,
                         SettingsViewModel settings, ISpeakerAnalysis? speakers = null,
                         AppConfig? config = null, Action<Action>? dispatch = null)
    {
        Live = live; Report = report; History = history; Settings = settings;
        _speakers = speakers;
        _config = config;
        _dispatch = dispatch ?? (a => a());
        _currentView = live;

        Live.Completed += s =>
        {
            Report.LoadSummary(s);
            History.RefreshCommand.Execute(null);
            CurrentView = Report;

            // Konuşmacı ayrımı raporu BEKLETMEZ: özet anında gösterilir, konuşmacılar
            // arka planda hesaplanıp sonradan düşer (spec 10 S2; 08e dersi: "Durdur"
            // sonrası kullanıcıyı bekletme).
            SpeakerAnalysisTask = StartSpeakerAnalysisAsync();
        };
    }

    private async Task StartSpeakerAnalysisAsync()
    {
        if (_speakers is null || !_speakers.Enabled) return;
        if (Live.LastSessionId is not Guid sessionId) return;

        _dispatch(Report.SetDiarizationRunning);

        SpeakerAnalysisResult result;
        try
        {
            result = await _speakers.RunAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Arka plan işi uygulamayı çökertmemeli; kullanıcıya dürüstçe "başarısız" denir.
            _dispatch(() => Report.SetDiarizationOutcome(DiarizationOutcome.Failed));
            return;
        }

        _dispatch(() =>
        {
            if (result.Outcome == DiarizationOutcome.Completed)
            {
                var minOverlap = _config?.Diarization.MinOverlapRatio ?? 0.5;
                Report.ApplySpeakers(result.Transcript, result.Speakers, minOverlap);
                Report.ApplySpeakerEmotions(result.Chunks, result.Speakers, minOverlap, result.SpeakerProsody);
            }

            // Sonuç DAİMA bildirilir — ApplySpeakers'tan SONRA çağrılır, çünkü durum metni
            // satır üretilip üretilmediğine bakar ("ayırt edilemedi" ile "hazır" ayrımı).
            Report.SetDiarizationOutcome(result.Outcome);
        });
    }

    [RelayCommand] private void ShowLive() => CurrentView = Live;
    [RelayCommand] private void ShowReport() => CurrentView = Report;
    [RelayCommand] private void ShowHistory() => CurrentView = History;
    [RelayCommand] private void ShowSettings() => CurrentView = Settings;
}
