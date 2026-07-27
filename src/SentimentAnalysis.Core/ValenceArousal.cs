namespace SentimentAnalysis.Core;

/// <summary>
/// Duygunun boyutsal çekirdek temsili. Valence olumsuz↔olumlu (−1..+1),
/// Arousal sakin↔uyarılmış (0..1). Sistemin iç para birimidir.
/// </summary>
public readonly record struct ValenceArousal(double Valence, double Arousal)
{
    /// <summary>Nötr merkez: valence 0, arousal 0.5.</summary>
    public static ValenceArousal Neutral => new(0.0, 0.5);

    /// <summary>Değerleri geçerli aralığa kırparak güvenli bir örnek üretir.</summary>
    public static ValenceArousal Clamp(double valence, double arousal) =>
        new(Math.Clamp(valence, -1.0, 1.0), Math.Clamp(arousal, 0.0, 1.0));

    /// <summary>İki VA noktası arasındaki Öklid uzaklığı (duygu haritalamasında kullanılır).</summary>
    public double DistanceTo(ValenceArousal other)
    {
        var dv = Valence - other.Valence;
        var da = Arousal - other.Arousal;
        return Math.Sqrt(dv * dv + da * da);
    }
}
