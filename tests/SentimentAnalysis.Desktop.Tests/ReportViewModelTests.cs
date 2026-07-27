using System;
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class ReportViewModelTests
{
    private sealed class FakeAggregator : IMeetingAggregator
    {
        public MeetingSummary Aggregate(MeetingSession s) => new(
            Emotion.Mutlu,
            new Dictionary<Emotion, double> { [Emotion.Mutlu] = 0.6, [Emotion.Uzgun] = 0.4 },
            ValenceTrend: 0.25, EmotionalVolatility: 0.1,
            PeakMoments: s.Chunks.Take(1).ToList(),
            AverageVa: new ValenceArousal(0.2, 0.5), AverageCongruence: 0.8,
            TurkishSummary: "Toplantı olumlu geçti.");
    }

    private static MeetingSession Session() => new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, DetectedLanguage.Turkish,
        new[] { new ChunkResult(TimeSpan.Zero, TimeSpan.FromSeconds(1),
            new FusedResult(new ValenceArousal(0.5,0.6), Emotion.Mutlu, Emotion.Notr, Confidence.Yuksek,
                true, false, IncongruencePattern.Yok, 0.9, 0.5, 0.5), "merhaba") });

    [Fact]
    public void Load_ProducesLocalizedReport()
    {
        var vm = new ReportViewModel(new FakeAggregator());
        Assert.False(vm.HasReport);

        vm.Load(Session());

        Assert.True(vm.HasReport);
        Assert.Equal("Mutlu", vm.DominantEmotion);
        Assert.Equal("Toplantı olumlu geçti.", vm.TurkishSummary);
        Assert.Equal(0.25, vm.ValenceTrend, precision: 5);
        Assert.Equal(2, vm.TimeShare.Count);
        Assert.Contains(vm.TimeShare, s => s.Emotion == "Mutlu" && Math.Abs(s.Share - 0.6) < 1e-9);
        Assert.Single(vm.PeakMoments);
        Assert.Equal("merhaba", vm.PeakMoments[0].Transcript);
    }
}
