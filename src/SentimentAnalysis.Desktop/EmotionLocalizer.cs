using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Core enum'larını UI için Türkçe metne çevirir (Core dil-nötr kalır — master kararı).
/// Bilinmeyen değer → enum adı fallback.</summary>
public static class EmotionLocalizer
{
    public static string Emotion(Emotion e) => e switch
    {
        Core.Emotion.Cosakulu => "Coşkulu",
        Core.Emotion.Mutlu => "Mutlu",
        Core.Emotion.Sakin => "Sakin",
        Core.Emotion.Sikilmis => "Sıkılmış",
        Core.Emotion.Uzgun => "Üzgün",
        Core.Emotion.Gergin => "Gergin",
        Core.Emotion.Kizgin => "Kızgın",
        Core.Emotion.Notr => "Nötr",
        _ => e.ToString(),
    };

    public static string Confidence(Confidence c) => c switch
    {
        Core.Confidence.Yuksek => "Yüksek",
        Core.Confidence.Orta => "Orta",
        Core.Confidence.Dusuk => "Düşük",
        _ => c.ToString(),
    };

    public static string Pattern(IncongruencePattern p) => p switch
    {
        IncongruencePattern.Yok => "Yok",
        IncongruencePattern.Maskeleme => "Maskeleme",
        IncongruencePattern.Saka => "Şaka",
        IncongruencePattern.OlasiIroni => "Olası ironi",
        _ => p.ToString(),
    };
}
