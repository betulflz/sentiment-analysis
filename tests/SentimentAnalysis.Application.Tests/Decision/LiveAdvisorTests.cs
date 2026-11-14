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
        Assert.Equal(0.8896, r.Closeness, 1e-3);
        Assert.Equal(new[] { Criterion.OlumsuzDuygu, Criterion.Gerilim }, r.TopCriteria);
    }

    [Fact]
    public void Satis_Ilgisiz_KonuyuDegistir() =>
        Assert.Equal(LiveAction.KonuyuDegistir, Once(MeetingProfile.Satis, Ilgisiz).Action);

    [Fact]
    public void Satis_Uyumsuz_TereddudunuSor()
    {
        // Genişletilmiş eylem setinde saf söz–his çelişkisine en uygun eylem "açıkça tereddüdü sor".
        var r = Once(MeetingProfile.Satis, Uyumsuz);
        Assert.Equal(LiveAction.TereddudunuSor, r.Action);
        Assert.Equal(Criterion.Uyumsuzluk, r.TopCriteria[0]);
    }

    [Fact]
    public void Mulakat_Gergin_EmpatiKur()
    {
        var r = Once(MeetingProfile.Mulakat, Gergin);
        Assert.Equal(LiveAction.EmpatiKur, r.Action);
        Assert.Equal(0.9067, r.Closeness, 1e-3);
        Assert.Equal(new[] { Criterion.Gerilim, Criterion.OlumsuzDuygu }, r.TopCriteria);
    }

    [Fact]
    public void Mulakat_Yorgun_AraVer_EmpatiKurlaNeredeyseBerabere()
    {
        // Yorgun durumda "kısa ara ver" (0,6054) ile "empati kur" (0,6045) neredeyse berabere; fark 0,01'in altında.
        var r = Once(MeetingProfile.Mulakat, Yorgun);
        Assert.Equal(LiveAction.AraVer, r.Action);
        Assert.True(r.Closeness - r.Ranking.First(x => x.Alternative == LiveAction.EmpatiKur).Closeness < 0.01);
    }

    [Fact]
    public void Toplanti_Ilgisiz_KonuyuDegistir()
    {
        var r = Once(MeetingProfile.Toplanti, Ilgisiz);
        Assert.Equal(LiveAction.KonuyuDegistir, r.Action);
        Assert.Equal(0.8966, r.Closeness, 1e-3);
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
        // Çeşitlilik kuralı (RepeatLimit) bu testin konusu değil; kapatılır.
        var advisor = new LiveAdvisor(MeetingProfile.Satis, Cfg with { RepeatLimit = 1000 });
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
        // Eşik bandını izole eder: çeşitlilik kuralı (art arda aynı öneri) bu testin konusu değil, kapalı.
        var advisor = new LiveAdvisor(MeetingProfile.Satis, Cfg with { RepeatLimit = 1000 });
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

    // ---- Çeşitlilik: aynı öneri art arda gösterilince yakın bir alternatife geçilir ----

    private static LiveAction[] Run(DecisionConfig cfg, MeetingProfile p, double[] n, int windows)
    {
        var advisor = new LiveAdvisor(p, cfg);
        return Enumerable.Range(0, windows)
            .Select(_ => advisor.Next(Ind(n), DecisionKnowledgeBase.DefaultWeights(p)).Action).ToArray();
    }

    private static int LongestRun(LiveAction[] actions)
    {
        int best = 0, run = 0;
        for (int i = 0; i < actions.Length; i++)
        {
            run = i > 0 && actions[i] == actions[i - 1] ? run + 1 : 1;
            best = Math.Max(best, run);
        }
        return best;
    }

    [Fact]
    public void Cesitlilik_AyniOneriArtArdaUcPenceredenFazlaGosterilmez()
    {
        var actions = Run(Cfg, MeetingProfile.Satis, Gergin, 12);

        Assert.True(LongestRun(actions) <= Cfg.RepeatLimit, string.Join(",", actions));
        Assert.True(actions.Distinct().Count() >= 2);
    }

    [Fact]
    public void Cesitlilik_OnerilenAlternatif_BirinciyeYakinOlanlardanDir()
    {
        var advisor = new LiveAdvisor(MeetingProfile.Satis, Cfg);
        var weights = DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis);
        for (int i = 0; i < 12; i++)
        {
            var r = advisor.Next(Ind(Gergin), weights);
            double top = r.Ranking[0].Closeness;
            double shown = r.Ranking.First(x => x.Alternative == r.Action).Closeness;
            Assert.True(top - shown <= Cfg.DiversityMargin + 1e-9, $"pencere {i}: {r.Action} birinciden fazla geride");
        }
    }

    [Fact]
    public void Cesitlilik_Kapatilinca_AyniOneriSurer()
    {
        var actions = Run(Cfg with { RepeatLimit = 1000 }, MeetingProfile.Satis, Gergin, 12);

        Assert.Single(actions.Distinct());
    }

    [Fact]
    public void Cesitlilik_YakinAlternatifYoksa_AyniOneriSurer()
    {
        var actions = Run(Cfg with { DiversityMargin = 0.0001 }, MeetingProfile.Satis, Gergin, 12);

        Assert.Single(actions.Distinct());
    }

    [Fact]
    public void Cesitlilik_AgirlikDegisimindeHemenEnIyiGosterilir()
    {
        var advisor = new LiveAdvisor(MeetingProfile.Satis, Cfg);
        var weights = DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis);
        Recommendation? last = null;
        for (int i = 0; i < Cfg.RepeatLimit + 1; i++) last = advisor.Next(Ind(Gergin), weights);

        var reranked = advisor.Rerank(Ind(Gergin), weights);

        Assert.Equal(reranked.Ranking[0].Alternative, reranked.Action);   // kaydırıcı oynatınca çeşitlilik değil en iyi
    }

    // ---- Olumlu durum: sorun yokken ve görüşme gerçekten olumluysa olumlu öneri ----

    private static readonly double[] Sakin = { .05, .05, .05, .05, .05, .05 };

    private static Indicators Pos(double[] n, double positivity, double quality = 0.8) =>
        new(n, new int?[6], quality) { Positivity = positivity };

    private static LiveAction[] RunPos(double[] n, double positivity, int windows, DecisionConfig? cfg = null)
    {
        var advisor = new LiveAdvisor(MeetingProfile.Satis, cfg ?? Cfg);
        var w = DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis);
        return Enumerable.Range(0, windows).Select(_ => advisor.Next(Pos(n, positivity), w).Action).ToArray();
    }

    [Fact]
    public void Olumlu_SorunYokVeOlumluysa_BirAdimIlerlet()
    {
        var r = new LiveAdvisor(MeetingProfile.Satis, Cfg).Next(Pos(Sakin, 0.8), DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis));

        Assert.Equal(LiveAction.BirAdimIlerlet, r.Action);
        Assert.Contains("olumlu", r.Reason);
    }

    [Fact]
    public void Olumlu_SorunYokAmaOlumluDegilse_DevamEt()
    {
        var r = new LiveAdvisor(MeetingProfile.Satis, Cfg).Next(Pos(Sakin, 0.3), DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis));

        Assert.Equal(LiveAction.DevamEt, r.Action);
    }

    [Fact]
    public void Olumlu_SorunVarsa_OlumluOneriVerilmez()
    {
        var r = new LiveAdvisor(MeetingProfile.Satis, Cfg).Next(Pos(Gergin, 0.9), DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis));

        Assert.NotEqual(LiveAction.BirAdimIlerlet, r.Action);
        Assert.NotEqual(LiveAction.OlumluyuPekistir, r.Action);
        Assert.NotEmpty(r.Ranking);
    }

    [Fact]
    public void Olumlu_ArtArdaUcPenceredenFazlaAyniOlumluOneriYok_IkisiDeGorunur()
    {
        var actions = RunPos(Sakin, 0.8, 9);

        Assert.True(LongestRun(actions) <= Cfg.RepeatLimit, string.Join(",", actions));
        Assert.Contains(LiveAction.BirAdimIlerlet, actions);
        Assert.Contains(LiveAction.OlumluyuPekistir, actions);
    }

    [Fact]
    public void Olumlu_OlumluOneriMudahaleSayilmaz_MudahaleCikisBandiUygulanmaz()
    {
        // İhtiyaç 0,28: müdahale girişi 0,30'un altında. Olumlu öneri müdahale olsaydı çıkış eşiği 0,25 uygulanır
        // ve 0,28 yeniden müdahaleye döndürürdü.
        var advisor = new LiveAdvisor(MeetingProfile.Satis, Cfg);
        var w = DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis);
        Assert.Equal(LiveAction.BirAdimIlerlet, advisor.Next(Pos(Sakin, 0.8), w).Action);

        var gri = new[] { .28, .05, .05, .05, .05, .05 };   // kalite 1,0 → ihtiyaç 0,28 (band: 0,25–0,30)
        var r = advisor.Next(Pos(gri, 0.8, quality: 1.0), w);

        Assert.Empty(r.Ranking);
        Assert.True(r.Action is LiveAction.BirAdimIlerlet or LiveAction.OlumluyuPekistir);
    }

    [Fact]
    public void Olumlu_VeriYetersizse_OlumluOneriYok()
    {
        var r = new LiveAdvisor(MeetingProfile.Satis, Cfg).Next(Pos(Sakin, 0.9, quality: 0.1), DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis));

        Assert.Equal(LiveAction.DevamEt, r.Action);
        Assert.Contains("Veri yetersiz", r.Reason);
    }
}

