using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Application.Decision;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Decision;

public class TopsisTests
{
    [Fact]
    public void Topsis_TextbookExample()
    {
        var m = new double[,] { { 7, 9, 9 }, { 8, 7, 8 }, { 9, 6, 8 } };
        var c = Topsis.Compute(m, new[] { 0.5, 0.3, 0.2 }).Closeness;
        Assert.Equal(0.4980, c[0], 1e-3);
        Assert.Equal(0.4146, c[1], 1e-3);
        Assert.Equal(0.5020, c[2], 1e-3);
    }

    [Fact]
    public void Topsis_DominantAlternative()
    {
        var c = Topsis.Compute(new double[,] { { 2, 2 }, { 1, 1 } }, new[] { 0.5, 0.5 }).Closeness;
        Assert.Equal(1.0, c[0], 1e-9);
        Assert.Equal(0.0, c[1], 1e-9);
    }

    [Fact]
    public void Topsis_ZeroColumn_NoNaN()
    {
        var c = Topsis.Compute(new double[,] { { 1, 0 }, { 2, 0 }, { 3, 0 } }, new[] { 0.5, 0.5 }).Closeness;
        Assert.All(c, v => Assert.True(double.IsFinite(v)));
    }

    [Fact]
    public void Topsis_IdenticalRows_AllZero()
    {
        var c = Topsis.Compute(new double[,] { { 1, 2 }, { 1, 2 } }, new[] { 0.5, 0.5 }).Closeness;
        Assert.All(c, v => Assert.Equal(0.0, v));
    }
}

public class ActionRankerTests
{
    [Fact]
    public void EffectiveWeights_MultipliesAndNormalizes()
    {
        var w = ActionRanker.EffectiveWeights(new[] { 0.5, 0.5 }, new[] { 1.0, 0.0 });
        Assert.Equal(1.0, w[0], 1e-9);
        Assert.Equal(0.0, w[1], 1e-9);
    }

    [Fact]
    public void EffectiveWeights_AllZeroWeights_FallsBackEqual()
    {
        var w = ActionRanker.EffectiveWeights(new[] { 0.0, 0.0, 0.0 }, new[] { 0.2, 0.2, 0.2 });
        Assert.All(w, v => Assert.Equal(1 / 3.0, v, 1e-9));
    }

    [Fact]
    public void EffectiveWeights_ZeroNeeds_FallsBackToNormalizedWeights()
    {
        var w = ActionRanker.EffectiveWeights(new[] { 3.0, 1.0 }, new[] { 0.0, 0.0 });
        Assert.Equal(0.75, w[0], 1e-9);
        Assert.Equal(0.25, w[1], 1e-9);
    }

    [Fact]
    public void Rank_OrdersByClosenessDescending_AndReportsTop2()
    {
        var impact = new Dictionary<string, double[]> { ["A"] = new[] { 1.0, 0.0 }, ["B"] = new[] { 0.0, 1.0 } };
        var r = ActionRanker.Rank(new[] { "B", "A" }, impact, new[] { 0.5, 0.5 }, new[] { 1.0, 0.1 });
        Assert.Equal("A", r[0].Alternative);
        Assert.True(r[0].Closeness > r[1].Closeness);
        Assert.Equal(Criterion.Gerilim, r[0].TopCriteria[0]);
        Assert.Equal(2, r[0].TopCriteria.Count);
    }
}
