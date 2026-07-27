namespace SentimentAnalysis.Core;

/// <summary>Bir duyguya düşen yakınlık payı (0..1). Tüm payların toplamı 1.0'dır.</summary>
public readonly record struct EmotionShare(Emotion Emotion, double Share);

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
    /// VA noktasını 8 duyguya <b>yakınlık payı</b> olarak dağıtır (spec 08d, Dilim 1):
    /// her merkeze Öklid uzaklığı <c>d</c> üzerinden <c>softmax(−d² / T)</c>.
    /// Paylar azalan sırada döner, toplamları 1.0'dır ve ilk eleman her zaman
    /// <see cref="Map"/> ile aynı duygudur.
    /// <para><b>DİKKAT — bu bir olasılık DEĞİLDİR.</b> Sistemde 8 duygu üzerinde olasılık üreten
    /// bir model yoktur (prozodi modeli regresyon yapar; metin modelinin softmax'ı 11 farklı
    /// etiket üzerinedir ve VA'ya çevrilir). Bu sayı yalnızca VA uzayındaki geometrik yakınlıktır.
    /// UI'da "yakınlık payı" denmeli, "olasılık"/"ihtimal" denmemelidir.</para>
    /// </summary>
    /// <param name="temperature">Keskinlik. Küçük T → tek duygu domine eder; büyük T → dağılım
    /// düzleşir. Kalibre edilmemiş bir başlangıç değeridir (eval-09).</param>
    public static IReadOnlyList<EmotionShare> Distribute(ValenceArousal va, double temperature)
    {
        // T ≤ 0 matematiksel olarak tanımsız (sıfıra bölme / taşma); en küçük pozitif değere kırp.
        double t = temperature > 1e-9 ? temperature : 1e-9;

        var scores = new double[Regions.Length];
        double max = double.NegativeInfinity;
        for (int i = 0; i < Regions.Length; i++)
        {
            double d = va.DistanceTo(Regions[i].Center);
            scores[i] = -(d * d) / t;
            if (scores[i] > max) max = scores[i];
        }

        // Sayısal kararlılık: üsleri almadan önce en büyük terimi çıkar (exp taşmasını önler).
        double sum = 0;
        var weights = new double[Regions.Length];
        for (int i = 0; i < Regions.Length; i++)
        {
            weights[i] = Math.Exp(scores[i] - max);
            sum += weights[i];
        }

        var result = new EmotionShare[Regions.Length];
        for (int i = 0; i < Regions.Length; i++)
            result[i] = new EmotionShare(Regions[i].Emotion, weights[i] / sum);

        // Azalan pay; eşitlikte enum sırası (deterministik). Bu sıralama, ilk elemanın
        // Map() ile aynı olmasını garanti eder: en büyük pay = en küçük uzaklık.
        Array.Sort(result, (a, b) =>
        {
            int c = b.Share.CompareTo(a.Share);
            return c != 0 ? c : ((int)a.Emotion).CompareTo((int)b.Emotion);
        });
        return result;
    }
}
