using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Emotion;

// Test ad alanı `...Infrastructure.Emotion.Tests` olduğu için sade `Emotion` adı, enum yerine
// ad alanını çözer (CS0118). Alias bu belirsizliği kaldırır.
using CoreEmotion = SentimentAnalysis.Core.Emotion;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class TurkishSerVaLookupTests
{
    private static readonly IReadOnlyList<string> L = TurkishSerLabels.Ordered;

    [Fact]
    public void Labels_MatchModelConfigOrder()
    {
        // config.json id2label: 0:Angry 1:Calm 2:Happy 3:Sad (2026-07-27'de doğrulandı).
        Assert.Equal(new[] { "angry", "calm", "happy", "sad" }, L);
    }

    [Fact]
    public void Table_CoversEveryLabel()
    {
        foreach (var label in L)
            Assert.True(TurkishSerVaLookup.Table.ContainsKey(label), $"VA tablosunda '{label}' yok");
    }

    [Fact]
    public void Anchors_RoundTripToTheirWheelEmotion()
    {
        // Çapa noktaları EmotionWheel merkezlerine bağlı: saf bir sınıf tahmini çarkta
        // aynı duyguya geri dönmeli. Aksi halde iki temsil arasında sessiz kayma olur.
        Assert.Equal(CoreEmotion.Kizgin, EmotionWheel.Map(TurkishSerVaLookup.Table["angry"]));
        Assert.Equal(CoreEmotion.Notr, EmotionWheel.Map(TurkishSerVaLookup.Table["calm"]));
        Assert.Equal(CoreEmotion.Mutlu, EmotionWheel.Map(TurkishSerVaLookup.Table["happy"]));
        Assert.Equal(CoreEmotion.Uzgun, EmotionWheel.Map(TurkishSerVaLookup.Table["sad"]));
    }

    [Theory]
    [InlineData(0, CoreEmotion.Kizgin)]   // angry
    [InlineData(1, CoreEmotion.Notr)]     // calm
    [InlineData(2, CoreEmotion.Mutlu)]    // happy
    [InlineData(3, CoreEmotion.Uzgun)]    // sad
    public void ConfidentPrediction_MapsToExpectedEmotion(int winningIndex, CoreEmotion expected)
    {
        var logits = new float[4];
        for (int i = 0; i < 4; i++) logits[i] = i == winningIndex ? 12f : 0f;

        var result = TurkishSerVaLookup.Map(logits, L);

        Assert.Equal(expected, EmotionWheel.Map(result.Va));
        Assert.True(result.ModelConfidence > 0.9, $"kesin tahminde güven yüksek olmalı ({result.ModelConfidence:F3})");
    }

    [Fact]
    public void UniformLogits_GiveZeroConfidence()
    {
        var result = TurkishSerVaLookup.Map(new[] { 1f, 1f, 1f, 1f }, L);
        Assert.Equal(0.0, result.ModelConfidence, precision: 6);
    }

    [Fact]
    public void UniformLogits_ProduceAveragedVa_WithinValidRange()
    {
        var result = TurkishSerVaLookup.Map(new[] { 0f, 0f, 0f, 0f }, L);
        Assert.InRange(result.Va.Valence, -1.0, 1.0);
        Assert.InRange(result.Va.Arousal, 0.0, 1.0);
    }

    [Fact]
    public void Map_IsNumericallyStableForLargeLogits()
    {
        var result = TurkishSerVaLookup.Map(new[] { 1000f, -1000f, 0f, 500f }, L);
        Assert.False(double.IsNaN(result.Va.Valence) || double.IsNaN(result.Va.Arousal));
        Assert.False(double.IsNaN(result.ModelConfidence));
        Assert.Equal(CoreEmotion.Kizgin, EmotionWheel.Map(result.Va));
    }

    [Fact]
    public void Map_LengthMismatch_Throws() =>
        Assert.Throws<ArgumentException>(() => TurkishSerVaLookup.Map(new[] { 1f, 2f }, L));

    [Fact]
    public void Map_EmptyLogits_Throws() =>
        Assert.Throws<ArgumentException>(() => TurkishSerVaLookup.Map(Array.Empty<float>(), Array.Empty<string>()));

    [Fact]
    public void Map_UnknownLabel_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            TurkishSerVaLookup.Map(new[] { 1f, 1f, 1f, 1f }, new[] { "angry", "calm", "happy", "bilinmeyen" }));
}
