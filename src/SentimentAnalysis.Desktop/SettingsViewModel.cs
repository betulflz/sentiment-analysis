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

    /// <summary>Seçilebilir dil değerleri. "auto" Whisper'ın dili kendisinin algılamasıdır —
    /// varsayılan budur, çünkü sabitlenmiş bir dil başka dildeki konuşmayı zorla o dile
    /// çözdürüp anlamsız transkript üretir.</summary>
    public IReadOnlyList<string> LanguageOptions { get; } = new[] { SttConfig.AutoLanguage, "tr", "en" };

    [ObservableProperty] private int _retentionDays;
    [ObservableProperty] private bool _keepAudio;
    [ObservableProperty] private string _language;

    public SettingsViewModel(AppConfig initial, Action<AppConfig> onSave)
    {
        _initial = initial;
        _onSave = onSave;
        _retentionDays = initial.Storage.AudioRetentionDays;
        _keepAudio = initial.Storage.KeepAudio;
        // null (otomatik) sessizce "tr"ye çevrilmemeli — kullanıcı sabitlemediyse sabitlenmiş görünmesin.
        _language = initial.Stt.Language ?? SttConfig.AutoLanguage;
    }

    [RelayCommand]
    private void Save()
    {
        var updated = _initial with
        {
            Storage = _initial.Storage with { AudioRetentionDays = RetentionDays, KeepAudio = KeepAudio },
            Stt = _initial.Stt with { Language = NormalizeLanguage(Language) },
        };
        _onSave(updated);
    }

    /// <summary>"auto"/boş → null (otomatik algılama); diğerleri küçük harfe indirilir.</summary>
    private static string? NormalizeLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim().ToLowerInvariant();
        return v == SttConfig.AutoLanguage ? null : v;
    }
}
