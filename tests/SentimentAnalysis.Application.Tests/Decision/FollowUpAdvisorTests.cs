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

    private static readonly double[] Kotulesen = { .3, .2, .4, .2, .2, .9 };
    private static readonly double[] Olumsuz = { .2, .1, .9, .3, .1, .3 };
    private static readonly double[] DusukEnerji = { .1, .3, .1, .1, .9, .2 };

    [Theory]
    [InlineData(MeetingProfile.Satis, "Gergin", FollowUpAction.IliskiyiOnar, FollowUpCategory.IliskiyiGozetin)]
    [InlineData(MeetingProfile.Satis, "Ilgisiz", FollowUpAction.IkinciGorusme, FollowUpCategory.Netlestirin)]
    [InlineData(MeetingProfile.Satis, "Uyumsuz", FollowUpAction.ItirazBilgisi, FollowUpCategory.Netlestirin)]
    [InlineData(MeetingProfile.Satis, "Kotulesen", FollowUpAction.Beklet, FollowUpCategory.Bekleyin)]
    [InlineData(MeetingProfile.Mulakat, "Uyumsuz", FollowUpAction.EkGorusme, FollowUpCategory.Netlestirin)]
    [InlineData(MeetingProfile.Mulakat, "Gergin", FollowUpAction.GorusmeyiTekrarla, FollowUpCategory.IliskiyiGozetin)]
    [InlineData(MeetingProfile.Mulakat, "Olumsuz", FollowUpAction.IkinciGorusAl, FollowUpCategory.Netlestirin)]
    [InlineData(MeetingProfile.Mulakat, "Kotulesen", FollowUpAction.AdaylarlaKarsilastir, FollowUpCategory.Bekleyin)]
    [InlineData(MeetingProfile.Toplanti, "Gergin", FollowUpAction.BireBirGorusme, FollowUpCategory.IliskiyiGozetin)]
    [InlineData(MeetingProfile.Toplanti, "Ilgisiz", FollowUpAction.TakipToplantisi, FollowUpCategory.Netlestirin)]
    [InlineData(MeetingProfile.Toplanti, "DusukEnerji", FollowUpAction.YaziliGorus, FollowUpCategory.Netlestirin)]
    [InlineData(MeetingProfile.Toplanti, "Kotulesen", FollowUpAction.KarariErtele, FollowUpCategory.Bekleyin)]
    public void Senaryo_BestVeYon(MeetingProfile p, string scenario, FollowUpAction best, FollowUpCategory direction)
    {
        var n = scenario switch
        {
            "Gergin" => Gergin, "Ilgisiz" => Ilgisiz, "Uyumsuz" => Uyumsuz, "Kotulesen" => Kotulesen,
            "Olumsuz" => Olumsuz, "DusukEnerji" => DusukEnerji, _ => throw new ArgumentException(scenario),
        };
        var d = Decide(p, n);
        Assert.Equal(best, d.Best);
        Assert.Equal(direction, d.Direction);
    }

    [Fact]
    public void Topsis_OptionsIlkUcVeIlkiBest()
    {
        var d = Decide(MeetingProfile.Satis, Gergin);
        Assert.Equal(3, d.Options.Count);
        Assert.Equal(d.Best, d.Options[0]);
        Assert.Equal(d.Ranking.Take(3).Select(r => r.Alternative), d.Options);
    }

    [Fact]
    public void Topsis_IlerletinSiralanmaz()
    {
        foreach (var p in Enum.GetValues<MeetingProfile>())
            foreach (var n in new[] { Gergin, Ilgisiz, Uyumsuz, Kotulesen, Olumsuz, DusukEnerji })
                Assert.DoesNotContain(Decide(p, n).Ranking,
                    r => DecisionKnowledgeBase.CategoryOf(r.Alternative) == FollowUpCategory.Ilerletin);
    }

    [Theory]
    [InlineData(MeetingProfile.Satis, FollowUpAction.TeklifGonder)]
    [InlineData(MeetingProfile.Mulakat, FollowUpAction.DegerlendirmeyeGec)]
    [InlineData(MeetingProfile.Toplanti, FollowUpAction.KararEpostasi)]
    public void Calm_PositiveFollowUp(MeetingProfile p, FollowUpAction expected)
    {
        var d = Decide(p, Enumerable.Repeat(0.1, 6).ToArray());
        Assert.Equal(expected, d.Best);
        Assert.Contains("belirgin bir sorun görülmedi", d.Reason);
        Assert.Equal(FollowUpCategory.Ilerletin, d.Direction);
        Assert.Equal(DecisionKnowledgeBase.Get(p).AdvanceOptions.Take(3), d.Options);
        Assert.Empty(d.Ranking);
    }

    [Fact]
    public void Calm_Toplanti_TekSecenek()
    {
        var d = Decide(MeetingProfile.Toplanti, Enumerable.Repeat(0.1, 6).ToArray());
        Assert.Single(d.Options);
    }

    [Fact]
    public void Mulakat_LowQuality_Repeat()
    {
        var d = Decide(MeetingProfile.Mulakat, Enumerable.Repeat(0.1, 6).ToArray(), q: 0.3);
        Assert.Equal(FollowUpAction.GorusmeyiTekrarla, d.Best);
        Assert.Contains("yetersiz", d.Reason);
        Assert.Equal(FollowUpCategory.IliskiyiGozetin, d.Direction);
        Assert.Equal(new[] { FollowUpAction.GorusmeyiTekrarla }, d.Options);
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
    public void Sensitivity_Mulakat_Yorgun_NeredeyseBerabereOlduguIcinKirilgan()
    {
        // "Kısa ara ver" ile "empati kur" neredeyse berabere: ağırlıklar hafif oynayınca birinci değişir.
        // Duyarlılık analizi tam bunu söylemeli — karar kırılgan, tek bir seçeneğe aşırı güvenilmemeli.
        var rows = Sensitivity.Analyze(DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Mulakat), BestOf(MeetingProfile.Mulakat));
        var changed = rows.Where(r => r.Changed).ToArray();
        Assert.NotEmpty(changed);
        Assert.All(changed, r => Assert.Equal(LiveAction.EmpatiKur, r.Best));
        Assert.All(rows.Where(r => !r.Changed), r => Assert.Equal(LiveAction.AraVer, r.Best));
    }

    [Fact]
    public void Sensitivity_Satis_Yorgun_Stable()
    {
        var rows = Sensitivity.Analyze(DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis), BestOf(MeetingProfile.Satis));
        Assert.DoesNotContain(rows, r => r.Changed);
    }
}
