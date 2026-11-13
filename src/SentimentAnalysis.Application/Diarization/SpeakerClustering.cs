namespace SentimentAnalysis.Application.Diarization;

/// <summary>Birim uzunlukta konuşmacı imzalarını kosinüsle kümeler (pencereli ayrım, İterasyon 2). Deterministik.</summary>
public static class SpeakerClustering
{
    private const int Trials = 40, Iterations = 30;
    private const double MinShare = 0.10;   // bundan küçük küme "aykırı" sayılır (tek pencere ayrı kişi olmasın)

    /// <summary>Tam <paramref name="k"/> kümeye böler: çok başlangıçlı k-means; aykırı küçük kümeli çözümler cezalı.</summary>
    public static int[] Cluster(IReadOnlyList<float[]> e, int k) => Run(e, k).Labels;

    /// <summary>Kişi sayısı bilinmiyor: en büyük k (≤ <paramref name="maxK"/>) ki her merkez çifti
    /// <paramref name="sameSpeakerCosine"/>'tan az benzer olsun; aksi halde tek kişi.</summary>
    public static int[] ClusterAuto(IReadOnlyList<float[]> e, int maxK, double sameSpeakerCosine)
    {
        var best = new int[e.Count];
        for (int k = 2; k <= Math.Min(maxK, e.Count); k++)
        {
            var (labels, cents) = Run(e, k);
            bool distinct = true;
            for (int a = 0; a < k && distinct; a++)
                for (int b = a + 1; b < k; b++)
                    if (Dot(cents[a], cents[b]) >= sameSpeakerCosine) { distinct = false; break; }
            if (!distinct) break;
            best = labels;
        }
        return best;
    }

    private static (int[] Labels, float[][] Cents) Run(IReadOnlyList<float[]> e, int k)
    {
        int n = e.Count;
        if (n == 0 || k <= 1) return (new int[n], new[] { Centroid(e, new int[n], 0) });
        k = Math.Min(k, n);
        var rng = new Random(1);
        int[] best = new int[n]; float[][] bestC = Array.Empty<float[]>(); double bestScore = double.NegativeInfinity;
        for (int trial = 0; trial < Trials; trial++)
        {
            var seeds = Enumerable.Range(0, n).OrderBy(_ => rng.Next()).Take(k).ToArray();
            var cents = seeds.Select(s => (float[])e[s].Clone()).ToArray();
            var lab = new int[n];
            for (int it = 0; it < Iterations; it++)
            {
                for (int i = 0; i < n; i++)
                {
                    int arg = 0; double max = double.NegativeInfinity;
                    for (int c = 0; c < k; c++) { double d = Dot(e[i], cents[c]); if (d > max) { max = d; arg = c; } }
                    lab[i] = arg;
                }
                for (int c = 0; c < k; c++) if (lab.Contains(c)) cents[c] = Centroid(e, lab, c);
            }
            double score = 0;
            for (int i = 0; i < n; i++) score += Dot(e[i], cents[lab[i]]);
            score /= n;
            for (int c = 0; c < k; c++) if (lab.Count(l => l == c) < MinShare * n) score -= 1;
            if (score > bestScore) { bestScore = score; best = (int[])lab.Clone(); bestC = cents; }
        }
        return (best, bestC);
    }

    private static float[] Centroid(IReadOnlyList<float[]> e, int[] lab, int c)
    {
        var m = new float[e.Count == 0 ? 0 : e[0].Length];
        for (int i = 0; i < e.Count; i++) if (lab[i] == c) for (int d = 0; d < m.Length; d++) m[d] += e[i][d];
        double norm = Math.Sqrt(m.Sum(v => (double)v * v));
        if (norm > 0) for (int d = 0; d < m.Length; d++) m[d] = (float)(m[d] / norm);
        return m;
    }

    internal static double Dot(float[] a, float[] b) { double d = 0; for (int i = 0; i < a.Length; i++) d += a[i] * b[i]; return d; }
}
