using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Örtüşen ses parçalarından gelen tekrar satırları ayıklar. Parçalar 2 sn örtüştüğü için
/// örtüşme bölgesi iki kez yazıya dökülür ("Hangi karta?" iki kez).
/// <para>Muhafazakâr: yanlışlıkla gerçek bir cümleyi silmektense tekrarı bırakmak yeğdir. Yalnızca
/// ikisi de en az <see cref="MinChars"/> harflik ve zamanı kısa olanın en az yarısı kadar çakışan
/// satırlarda: (A) kısa olan uzun olanın içinde BİTİŞİK geçiyorsa, ya da (B) uzunlukları birbirine
/// yakınsa (kısa ≥ yarısı) ve harf sırası örtüşmesi ≥ %70 ise kısa olan atılır. "Evet." gibi kısa
/// cevaplar ve zamanı çakışmayan aynı kelimeler ("Tamam." … "Tamam.") korunur. Ham yazı kayıtta
/// değişmez; bu yalnızca raporda gösterilen listeyi temizler. Saf; çıktı başlangıca göre sıralı.</para></summary>
public static class TranscriptDeduper
{
    private const int MinChars = 8;
    private const double MinContainment = 0.7;
    private const double MinLengthRatio = 0.5;
    private const double MinTimeOverlap = 0.5;

    public static IReadOnlyList<TranscriptSegment> Dedupe(IReadOnlyList<TranscriptSegment> segments)
    {
        var ordered = segments.OrderBy(s => s.Start).ThenBy(s => s.End).ToList();
        var norm = ordered.Select(s => Normalize(s.Text)).ToList();
        var dropped = new bool[ordered.Count];
        for (int i = 0; i < ordered.Count; i++)
        {
            if (dropped[i]) continue;
            for (int j = i + 1; j < ordered.Count && ordered[j].Start < ordered[i].End; j++)
            {
                if (dropped[j]) continue;
                var (shortIdx, longIdx) = norm[i].Length <= norm[j].Length ? (i, j) : (j, i);
                if (!IsDuplicate(ordered[shortIdx], norm[shortIdx], ordered[longIdx], norm[longIdx])) continue;
                dropped[shortIdx] = true;
                if (shortIdx == i) break;
            }
        }
        return ordered.Where((_, k) => !dropped[k]).ToList();
    }

    private static bool IsDuplicate(TranscriptSegment shortSeg, string shortText, TranscriptSegment longSeg, string longText)
    {
        if (shortText.Length < MinChars) return false;
        double shortDur = (shortSeg.End - shortSeg.Start).TotalSeconds;
        var overlap = (TimeSpan.FromTicks(Math.Min(shortSeg.End.Ticks, longSeg.End.Ticks))
                       - TimeSpan.FromTicks(Math.Max(shortSeg.Start.Ticks, longSeg.Start.Ticks))).TotalSeconds;
        if (shortDur <= 0 || overlap < MinTimeOverlap * shortDur) return false;

        if (longText.Contains(shortText, StringComparison.Ordinal)) return true;                    // (A)
        return shortText.Length >= MinLengthRatio * longText.Length
            && Lcs(shortText, longText) >= MinContainment * shortText.Length;                       // (B)
    }

    private static string Normalize(string text) =>
        string.Join(' ', new string(text.ToLower(new System.Globalization.CultureInfo("tr-TR"))
                .Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>En uzun ortak alt dizi (harf sırası korunur, bitişik olması gerekmez).</summary>
    private static int Lcs(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        foreach (char ca in a)
        {
            for (int k = 1; k <= b.Length; k++)
                cur[k] = ca == b[k - 1] ? prev[k - 1] + 1 : Math.Max(prev[k], cur[k - 1]);
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
