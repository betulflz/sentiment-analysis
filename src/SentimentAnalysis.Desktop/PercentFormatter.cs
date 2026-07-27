using System;
using System.Collections.Generic;
using System.Linq;

namespace SentimentAnalysis.Desktop;

/// <summary>
/// Payları (toplamı 1.0) toplamı <b>tam 100</b> olan tam sayı yüzdelere çevirir.
/// </summary>
/// <remarks>
/// En-büyük-kalan (largest remainder) yöntemi: her pay 100 ile çarpılıp tabanı alınır, 100'e
/// kadar eksik kalan puanlar en büyük ondalık kalana sahip öğelere birer birer dağıtılır.
/// Naif yuvarlama toplamı 99 veya 101 yapabilir; 8 eşit payda (%12.5) toplam 96'ya düşer.
/// Saf ve deterministik.
/// </remarks>
public static class PercentFormatter
{
    public static IReadOnlyList<int> ToPercents(IReadOnlyList<double> shares)
    {
        if (shares.Count == 0) return Array.Empty<int>();

        var scaled = shares.Select(s => s * 100.0).ToArray();
        var percents = scaled.Select(x => (int)Math.Floor(x)).ToArray();

        int remainder = 100 - percents.Sum();
        if (remainder <= 0) return percents;

        // Kalanı en büyük olan öğelerden başlayarak birer puan dağıt.
        var byRemainder = Enumerable.Range(0, shares.Count)
            .OrderByDescending(i => scaled[i] - percents[i])
            .ToArray();

        for (int k = 0; k < remainder; k++)
            percents[byRemainder[k % byRemainder.Length]]++;

        return percents;
    }
}
