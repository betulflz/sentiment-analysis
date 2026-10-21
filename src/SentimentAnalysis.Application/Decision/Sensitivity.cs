using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Decision;

/// <summary>Ağırlık duyarlılık analizi (spec 11 §6.6): her kriterin ağırlığı ±%10 ve ±%20 oynatılır,
/// yeniden normalize edilir ve en iyi seçenek değişiyor mu bakılır. Kararın sağlamlığını gösterir.</summary>
public static class Sensitivity
{
    public static readonly double[] Deltas = { -0.2, -0.1, 0.1, 0.2 };

    public static IReadOnlyList<SensitivityRow<T>> Analyze<T>(IReadOnlyList<double> weights,
        Func<IReadOnlyList<double>, T> bestOf)
    {
        var baseline = bestOf(weights);
        var rows = new List<SensitivityRow<T>>();
        for (int j = 0; j < weights.Count; j++)
            foreach (var d in Deltas)
            {
                var w = weights.ToArray();
                w[j] *= 1 + d;
                var sum = w.Sum();
                if (sum > 0) for (int k = 0; k < w.Length; k++) w[k] /= sum;
                var best = bestOf(w);
                rows.Add(new SensitivityRow<T>((Criterion)j, d, best, !EqualityComparer<T>.Default.Equals(best, baseline)));
            }
        return rows;
    }
}
