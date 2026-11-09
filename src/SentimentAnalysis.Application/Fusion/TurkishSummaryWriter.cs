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

    // Konuyu bilmeyen kullanıcı için düz dil: "baskın", "oynaklık", "örtüşme" gibi terimler yok.
    // Cümle (Write) ve rapor etiketleri (Tags) aynı ifadeleri buradan alır — ikisi ayrışmaz.
    private static (string Text, SummaryTone Tone) Trend(MeetingSummary m) =>
        m.ValenceTrend > 0.05 ? ("olumluya döndü", SummaryTone.Good)
        : m.ValenceTrend < -0.05 ? ("olumsuza döndü", SummaryTone.Bad)
        : ("pek değişmedi", SummaryTone.Neutral);

    private static (string Text, SummaryTone Tone) Volatility(MeetingSummary m) =>
        m.EmotionalVolatility > 0.35 ? ("sık sık değişti", SummaryTone.Warn)
        : m.EmotionalVolatility > 0.15 ? ("ara ara değişti", SummaryTone.Neutral)
        : ("genelde aynı kaldı", SummaryTone.Neutral);

    private static (string Text, SummaryTone Tone) Congruence(MeetingSummary m) =>
        m.AverageCongruence >= 0.7 ? ("çoğunlukla uyumluydu", SummaryTone.Good)
        : m.AverageCongruence >= 0.4 ? ("kısmen uyumluydu", SummaryTone.Warn)
        : ("sık sık çelişti", SummaryTone.Bad);

    /// <summary>Rapor özet kartındaki renkli etiketler: hava gidişatı, duygu değişkenliği, söz–ton uyumu.</summary>
    public static IReadOnlyList<(string Text, SummaryTone Tone)> Tags(MeetingSummary m)
    {
        var (trend, trendTone) = Trend(m);
        var (vol, volTone) = Volatility(m);
        var (cong, congTone) = Congruence(m);
        return new[]
        {
            ($"Hava: {trend}", trendTone),
            ($"Duygular: {vol}", volTone),
            ($"Söz ve ses tonu: {cong}", congTone),
        };
    }

    public static string Write(MeetingSummary m)
    {
        string dominant = Adlar[m.DominantEmotion].ToLower(new System.Globalization.CultureInfo("tr-TR"));
        string trend = Trend(m).Text, volatilite = Volatility(m).Text, congruence = Congruence(m).Text;

        return $"Görüşmede en çok {dominant} bir hava vardı. "
             + $"Görüşme ilerledikçe hava {trend}; duygular {volatilite}. "
             + $"Söylenenlerle ses tonu {congruence}.";
    }
}

/// <summary>Özet etiketinin rengi: iyi (yeşil), dikkat (sarı), kötü (kırmızı), nötr (gri).</summary>
public enum SummaryTone { Good, Warn, Bad, Neutral }
