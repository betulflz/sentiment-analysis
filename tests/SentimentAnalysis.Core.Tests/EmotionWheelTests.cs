using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Core.Tests;

public class EmotionWheelTests
{
    [Theory]
    [InlineData(0.6, 0.9, Emotion.Cosakulu)]   // yüksek valence + yüksek arousal
    [InlineData(0.8, 0.5, Emotion.Mutlu)]      // yüksek valence + orta arousal
    [InlineData(0.6, 0.15, Emotion.Sakin)]     // olumlu + düşük enerji
    [InlineData(-0.4, 0.1, Emotion.Sikilmis)]  // olumsuz + çok düşük enerji
    [InlineData(-0.6, 0.3, Emotion.Uzgun)]     // olumsuz + düşük-orta enerji
    [InlineData(-0.5, 0.8, Emotion.Gergin)]    // olumsuz + yüksek enerji
    [InlineData(-0.8, 0.9, Emotion.Kizgin)]    // çok olumsuz + yüksek enerji
    [InlineData(0.0, 0.5, Emotion.Notr)]       // merkez
    public void Map_ReturnsNearestRegion(double v, double a, Emotion expected)
    {
        Assert.Equal(expected, EmotionWheel.Map(new ValenceArousal(v, a)));
    }

    [Fact]
    public void MapWithSecondary_PrimaryAndSecondaryDiffer()
    {
        var (primary, secondary) = EmotionWheel.MapWithSecondary(new ValenceArousal(0.7, 0.7));
        Assert.NotEqual(primary, secondary);
    }
}
