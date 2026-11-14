using System;
using System.IO;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

/// <summary>İt.5: Ayarlar diske yazılır ve açılışta AppConfig'e uygulanır.</summary>
public class UserSettingsFileTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), "sa-settings-" + Guid.NewGuid().ToString("N"), "settings.json");

    [Fact]
    public void KaydetVeOku_AyniAyarlarUygulanir()
    {
        var path = TempPath();
        var changed = AppConfig.Default with
        {
            Storage = AppConfig.Default.Storage with { AudioRetentionDays = 30, KeepAudio = false },
            Stt = AppConfig.Default.Stt with { Language = "en" },
            Diarization = AppConfig.Default.Diarization with { Enabled = true },
        };
        UserSettingsFile.Save(path, changed);

        var applied = UserSettingsFile.Apply(path, AppConfig.Default);

        Assert.Equal(30, applied.Storage.AudioRetentionDays);
        Assert.False(applied.Storage.KeepAudio);
        Assert.Equal("en", applied.Stt.Language);
        Assert.True(applied.Diarization.Enabled);
        // Kaydedilmeyen ayarlar dokunulmadan kalır.
        Assert.Equal(AppConfig.Default.Fusion, applied.Fusion);
    }

    [Fact]
    public void DosyaYoksa_VarsayilanAynen()
    {
        var d = AppConfig.Default;
        Assert.Same(d, UserSettingsFile.Apply(TempPath(), d));
    }

    [Fact]
    public void BozukDosya_VarsayilanAynen_Cokmez()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ bozuk");
        var d = AppConfig.Default;
        Assert.Same(d, UserSettingsFile.Apply(path, d));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100000)]
    public void GecersizSaklamaSuresi_Yoksayilir(int days)
    {
        var path = TempPath();
        UserSettingsFile.Save(path, AppConfig.Default with { Storage = AppConfig.Default.Storage with { AudioRetentionDays = days } });
        Assert.Equal(AppConfig.Default.Storage.AudioRetentionDays, UserSettingsFile.Apply(path, AppConfig.Default).Storage.AudioRetentionDays);
    }
}
