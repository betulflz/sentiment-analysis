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
        string dominant = Adlar[m.DominantEmotion];
        string trend = m.ValenceTrend > 0.05 ? "ısındı"
                     : m.ValenceTrend < -0.05 ? "soğudu"
                     : "sabit kaldı";
        string volatilite = m.EmotionalVolatility > 0.35 ? "yüksek"
                          : m.EmotionalVolatility > 0.15 ? "orta"
                          : "düşük";
        string congruence = m.AverageCongruence >= 0.7 ? "yüksek"
                          : m.AverageCongruence >= 0.4 ? "orta"
                          : "düşük";

        return $"Toplantının baskın duygusu {dominant}. "
             + $"Genel hava {trend}, duygusal oynaklık {volatilite}. "
             + $"Söz-ton örtüşmesi {congruence}.";
    }
}
