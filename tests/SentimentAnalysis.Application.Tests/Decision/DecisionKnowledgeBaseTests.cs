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
}
