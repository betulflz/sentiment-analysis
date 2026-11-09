using System;
using System.Linq;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Decision;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Decision;

public class DecisionKnowledgeBaseTests
{
    [Theory]
    [InlineData(MeetingProfile.Satis)]
    [InlineData(MeetingProfile.Mulakat)]
    [InlineData(MeetingProfile.Toplanti)]
    public void EveryProfile_ConsistencyRatioBelowTenPercent(MeetingProfile p)
    {
        var cr = Ahp.Compute(DecisionKnowledgeBase.Get(p).Pairwise).ConsistencyRatio;
        Assert.True(cr < 0.10, $"{p} CR = {cr}");
    }

    [Theory]
    [InlineData(MeetingProfile.Satis, new[] { .1888, .1738, .3129, .0909, .0597, .1738 })]
    [InlineData(MeetingProfile.Mulakat, new[] { .3409, .1063, .1931, .1931, .0604, .1063 })]
    [InlineData(MeetingProfile.Toplanti, new[] { .1366, .3525, .1189, .0673, .2171, .1077 })]
    public void DefaultWeights_MatchDocumentedValues(MeetingProfile p, double[] expected)
    {
        var w = DecisionKnowledgeBase.DefaultWeights(p);
        for (int j = 0; j < 6; j++) Assert.Equal(expected[j], w[j], 1e-3);
    }

    [Fact]
    public void ImpactRows_HaveSixValuesInUnitRange()
    {
        var rows = DecisionKnowledgeBase.LiveImpact.Values.Concat(DecisionKnowledgeBase.FollowUpImpact.Values);
        Assert.All(rows, r =>
        {
            Assert.Equal(6, r.Length);
            Assert.All(r, v => Assert.InRange(v, 0.0, 1.0));
        });
    }

    [Fact]
    public void EveryListedAlternative_HasAnImpactRow()
    {
        foreach (var p in Enum.GetValues<MeetingProfile>())
        {
            var d = DecisionKnowledgeBase.Get(p);
            Assert.All(d.Interventions, a => Assert.True(DecisionKnowledgeBase.LiveImpact.ContainsKey(a)));
            Assert.All(d.FollowUps, a => Assert.True(DecisionKnowledgeBase.FollowUpImpact.ContainsKey(a)));
            Assert.DoesNotContain(LiveAction.DevamEt, d.Interventions);
        }
    }

    [Theory]
    [InlineData(MeetingProfile.Satis)]
    [InlineData(MeetingProfile.Mulakat)]
    [InlineData(MeetingProfile.Toplanti)]
    public void Pairwise_IsReciprocal(MeetingProfile p)
    {
        var m = DecisionKnowledgeBase.Get(p).Pairwise;
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 6; j++)
                Assert.Equal(1.0, m[i, j] * m[j, i], 1e-12);
    }

    [Fact]
    public void AppConfig_Default_HasDecisionDefaults()
    {
        Assert.Equal(0.35, AppConfig.Default.Decision.InterventionThreshold);
        Assert.Equal(2, AppConfig.Default.Decision.SwitchPersistence);
    }

    private static readonly LiveAction[] Positive = { LiveAction.BirAdimIlerlet, LiveAction.OlumluyuPekistir };

    [Theory]
    [InlineData(MeetingProfile.Satis, LiveAction.FaydayaDon)]
    [InlineData(MeetingProfile.Mulakat, LiveAction.KolaySoruSor)]
    [InlineData(MeetingProfile.Toplanti, LiveAction.SozVer)]
    public void Profil_GenisEylemListesiVeKendineOzelEylemi_Var(MeetingProfile p, LiveAction ozel)
    {
        var list = DecisionKnowledgeBase.Get(p).Interventions;

        Assert.True(list.Count >= 15, $"{p}: {list.Count} eylem");
        Assert.Contains(ozel, list);
        Assert.Equal(list.Count, list.Distinct().Count());
        // Başka profilin özel eylemi bu profilde yok.
        var digerleri = new[] { LiveAction.FaydayaDon, LiveAction.KolaySoruSor, LiveAction.SozVer }.Where(a => a != ozel);
        Assert.All(digerleri, a => Assert.DoesNotContain(a, list));
    }

    [Fact]
    public void OlumluEylemler_MudahaleListesinde_DegilVeEtkiSatiriYok()
    {
        foreach (var p in Enum.GetValues<MeetingProfile>())
            Assert.All(Positive, a => Assert.DoesNotContain(a, DecisionKnowledgeBase.Get(p).Interventions));
        Assert.All(Positive, a => Assert.False(DecisionKnowledgeBase.LiveImpact.ContainsKey(a)));
    }

    [Fact]
    public void HerEylemin_BenzersizTurkceAdiVar()
    {
        var names = Enum.GetValues<LiveAction>().Select(DecisionText.Name).ToArray();

        Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        Assert.Equal(names.Length, names.Distinct().Count());
    }

    [Theory]
    [InlineData(LiveAction.SakinOl, "Sakin ol, tonunu düşür")]
    [InlineData(LiveAction.AciklayiciOl, "Açıklayıcı ol, ayrıntıyı ver")]
    [InlineData(LiveAction.TebessumEt, "Tebessüm et, yumuşak başla")]
    [InlineData(LiveAction.FaydayaDon, "Fiyata değil faydaya dön")]
    [InlineData(LiveAction.KolaySoruSor, "Adayı rahatlat, daha kolay bir soru sor")]
    [InlineData(LiveAction.SozVer, "Katılımı sağla, sessiz kişiye söz ver")]
    [InlineData(LiveAction.BirAdimIlerlet, "Bu havayı kullan, bir adım ileri götür")]
    public void EylemAdlari_KonusmaDiliyle(LiveAction a, string expected) => Assert.Equal(expected, DecisionText.Name(a));

    [Fact]
    public void EskiEylemlerinTamSayiDegeri_Degismedi_KayitliVeriBozulmaz()
    {
        Assert.Equal(0, (int)LiveAction.DevamEt);
        Assert.Equal(1, (int)LiveAction.KonuyuDegistir);
        Assert.Equal(5, (int)LiveAction.KapanisaGec);
    }
}
