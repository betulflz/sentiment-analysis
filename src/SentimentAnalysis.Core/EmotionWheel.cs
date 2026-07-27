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

    /// <summary>
    /// VA noktasını 8 duyguya <b>yakınlık payı</b> olarak dağıtır (toplam 1.0, azalan sırada).
    /// Her merkeze Öklid uzaklığı <c>d</c> hesaplanır ve <c>softmax(−d² / T)</c> uygulanır.
    /// </summary>
    /// <param name="temperature">
    /// Keskinlik: küçük değer tek duyguyu domine ettirir, büyük değer dağılımı düzleştirir.
    /// Pozitif olmalıdır. Varsayılan <c>FusionConfig.DistributionTemperature</c>'dan gelir ve
    /// <b>kalibre edilmemiştir</b> (eval-09).
    /// </param>
    /// <remarks>
    /// Dönen ilk öğe her zaman <see cref="Map"/> ile aynı duygudur: pay, uzaklığın monoton
    /// azalan bir fonksiyonudur ve sıralama <see cref="Map"/> ile aynı kararlı (stable) LINQ
    /// sıralamasını kullanır — eşitlik durumunda bildirim sırası korunur.
    /// Sonuç bir olasılık değil, geometrik yakınlık payıdır (bkz. <see cref="EmotionShare"/>).
    /// </remarks>
    public static IReadOnlyList<EmotionShare> Distribute(ValenceArousal va, double temperature)
    {
        if (temperature <= 0.0)
            throw new ArgumentOutOfRangeException(
                nameof(temperature), temperature, "Dağılım sıcaklığı pozitif olmalıdır.");

        var logits = Regions
            .Select(r =>
            {
                double d = va.DistanceTo(r.Center);
                return -(d * d) / temperature;
            })
            .ToArray();

        // Taşma-güvenli softmax: üsleri almadan önce en büyük terim çıkarılır.
        double max = logits.Max();
        var weights = logits.Select(l => Math.Exp(l - max)).ToArray();
        double sum = weights.Sum();

        return Regions
            .Select((r, i) => new EmotionShare(r.Emotion, weights[i] / sum))
            .OrderByDescending(s => s.Share)
            .ToArray();
    }
}
