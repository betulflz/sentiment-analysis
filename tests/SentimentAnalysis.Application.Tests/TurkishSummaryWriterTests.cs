using System.Collections.Generic;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class TurkishSummaryWriterTests
{
    private static MeetingSummary Metrics(
        Emotion dominant, double trend, double volatility, double congruence) =>
        new(dominant, new Dictionary<Emotion, double> { [dominant] = 1.0 },
            trend, volatility, new List<ChunkResult>(),
            new ValenceArousal(0.0, 0.5), congruence, TurkishSummary: string.Empty);

    [Fact]
    public void Write_MentionsDominantEmotionInTurkish()
    {
        var text = TurkishSummaryWriter.Write(Metrics(Emotion.Mutlu, 0.1, 0.05, 0.9));
        Assert.Contains("Mutlu", text);
    }

    [Fact]
    public void Write_SaysWarming_ForPositiveTrend()
    {
        var text = TurkishSummaryWriter.Write(Metrics(Emotion.Mutlu, 0.2, 0.05, 0.9));
        Assert.Contains("ısındı", text);
    }

    [Fact]
    public void Write_SaysCooling_ForNegativeTrend()
    {
        var text = TurkishSummaryWriter.Write(Metrics(Emotion.Uzgun, -0.2, 0.05, 0.9));
        Assert.Contains("soğudu", text);
    }

    [Fact]
    public void Write_FlagsLowCongruence()
    {
        var text = TurkishSummaryWriter.Write(Metrics(Emotion.Gergin, 0.0, 0.05, 0.2));
        Assert.Contains("düşük", text);
    }

    [Fact]
    public void Write_IsNonEmpty()
    {
        var text = TurkishSummaryWriter.Write(Metrics(Emotion.Notr, 0.0, 0.0, 1.0));
        Assert.False(string.IsNullOrWhiteSpace(text));
    }
}
