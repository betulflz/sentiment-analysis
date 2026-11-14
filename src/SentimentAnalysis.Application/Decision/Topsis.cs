namespace SentimentAnalysis.Application.Decision;

/// <summary>TOPSIS sonucu: seçenek başına ideale yakınlık katsayısı (0..1) ve ağırlıklı
/// normalize matris (katkı analizi için).</summary>
public sealed record TopsisResult(IReadOnlyList<double> Closeness, double[,] WeightedNormalized);

/// <summary>TOPSIS (Hwang &amp; Yoon). Tüm kriterler FAYDA yönlüdür (büyük = iyi). Saf, deterministik.
/// Vektör normalizasyonu kullanır — bu yüzden bir sütunu sabitle çarpmak sonucu DEĞİŞTİRMEZ;
/// anlık ihtiyacın sıralamaya yansıması için ihtiyaç ağırlığa taşınır (bkz. <see cref="ActionRanker"/>).</summary>
public static class Topsis
{
    public static TopsisResult Compute(double[,] matrix, IReadOnlyList<double> weights)
    {
        int m = matrix.GetLength(0), n = matrix.GetLength(1);
        if (weights.Count != n)
            throw new ArgumentException("Ağırlık sayısı kriter sayısına eşit olmalı.", nameof(weights));

        var v = new double[m, n];
        for (int j = 0; j < n; j++)
        {
            double norm = 0;
            for (int i = 0; i < m; i++) norm += matrix[i, j] * matrix[i, j];
            norm = Math.Sqrt(norm);
            for (int i = 0; i < m; i++)
                v[i, j] = norm == 0 ? 0 : matrix[i, j] / norm * weights[j];
        }

        var best = new double[n];
        var worst = new double[n];
        for (int j = 0; j < n; j++)
        {
            best[j] = double.MinValue;
            worst[j] = double.MaxValue;
            for (int i = 0; i < m; i++)
            {
                best[j] = Math.Max(best[j], v[i, j]);
                worst[j] = Math.Min(worst[j], v[i, j]);
            }
        }

        var closeness = new double[m];
        for (int i = 0; i < m; i++)
        {
            double dPlus = 0, dMinus = 0;
            for (int j = 0; j < n; j++)
            {
                dPlus += Math.Pow(v[i, j] - best[j], 2);
                dMinus += Math.Pow(v[i, j] - worst[j], 2);
            }
            dPlus = Math.Sqrt(dPlus);
            dMinus = Math.Sqrt(dMinus);
            closeness[i] = dPlus + dMinus == 0 ? 0 : dMinus / (dPlus + dMinus);
        }
        return new TopsisResult(closeness, v);
    }
}
