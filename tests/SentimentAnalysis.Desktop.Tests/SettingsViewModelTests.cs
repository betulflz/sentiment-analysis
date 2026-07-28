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

    // --- 10b: konuşmacı ayrımı anahtarı ---

    private sealed class FakeDiarizationState : SentimentAnalysis.Application.Abstractions.IDiarizationState
    {
        public bool Enabled { get; set; }
    }

    [Fact]
    public void Save_KonusmaciAyrimi_CalismaAniDurumunuGuncellerAnında()
    {
        // AppConfig immutable singleton olduğundan yalnızca config kopyasını güncellemek
        // HİÇBİR ŞEYİ değiştirmez — kullanıcı kutuyu işaretler, özellik yine kapalı kalırdı.
        var state = new FakeDiarizationState { Enabled = false };
        var vm = new SettingsViewModel(AppConfig.Default, _ => { }, state);

        vm.DiarizationEnabled = true;
        vm.SaveCommand.Execute(null);

        Assert.True(state.Enabled);
    }

    [Fact]
    public void Save_KonusmaciAyrimi_ConfigKopyasinaDaYansir()
    {
        AppConfig? saved = null;
        var vm = new SettingsViewModel(AppConfig.Default, c => saved = c, new FakeDiarizationState());

        vm.DiarizationEnabled = true;
        vm.SaveCommand.Execute(null);

        Assert.True(saved!.Diarization.Enabled);
    }

    [Fact]
    public void Baslangicta_DurumdanOkur_ConfigtenDegil()
    {
        var state = new FakeDiarizationState { Enabled = true };

        var vm = new SettingsViewModel(AppConfig.Default, _ => { }, state);

        Assert.True(vm.DiarizationEnabled);   // AppConfig.Default kapalı olsa bile
    }
}
