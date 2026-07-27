using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SentimentAnalysis.Desktop;

/// <summary>Kök VM: alt görünümleri barındırır + basit navigasyon.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    public LiveViewModel Live { get; }
    public ReportViewModel Report { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty] private object _currentView;

    public MainViewModel(LiveViewModel live, ReportViewModel report, HistoryViewModel history, SettingsViewModel settings)
    {
        Live = live; Report = report; History = history; Settings = settings;
        _currentView = live;

        Live.Completed += s =>
        {
            Report.LoadSummary(s);
            History.RefreshCommand.Execute(null);
            CurrentView = Report;
        };
    }

    [RelayCommand] private void ShowLive() => CurrentView = Live;
    [RelayCommand] private void ShowReport() => CurrentView = Report;
    [RelayCommand] private void ShowHistory() => CurrentView = History;
    [RelayCommand] private void ShowSettings() => CurrentView = Settings;
}
