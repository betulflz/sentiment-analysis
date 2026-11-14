using System;
using System.Collections.Generic;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Core.Tests;

public class MeetingSummaryTests
{
    [Fact]
    public void MeetingSummary_CarriesAggregateMetrics()
    {
        var summary = new MeetingSummary(
            DominantEmotion: Emotion.Mutlu,
            TimeShareByEmotion: new Dictionary<Emotion, double> { [Emotion.Mutlu] = 1.0 },
            ValenceTrend: 0.1,
            EmotionalVolatility: 0.05,
            PeakMoments: new List<ChunkResult>(),
            AverageVa: new ValenceArousal(0.6, 0.5),
            AverageCongruence: 0.9,
            TurkishSummary: "Toplantı genel olarak olumluydu.");

        Assert.Equal(Emotion.Mutlu, summary.DominantEmotion);
        Assert.Equal(1.0, summary.TimeShareByEmotion[Emotion.Mutlu]);
        Assert.Equal(0.9, summary.AverageCongruence);
    }

    [Theory]
    [InlineData(Confidence.Dusuk, true)]
    [InlineData(Confidence.Orta, false)]
    [InlineData(Confidence.Yuksek, false)]
    public void FusedResult_Belirsiz_YalnizDusukGuvende(Confidence c, bool expected)
    {
        var f = new FusedResult(ValenceArousal.Neutral, Emotion.Mutlu, Emotion.Notr, c, true, false,
            IncongruencePattern.Yok, 1, ValenceArousal.Neutral, ValenceArousal.Neutral, 0.5, 0.5);
        Assert.Equal(expected, f.Uncertain);
    }
}
