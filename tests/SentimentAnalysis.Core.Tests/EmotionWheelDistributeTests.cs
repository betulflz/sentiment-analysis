using SentimentAnalysis.Core;

namespace SentimentAnalysis.Core.Tests;

public class EmotionWheelDistributeTests
{
    private const double T = 0.35;   // AppConfig varsayılanı

    [Fact]
    public void Distribute_AlwaysReturnsAllEightEmotions()
    {
        var shares = EmotionWheel.Distribute(new ValenceArousal(0.2, 0.4), T);
        Assert.Equal(8, shares.Count);
        Assert.Equal(8, shares.Select(s => s.Emotion).Distinct().Count());
    }

    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(0.8, 0.5)]
    [InlineData(-1.0, 1.0)]
    [InlineData(1.0, 0.0)]
    public void Distribute_SharesSumToOne(double v, double a)
    {
        var shares = EmotionWheel.Distribute(new ValenceArousal(v, a), T);
        Assert.Equal(1.0, shares.Sum(s => s.Share), precision: 9);
    }

    [Fact]
    public void Distribute_AllSharesPositive()
    {
        // softmax hiçbir zaman tam sıfır vermez; bir duygu "imkânsız" diye gösterilmemeli.
        var shares = EmotionWheel.Distribute(new ValenceArousal(-1.0, 1.0), T);
        Assert.All(shares, s => Assert.True(s.Share > 0.0, $"{s.Emotion} payı sıfır."));
    }

    [Fact]
    public void Distribute_IsSortedDescending()
    {
        var shares = EmotionWheel.Distribute(new ValenceArousal(0.3, 0.7), T);
        for (int i = 1; i < shares.Count; i++)
            Assert.True(shares[i - 1].Share >= shares[i].Share, "paylar azalan sırada olmalı");
    }

    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(0.8, 0.5)]
    [InlineData(-0.8, 0.9)]
    [InlineData(0.6, 0.15)]
    [InlineData(-0.4, 0.1)]
    [InlineData(0.25, 0.62)]
    public void Distribute_FirstElementAlwaysMatchesMap(double v, double a)
    {
        // Yapısal kilit: Distribute ve Map aynı merkezleri kullanır, çelişemezler.
        var va = new ValenceArousal(v, a);
        Assert.Equal(EmotionWheel.Map(va), EmotionWheel.Distribute(va, T)[0].Emotion);
    }

    [Fact]
    public void Distribute_PointOnCenter_GivesThatEmotionHighestShare()
    {
        // Mutlu merkezi (0.8, 0.5)
        var shares = EmotionWheel.Distribute(new ValenceArousal(0.8, 0.5), T);
        Assert.Equal(Emotion.Mutlu, shares[0].Emotion);
    }

    [Fact]
    public void Distribute_LowerTemperature_SharpensDominantShare()
    {
        var va = new ValenceArousal(0.7, 0.55);
        double sharp = EmotionWheel.Distribute(va, 0.05)[0].Share;
        double soft = EmotionWheel.Distribute(va, 2.0)[0].Share;
        Assert.True(sharp > soft, $"küçük T daha keskin olmalı (T=0.05 → {sharp:F3}, T=2.0 → {soft:F3})");
    }

    [Fact]
    public void Distribute_HighTemperature_ApproachesUniform()
    {
        var shares = EmotionWheel.Distribute(new ValenceArousal(0.7, 0.55), 1000.0);
        Assert.All(shares, s => Assert.InRange(s.Share, 0.11, 0.14));   // ~1/8 = 0.125
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void Distribute_NonPositiveTemperature_DoesNotThrowOrProduceNaN(double t)
    {
        var shares = EmotionWheel.Distribute(new ValenceArousal(0.3, 0.6), t);
        Assert.Equal(8, shares.Count);
        Assert.All(shares, s => Assert.False(double.IsNaN(s.Share) || double.IsInfinity(s.Share)));
        Assert.Equal(1.0, shares.Sum(s => s.Share), precision: 9);
    }

    [Fact]
    public void Distribute_IsDeterministic()
    {
        var va = new ValenceArousal(0.1, 0.42);
        var a = EmotionWheel.Distribute(va, T);
        var b = EmotionWheel.Distribute(va, T);
        Assert.Equal(a.Select(x => x.Emotion), b.Select(x => x.Emotion));
    }
}
