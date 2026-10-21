using System;
using System.Linq;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Decision;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Decision;

public class FollowUpAdvisorTests
{
    private static readonly DecisionConfig Cfg = new();
    private static Indicators Ind(double[] n, double q = 0.8) => new(n, new int?[6], q);

    private static FollowUpDecision Decide(MeetingProfile p, double[] n, double q = 0.8) =>
        FollowUpAdvisor.Decide(p, Ind(n, q), DecisionKnowledgeBase.DefaultWeights(p), Cfg);

    private static readonly double[] Gergin = { .8, .1, .7, .2, .1, .3 };
    private static readonly double[] Ilgisiz = { .1, .9, .1, .1, .6, .2 };
    private static readonly double[] Uyumsuz = { .2, .1, .2, .9, .1, .1 };

    [Fact]
    public void Satis_Gergin_Beklet()
    {
        var d = Decide(MeetingProfile.Satis, Gergin);
        Assert.Equal(FollowUpAction.Beklet, d.Best);
        Assert.Equal(0.6365, d.Ranking[0].Closeness, 1e-3);
    }

    [Fact] public void Satis_Ilgisiz_IkinciGorusme() => Assert.Equal(FollowUpAction.IkinciGorusme, Decide(MeetingProfile.Satis, Ilgisiz).Best);
    [Fact] public void Satis_Uyumsuz_ItirazBilgisi() => Assert.Equal(FollowUpAction.ItirazBilgisi, Decide(MeetingProfile.Satis, Uyumsuz).Best);
    [Fact] public void Mulakat_Uyumsuz_EkGorusme() => Assert.Equal(FollowUpAction.EkGorusme, Decide(MeetingProfile.Mulakat, Uyumsuz).Best);
    [Fact] public void Mulakat_Gergin_Tekrarla() => Assert.Equal(FollowUpAction.GorusmeyiTekrarla, Decide(MeetingProfile.Mulakat, Gergin).Best);
    [Fact] public void Toplanti_Gergin_BireBir() => Assert.Equal(FollowUpAction.BireBirGorusme, Decide(MeetingProfile.Toplanti, Gergin).Best);
    [Fact] public void Toplanti_Ilgisiz_Takip() => Assert.Equal(FollowUpAction.TakipToplantisi, Decide(MeetingProfile.Toplanti, Ilgisiz).Best);

    [Theory]
    [InlineData(MeetingProfile.Satis, FollowUpAction.TeklifGonder)]
    [InlineData(MeetingProfile.Mulakat, FollowUpAction.DegerlendirmeyeGec)]
    [InlineData(MeetingProfile.Toplanti, FollowUpAction.KararEpostasi)]
    public void Calm_PositiveFollowUp(MeetingProfile p, FollowUpAction expected)
    {
        var d = Decide(p, Enumerable.Repeat(0.1, 6).ToArray());
        Assert.Equal(expected, d.Best);
        Assert.Contains("belirgin bir sorun görülmedi", d.Reason);
    }

    [Fact]
    public void Mulakat_LowQuality_Repeat()
    {
        var d = Decide(MeetingProfile.Mulakat, Enumerable.Repeat(0.1, 6).ToArray(), q: 0.3);
        Assert.Equal(FollowUpAction.GorusmeyiTekrarla, d.Best);
        Assert.Contains("yetersiz", d.Reason);
    }

    [Fact]
    public void Aggregate_SingleWindow_EqualsThatWindow()
    {
        var w = new Indicators(Gergin, new int?[] { 1, null, 1, null, null, null }, 0.7);
        var m = FollowUpAdvisor.AggregateMeeting(new[] { w }, Cfg);
        for (int j = 0; j < 6; j++) Assert.Equal(Gergin[j], m.Values[j], 1e-9);
        Assert.Equal(0.7, m.DataQuality, 1e-9);
        Assert.Equal(1, m.SourceFace[(int)Criterion.Gerilim]);
    }

    [Fact]
    public void Aggregate_Empty_AllZero()
    {
        var m = FollowUpAdvisor.AggregateMeeting(Array.Empty<Indicators>(), Cfg);
        Assert.Equal(6, m.Values.Count);
        Assert.All(m.Values, v => Assert.Equal(0.0, v));
        Assert.Equal(0.0, m.DataQuality);
    }

    [Fact]
    public void Aggregate_P90_WeighsPeaks()
    {
        Indicators W(double k1) => new(new[] { k1, 0, 0, 0, 0, 0 }, new int?[6], 1);
        var oneSpike = Enumerable.Repeat(0.0, 9).Append(1.0).Select(W).ToArray();
        var twoSpikes = Enumerable.Repeat(0.0, 8).Concat(new[] { 1.0, 1.0 }).Select(W).ToArray();
        Assert.Equal(0.05, FollowUpAdvisor.AggregateMeeting(oneSpike, Cfg).Values[0], 1e-9);
        Assert.Equal(0.6, FollowUpAdvisor.AggregateMeeting(twoSpikes, Cfg).Values[0], 1e-9);
    }

    [Fact]
    public void Aggregate_SourceFace_MostFrequentAboveThreshold()
    {
        Indicators W(double k1, int? face) => new(new[] { k1, 0, 0, 0, 0, 0 }, new int?[] { face, null, null, null, null, null }, 1);
        var m = FollowUpAdvisor.AggregateMeeting(new[] { W(0.9, 1), W(0.8, 1), W(0.9, 0), W(0.1, 0), W(0.1, 0) }, Cfg);
        Assert.Equal(1, m.SourceFace[0]);
    }
}

public class SensitivityTests
{
    private static readonly double[] Yorgun = { .3, .4, .1, .1, .9, .2 };

    private static Func<System.Collections.Generic.IReadOnlyList<double>, LiveAction> BestOf(MeetingProfile p) =>
        w => ActionRanker.Rank(DecisionKnowledgeBase.Get(p).Interventions, DecisionKnowledgeBase.LiveImpact, w, Yorgun)[0].Alternative;

    [Fact]
    public void Sensitivity_Returns24Rows()
    {
        var rows = Sensitivity.Analyze(DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis), BestOf(MeetingProfile.Satis));
        Assert.Equal(24, rows.Count);
    }

    [Fact]
    public void Sensitivity_Mulakat_Yorgun()
    {
        var rows = Sensitivity.Analyze(DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Mulakat), BestOf(MeetingProfile.Mulakat));
        var changed = rows.Where(r => r.Changed).ToArray();
        Assert.Equal(2, changed.Length);
        Assert.Contains(changed, r => r.Criterion == Criterion.Gerilim && Math.Abs(r.Delta + 0.2) < 1e-9 && r.Best == LiveAction.AraVer);
        Assert.Contains(changed, r => r.Criterion == Criterion.DusukEnerji && Math.Abs(r.Delta - 0.2) < 1e-9 && r.Best == LiveAction.AraVer);
        Assert.All(rows.Where(r => !r.Changed), r => Assert.Equal(LiveAction.EmpatiKur, r.Best));
    }

    [Fact]
    public void Sensitivity_Satis_Yorgun_Stable()
    {
        var rows = Sensitivity.Analyze(DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis), BestOf(MeetingProfile.Satis));
        Assert.DoesNotContain(rows, r => r.Changed);
    }
}
