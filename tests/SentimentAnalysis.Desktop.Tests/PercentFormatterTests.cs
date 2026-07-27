using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class PercentFormatterTests
{
    [Fact]
    public void ToPercents_ExactValues_MapDirectly()
    {
        var result = PercentFormatter.ToPercents(new[] { 0.5, 0.3, 0.2 });

        Assert.Equal(new[] { 50, 30, 20 }, result);
    }

    [Fact]
    public void ToPercents_ThirdsSumToExactlyHundred()
    {
        var result = PercentFormatter.ToPercents(new[] { 1.0 / 3, 1.0 / 3, 1.0 / 3 });

        Assert.Equal(100, result.Sum());
    }

    // Naif yuvarlamanın bozulduğu asıl senaryo: 8 eşit pay (%12.5 → taban %12, toplam 96).
    [Fact]
    public void ToPercents_EightEqualShares_SumToExactlyHundred()
    {
        var shares = Enumerable.Repeat(0.125, 8).ToArray();

        var result = PercentFormatter.ToPercents(shares);

        Assert.Equal(100, result.Sum());
        Assert.Equal(8, result.Count);
    }

    [Fact]
    public void ToPercents_LargestRemainder_GoesToBiggestFraction()
    {
        // 0.455 → 45.5 (kalan .5), 0.445 → 44.5 (kalan .5), 0.10 → 10.0 (kalan 0)
        // Tabanlar: 45 + 44 + 10 = 99. Artan 1 puan en büyük kalana (ilk .5) gider.
        var result = PercentFormatter.ToPercents(new[] { 0.455, 0.445, 0.10 });

        Assert.Equal(100, result.Sum());
        Assert.Equal(46, result[0]);
        Assert.Equal(44, result[1]);
        Assert.Equal(10, result[2]);
    }

    [Fact]
    public void ToPercents_SingleShare_IsHundred()
    {
        Assert.Equal(new[] { 100 }, PercentFormatter.ToPercents(new[] { 1.0 }));
    }

    [Fact]
    public void ToPercents_Empty_ReturnsEmpty()
    {
        Assert.Empty(PercentFormatter.ToPercents(new List<double>()));
    }

    [Fact]
    public void ToPercents_NeverReturnsNegative()
    {
        var result = PercentFormatter.ToPercents(new[] { 0.999, 0.001 });

        Assert.All(result, p => Assert.True(p >= 0));
    }
}
