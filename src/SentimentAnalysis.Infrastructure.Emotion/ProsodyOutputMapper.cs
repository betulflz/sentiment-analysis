using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>audeering çıktısını (`logits` = [arousal, dominance, valence], 0..1) domain
/// <see cref="ProsodyEmotion"/>'a eşler. valence 0..1 → −1..1; arousal direkt.
/// modelConfidence = nötr merkeze (0, 0.5) uzaklık (PROVISIONAL heuristik; Alt-Proje 09 kalibre eder).</summary>
public static class ProsodyOutputMapper
{
    // Merkezden en büyük Öklid uzaklık: sqrt(1^2 + 0.5^2).
    private static readonly double MaxDistance = Math.Sqrt(1.0 + 0.25);

    public static ProsodyEmotion Map(float[] logits)
    {
        double arousal = logits.Length > 0 ? logits[0] : 0.5;
        double valence01 = logits.Length > 2 ? logits[2] : 0.5;

        var va = ValenceArousal.Clamp(valence01 * 2.0 - 1.0, arousal);

        double dv = va.Valence;                 // nötr valence 0
        double da = va.Arousal - 0.5;           // nötr arousal 0.5
        double confidence = Math.Clamp(Math.Sqrt(dv * dv + da * da) / MaxDistance, 0.0, 1.0);

        return new ProsodyEmotion(va, confidence);
    }
}
