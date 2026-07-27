using System;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class QualityWeightingTests
{
    private static readonly FusionConfig Cfg = new();

    private static QualitySignals Signals(
        double snr = 20, double speech = 1.0, double seconds = 10,
        int words = 20, double logprob = 0.0) =>
        new(snr, speech, TimeSpan.FromSeconds(seconds), words, logprob);

    [Fact]
    public void AudioReliability_IsHigh_ForCleanLongSpeech()
    {
        var r = QualityWeighting.AudioReliability(Signals(snr: 20, speech: 1.0, seconds: 10), Cfg);
        Assert.True(r > 0.9, $"beklenen yüksek, gelen {r}");
    }

    [Fact]
    public void AudioReliability_IsZero_ForZeroDuration()
    {
        var r = QualityWeighting.AudioReliability(Signals(seconds: 0), Cfg);
        Assert.Equal(0.0, r, precision: 10);
    }

    [Fact]
    public void AudioReliability_DropsWithSilence()
    {
        var loud = QualityWeighting.AudioReliability(Signals(speech: 1.0), Cfg);
        var quiet = QualityWeighting.AudioReliability(Signals(speech: 0.1), Cfg);
        Assert.True(quiet < loud);
    }

    [Fact]
    public void TextReliability_IsHigh_ForLongConfidentTranscript()
    {
        var r = QualityWeighting.TextReliability(Signals(words: 20, logprob: 0.0), Cfg);
        Assert.True(r > 0.9, $"beklenen yüksek, gelen {r}");
    }

    [Fact]
    public void TextReliability_IsZero_ForEmptyTranscript()
    {
        var r = QualityWeighting.TextReliability(Signals(words: 0, logprob: -1.0), Cfg);
        Assert.Equal(0.0, r, precision: 10);
    }

    [Fact]
    public void Reliabilities_StayInUnitRange()
    {
        var extreme = QualityWeighting.AudioReliability(Signals(snr: 999, speech: 5, seconds: 999), Cfg);
        Assert.InRange(extreme, 0.0, 1.0);
    }

    [Fact]
    public void Reliabilities_NoNaN_WhenConfigRangesDegenerate()
    {
        var degenerate = new FusionConfig(SnrHighDb: 5.0, SnrLowDb: 5.0, MinReliableSeconds: 0.0,
            MinReliableWords: 0.0, LogProbFloor: 0.0);
        var a = QualityWeighting.AudioReliability(Signals(), degenerate);
        var t = QualityWeighting.TextReliability(Signals(), degenerate);
        Assert.False(double.IsNaN(a));
        Assert.InRange(a, 0.0, 1.0);
        Assert.False(double.IsNaN(t));
        Assert.InRange(t, 0.0, 1.0);
    }
}
