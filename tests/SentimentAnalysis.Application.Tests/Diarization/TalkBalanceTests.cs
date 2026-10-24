using System;
using System.Linq;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

public class TalkBalanceTests
{
    private static SpeakerSegment S(int speaker, double start, double end) =>
        new(new SpeakerId(speaker), TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end));

    [Fact]
    public void Compute_SureyeGorePay_BilinmeyenHaric()
    {
        var shares = TalkBalance.Compute(new[] { S(0, 0, 30), S(1, 30, 60), S(0, 60, 90), S(-1, 90, 100) });
        Assert.Equal(new[] { 0, 1 }, shares.Select(s => s.Speaker.Index));
        Assert.Equal(TimeSpan.FromSeconds(60), shares[0].Duration);
        Assert.Equal(2 / 3.0, shares[0].Share, 9);
        Assert.Equal(1 / 3.0, shares[1].Share, 9);
    }

    [Fact]
    public void Compute_SifirVeNegatifSureliBolge_YokSayilir()
    {
        var share = Assert.Single(TalkBalance.Compute(new[] { S(0, 0, 10), S(1, 20, 20), S(1, 30, 25) }));
        Assert.Equal(0, share.Speaker.Index);
        Assert.Equal(1.0, share.Share, 9);
    }

    [Fact]
    public void Compute_Bos_BosListe() => Assert.Empty(TalkBalance.Compute(Array.Empty<SpeakerSegment>()));

    [Fact]
    public void Compute_YalnizcaBilinmeyen_BosListe() => Assert.Empty(TalkBalance.Compute(new[] { S(-1, 0, 10) }));
}
