using System;
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class TextVaLookupTests
{
    private static IReadOnlyList<string> Labels => XlmrEmotionLabels.Ordered;

    private static float[] OneHotLogits(string emotion, float hot = 20f)
    {
        // Seçilen duyguya çok büyük logit → softmax ~ tek-nokta dağılım.
        return Labels.Select(l => l == emotion ? hot : 0f).ToArray();
    }

    [Fact]
    public void Table_HasElevenEmotions_WithExpectedRanges()
    {
        Assert.Equal(11, TextVaLookup.Table.Count);
        Assert.Equal(11, XlmrEmotionLabels.Ordered.Count);
        foreach (var (name, va) in TextVaLookup.Table)
        {
            Assert.InRange(va.Valence, -1.0, 1.0);
            Assert.InRange(va.Arousal, 0.0, 1.0);
            Assert.Contains(name, XlmrEmotionLabels.Ordered); // etiket ↔ tablo tutarlı
        }
    }

    [Fact]
    public void Map_PeakedJoy_ReturnsJoyVa_HighConfidence()
    {
        var e = TextVaLookup.Map(OneHotLogits("joy"), Labels);
        Assert.Equal(0.8, e.Va.Valence, precision: 2);
        Assert.Equal(0.7, e.Va.Arousal, precision: 2);
        Assert.True(e.ModelConfidence > 0.95, $"kararlı dağılım → yüksek güven, {e.ModelConfidence}");
    }

    [Fact]
    public void Map_PeakedAnger_NegativeValence_HighArousal()
    {
        var e = TextVaLookup.Map(OneHotLogits("anger"), Labels);
        Assert.True(e.Va.Valence < -0.5, $"anger negatif valence, {e.Va.Valence}");
        Assert.True(e.Va.Arousal > 0.6, $"anger yüksek arousal, {e.Va.Arousal}");
    }

    [Fact]
    public void Map_UniformLogits_LowConfidence_AveragedVa()
    {
        var uniform = Labels.Select(_ => 1.0f).ToArray();
        var e = TextVaLookup.Map(uniform, Labels);
        Assert.True(e.ModelConfidence < 0.05, $"düz dağılım → düşük güven, {e.ModelConfidence}");
        // Ortalama VA: 11 duygunun aritmetik ortası, ~nötr civarı.
        double meanV = TextVaLookup.Table.Values.Average(v => v.Valence);
        Assert.Equal(meanV, e.Va.Valence, precision: 3);
        Assert.InRange(e.Va.Valence, -1.0, 1.0);
        Assert.InRange(e.Va.Arousal, 0.0, 1.0);
    }

    [Fact]
    public void Map_ThrowsOnLengthMismatch()
    {
        Assert.Throws<ArgumentException>(() => TextVaLookup.Map(new float[] { 1f, 2f }, Labels));
    }

    [Fact]
    public void Map_ThrowsOnUnknownLabel()
    {
        var labels = Labels.Take(10).Append("bilinmeyen").ToArray();
        var logits = labels.Select(_ => 1f).ToArray();
        Assert.Throws<ArgumentException>(() => TextVaLookup.Map(logits, labels));
    }
}
