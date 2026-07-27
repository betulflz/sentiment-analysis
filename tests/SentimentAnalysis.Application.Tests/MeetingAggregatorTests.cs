using System;
using System.Collections.Generic;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class MeetingAggregatorTests
{
    private static readonly MeetingAggregator Agg = new(AppConfig.Default);

    private static FusedResult Fused(double v, double a, Emotion primary, double congruence = 0.9) =>
        new(new ValenceArousal(v, a), primary, Emotion.Notr,
            Confidence.Orta, true, false, IncongruencePattern.Yok, congruence,
            TextVa: new ValenceArousal(v, a), ProsodyVa: new ValenceArousal(v, a),
            TextConfidence: 0.7, ProsodyConfidence: 0.7);

    private static ChunkResult Chunk(int startSec, int endSec, double v, double a, Emotion e, double congruence = 0.9) =>
        new(TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec), Fused(v, a, e, congruence), "x");

    private static MeetingSession Session(params ChunkResult[] chunks) =>
        new(Guid.Empty, DateTimeOffset.UnixEpoch, DetectedLanguage.Turkish, chunks);

    [Fact]
    public void EmptySession_ReturnsNeutralSummary()
    {
        var s = Agg.Aggregate(Session());
        Assert.Equal(Emotion.Notr, s.DominantEmotion);
        Assert.Empty(s.PeakMoments);
        Assert.Equal(0.0, s.ValenceTrend);
        Assert.Equal(0.0, s.EmotionalVolatility);
    }

    [Fact]
    public void DominantEmotion_IsLongestByDuration()
    {
        // Mutlu 20sn, Uzgun 5sn → baskın Mutlu
        var s = Agg.Aggregate(Session(
            Chunk(0, 20, 0.8, 0.5, Emotion.Mutlu),
            Chunk(20, 25, -0.6, 0.3, Emotion.Uzgun)));
        Assert.Equal(Emotion.Mutlu, s.DominantEmotion);
        Assert.True(s.TimeShareByEmotion[Emotion.Mutlu] > s.TimeShareByEmotion[Emotion.Uzgun]);
    }

    [Fact]
    public void TimeShare_SumsToOne()
    {
        var s = Agg.Aggregate(Session(
            Chunk(0, 10, 0.8, 0.5, Emotion.Mutlu),
            Chunk(10, 20, -0.6, 0.3, Emotion.Uzgun)));
        double total = 0;
        foreach (var kv in s.TimeShareByEmotion) total += kv.Value;
        Assert.Equal(1.0, total, precision: 10);
    }

    [Fact]
    public void ValenceTrend_PositiveWhenWarming()
    {
        // valence artıyor: −0.5 → 0.0 → 0.5
        var s = Agg.Aggregate(Session(
            Chunk(0, 10, -0.5, 0.5, Emotion.Uzgun),
            Chunk(10, 20, 0.0, 0.5, Emotion.Notr),
            Chunk(20, 30, 0.5, 0.5, Emotion.Mutlu)));
        Assert.True(s.ValenceTrend > 0, $"ısınan toplantı, gelen {s.ValenceTrend}");
    }

    [Fact]
    public void Volatility_HigherForSwings()
    {
        var stable = Agg.Aggregate(Session(
            Chunk(0, 10, 0.5, 0.5, Emotion.Mutlu), Chunk(10, 20, 0.5, 0.5, Emotion.Mutlu)));
        var swingy = Agg.Aggregate(Session(
            Chunk(0, 10, -0.9, 0.5, Emotion.Kizgin), Chunk(10, 20, 0.9, 0.5, Emotion.Cosakulu)));
        Assert.True(swingy.EmotionalVolatility > stable.EmotionalVolatility);
    }

    [Fact]
    public void PeakMoments_AreMostCritical_AndCapped()
    {
        // PeakMomentCount = 3; en kritik = yüksek arousal + düşük valence
        var s = Agg.Aggregate(Session(
            Chunk(0, 10, 0.8, 0.2, Emotion.Sakin),    // sakin
            Chunk(10, 20, -0.9, 0.95, Emotion.Kizgin), // en kritik
            Chunk(20, 30, 0.5, 0.5, Emotion.Mutlu),
            Chunk(30, 40, -0.5, 0.8, Emotion.Gergin),
            Chunk(40, 50, 0.7, 0.4, Emotion.Sakin)));
        Assert.Equal(3, s.PeakMoments.Count);
        Assert.Equal(-0.9, s.PeakMoments[0].Fused.Combined.Valence, precision: 10); // en kritik (Kizgin, arousal−valence=1.85)
        Assert.Equal(-0.5, s.PeakMoments[1].Fused.Combined.Valence, precision: 10); // 2. kritik (Gergin, 1.3)
        Assert.Equal(0.5, s.PeakMoments[2].Fused.Combined.Valence, precision: 10);  // 3. kritik (Mutlu, 0.0)
    }

    [Fact]
    public void DominantEmotion_TieBreaksByEnumOrderDeterministically()
    {
        // Mutlu ve Uzgun tam eşit süre (10sn). Deterministik tie-break: enum sırası küçük olan
        // (Mutlu=1 < Uzgun=4) kazanır. Aynı girdi hep aynı sonucu vermeli.
        var session = Session(
            Chunk(0, 10, 0.8, 0.5, Emotion.Mutlu),
            Chunk(10, 20, -0.6, 0.3, Emotion.Uzgun));
        Assert.Equal(Emotion.Mutlu, Agg.Aggregate(session).DominantEmotion);
        Assert.Equal(Emotion.Mutlu, Agg.Aggregate(session).DominantEmotion);
    }

    [Fact]
    public void AverageCongruence_IsMean()
    {
        var s = Agg.Aggregate(Session(
            Chunk(0, 10, 0.5, 0.5, Emotion.Mutlu, congruence: 0.8),
            Chunk(10, 20, 0.5, 0.5, Emotion.Mutlu, congruence: 0.4)));
        Assert.Equal(0.6, s.AverageCongruence, precision: 10);
    }

    [Fact]
    public void AverageVa_IsMean()
    {
        var s = Agg.Aggregate(Session(
            Chunk(0, 10, 0.4, 0.6, Emotion.Mutlu),
            Chunk(10, 20, 0.6, 0.4, Emotion.Mutlu)));
        Assert.Equal(0.5, s.AverageVa.Valence, precision: 10);
        Assert.Equal(0.5, s.AverageVa.Arousal, precision: 10);
    }
}
