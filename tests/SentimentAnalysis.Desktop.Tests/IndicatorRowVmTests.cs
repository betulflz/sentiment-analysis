using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class IndicatorRowVmTests
{
    [Fact]
    public void Artan_KirmiziOkVeBasSon()
    {
        var row = new IndicatorRowVm("Gerilim", new[] { 0.2, 0.3, 0.5, 0.6, 0.8 });
        Assert.Equal("↑ arttı %20 → %80", row.TrendText);
        Assert.True(row.TrendUp);
        Assert.False(row.TrendDown);
        Assert.False(row.TrendFlat);
    }

    [Fact]
    public void Azalan_YesilOk()
    {
        var row = new IndicatorRowVm("İlgi kaybı", new[] { 0.75, 0.6, 0.4, 0.3, 0.15 });
        Assert.Equal("↓ azaldı %75 → %15", row.TrendText);
        Assert.True(row.TrendDown);
    }

    [Fact]
    public void BasVeSonUctebirOrtalamasiyla_TekSicramaEgilimSayilmaz()
    {
        // 9 pencere → baş/son 3'er pencere: %10 vs %17 — ortadaki sıçrama ve sondaki tek tepe eğilim değil.
        var row = new IndicatorRowVm("Gerilim", new[] { 0.1, 0.1, 0.1, 0.5, 0.5, 0.5, 0.1, 0.1, 0.3 });
        Assert.True(row.TrendFlat);
        Assert.Equal("→ belirgin değişim yok (ortalama %26)", row.TrendText);
    }

    [Fact]
    public void TekPencere_Degisimyok()
    {
        var row = new IndicatorRowVm("Gerilim", new[] { 0.4 });
        Assert.True(row.TrendFlat);
        Assert.Equal("→ belirgin değişim yok (ortalama %40)", row.TrendText);
    }

    [Fact]
    public void Bos_Metinsiz()
    {
        var row = new IndicatorRowVm("Gerilim", System.Array.Empty<double>());
        Assert.True(row.TrendFlat);
        Assert.Equal(string.Empty, row.TrendText);
    }
}
