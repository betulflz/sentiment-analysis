using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class EmotionLocalizerTests
{
    [Theory]
    [InlineData(Emotion.Mutlu, "Mutlu")]
    [InlineData(Emotion.Kizgin, "Kızgın")]
    [InlineData(Emotion.Uzgun, "Üzgün")]
    [InlineData(Emotion.Notr, "Nötr")]
    [InlineData(Emotion.Cosakulu, "Coşkulu")]
    [InlineData(Emotion.Sikilmis, "Sıkılmış")]
    public void Emotion_LocalizesToTurkish(Emotion e, string expected)
        => Assert.Equal(expected, EmotionLocalizer.Emotion(e));

    [Fact]
    public void AllEmotions_HaveNonEmptyTurkish()
    {
        foreach (Emotion e in System.Enum.GetValues<Emotion>())
            Assert.False(string.IsNullOrWhiteSpace(EmotionLocalizer.Emotion(e)));
    }

    [Theory]
    [InlineData(Confidence.Yuksek, "Yüksek")]
    [InlineData(Confidence.Dusuk, "Düşük")]
    public void Confidence_LocalizesToTurkish(Confidence c, string expected)
        => Assert.Equal(expected, EmotionLocalizer.Confidence(c));

    [Theory]
    [InlineData(IncongruencePattern.Yok, "Yok")]
    [InlineData(IncongruencePattern.Maskeleme, "Maskeleme")]
    [InlineData(IncongruencePattern.OlasiIroni, "Olası ironi")]
    public void Pattern_LocalizesToTurkish(IncongruencePattern p, string expected)
        => Assert.Equal(expected, EmotionLocalizer.Pattern(p));
}
