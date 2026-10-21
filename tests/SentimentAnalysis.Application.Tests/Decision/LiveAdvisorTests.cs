using System;
using System.Linq;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Decision;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Decision;

public class LiveAdvisorTests
{
    private static readonly DecisionConfig Cfg = new();

    private static Indicators Ind(double[] n, double quality = 0.8, int?[]? faces = null) =>
        new(n, faces ?? new int?[6], quality);

    private static Recommendation Once(MeetingProfile p, double[] n, double quality = 0.8, int?[]? faces = null) =>
        new LiveAdvisor(p, Cfg).Next(Ind(n, quality, faces), DecisionKnowledgeBase.DefaultWeights(p));

    private static readonly double[] Gergin = { .8, .1, .7, .2, .1, .3 };
    private static readonly double[] Ilgisiz = { .1, .9, .1, .1, .6, .2 };
    private static readonly double[] Uyumsuz = { .2, .1, .2, .9, .1, .1 };
    private static readonly double[] Yorgun = { .3, .4, .1, .1, .9, .2 };

    [Fact]
    public void Satis_Gergin_EmpatiKur()
    {
        var r = Once(MeetingProfile.Satis, Gergin);
        Assert.Equal(LiveAction.EmpatiKur, r.Action);
        Assert.Equal(0.8790, r.Closeness, 1e-3);
        Assert.Equal(new[] { Criterion.OlumsuzDuygu, Criterion.Gerilim }, r.TopCriteria);
    }

    [Fact]
    public void Satis_Ilgisiz_KonuyuDegistir() =>
        Assert.Equal(LiveAction.KonuyuDegistir, Once(MeetingProfile.Satis, Ilgisiz).Action);

    [Fact]
    public void Satis_Uyumsuz_SoruSor()
    {
        var r = Once(MeetingProfile.Satis, Uyumsuz);
        Assert.Equal(LiveAction.SoruSor, r.Action);
        Assert.Equal(Criterion.Uyumsuzluk, r.TopCriteria[0]);
    }

    [Fact]
    public void Mulakat_Gergin_EmpatiKur()
    {
        var r = Once(MeetingProfile.Mulakat, Gergin);
        Assert.Equal(LiveAction.EmpatiKur, r.Action);
        Assert.Equal(0.8894, r.Closeness, 1e-3);
        Assert.Equal(new[] { Criterion.Gerilim, Criterion.OlumsuzDuygu }, r.TopCriteria);
    }

    [Fact]
    public void Mulakat_Yorgun_EmpatiKur() =>
        Assert.Equal(LiveAction.EmpatiKur, Once(MeetingProfile.Mulakat, Yorgun).Action);

    [Fact]
    public void Toplanti_Ilgisiz_KonuyuDegistir()
    {
        var r = Once(MeetingProfile.Toplanti, Ilgisiz);
        Assert.Equal(LiveAction.KonuyuDegistir, r.Action);
        Assert.Equal(0.8918, r.Closeness, 1e-3);
        Assert.Equal(new[] { Criterion.IlgiKaybi, Criterion.DusukEnerji }, r.TopCriteria);
    }

    [Fact]
    public void Toplanti_Yorgun_KonuyuDegistir() =>
        Assert.Equal(LiveAction.KonuyuDegistir, Once(MeetingProfile.Toplanti, Yorgun).Action);

    [Theory]
    [InlineData(MeetingProfile.Satis)]
    [InlineData(MeetingProfile.Mulakat)]
    [InlineData(MeetingProfile.Toplanti)]
    public void BelowThreshold_DevamEt(MeetingProfile p)
    {
        Assert.Equal(LiveAction.DevamEt, Once(p, Enumerable.Repeat(0.05, 6).ToArray()).Action);
        Assert.Equal(LiveAction.DevamEt, Once(p, new[] { .25, .2, .2, .2, .2, .2 }).Action);
    }

    [Fact]
    public void InsufficientQuality_ReturnsDevamEtLow()
    {
        var r = Once(MeetingProfile.Satis, Gergin, quality: 0.1);
        Assert.Equal(LiveAction.DevamEt, r.Action);
        Assert.Equal(Confidence.Dusuk, r.Confidence);
        Assert.Contains("Veri yetersiz", r.Reason);
    }

    [Theory]
    [InlineData(0.3, Confidence.Dusuk)]
    [InlineData(0.5, Confidence.Orta)]
    [InlineData(0.8, Confidence.Yuksek)]
    public void Confidence_FollowsDataQuality(double q, Confidence expected) =>
        Assert.Equal(expected, Once(MeetingProfile.Satis, Gergin, quality: q).Confidence);

