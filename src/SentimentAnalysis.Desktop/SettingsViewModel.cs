using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Desktop;

/// <summary>Ayarlar VM'i: mevcut <see cref="AppConfig"/>'ten okur, Save ile güncellenmiş config'i callback'e verir
/// (kalıcılık/uygulama composition root'ta). `with { }` ile immutable güncelleme.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppConfig _initial;
    private readonly Action<AppConfig> _onSave;

    [ObservableProperty] private int _retentionDays;
    [ObservableProperty] private bool _keepAudio;
    [ObservableProperty] private string _language;

    public SettingsViewModel(AppConfig initial, Action<AppConfig> onSave)
    {
        _initial = initial;
        _onSave = onSave;
        _retentionDays = initial.Storage.AudioRetentionDays;
        _keepAudio = initial.Storage.KeepAudio;
        _language = initial.Stt.Language ?? "tr";
    }

    [RelayCommand]
    private void Save()
    {
        var updated = _initial with
        {
            Storage = _initial.Storage with { AudioRetentionDays = RetentionDays, KeepAudio = KeepAudio },
            Stt = _initial.Stt with { Language = Language },
        };
        _onSave(updated);
    }
}
