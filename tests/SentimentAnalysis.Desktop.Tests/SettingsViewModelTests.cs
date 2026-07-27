using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class SettingsViewModelTests
{
    [Fact]
    public void Ctor_LoadsFromAppConfig()
    {
        var cfg = AppConfig.Default with { Storage = AppConfig.Default.Storage with { AudioRetentionDays = 14, KeepAudio = false } };
        var vm = new SettingsViewModel(cfg, _ => { });
        Assert.Equal(14, vm.RetentionDays);
        Assert.False(vm.KeepAudio);
    }

    [Fact]
    public void Save_ProducesUpdatedAppConfig()
    {
        AppConfig? saved = null;
        var vm = new SettingsViewModel(AppConfig.Default, c => saved = c);
        vm.RetentionDays = 30;
        vm.KeepAudio = false;
        vm.SaveCommand.Execute(null);

        Assert.NotNull(saved);
        Assert.Equal(30, saved!.Storage.AudioRetentionDays);
        Assert.False(saved.Storage.KeepAudio);
    }
}
