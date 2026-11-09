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
    [InlineData(IncongruencePattern.Maskeleme, "Sözler olumlu ama ses tonu olumsuz")]
    [InlineData(IncongruencePattern.Saka, "Sözler olumsuz ama ses tonu neşeli")]
    [InlineData(IncongruencePattern.OlasiIroni, "Sözler olumlu ama ses düz — iğneleme olabilir")]
    public void Pattern_LocalizesToTurkish(IncongruencePattern p, string expected)
        => Assert.Equal(expected, EmotionLocalizer.Pattern(p));

    // Sınırlar üstteki karşılaştırma "> -0.2" / "> -0.6" (kesin eşitlik dahil değil) olduğu için
    // -0.2 ve -0.6 tam olarak bir alt banda düşer — bu satırlar bunu kasıtlı doğrular.
    [Theory]
    [InlineData(1.0, "Çok olumlu")]
    [InlineData(0.6, "Çok olumlu")]
    [InlineData(0.599, "Olumlu")]
    [InlineData(0.2, "Olumlu")]
    [InlineData(0.199, "Ne olumlu ne olumsuz")]
    [InlineData(0.0, "Ne olumlu ne olumsuz")]
    [InlineData(-0.199, "Ne olumlu ne olumsuz")]
    [InlineData(-0.2, "Olumsuz")]
    [InlineData(-0.599, "Olumsuz")]
    [InlineData(-0.6, "Çok olumsuz")]
    [InlineData(-1.0, "Çok olumsuz")]
    public void ValenceText_MapsBandsAndBoundaries(double valence, string expected)
        => Assert.Equal(expected, EmotionLocalizer.ValenceText(valence));

    [Theory]
    [InlineData(1.0, "Çok hareketli")]
    [InlineData(0.8, "Çok hareketli")]
    [InlineData(0.799, "Hareketli")]
    [InlineData(0.6, "Hareketli")]
    [InlineData(0.599, "Orta canlılıkta")]
    [InlineData(0.4, "Orta canlılıkta")]
    [InlineData(0.399, "Sakin")]
    [InlineData(0.2, "Sakin")]
    [InlineData(0.199, "Çok durgun")]
    [InlineData(0.0, "Çok durgun")]
    [InlineData(-1.0, "Çok durgun")]
    public void ArousalText_MapsBandsAndBoundaries(double arousal, string expected)
        => Assert.Equal(expected, EmotionLocalizer.ArousalText(arousal));
}
