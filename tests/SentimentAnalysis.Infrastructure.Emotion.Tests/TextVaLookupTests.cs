using System;
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class TextVaLookupTests
{
    private static IReadOnlyList<string> Labels => XlmrEmotionLabels.Ordered;

    // Model çok etiketli (bağımsız sigmoid, τ=0,5): etkin olmayan duyguların gerçek logitleri -5…-8 civarı.
    private static float[] OneHotLogits(string emotion, float hot = 8f, float rest = -8f)
        => Labels.Select(l => l == emotion ? hot : rest).ToArray();

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
    public void Map_HicbirDuyguEsigiGecmez_DusukGuven_OrtalamaVa()
    {
        // Hepsi -3 → her duygu ~%5: model hiçbir duyguya "var" demiyor. Softmax bunu %9'luk
        // düz dağılıma çevirip anlamı kaybederdi; sigmoid düşük güven verir.
        var uniform = Labels.Select(_ => -3.0f).ToArray();
        var e = TextVaLookup.Map(uniform, Labels);
        Assert.True(e.ModelConfidence < 0.05, $"eşik altı → düşük güven, {e.ModelConfidence}");
        // Ortalama VA: 11 duygunun aritmetik ortası, ~nötr civarı.
        double meanV = TextVaLookup.Table.Values.Average(v => v.Valence);
        Assert.Equal(meanV, e.Va.Valence, precision: 3);
        Assert.InRange(e.Va.Valence, -1.0, 1.0);
        Assert.InRange(e.Va.Arousal, 0.0, 1.0);
    }

    [Fact]
    public void Map_Sigmoid_GuvenEnYuksekDuygununOlasiligi()
    {
        // Ölçülen gerçek çıktı ("they cancelled the flight again…"): frustration 0,5, anger -0,2.
        // Softmax entropisi güveni modelin kendi olasılığından kopuk 0,71'e çıkarırdı; doğrusu p(frustration) ≈ 0,62.
        var logits = OneHotLogits("frustration", hot: 0.5f);
        logits[Labels.ToList().IndexOf("anger")] = -0.2f;
        var e = TextVaLookup.Map(logits, Labels);
        Assert.Equal(1 / (1 + Math.Exp(-0.5)), e.ModelConfidence, precision: 3);
        Assert.True(e.Va.Valence < -0.5, $"iki olumsuz duygu → olumsuz valence, {e.Va.Valence}");
    }

    [Fact]
    public void Map_IkiDuyguBirlikte_IkisiDeTamGuven()
    {
        // Çok etiketli: "harika bir sürpriz" → surprise VE joy etkin olabilir; biri diğerinin güvenini düşürmez.
        var logits = OneHotLogits("surprise");
        logits[Labels.ToList().IndexOf("joy")] = 8f;
        var e = TextVaLookup.Map(logits, Labels);
        Assert.True(e.ModelConfidence > 0.95, $"{e.ModelConfidence}");
        Assert.Equal((0.2 + 0.8) / 2, e.Va.Valence, precision: 1);
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
