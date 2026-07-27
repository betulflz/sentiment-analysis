namespace SentimentAnalysis.Core;

/// <summary>
/// Valence-Arousal uzayından haritalanan dil-bağımsız 8 duygu (circumplex).
/// UI metni bu katmanda YOK; enum → yerelleştirme UI katmanında yapılır.
/// </summary>
public enum Emotion
{
    Cosakulu,  // V+ A+
    Mutlu,     // V+ orta-A
    Sakin,     // V+ A-
    Sikilmis,  // V- A-
    Uzgun,     // V- düşük-A
    Gergin,    // V- yüksek-A
    Kizgin,    // V-- A+
    Notr       // merkez
}
