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

    /// <summary>Valence (olumluluk ekseni) değerini sıradan kullanıcı için düz Türkçeye çevirir.</summary>
    public static string ValenceText(double valence) => valence switch
    {
        >= 0.6 => "Çok olumlu",
        >= 0.2 => "Olumlu",
        > -0.2 => "Ne olumlu ne olumsuz",
        > -0.6 => "Olumsuz",
        _ => "Çok olumsuz",
    };

    /// <summary>Arousal (canlılık ekseni) değerini sıradan kullanıcı için düz Türkçeye çevirir.</summary>
    public static string ArousalText(double arousal) => arousal switch
    {
        >= 0.8 => "Çok hareketli",
        >= 0.6 => "Hareketli",
        >= 0.4 => "Orta canlılıkta",
        >= 0.2 => "Sakin",
        _ => "Çok durgun",
    };
}
