using System.Collections.Generic;
using System.Linq;

namespace SentimentAnalysis.Core;

/// <summary>
/// Chunk'ların algılanan dillerinden oturumun baskın dilini türetir. Saf, deterministik.
/// <para>Bir oturum dil değiştirebilir (kod değiştirme: Türkçe toplantıda İngilizce bir sunum).
/// Baskın dil <b>süreye göre</b> belirlenir — chunk sayısına göre değil; 30 saniyelik bir Türkçe
/// bölüm, 3 saniyelik iki İngilizce bölümden ağır basmalıdır.</para>
/// </summary>
public static class SessionLanguage
{
    /// <summary>
    /// Baskın dili döndürür. <see cref="DetectedLanguage.Other"/> ("bilinmiyor") yalnızca
    /// tanımlı bir dil hiç yoksa kazanır: birkaç sessiz/anlaşılmaz chunk, gerçekten tanınmış
    /// Türkçe konuşmayı bastırmamalı. Eşitlikte enum sırası (deterministik).
    /// </summary>
    public static DetectedLanguage Dominant(IEnumerable<ChunkResult> chunks)
    {
        var weights = new Dictionary<DetectedLanguage, double>();

        foreach (var c in chunks)
        {
            double seconds = (c.TimeEnd - c.TimeStart).TotalSeconds;
            if (seconds <= 0) seconds = 1.0;   // dejenere/sıfır süreli chunk'ı eşit say
            weights[c.Language] = weights.GetValueOrDefault(c.Language) + seconds;
        }

        if (weights.Count == 0) return DetectedLanguage.Other;

        // Önce tanımlı diller arasında yarış; hiçbiri yoksa Other'a düş.
        var known = weights.Where(kv => kv.Key != DetectedLanguage.Other).ToList();
        var pool = known.Count > 0 ? known : weights.ToList();

        return pool
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => (int)kv.Key)
            .First().Key;
    }
}