public class ContextDiversityTests
{
    private static readonly DecisionConfig Cfg = new();
    private static readonly double[] Mixed = { .5, .4, .5, .6, .4, .3 };

    private static Indicators Ind(ContextCues? cues) => new(Mixed, new int?[6], 0.8) { Cues = cues };

    private static LiveAction First(MeetingProfile p, ContextCues? cues) =>
        new LiveAdvisor(p, Cfg).Next(Ind(cues), DecisionKnowledgeBase.DefaultWeights(p)).Action;

    private static ContextCues Cue(Emotion e, double wps = 1.5, double away = 0, double smile = 0) =>
        new(e, 0.5, -0.3, wps, away, smile, 0.2, true);

    [Fact]
    public void FarkliDuygular_FarkliOneriler()
    {
        var actions = new[] { Emotion.Kizgin, Emotion.Uzgun, Emotion.Sikilmis, Emotion.Gergin }
            .Select(e => First(MeetingProfile.Satis, Cue(e))).ToArray();
        Assert.True(actions.Distinct().Count() >= 3, string.Join(",", actions));
        Assert.DoesNotContain(LiveAction.SoruSor, actions);
    }

    [Fact]
    public void BasCevirme_GozTemasiOnerilir() =>
        Assert.Contains(First(MeetingProfile.Toplanti, Cue(Emotion.Notr, away: 0.9)),
            new[] { LiveAction.GozTemasiKur, LiveAction.SozVer, LiveAction.EnerjiYukselt, LiveAction.KonuyuDegistir });

