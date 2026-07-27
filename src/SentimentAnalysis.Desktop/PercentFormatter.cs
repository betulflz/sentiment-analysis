namespace SentimentAnalysis.Desktop;

/// <summary>
/// Ondalık payları, toplamı <b>tam 100</b> olan tam sayı yüzdelere çevirir (en-büyük-kalan yöntemi).
/// <para>Naif yuvarlama toplamı 99 veya 101 yapabilir; kullanıcı bunu ekranda hata olarak görür.
/// Saf ve deterministik.</para>
/// </summary>
public static class PercentFormatter
{
    /// <summary>
    /// <paramref name="shares"/> (toplamı ~1.0 olan paylar) → toplamı tam 100 olan tam sayı yüzdeler.
    /// Girdi sırası korunur. Boş girdi → boş çıktı. Toplam 0 ise tümü 0 döner (uydurma dağıtım yapılmaz).
    /// </summary>
    public static IReadOnlyList<int> ToPercents(IReadOnlyList<double> shares)
    {
        int n = shares.Count;
        if (n == 0) return Array.Empty<int>();

        double total = 0;
        for (int i = 0; i < n; i++) total += Math.Max(0.0, shares[i]);
        if (total <= 1e-12) return new int[n];   // hepsi sıfır → 100'e tamamlanacak bir şey yok

        var scaled = new double[n];
        var floors = new int[n];
        int assigned = 0;
        for (int i = 0; i < n; i++)
        {
            scaled[i] = Math.Max(0.0, shares[i]) / total * 100.0;
            floors[i] = (int)Math.Floor(scaled[i]);
            assigned += floors[i];
        }

        int remaining = 100 - assigned;

        // Kalanı en büyük ondalık artığa sahip öğelere birer birer dağıt.
        // Eşitlikte küçük indeks önce (deterministik).
        var order = Enumerable.Range(0, n)
            .OrderByDescending(i => scaled[i] - floors[i])
            .ThenBy(i => i)
            .ToArray();

        for (int k = 0; k < remaining && k < n; k++) floors[order[k]]++;

        return floors;
    }
}
