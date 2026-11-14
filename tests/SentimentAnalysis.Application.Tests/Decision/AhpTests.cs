using System;
using System.Linq;
using SentimentAnalysis.Application.Decision;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Decision;

public class AhpTests
{
    [Fact]
    public void Compute_TextbookThreeByThree()
    {
        var m = new double[,] { { 1, 3, 5 }, { 1 / 3.0, 1, 3 }, { 1 / 5.0, 1 / 3.0, 1 } };
        var r = Ahp.Compute(m);
        Assert.Equal(0.6370, r.Weights[0], 1e-3);
        Assert.Equal(0.2583, r.Weights[1], 1e-3);
        Assert.Equal(0.1047, r.Weights[2], 1e-3);
        Assert.Equal(3.0385, r.LambdaMax, 1e-3);
        Assert.Equal(0.0332, r.ConsistencyRatio, 1e-3);
    }

    [Fact]
    public void Compute_PerfectlyConsistent_CrZero()
    {
        double[] s = { 1, 2, 4 };
        var m = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                m[i, j] = s[i] / s[j];
        var r = Ahp.Compute(m);
        Assert.Equal(1 / 7.0, r.Weights[0], 1e-6);
        Assert.Equal(2 / 7.0, r.Weights[1], 1e-6);
        Assert.Equal(4 / 7.0, r.Weights[2], 1e-6);
        Assert.True(Math.Abs(r.ConsistencyRatio) < 1e-9, $"CR {r.ConsistencyRatio}");
    }

    [Fact]
    public void Compute_WeightsSumToOne()
    {
        var m = new double[6, 6];
        var rnd = new double[] { 3, 2, 2, 5, 3, 0.5, 0.5, 2, 1, 1, 3, 2, 3, 2, 0.5 };
        int k = 0;
        for (int i = 0; i < 6; i++)
        {
            m[i, i] = 1;
            for (int j = i + 1; j < 6; j++) { m[i, j] = rnd[k]; m[j, i] = 1 / rnd[k]; k++; }
        }
        Assert.Equal(1.0, Ahp.Compute(m).Weights.Sum(), 1e-9);
    }

    [Fact]
    public void Compute_NonSquare_Throws()
    {
        Assert.Throws<ArgumentException>(() => Ahp.Compute(new double[2, 3]));
    }
}
