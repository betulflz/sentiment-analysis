using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Desktop;

/// <summary>Ayarlar VM'i: mevcut <see cref="AppConfig"/>'ten okur, Save ile güncellenmiş config'i callback'e verir
/// (kalıcılık/uygulama composition root'ta). `with { }` ile immutable güncelleme.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppConfig _initial;
    private readonly Action<AppConfig> _onSave;
    private readonly IDiarizationState? _diarization;

    [ObservableProperty] private int _retentionDays;
    [ObservableProperty] private bool _keepAudio;
    /// <summary>Whisper'a yalnız bilinen dil kodu gider (serbest metin kalıcı STT bozulmasına yol açıyordu).</summary>
    public static readonly LanguageOption[] Options =
        [new("tr", "Türkçe"), new("en", "İngilizce"), new("auto", "Otomatik algıla")];
    public System.Collections.Generic.IReadOnlyList<LanguageOption> LanguageOptions => Options;
    [ObservableProperty] private LanguageOption _selectedLanguage;

    /// <summary>Konuşmacı ayrımı açık mı (10b). AppConfig immutable singleton olduğundan
    /// gerçek etki <see cref="IDiarizationState"/> üzerinden ANINDA uygulanır — kullanıcı
    /// kutuyu işaretleyip Kaydet'e bastığında uygulamayı yeniden başlatması gerekmez.</summary>
    [ObservableProperty] private bool _diarizationEnabled;

    /// <summary>Kaydet sonrası kısa bildirim (ne zaman uygulanacağı ya da yazma hatası); boşsa gösterilmez.</summary>
    [ObservableProperty] private string _savedNotice = string.Empty;

    public SettingsViewModel(AppConfig initial, Action<AppConfig> onSave, IDiarizationState? diarization = null)
    {
        _initial = initial;
        _onSave = onSave;
        _diarization = diarization;
        _diarizationEnabled = diarization?.Enabled ?? initial.Diarization.Enabled;
        _retentionDays = initial.Storage.AudioRetentionDays;
        _keepAudio = initial.Storage.KeepAudio;
        _selectedLanguage = System.Array.Find(Options, o => o.Code == (initial.Stt.Language ?? "auto")) ?? Options[0];
    }

    [RelayCommand]
    private void Save()
    {
        var updated = _initial with
        {
            Storage = _initial.Storage with { AudioRetentionDays = RetentionDays, KeepAudio = KeepAudio },
            Stt = _initial.Stt with { Language = SelectedLanguage.Code == "auto" ? null : SelectedLanguage.Code },
            Diarization = _initial.Diarization with { Enabled = DiarizationEnabled },
        };
        // Çalışma-anı durumunu da güncelle: config kopyası tek başına hiçbir şeyi değiştirmez.
        if (_diarization is not null) _diarization.Enabled = DiarizationEnabled;
        try
        {
            _onSave(updated);
            SavedNotice = "Kaydedildi. Konuşmacı ayrımı hemen; dil ve ses saklama ayarları uygulama yeniden açılınca geçerli olur.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            SavedNotice = "Ayarlar kaydedilemedi: ayar dosyasına yazılamadı.";
        }
    }
}

public sealed record LanguageOption(string Code, string Label)
{
    public override string ToString() => Label;
}
