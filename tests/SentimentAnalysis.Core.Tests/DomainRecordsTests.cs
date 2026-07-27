using System;
using System.Collections.Generic;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Core.Tests;

public class DomainRecordsTests
{
    [Fact]
    public void FusedResult_CarriesBothSignalsAndCongruence()
    {
        var result = new FusedResult(
            Combined: new ValenceArousal(-0.5, 0.7),
            PrimaryEmotion: Emotion.Gergin,
            SecondaryEmotion: Emotion.Kizgin,
            Confidence: Confidence.Orta,
            Agreement: false,
            Incongruent: true,
            Pattern: IncongruencePattern.Maskeleme,
            Congruence: 0.2,
            TextVa: new ValenceArousal(0.6, 0.5),
            ProsodyVa: new ValenceArousal(-0.6, 0.7),
            TextConfidence: 0.8,
            ProsodyConfidence: 0.6);

        Assert.True(result.Incongruent);
        Assert.Equal(IncongruencePattern.Maskeleme, result.Pattern);
        Assert.Equal(Emotion.Gergin, result.PrimaryEmotion);
    }

    [Fact]
    public void MeetingSession_AggregatesChunks()
    {
        var chunk = new ChunkResult(
            TimeStart: TimeSpan.Zero,
            TimeEnd: TimeSpan.FromSeconds(15),
            Fused: new FusedResult(
                new ValenceArousal(0.8, 0.5), Emotion.Mutlu, Emotion.Sakin,
                Confidence.Yuksek, true, false, IncongruencePattern.Yok, 0.9,
                TextVa: new ValenceArousal(0.8, 0.5), ProsodyVa: new ValenceArousal(0.7, 0.5),
                TextConfidence: 0.8, ProsodyConfidence: 0.7),
            Transcript: "merhaba");

        var session = new MeetingSession(
            Id: Guid.NewGuid(),
            StartedAt: DateTimeOffset.UtcNow,
            Language: DetectedLanguage.Turkish,
            Chunks: new List<ChunkResult> { chunk });

        Assert.Single(session.Chunks);
        Assert.Equal(DetectedLanguage.Turkish, session.Language);
    }

    [Fact]
    public void FusedResult_Flagged_TrueForSubThresholdIrony()
    {
        // Eşik-altı ironi: Incongruent=false ama Pattern=OlasiIroni → Flagged true olmalı.
        var ironi = new FusedResult(
            new ValenceArousal(0.5, 0.2), Emotion.Sakin, Emotion.Mutlu,
            Confidence.Orta, Agreement: false, Incongruent: false,
            IncongruencePattern.OlasiIroni, Congruence: 0.7,
            TextVa: new ValenceArousal(0.6, 0.5), ProsodyVa: new ValenceArousal(0.0, 0.5),
            TextConfidence: 0.8, ProsodyConfidence: 0.6);
        Assert.True(ironi.Flagged);

        var temiz = ironi with { Pattern = IncongruencePattern.Yok, Incongruent = false };
        Assert.False(temiz.Flagged);

        var uyumsuz = temiz with { Incongruent = true };
        Assert.True(uyumsuz.Flagged);
    }
}
