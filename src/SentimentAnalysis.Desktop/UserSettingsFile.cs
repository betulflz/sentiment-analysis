using System;
using System.IO;
using System.Text.Json;
using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Desktop;

/// <summary>İt.5: kullanıcının Ayarlar ekranındaki seçimleri — JSON dosyasına yazılır, açılışta <see cref="AppConfig"/>'e
/// uygulanır. Yalnız Ayarlar ekranındaki alanlar saklanır; diğer her şey koddaki varsayılanlardan gelir.</summary>
public static class UserSettingsFile
{
    private const int MaxRetentionDays = 3650;

    private sealed record Dto(int? AudioRetentionDays, bool? KeepAudio, string? Language, bool? DiarizationEnabled);

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SentimentAnalysis", "settings.json");

    public static void Save(string path, AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var dto = new Dto(config.Storage.AudioRetentionDays, config.Storage.KeepAudio, config.Stt.Language, config.Diarization.Enabled);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(dto));
        File.Move(tmp, path, overwrite: true); // yarım yazılmış dosya kalmasın
    }

    /// <summary>Dosya yoksa ya da okunamıyorsa <paramref name="config"/> aynen döner (açılış asla ayar dosyası yüzünden çökmez).
    /// Geçersiz değerler (ör. saklama süresi ≤ 0) yok sayılır.</summary>
    public static AppConfig Apply(string path, AppConfig config)
    {
        Dto? dto;
        try
        {
            if (!File.Exists(path)) return config;
            dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return config;
        }
        if (dto is null) return config;

        int days = dto.AudioRetentionDays is int d && d > 0 && d <= MaxRetentionDays ? d : config.Storage.AudioRetentionDays;
        string? language = dto.Language is { Length: > 0 and <= 8 } l ? l : config.Stt.Language;
        return config with
        {
            Storage = config.Storage with { AudioRetentionDays = days, KeepAudio = dto.KeepAudio ?? config.Storage.KeepAudio },
            Stt = config.Stt with { Language = language },
            Diarization = config.Diarization with { Enabled = dto.DiarizationEnabled ?? config.Diarization.Enabled },
        };
    }
}
