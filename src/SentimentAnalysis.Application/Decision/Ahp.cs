namespace SentimentAnalysis.Application.Decision;

/// <summary>AHP sonucu: öncelik vektörü, en büyük özdeğer ve tutarlılık oranı (CR).</summary>
public sealed record AhpResult(IReadOnlyList<double> Weights, double LambdaMax, double ConsistencyRatio);

/// <summary>Saaty AHP: ikili karşılaştırma matrisinden özvektör ağırlıkları ve CR.
/// Saf, deterministik. CR &lt; 0,10 kabul edilebilir tutarlılıktır.</summary>
public static class Ahp
{
    // Saaty rastgele indeksi (n = 1..9).
    private static readonly double[] RandomIndex = { 0, 0, 0, 0.58, 0.90, 1.12, 1.24, 1.32, 1.41, 1.45 };

    public static AhpResult Compute(double[,] pairwise)
    {
        int n = pairwise.GetLength(0);
        if (n == 0 || n != pairwise.GetLength(1))
            throw new ArgumentException("İkili karşılaştırma matrisi kare olmalı.", nameof(pairwise));
        if (n >= RandomIndex.Length)
            throw new ArgumentException($"En fazla {RandomIndex.Length - 1} kriter desteklenir.", nameof(pairwise));

        // Kuvvet yinelemesi: w ← Mw / Σ(Mw)
        var w = Enumerable.Repeat(1.0 / n, n).ToArray();
        for (int iter = 0; iter < 1000; iter++)
        {
            var next = Multiply(pairwise, w);
            var sum = next.Sum();
            for (int i = 0; i < n; i++) next[i] /= sum;
            var delta = next.Select((v, i) => Math.Abs(v - w[i])).Max();
            w = next;
            if (delta < 1e-12) break;
        }

        var mw = Multiply(pairwise, w);
        var lambdaMax = Enumerable.Range(0, n).Average(i => mw[i] / w[i]);
        var ri = RandomIndex[n];
        var cr = n <= 2 || ri == 0 ? 0.0 : (lambdaMax - n) / (n - 1) / ri;
        return new AhpResult(w, lambdaMax, cr);
    }

    private static double[] Multiply(double[,] m, double[] v)
    {
        int n = v.Length;
        var r = new double[n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                r[i] += m[i, j] * v[j];
        return r;
    }
}
