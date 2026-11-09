using System.Collections.Generic;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Fusion;

/// <summary>
/// Toplantı metriklerinden deterministik Türkçe özet cümlesi üretir. Emotion→Türkçe
/// ad haritası burada (Application/sunum) yaşar; Core dil-nötr kalır (master §D5, §12).
/// </summary>
public static class TurkishSummaryWriter
{
    private static readonly IReadOnlyDictionary<Emotion, string> Adlar = new Dictionary<Emotion, string>
    {
        [Emotion.Cosakulu] = "Coşkulu",
        [Emotion.Mutlu] = "Mutlu",
        [Emotion.Sakin] = "Sakin",
        [Emotion.Sikilmis] = "Sıkılmış",
        [Emotion.Uzgun] = "Üzgün",
        [Emotion.Gergin] = "Gergin",
        [Emotion.Kizgin] = "Kızgın",
        [Emotion.Notr] = "Nötr",
    };

    public static string Write(MeetingSummary m)
    {
        // Konuyu bilmeyen kullanıcı için düz dil: "baskın", "oynaklık", "örtüşme" gibi terimler yok.
        string dominant = Adlar[m.DominantEmotion].ToLower(new System.Globalization.CultureInfo("tr-TR"));
        string trend = m.ValenceTrend > 0.05 ? "olumluya döndü"
                     : m.ValenceTrend < -0.05 ? "olumsuza döndü"
                     : "pek değişmedi";
        string volatilite = m.EmotionalVolatility > 0.35 ? "sık sık değişti"
                          : m.EmotionalVolatility > 0.15 ? "ara ara değişti"
                          : "genelde aynı kaldı";
        string congruence = m.AverageCongruence >= 0.7 ? "çoğunlukla uyumluydu"
                          : m.AverageCongruence >= 0.4 ? "kısmen uyumluydu"
                          : "sık sık çelişti";

        return $"Görüşmede en çok {dominant} bir hava vardı. "
             + $"Görüşme ilerledikçe hava {trend}; duygular {volatilite}. "
             + $"Söylenenlerle ses tonu {congruence}.";
    }
}
