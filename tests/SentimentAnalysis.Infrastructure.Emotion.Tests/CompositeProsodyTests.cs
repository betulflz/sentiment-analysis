using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Emotion;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

/// <summary>
/// İki prozodi modelinin (genel audeering + Türkçe TurEV-DB) harmanlanması.
/// Harman saf ve deterministiktir; ONNX'siz test edilir.
/// </summary>
public class CompositeProsodyTests
{
    private static ProsodyEmotion P(double v, double a, double conf) => new(new ValenceArousal(v, a), conf);

    [Fact]
    public void Blend_EqualWeightsAndConfidence_IsMidpoint()
    {
        var r = CompositeProsodyEmotionModel.Blend(P(0.8, 0.4, 0.6), P(0.2, 0.8, 0.6), 0.5, 0.5);
        Assert.Equal(0.5, r.Va.Valence, precision: 6);
        Assert.Equal(0.6, r.Va.Arousal, precision: 6);
    }

    [Fact]
    public void Blend_HigherConfidenceModelPullsResult()
    {
        // Türkçe model çok emin, genel model kararsız → sonuç Türkçe modele yakın olmalı.
        var r = CompositeProsodyEmotionModel.Blend(P(-0.8, 0.9, 0.1), P(0.6, 0.3, 0.9), 0.5, 0.5);
        Assert.True(r.Va.Valence > 0.3, $"emin modelin tarafında olmalıydı (çıkan: {r.Va.Valence:F3})");
    }

    [Fact]
    public void Blend_ZeroBaseWeight_IgnoresThatModel()
    {
        // Türkçe SER kapatıldığında (ağırlık 0) sonuç tamamen genel modelden gelmeli.
        var r = CompositeProsodyEmotionModel.Blend(P(0.7, 0.4, 0.8), P(-0.9, 0.95, 0.9), 1.0, 0.0);
        Assert.Equal(0.7, r.Va.Valence, precision: 6);
        Assert.Equal(0.4, r.Va.Arousal, precision: 6);
    }

    [Fact]
    public void Blend_BothCompletelyUnconfident_ReturnsNeutralWithZeroConfidence()
    {
        // Uydurma bir duygu iddia edilmemeli.
        var r = CompositeProsodyEmotionModel.Blend(P(0.9, 0.9, 0.0), P(-0.9, 0.1, 0.0), 0.5, 0.5);
        Assert.Equal(ValenceArousal.Neutral, r.Va);
        Assert.Equal(0.0, r.ModelConfidence, precision: 9);
    }

    [Fact]
    public void Blend_ModelsAgree_KeepsConfidenceHigh()
    {
        var r = CompositeProsodyEmotionModel.Blend(P(0.7, 0.6, 0.9), P(0.72, 0.62, 0.9), 0.5, 0.5);
        Assert.True(r.ModelConfidence > 0.8,
            $"anlaşan iki emin model güveni korumalı (çıkan: {r.ModelConfidence:F3})");
    }

    [Fact]
    public void Blend_ModelsDisagree_LowersConfidence()
    {
        // Zıt uçlar: ikisi de emin ama çelişiyorlar → birleşik güven düşmeli (anlaşmazlık = belirsizlik).
        var agree = CompositeProsodyEmotionModel.Blend(P(0.8, 0.8, 0.9), P(0.8, 0.8, 0.9), 0.5, 0.5);
        var clash = CompositeProsodyEmotionModel.Blend(P(0.9, 0.9, 0.9), P(-0.9, 0.1, 0.9), 0.5, 0.5);

        Assert.True(clash.ModelConfidence < agree.ModelConfidence,
            $"çelişkide güven düşmeliydi (anlaşma {agree.ModelConfidence:F3}, çelişki {clash.ModelConfidence:F3})");
    }

    [Fact]
    public void Blend_ResultStaysInValidRange()
    {
        var r = CompositeProsodyEmotionModel.Blend(P(-1.0, 0.0, 1.0), P(1.0, 1.0, 1.0), 0.5, 0.5);
        Assert.InRange(r.Va.Valence, -1.0, 1.0);
        Assert.InRange(r.Va.Arousal, 0.0, 1.0);
        Assert.InRange(r.ModelConfidence, 0.0, 1.0);
    }

    [Fact]
    public void Blend_IsDeterministic()
    {
        var a = CompositeProsodyEmotionModel.Blend(P(0.3, 0.4, 0.7), P(-0.2, 0.6, 0.5), 0.5, 0.5);
        var b = CompositeProsodyEmotionModel.Blend(P(0.3, 0.4, 0.7), P(-0.2, 0.6, 0.5), 0.5, 0.5);
        Assert.Equal(a, b);
    }
}
