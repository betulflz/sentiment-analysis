using System;
using System.Linq;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class ProsodyMathTests
{
    [Fact]
    public void Standardize_ProducesZeroMeanUnitVariance()
    {
        var x = new float[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var y = AudioNormalizer.Standardize(x);
        double mean = y.Average();
        double var = y.Select(v => (v - mean) * (v - mean)).Average();
        Assert.True(Math.Abs(mean) < 1e-4, $"ort ~0 bekleniyor, {mean}");
        Assert.True(Math.Abs(var - 1.0) < 1e-2, $"varyans ~1 bekleniyor, {var}");
    }

    [Fact]
    public void Standardize_ConstantSignal_ReturnsZeros()
    {
        var y = AudioNormalizer.Standardize(new float[] { 0.4f, 0.4f, 0.4f });
        Assert.All(y, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Standardize_Empty_ReturnsEmpty()
    {
        Assert.Empty(AudioNormalizer.Standardize(Array.Empty<float>()));
    }

    [Fact]
    public void Map_ValenceScaledToMinusOneOne_ArousalDirect()
    {
        // logits = [arousal, dominance, valence]
        var p = ProsodyOutputMapper.Map(new float[] { 0.9f, 0.5f, 0.75f });
        Assert.Equal(0.5, p.Va.Valence, precision: 5);   // 0.75*2-1 = 0.5
        Assert.Equal(0.9, p.Va.Arousal, precision: 5);   // direkt
    }

    [Fact]
    public void Map_NeutralOutput_LowConfidence()
    {
        // arousal 0.5, valence 0.5 → domain (0, 0.5) = nötr merkez → güven ~0
        var p = ProsodyOutputMapper.Map(new float[] { 0.5f, 0.5f, 0.5f });
        Assert.True(p.ModelConfidence < 0.05, $"nötr → düşük güven, {p.ModelConfidence}");
    }

    [Fact]
    public void Map_ExtremeOutput_HighConfidence()
    {
        // arousal 0.95, valence 1.0 → merkezden uzak → yüksek güven
        var p = ProsodyOutputMapper.Map(new float[] { 0.95f, 0.5f, 1.0f });
        Assert.True(p.ModelConfidence > 0.8, $"uç → yüksek güven, {p.ModelConfidence}");
    }

    [Fact]
    public void Map_ClampsOutOfRangeLogits()
    {
        var p = ProsodyOutputMapper.Map(new float[] { 1.3f, 0.5f, -0.2f });
        Assert.InRange(p.Va.Arousal, 0.0, 1.0);
        Assert.InRange(p.Va.Valence, -1.0, 1.0);
        Assert.InRange(p.ModelConfidence, 0.0, 1.0);
    }
}
