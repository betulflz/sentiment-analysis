using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Decision;

/// <summary>İhtiyaç-ağırlıklı TOPSIS: karar matrisi sabit etki tablosudur (bilgi tabanı), anlık
/// ihtiyaç <c>n</c> ise ağırlığa girer: <c>w' = w·n / Σ(w·n)</c>. Durumsuzdur — histerezis
/// <see cref="LiveAdvisor"/>'dadır.</summary>
public static class ActionRanker
{
    /// <summary>Σw = 0 ise eşit ağırlık; Σ(w·n) = 0 ise normalize edilmiş w.</summary>
    public static IReadOnlyList<double> EffectiveWeights(IReadOnlyList<double> weights, IReadOnlyList<double> needs)
    {
        int n = weights.Count;
        var w = weights.Select(x => Math.Max(0, x)).ToArray();
        var sumW = w.Sum();
        if (sumW <= 0) return Enumerable.Repeat(1.0 / n, n).ToArray();

        var eff = w.Select((x, j) => x * Math.Clamp(needs[j], 0, 1)).ToArray();
        var sumEff = eff.Sum();
        return sumEff <= 0
            ? w.Select(x => x / sumW).ToArray()
            : eff.Select(x => x / sumEff).ToArray();
    }

    /// <summary>Closeness'a göre azalan; eşitlikte <paramref name="alternatives"/> sırası korunur.
    /// TopCriteria = o satırın ağırlıklı normalize değerine göre en büyük 2 kriter.</summary>
    public static IReadOnlyList<Ranked<T>> Rank<T>(IReadOnlyList<T> alternatives,
        IReadOnlyDictionary<T, double[]> impact, IReadOnlyList<double> weights, IReadOnlyList<double> needs)
        where T : notnull
    {
        int k = weights.Count;
        var matrix = new double[alternatives.Count, k];
        for (int i = 0; i < alternatives.Count; i++)
            for (int j = 0; j < k; j++)
                matrix[i, j] = impact[alternatives[i]][j];

        var result = Topsis.Compute(matrix, EffectiveWeights(weights, needs));
        return alternatives
            .Select((a, i) => new Ranked<T>(a, result.Closeness[i], TopTwo(result.WeightedNormalized, i, k)))
            .Select((r, i) => (r, i))
            .OrderByDescending(x => x.r.Closeness).ThenBy(x => x.i)
            .Select(x => x.r)
            .ToArray();
    }

    private static IReadOnlyList<Criterion> TopTwo(double[,] v, int row, int k) =>
        Enumerable.Range(0, k)
            .OrderByDescending(j => v[row, j]).ThenBy(j => j)
            .Take(2)
            .Select(j => (Criterion)j)
            .ToArray();
}
