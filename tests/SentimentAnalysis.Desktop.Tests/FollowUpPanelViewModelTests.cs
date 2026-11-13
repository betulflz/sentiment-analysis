using System;
using System.Linq;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Decision;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class FollowUpPanelViewModelTests
{
    private static readonly DecisionConfig Cfg = new();
    private static readonly double[] Gergin = { .8, .1, .7, .2, .1, .3 };
    private static readonly double[] Sakin = { .1, .1, .1, .1, .1, .1 };
    private static readonly double[] Uyumsuz = { .2, .1, .2, .9, .1, .1 };
    private static readonly double[] FinalW = { 0.1, 0.1, 0.6, 0.1, 0.05, 0.05 };

    internal static DecisionWindow Window(double start, double[] values, double quality = 0.8) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(start + 10),
            new Indicators(values, new int?[6], quality),
            new Recommendation(LiveAction.DevamEt, 1, Array.Empty<Ranked<LiveAction>>(), Array.Empty<Criterion>(),
                null, Confidence.Orta, "Belirgin bir sorun yok."));

    private static DecisionSession Session(double[] values, MeetingProfile p = MeetingProfile.Satis, double[]? final = null) =>
        new(p, DecisionKnowledgeBase.DefaultWeights(p), final ?? FinalW,
            new[] { Window(0, values), Window(10, values), Window(20, values) });

    private static FollowUpDecision Expected(DecisionSession s, double[] weights) =>
        FollowUpAdvisor.Decide(s.Profile,
            FollowUpAdvisor.AggregateMeeting(s.Windows.Select(w => w.Indicators).ToArray(), Cfg), weights, Cfg);

    private static FollowUpPanelViewModel Loaded(DecisionSession s)
    {
        var vm = new FollowUpPanelViewModel(Cfg);
        vm.Load(s);
        return vm;
    }

    [Fact]
    public void Load_GorusmeSonuAgirliklariylaBaslar()
    {
        var s = Session(Gergin);
        var vm = Loaded(s);
        Assert.True(vm.HasDecision);
        Assert.Equal(FinalW, vm.Weights.Select(w => w.Value));
        Assert.Equal(Expected(s, FinalW).Best, vm.Current!.Best);
        Assert.Equal(DecisionText.Name(vm.Current.Best), vm.BestText);
        Assert.Equal(vm.Current.Reason, vm.ReasonText);
        Assert.Equal(6, vm.MeetingIndicators.Count);
    }

    [Fact]
    public void KaydiriciDegisince_KararYenidenHesaplanir()
    {
        var s = Session(Gergin);
        var vm = Loaded(s);
        vm.Weights[(int)Criterion.OlumsuzDuygu].Value = 0;
        var w = FinalW.ToArray();
        w[(int)Criterion.OlumsuzDuygu] = 0;
        var expected = Expected(s, w);
        Assert.Equal(expected.Best, vm.Current!.Best);
        Assert.Equal(expected.Ranking.Count, vm.Ranking.Count);
    }

    [Fact]
    public void ResetWeights_GorusmeSonuAgirliklarinaDoner()
    {
        var vm = Loaded(Session(Gergin));
        vm.Weights[0].Value = 0.9;
        vm.ResetWeightsCommand.Execute(null);
        Assert.Equal(FinalW, vm.Weights.Select(w => w.Value));
    }

    [Fact]
    public void Sakin_Gorusme_OlumluTakip_SiralamaBos()
    {
        var vm = Loaded(Session(Sakin));
        Assert.Equal("Teklif veya fiyat önerisi gönderin", vm.BestText);
        Assert.Empty(vm.Ranking);
        Assert.All(vm.Sensitivity.SelectMany(r => r.Cells), c => Assert.Equal("aynı", c.Text));
        // Karar kuraldan geldi (TOPSIS çalışmadı): "ağırlıklara duyarlı değil" demek yanıltır.
        Assert.Equal("Bu öneri önem ayarlarına bağlı değil, görüşmenin genel durumundan geliyor (gerekçeye bakın).", vm.SensitivitySummary);
        Assert.Equal("Karar sağlam", vm.SensitivityVerdict);
        Assert.True(vm.SensitivityStable);
    }

    [Fact]
    public void Duyarlilik_6Satir4Hucre_AnalizleAyni()
    {
        var s = Session(Gergin);
        var vm = Loaded(s);
        var rows = Sensitivity.Analyze(FinalW, w => Expected(s, w.ToArray()).Best);

        Assert.Equal(6, vm.Sensitivity.Count);
        Assert.All(vm.Sensitivity, r => Assert.Equal(4, r.Cells.Count));
        var cells = vm.Sensitivity.SelectMany(r => r.Cells).ToArray();
        for (int i = 0; i < rows.Count; i++)
        {
            Assert.Equal(rows[i].Changed, cells[i].Changed);
            Assert.Equal(rows[i].Changed ? DecisionText.Name(rows[i].Best) : "aynı", cells[i].Text);
        }
        Assert.Equal("Gerilim", vm.Sensitivity[0].Criterion);

        int changed = rows.Count(r => r.Changed);
        if (changed == 0)
        {
            Assert.Equal("Önem ayarlarını biraz artırıp azalttığımız 24 denemenin hepsinde aynı öneri çıktı.", vm.SensitivitySummary);
            Assert.Equal("Karar sağlam", vm.SensitivityVerdict);
        }
        else
        {
            var most = rows.Where(r => r.Changed).GroupBy(r => r.Criterion)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
            Assert.Equal($"Önem ayarlarını biraz artırıp azalttığımız 24 denemenin {changed} tanesinde öneri değişti; en çok \"{DecisionText.Name(most)}\" ayarına bağlı.",
                vm.SensitivitySummary);
        }
    }

    [Fact]
    public void Duyarlilik_DegisenSenaryo_OzetteSayilir()
    {
        // Bu ağırlıklarda söz–his çelişkili satış görüşmesinin kararı sınırdadır: 24 senaryonun 4'ünde değişir
        // (motorla tarandı, 2026-11-13 kategorili seçeneklerle; gergin senaryo artık "ilişkiyi onar"da net).
        var s = Session(Uyumsuz, final: new[] { 0.4, 0.2, 0.9, 0.1, 1.0, 0.5 });
        var vm = Loaded(s);
        var rows = Sensitivity.Analyze(s.FinalWeights, w => Expected(s, w.ToArray()).Best);
        Assert.Contains(rows, r => r.Changed);
        Assert.Contains("24 denemenin 4 tanesinde öneri değişti", vm.SensitivitySummary);
        // 4/24 ≤ %25 → büyük ölçüde sağlam (sarı), kırılgan değil.
        Assert.Equal("Karar büyük ölçüde sağlam", vm.SensitivityVerdict);
        Assert.False(vm.SensitivityStable);
        Assert.True(vm.SensitivityMostlyStable);
        Assert.False(vm.SensitivityFragile);
        Assert.Equal(rows.Count(r => r.Changed), vm.Sensitivity.SelectMany(r => r.Cells).Count(c => c.Changed));
    }

    [Theory]
    [InlineData(0, 24, "Karar sağlam")]
    [InlineData(6, 24, "Karar büyük ölçüde sağlam")]
    [InlineData(7, 24, "Karar kırılgan")]
    public void DuyarlilikHukmu_DegisenOranaGore(int changed, int total, string expected) =>
        Assert.Equal(expected, FollowUpPanelViewModel.Verdict(changed, total));

    [Fact]
    public void TumAgirliklarSifir_CokmezEsitAgirlik()
    {
        var vm = Loaded(Session(Gergin));
        foreach (var w in vm.Weights) w.Value = 0;
        Assert.NotNull(vm.Current);
        Assert.False(string.IsNullOrEmpty(vm.BestText));
        Assert.Equal(6, vm.Sensitivity.Count);
    }

    [Fact]
    public void ProfileText_CrIcerir() =>
        Assert.StartsWith("Profil: Mülakat · AHP tutarlılık oranı CR = 0,",
            Loaded(Session(Gergin, MeetingProfile.Mulakat)).ProfileText);

    [Fact]
    public void Load_PencereYok_HasDecisionFalse()
    {
        var vm = Loaded(new DecisionSession(MeetingProfile.Satis, FinalW, FinalW, Array.Empty<DecisionWindow>()));
        Assert.False(vm.HasDecision);
        Assert.Null(vm.Current);
    }

    [Fact]
    public void HasRanking_SiralamayaUyar()
    {
        var calm = Loaded(Session(Sakin));
        Assert.False(calm.HasRanking);

        var tense = Loaded(Session(Gergin));
        Assert.NotEmpty(tense.Ranking);
        Assert.True(tense.HasRanking);
    }
}
