using System;
using System.Linq;
using Xunit;

namespace SentimentAnalysis.Core.Tests;

public class EmotionDistributionTests
{
    private const double T = 0.35;   // FusionConfig.DistributionTemperature varsayılanı

    [Fact]
    public void Distribute_ReturnsAllEightEmotionsExactlyOnce()
    {
        var shares = EmotionWheel.Distribute(new ValenceArousal(0.2, 0.4), T);

        Assert.Equal(8, shares.Count);
        Assert.Equal(8, shares.Select(s => s.Emotion).Distinct().Count());
    }

    [Fact]
    public void Distribute_SharesSumToOne()
    {
        var shares = EmotionWheel.Distribute(new ValenceArousal(-0.3, 0.7), T);

        Assert.Equal(1.0, shares.Sum(s => s.Share), precision: 9);
    }

    [Fact]
    public void Distribute_AllSharesArePositive()
    {
        var shares = EmotionWheel.Distribute(new ValenceArousal(0.9, 0.95), T);

        Assert.All(shares, s => Assert.True(s.Share > 0.0, $"{s.Emotion} payı pozitif olmalı"));
    }

    [Fact]
    public void Distribute_IsSortedByShareDescending()
    {
        var shares = EmotionWheel.Distribute(new ValenceArousal(0.5, 0.2), T);

        for (int i = 1; i < shares.Count; i++)
            Assert.True(shares[i - 1].Share >= shares[i].Share, $"{i}. öğe sıralamayı bozuyor");
    }

    // En kritik test: Distribute ile Map asla çelişmez.
    [Theory]
    [InlineData(0.6, 0.9)]
    [InlineData(0.8, 0.5)]
    [InlineData(-0.8, 0.9)]
    [InlineData(0.0, 0.5)]
    [InlineData(0.13, 0.42)]
    [InlineData(-0.55, 0.66)]
    public void Distribute_TopShareAlwaysMatchesMap(double v, double a)
    {
        var va = new ValenceArousal(v, a);

        Assert.Equal(EmotionWheel.Map(va), EmotionWheel.Distribute(va, T)[0].Emotion);
    }

    [Fact]
    public void Distribute_LowerTemperature_SharpensDominantShare()
    {
        var va = new ValenceArousal(0.75, 0.5);

        double sharp = EmotionWheel.Distribute(va, 0.10)[0].Share;
        double flat  = EmotionWheel.Distribute(va, 1.00)[0].Share;

        Assert.True(sharp > flat, $"küçük T daha keskin olmalı (sharp={sharp}, flat={flat})");
    }

    [Fact]
    public void Distribute_VeryHighTemperature_ApproachesUniform()
    {
        var shares = EmotionWheel.Distribute(new ValenceArousal(0.5, 0.5), 1000.0);

        Assert.All(shares, s => Assert.Equal(0.125, s.Share, precision: 3));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    public void Distribute_NonPositiveTemperature_Throws(double t)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EmotionWheel.Distribute(new ValenceArousal(0.0, 0.5), t));
    }

    // --- Nötr yarıçapı ---
    // Dağılım UI'ın ana sütunlarını besler; yarıçap kuralı buraya bağlanmazsa etiket
    // iyileştirmesi ekranda GÖRÜNMEZ ve "Map başka der, sütun başka gösterir" çelişkisi doğar.

    [Theory]
    [InlineData(0.3, 0.5)]
    [InlineData(0.0, 0.95)]
    [InlineData(0.05, 0.52)]
    [InlineData(-0.25, 0.45)]
    public void Distribute_YaricapliEnYuksekPay_MapIleAynidir(double v, double a)
    {
        var va = new ValenceArousal(v, a);
        Assert.Equal(
            EmotionWheel.Map(va, neutralRadius: 0.2),
            EmotionWheel.Distribute(va, T, neutralRadius: 0.2)[0].Emotion);
    }

    [Fact]
    public void Distribute_YaricapDisinda_NotrPayiSifirdir()
    {
        var shares = EmotionWheel.Distribute(new ValenceArousal(0.3, 0.5), T, neutralRadius: 0.2);
        var notr = shares.Single(s => s.Emotion == Emotion.Notr);
        Assert.Equal(0.0, notr.Share, precision: 9);
        Assert.Equal(1.0, shares.Sum(s => s.Share), precision: 9);
    }

    [Fact]
    public void Distribute_YaricapIcinde_NotrBaskindir()
    {
        var shares = EmotionWheel.Distribute(new ValenceArousal(0.05, 0.52), T, neutralRadius: 0.2);
        Assert.Equal(Emotion.Notr, shares[0].Emotion);
        Assert.Equal(1.0, shares.Sum(s => s.Share), precision: 9);
    }

    [Fact]
    public void Distribute_YaricapVerilmezse_KlasikDavranisKorunur()
    {
        var va = new ValenceArousal(0.2, 0.55);
        var classic = EmotionWheel.Distribute(va, T);
        var explicitNull = EmotionWheel.Distribute(va, T, neutralRadius: null);
        Assert.Equal(
            classic.Select(s => (s.Emotion, Math.Round(s.Share, 9))),
            explicitNull.Select(s => (s.Emotion, Math.Round(s.Share, 9))));
    }
}