    [Fact]
    public void HizliKonusma_YavaslamaOnerilir() =>
        Assert.Contains(First(MeetingProfile.Mulakat, Cue(Emotion.Gergin, wps: 3.5)),
            new[] { LiveAction.YavaslaKonus, LiveAction.SakinOl, LiveAction.GuvenceVer });

    // ---- 12: konuşma payı ve yönetenin hızı ----

    [Fact]
    public void YonetenCokKonusuyor_DinlemeVeSoruOneCikar() =>
        Assert.Contains(First(MeetingProfile.Mulakat, Cue(Emotion.Notr) with { HostShare = 0.85 }),
            new[] { LiveAction.SusVeDinle, LiveAction.SoruSor, LiveAction.KisaTut, LiveAction.TereddudunuSor });

    [Fact]
    public void YonetenHizli_KarisikHizYavasOlsaBile_YavaslamaOnerilir() =>
        Assert.Contains(First(MeetingProfile.Mulakat, Cue(Emotion.Gergin, wps: 0.5) with { HostWordsPerSecond = 3.6 }),
            new[] { LiveAction.YavaslaKonus, LiveAction.SakinOl, LiveAction.GuvenceVer });

    [Fact]
    public void SorunYokAmaKonusmaYonetende_SusVeDinleKuralla()
    {
        var calm = new Indicators(new[] { .05, .05, .05, .05, .05, .05 }, new int?[6], 0.8)
        { Positivity = 0.9, Cues = Cue(Emotion.Mutlu) with { HostShare = 0.8 } };

        var r = new LiveAdvisor(MeetingProfile.Satis, Cfg).Next(calm, DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis));

        Assert.Equal(LiveAction.SusVeDinle, r.Action);
        Assert.Contains("%80", r.Reason);
    }

    [Fact]
    public void KonusmaPayiKurali_DusukKaliteliSinyaldeDevredeDegil()
    {
        // Bir gösterge eşiği aşıyor ama kalite düşük (ihtiyaç eşik altı): kardeş kurallar gibi öneri yapılmaz.
        var weak = new Indicators(new[] { .6, .05, .05, .05, .05, .05 }, new int?[6], 0.35)
        { Cues = Cue(Emotion.Notr) with { HostShare = 0.9 } };

        var r = new LiveAdvisor(MeetingProfile.Satis, Cfg).Next(weak, DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis));

        Assert.Equal(LiveAction.DevamEt, r.Action);
    }

    [Fact]
    public void SorunYokKonusmaDengeli_KuralDevredeDegil()
    {
        var calm = new Indicators(new[] { .05, .05, .05, .05, .05, .05 }, new int?[6], 0.8)
        { Cues = Cue(Emotion.Notr) with { HostShare = 0.5 } };

        var r = new LiveAdvisor(MeetingProfile.Satis, Cfg).Next(calm, DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis));

        Assert.Equal(LiveAction.DevamEt, r.Action);
    }

    [Fact]
    public void BaglamYoksa_EskiSiralamaAynen() =>
        Assert.Equal(new LiveAdvisor(MeetingProfile.Satis, Cfg).Next(Ind(null), DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis)).Action,
            First(MeetingProfile.Satis, null));
}
