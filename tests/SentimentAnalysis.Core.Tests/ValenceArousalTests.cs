using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Core.Tests;

public class ValenceArousalTests
{
    [Fact]
    public void Clamp_KeepsValuesInRange()
    {
        var va = ValenceArousal.Clamp(valence: 2.5, arousal: -0.4);
        Assert.Equal(1.0, va.Valence);
        Assert.Equal(0.0, va.Arousal);
    }

    [Fact]
    public void Clamp_LeavesInRangeValuesUntouched()
    {
        var va = ValenceArousal.Clamp(valence: 0.3, arousal: 0.7);
        Assert.Equal(0.3, va.Valence, precision: 10);
        Assert.Equal(0.7, va.Arousal, precision: 10);
    }

    [Fact]
    public void Neutral_IsCenter()
    {
        Assert.Equal(0.0, ValenceArousal.Neutral.Valence);
        Assert.Equal(0.5, ValenceArousal.Neutral.Arousal);
    }

    [Fact]
    public void DistanceTo_IsEuclidean()
    {
        var a = new ValenceArousal(0.0, 0.0);
        var b = new ValenceArousal(3.0, 4.0);
        Assert.Equal(5.0, a.DistanceTo(b), precision: 10);
    }
}
