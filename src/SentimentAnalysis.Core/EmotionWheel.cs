namespace SentimentAnalysis.Core;

/// <summary>
/// Sürekli Valence-Arousal noktasını en yakın ayrık duyguya haritalar.
/// Her duygunun VA uzayında bir merkez noktası vardır; haritalama en yakın
/// merkeze (Öklid) göre yapılır. Saf, deterministik.
/// </summary>
public static class EmotionWheel
{
    private static readonly (Emotion Emotion, ValenceArousal Center)[] Regions =
    [
        (Emotion.Cosakulu, new ValenceArousal(0.6, 0.9)),
        (Emotion.Mutlu,    new ValenceArousal(0.8, 0.5)),
        (Emotion.Sakin,    new ValenceArousal(0.6, 0.15)),
        (Emotion.Sikilmis, new ValenceArousal(-0.4, 0.1)),
        (Emotion.Uzgun,    new ValenceArousal(-0.6, 0.3)),
        (Emotion.Gergin,   new ValenceArousal(-0.5, 0.8)),
        (Emotion.Kizgin,   new ValenceArousal(-0.8, 0.9)),
        (Emotion.Notr,     new ValenceArousal(0.0, 0.5)),
    ];

    /// <summary>Verilen VA noktasına en yakın duyguyu döndürür.</summary>
    public static Emotion Map(ValenceArousal va) => MapWithSecondary(va).Primary;

    /// <summary>En yakın (primary) ve ikinci en yakın (secondary) duyguyu döndürür.</summary>
    public static (Emotion Primary, Emotion Secondary) MapWithSecondary(ValenceArousal va)
    {
        var ranked = Regions
            .OrderBy(r => va.DistanceTo(r.Center))
            .Select(r => r.Emotion)
            .ToArray();
        return (ranked[0], ranked[1]);
    }
}
