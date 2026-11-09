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
    public void Rerank_SkipsHysteresis()
    {
        var advisor = new LiveAdvisor(MeetingProfile.Satis, Cfg);
        var w = DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis);
        Assert.Equal(LiveAction.EmpatiKur, advisor.Next(Ind(A), w).Action);
        // Next(B) burada histerezis yüzünden Empati'de kalırdı (bkz. Hysteresis_SmallLead_NeedsTwoWindows).
        Assert.Equal(LiveAction.KonuyuDegistir, advisor.Rerank(Ind(B), w).Action);
        Assert.Equal(LiveAction.KonuyuDegistir, advisor.Next(Ind(A), w).Action);  // Rerank sonucu mevcut öneri oldu
    }

    [Fact]
    public void Rerank_VeriYetersizBandiniKorur()
    {
        // 0,27: yetersiz duruma girmek için yüksek (≥0,25), çıkmak için düşük (≤0,30). Kaydırıcı oynatmak
        // bandı sıfırlamamalı — yoksa kart "Veri yetersiz"den müdahaleye zıplar (11d minor).
        var advisor = new LiveAdvisor(MeetingProfile.Satis, Cfg);
        var w = DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis);
        Assert.StartsWith("Veri yetersiz", advisor.Next(Ind(Gergin, 0.2), w).Reason);
        Assert.StartsWith("Veri yetersiz", advisor.Next(Ind(Gergin, 0.27), w).Reason);
        Assert.StartsWith("Veri yetersiz", advisor.Rerank(Ind(Gergin, 0.27), w).Reason);
    }

    [Fact]
    public void Rerank_MudahaleBandiniKorur()
    {
        // Gösterilen müdahale ancak max ihtiyaç < 0,30'da "Devam et"e döner; 0,32'de Rerank da müdahalede kalmalı.
        var advisor = new LiveAdvisor(MeetingProfile.Satis, Cfg);
        var w = DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis);
        var sinirda = new[] { .4, .1, .4, .1, .1, .1 }; // × kalite 0,8 → max ihtiyaç 0,32
        Assert.NotEqual(LiveAction.DevamEt, advisor.Next(Ind(Gergin), w).Action);
        Assert.NotEqual(LiveAction.DevamEt, advisor.Next(Ind(sinirda), w).Action);
        Assert.NotEqual(LiveAction.DevamEt, advisor.Rerank(Ind(sinirda), w).Action);
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
    public void LowQuality_ScalesNeeds_NoIntervention()
    {
        // Spec §6.4.1: ihtiyaç = gösterge × veri kalitesi. 0,8 × 0,3 = 0,24 < 0,35 → müdahale yok.
        var r = Once(MeetingProfile.Satis, Gergin, quality: 0.3);
        Assert.Equal(LiveAction.DevamEt, r.Action);
        Assert.Equal(Confidence.Dusuk, r.Confidence);
        Assert.Contains("veri kalitesi düşük", r.Reason);
    }

    [Fact]
    public void ThresholdBand_NoFlickerAroundThreshold()
    {
        var advisor = new LiveAdvisor(MeetingProfile.Satis, Cfg);
        var w = DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis);
        double[] Need(double k1) => new[] { k1, .1, k1 * .9, .1, .1, .1 };
        // kalite 0,8 → ölçekli maks: 0,36 / 0,32 / 0,36 / 0,32
        Assert.Equal(LiveAction.EmpatiKur, advisor.Next(Ind(Need(.45)), w).Action);
        Assert.Equal(LiveAction.EmpatiKur, advisor.Next(Ind(Need(.40)), w).Action);
        Assert.Equal(LiveAction.EmpatiKur, advisor.Next(Ind(Need(.45)), w).Action);
        Assert.Equal(LiveAction.EmpatiKur, advisor.Next(Ind(Need(.40)), w).Action);
        // gerçekten sakinleşince (0,2 × 0,8 = 0,16 < 0,30) Devam et
        Assert.Equal(LiveAction.DevamEt, advisor.Next(Ind(Need(.2)), w).Action);
    }

    [Fact]
    public void QualityBand_NoFlickerAroundInsufficient()
    {
        var advisor = new LiveAdvisor(MeetingProfile.Satis, Cfg);
        var w = DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis);
        Assert.Contains("Veri yetersiz", advisor.Next(Ind(Gergin, quality: 0.24), w).Reason);
        Assert.Contains("Veri yetersiz", advisor.Next(Ind(Gergin, quality: 0.26), w).Reason);
        Assert.Contains("Veri yetersiz", advisor.Next(Ind(Gergin, quality: 0.24), w).Reason);
        Assert.Equal(LiveAction.EmpatiKur, advisor.Next(Ind(Gergin, quality: 0.8), w).Action);
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

    [Fact]
    public void DecisionText_CriteriaHavePlainNamesAndDescriptions()
    {
        // Konuyu bilmeyen kullanıcı için: teknik terim yok, her kriterin tek satırlık açıklaması var.
        Assert.Equal("söz ile his çelişkisi", DecisionText.Name(Criterion.Uyumsuzluk));
        Assert.All(Enum.GetValues<Criterion>(), c => Assert.False(string.IsNullOrWhiteSpace(DecisionText.Description(c))));
        Assert.Equal(Enum.GetValues<Criterion>().Length,
            Enum.GetValues<Criterion>().Select(DecisionText.Description).Distinct().Count());
    }
}