    [Fact]
    public void AllZeroWeights_NoNaN()
    {
        var r = new LiveAdvisor(MeetingProfile.Satis, Cfg).Next(Ind(Gergin), new double[6]);
        Assert.True(double.IsFinite(r.Closeness));
        Assert.All(r.Ranking, x => Assert.True(double.IsFinite(x.Closeness)));
    }

    [Fact]
    public void UnnormalizedWeights_SameAsNormalized()
    {
        var w = DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis);
        var a = new LiveAdvisor(MeetingProfile.Satis, Cfg).Next(Ind(Uyumsuz), w);
        var b = new LiveAdvisor(MeetingProfile.Satis, Cfg).Next(Ind(Uyumsuz), w.Select(x => x * 100).ToArray());
        Assert.Equal(a.Action, b.Action);
        Assert.Equal(a.Closeness, b.Closeness, 1e-9);
    }

    // Satış ağırlıklarıyla: A → Empati (Konu'nun 0,046 önünde); B → Konu (Empati'nin yalnızca 0,018 önünde).
    private static readonly double[] A = { .40, .6, .32, .1, .3, .2 };
    private static readonly double[] B = { .35, .6, .28, .1, .3, .2 };

    [Fact]
    public void Hysteresis_SmallLead_NeedsTwoWindows()
    {
        var advisor = new LiveAdvisor(MeetingProfile.Satis, Cfg);
        var w = DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis);
        Assert.Equal(LiveAction.EmpatiKur, advisor.Next(Ind(A), w).Action);
        Assert.Equal(LiveAction.EmpatiKur, advisor.Next(Ind(B), w).Action);
        Assert.Equal(LiveAction.KonuyuDegistir, advisor.Next(Ind(B), w).Action);
    }

    [Fact]
    public void Hysteresis_LargeLead_SwitchesImmediately()
    {
        var advisor = new LiveAdvisor(MeetingProfile.Satis, Cfg);
        var w = DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis);
        Assert.Equal(LiveAction.EmpatiKur, advisor.Next(Ind(A), w).Action);
        Assert.Equal(LiveAction.KonuyuDegistir, advisor.Next(Ind(Ilgisiz), w).Action);
    }

    [Fact]
    public void Hysteresis_InterruptedCandidate_ResetsCount()
    {
        var advisor = new LiveAdvisor(MeetingProfile.Satis, Cfg);
        var w = DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis);
        advisor.Next(Ind(A), w);
        advisor.Next(Ind(B), w);           // Konu 1. kez önde
        advisor.Next(Ind(A), w);           // Empati yine önde → sayaç sıfırlanır
        Assert.Equal(LiveAction.EmpatiKur, advisor.Next(Ind(B), w).Action);
    }

    [Fact]
    public void Reset_ForgetsCurrent()
    {
        var advisor = new LiveAdvisor(MeetingProfile.Satis, Cfg);
        var w = DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis);
        advisor.Next(Ind(A), w);
        advisor.Reset();
        Assert.Equal(LiveAction.KonuyuDegistir, advisor.Next(Ind(B), w).Action);
    }

    [Fact]
    public void Reason_NamesFace()
    {
        var faces = new int?[6];
        faces[(int)Criterion.OlumsuzDuygu] = 1;
        var r = Once(MeetingProfile.Satis, Gergin, faces: faces);
        Assert.Equal(1, r.Face);
        Assert.StartsWith("Yüz 2:", r.Reason);
        Assert.Contains("olumsuz duygu %70", r.Reason);
        Assert.Contains("gerilim %80", r.Reason);
    }

    [Fact]
    public void Reason_GeneralWhenNoFace() =>
        Assert.StartsWith("Görüşme geneli:", Once(MeetingProfile.Satis, Gergin).Reason);

    [Fact]
    public void Reason_NeverJudgesPerson()
    {
        foreach (var p in Enum.GetValues<MeetingProfile>())
            foreach (var n in new[] { Gergin, Ilgisiz, Uyumsuz, Yorgun })
            {
                var reason = Once(p, n).Reason.ToLowerInvariant();
                Assert.DoesNotContain("yalan", reason);
                Assert.DoesNotContain("dürüst", reason);
                Assert.DoesNotContain("işe al", reason);
            }
    }

    [Fact]
    public void DecisionText_NamesAllEnums()
    {
        Assert.All(Enum.GetValues<Criterion>(), c => Assert.False(string.IsNullOrWhiteSpace(DecisionText.Name(c))));
        Assert.All(Enum.GetValues<LiveAction>(), a => Assert.False(string.IsNullOrWhiteSpace(DecisionText.Name(a))));
        Assert.All(Enum.GetValues<FollowUpAction>(), a => Assert.False(string.IsNullOrWhiteSpace(DecisionText.Name(a))));
        Assert.Equal("Yüz 1", DecisionText.Subject(0));
        Assert.Equal("Görüşme geneli", DecisionText.Subject(null));
    }
}
