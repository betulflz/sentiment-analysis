using SentimentAnalysis.Desktop;

namespace SentimentAnalysis.Desktop.Tests;

public class PercentFormatterTests
{
    [Fact]
    public void Empty_ReturnsEmpty() =>
        Assert.Empty(PercentFormatter.ToPercents(Array.Empty<double>()));

    [Fact]
    public void SingleShare_IsHundred() =>
        Assert.Equal(new[] { 100 }, PercentFormatter.ToPercents(new[] { 1.0 }));

    [Fact]
    public void EqualShares_SumToExactlyHundred()
    {
        // 3 eşit pay: 33.33... → naif yuvarlama 99 verir; en-büyük-kalan 100 vermeli.
        var p = PercentFormatter.ToPercents(new[] { 1.0 / 3, 1.0 / 3, 1.0 / 3 });
        Assert.Equal(100, p.Sum());
        Assert.Equal(new[] { 34, 33, 33 }, p);
    }

    [Fact]
    public void SevenEqualShares_SumToExactlyHundred()
    {
        var p = PercentFormatter.ToPercents(Enumerable.Repeat(1.0 / 7, 7).ToArray());
        Assert.Equal(100, p.Sum());
    }

    [Fact]
    public void EightEqualShares_SumToExactlyHundred()
    {
        // Duygu çarkının gerçek boyutu.
        var p = PercentFormatter.ToPercents(Enumerable.Repeat(0.125, 8).ToArray());
        Assert.Equal(100, p.Sum());
        Assert.All(p, x => Assert.InRange(x, 12, 13));
    }

    [Fact]
    public void LargestRemainder_GoesToBiggestFraction()
    {
        // 0.605 / 0.395 → 60.5 / 39.5; kalan 1 puan daha büyük artığa gitmeli (ikisi de .5 → ilk indeks)
        var p = PercentFormatter.ToPercents(new[] { 0.605, 0.395 });
        Assert.Equal(100, p.Sum());
        Assert.Equal(61, p[0]);
        Assert.Equal(39, p[1]);
    }

    [Fact]
    public void InputOrderIsPreserved()
    {
        var p = PercentFormatter.ToPercents(new[] { 0.1, 0.7, 0.2 });
        Assert.Equal(100, p.Sum());
        Assert.True(p[1] > p[2] && p[2] > p[0], "sıra korunmalı, değerler paya karşılık gelmeli");
    }

    [Fact]
    public void UnnormalizedInput_IsNormalized()
    {
        // Toplamı 1 olmayan girdi de 100'e ölçeklenmeli.
        var p = PercentFormatter.ToPercents(new[] { 2.0, 2.0 });
        Assert.Equal(new[] { 50, 50 }, p);
    }

    [Fact]
    public void AllZero_ReturnsAllZero_NoInventedDistribution()
    {
        var p = PercentFormatter.ToPercents(new[] { 0.0, 0.0, 0.0 });
        Assert.Equal(new[] { 0, 0, 0 }, p);
    }

    [Fact]
    public void NegativeSharesAreTreatedAsZero()
    {
        var p = PercentFormatter.ToPercents(new[] { -0.5, 1.0 });
        Assert.Equal(100, p.Sum());
        Assert.Equal(0, p[0]);
    }

    [Fact]
    public void ManyTinyShares_StillSumToHundred()
    {
        var shares = Enumerable.Range(0, 50).Select(i => (i + 1) / 1275.0).ToArray();
        Assert.Equal(100, PercentFormatter.ToPercents(shares).Sum());
    }
}
