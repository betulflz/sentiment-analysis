using System.Linq;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class SourceAnalysisVmTests
{
    private const double T = 0.35;

    [Fact]
    public void Ctor_ExposesTitleAndAxes()
    {
        var vm = new SourceAnalysisVm("Metin Analizi", new ValenceArousal(0.62, 0.41), T);

        Assert.Equal("Metin Analizi", vm.Title);
        Assert.Equal(0.62, vm.Valence, precision: 6);
        Assert.Equal(0.41, vm.Arousal, precision: 6);
        Assert.True(vm.HasData);
    }

    [Fact]
    public void Ctor_DominantEmotionIsLocalizedAndMatchesWheel()
    {
        var va = new ValenceArousal(-0.8, 0.9);

        var vm = new SourceAnalysisVm("Ses Analizi", va, T);

        Assert.Equal("Kızgın", vm.Emotion);
        Assert.Equal("Kızgın", vm.Shares[0].Emotion);
    }

    [Fact]
    public void Ctor_ProducesEightSharesSummingToHundred()
    {
        var vm = new SourceAnalysisVm("Nihai Karar", new ValenceArousal(0.1, 0.5), T);

        Assert.Equal(8, vm.Shares.Count);
        Assert.Equal(100, vm.Shares.Sum(s => s.Percent));
    }

    [Fact]
    public void Ctor_SharesAreSortedDescending()
    {
        var vm = new SourceAnalysisVm("Nihai Karar", new ValenceArousal(0.4, 0.7), T);

        for (int i = 1; i < vm.Shares.Count; i++)
            Assert.True(vm.Shares[i - 1].Percent >= vm.Shares[i].Percent);
    }

    [Fact]
    public void Empty_HasNoDataAndNoShares()
    {
        var vm = SourceAnalysisVm.Empty("Metin Analizi");

        Assert.Equal("Metin Analizi", vm.Title);
        Assert.False(vm.HasData);
        Assert.Empty(vm.Shares);
        Assert.Equal("—", vm.Emotion);
    }

    [Fact]
    public void Ctor_AxisSummaryUsesPlainTurkishForKnownVaPoint()
    {
        var vm = new SourceAnalysisVm("Metin Analizi", new ValenceArousal(0.62, 0.41), T);

        Assert.Equal("Çok olumlu · Orta canlılıkta", vm.AxisSummary);
    }

    [Fact]
    public void Empty_AxisSummaryIsEmpty()
    {
        var vm = SourceAnalysisVm.Empty("Metin Analizi");

        Assert.Equal(string.Empty, vm.AxisSummary);
    }

    [Fact]
    public void Ctor_NotrYaricapi_SutunEtiketiniDegistirir()
    {
        // Kullanıcının gördüğü sütun başlığı yarıçap kuralına UYMALI; yoksa zaman çizelgesi
        // "Sakin" derken sütun "Nötr" der ve iyileştirme ekranda görünmez.
        var va = new ValenceArousal(0.3, 0.5);

        Assert.Equal("Nötr", new SourceAnalysisVm("Nihai Karar", va, T).Emotion);
        Assert.Equal("Sakin", new SourceAnalysisVm("Nihai Karar", va, T, neutralRadius: 0.2).Emotion);
    }

    [Fact]
    public void Ctor_NotrYaricapi_PaylarHalaYuzdeYuz()
    {
        var vm = new SourceAnalysisVm("Nihai Karar", new ValenceArousal(0.3, 0.5), T, neutralRadius: 0.2);

        Assert.Equal(8, vm.Shares.Count);
        Assert.Equal(100, vm.Shares.Sum(s => s.Percent));
        Assert.Equal(0, vm.Shares.Single(s => s.Emotion == "Nötr").Percent);
    }
}
