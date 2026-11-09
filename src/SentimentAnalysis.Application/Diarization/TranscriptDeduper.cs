using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Örtüşen ses parçalarından gelen tekrar satırları ayıklar. Parçalar 2 sn örtüştüğü için
/// örtüşme bölgesi iki kez yazıya dökülür ("Hangi karta?" iki kez; "Bonus" / "Bones").
/// <para>Kural: zamanları çakışan iki satırdan kısa olanın yazısı uzun olanın içinde en az %70
/// oranında (harf sırasıyla) geçiyorsa kısa olan atılır. Zamanı çakışmayan aynı kelimeler
/// ("Tamam." … "Tamam.") ayrı söylenmiştir, korunur. Saf; çıktı başlangıç zamanına göre sıralı.</para></summary>
public static class TranscriptDeduper
{
    private const double MinContainment = 0.7;

    public static IReadOnlyList<TranscriptSegment> Dedupe(IReadOnlyList<TranscriptSegment> segments)
    {
        var ordered = segments.OrderBy(s => s.Start).ThenBy(s => s.End).ToList();
        var dropped = new bool[ordered.Count];
        for (int i = 0; i < ordered.Count; i++)
        {
            if (dropped[i]) continue;
            for (int j = i + 1; j < ordered.Count && ordered[j].Start < ordered[i].End; j++)
            {
                if (dropped[j]) continue;
                string a = Normalize(ordered[i].Text), b = Normalize(ordered[j].Text);
                var (shortText, shortIndex, longText) = a.Length < b.Length ? (a, i, b) : (b, j, a);
                if (shortText.Length == 0 || Lcs(shortText, longText) < MinContainment * shortText.Length) continue;
                dropped[shortIndex] = true;
                if (shortIndex == i) break;
            }
        }
        return ordered.Where((_, k) => !dropped[k]).ToList();
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
