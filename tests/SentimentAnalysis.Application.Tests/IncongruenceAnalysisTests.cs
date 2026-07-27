using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class IncongruenceAnalysisTests
{
    private static readonly FusionConfig Cfg = new();

    [Fact]
    public void Congruence_IsOne_WhenValencesMatch()
    {
        Assert.Equal(1.0, IncongruenceAnalysis.Congruence(0.5, 0.5), precision: 10);
    }

    [Fact]
    public void Congruence_IsZero_WhenValencesOpposedExtremes()
    {
        Assert.Equal(0.0, IncongruenceAnalysis.Congruence(1.0, -1.0), precision: 10);
    }

    [Fact]
    public void IsIncongruent_TrueWhenGapExceedsThreshold()
    {
        Assert.True(IncongruenceAnalysis.IsIncongruent(0.6, -0.6, Cfg));   // fark 1.2 ≥ 0.8
        Assert.False(IncongruenceAnalysis.IsIncongruent(0.6, 0.2, Cfg));   // fark 0.4 < 0.8
    }

    // master §5.1 tablosu — üç örüntü:

    [Fact]
    public void Classify_Maskeleme_PositiveWordsNegativeTone()
    {
        // Ağlamaklı "çok mutluyum": metin +, ses −, arousal orta-yüksek
        var p = IncongruenceAnalysis.Classify(
            text: new ValenceArousal(0.6, 0.5), prosody: new ValenceArousal(-0.6, 0.6),
            incongruent: true, Cfg);
        Assert.Equal(IncongruencePattern.Maskeleme, p);
    }

    [Fact]
    public void Classify_Saka_NegativeWordsCheerfulTone()
    {
        // Gülerek "çok üzgünüm": metin −, ses +, arousal yüksek
        var p = IncongruenceAnalysis.Classify(
            text: new ValenceArousal(-0.6, 0.5), prosody: new ValenceArousal(0.6, 0.85),
            incongruent: true, Cfg);
        Assert.Equal(IncongruencePattern.Saka, p);
    }

    [Fact]
    public void Classify_OlasiIroni_PositiveWordsFlatDelivery()
    {
        // Düz tonla "harika, bayıldım": metin +, ses nötr/düz, arousal düşük
        var p = IncongruenceAnalysis.Classify(
            text: new ValenceArousal(0.6, 0.4), prosody: new ValenceArousal(0.0, 0.2),
            incongruent: false, Cfg);
        Assert.Equal(IncongruencePattern.OlasiIroni, p);
    }

    [Fact]
    public void Classify_Yok_WhenCongruentAndNotIronic()
    {
        var p = IncongruenceAnalysis.Classify(
            text: new ValenceArousal(0.7, 0.5), prosody: new ValenceArousal(0.6, 0.55),
            incongruent: false, Cfg);
        Assert.Equal(IncongruencePattern.Yok, p);
    }

    [Fact]
    public void Classify_Yok_ForIncongruentButUnnamedDivergence()
    {
        // Net olumsuz söz + nötr ton: eşiği aşar (uyumsuz) ama Maskeleme/Şaka/ironi değil.
        // OlasiIroni yalnızca "olumlu söz + düz ton" içindir; buraya düşmemeli.
        var p = IncongruenceAnalysis.Classify(
            text: new ValenceArousal(-0.9, 0.5), prosody: new ValenceArousal(0.0, 0.5),
            incongruent: true, Cfg);
        Assert.Equal(IncongruencePattern.Yok, p);
    }
}
