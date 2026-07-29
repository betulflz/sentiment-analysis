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

    private static readonly ValenceArousal NeutralCenter = new(0.0, 0.5);

    /// <summary>Verilen VA noktasına en yakın duyguyu döndürür.</summary>
    /// <param name="neutralRadius">
    /// Verilirse <b>yarıçap kuralı</b> uygulanır: Nötr artık "en yakın merkez" yarışına girmez;
    /// nokta merkez (0, 0.5) çevresindeki bu yarıçaplı diskin İÇİNDEYSE Nötr, DIŞINDAYSA diğer
    /// 7 duygunun en yakını seçilir. <c>null</c> (varsayılan) klasik en-yakın-merkez kuralıdır.
    /// </param>
    public static Emotion Map(ValenceArousal va, double? neutralRadius = null) =>
        MapWithSecondary(va, neutralRadius).Primary;

    /// <summary>En yakın (primary) ve ikinci en yakın (secondary) duyguyu döndürür.
    /// <paramref name="neutralRadius"/> semantiği için bkz. <see cref="Map"/>.</summary>
    /// <remarks>
    /// <b>Neden yarıçap:</b> klasik kuralda Nötr merkezi çarkın tam ortasındadır, diğer 7 duygu
    /// ise kenarında — bu geometri Nötr'e orantısız büyük bir havza verir (a=0,5'te valence
    /// −0,33…+0,40 arası her şey Nötr; v≈0 iken arousal 1,0 olsa bile Nötr kazanır). Yarıçap
    /// kuralı Nötr'ü "her şeye en yakın merkez" olmaktan çıkarıp açıkça tanımlı küçük bir
    /// bölgeye indirir. Yarıçap KALİBRE EDİLMEMİŞtir (eval-09).
    /// </remarks>
    public static (Emotion Primary, Emotion Secondary) MapWithSecondary(
        ValenceArousal va, double? neutralRadius = null)
    {
        if (neutralRadius is < 0.0)
            throw new ArgumentOutOfRangeException(
                nameof(neutralRadius), neutralRadius, "Nötr yarıçapı negatif olamaz.");

        if (neutralRadius is not { } radius)
        {
            var classic = Rank(Regions);
            return (classic[0], classic[1]);
        }

        // Nötr yarıçap dışında bir ADAY DEĞİLDİR — ikincil olarak da sızmamalı, yoksa aynı
        // "her şey nötre yakın" yanılgısı arka kapıdan geri gelir.
        var ranked = Rank(Regions.Where(r => r.Emotion != Emotion.Notr));
        return va.DistanceTo(NeutralCenter) <= radius
            ? (Emotion.Notr, ranked[0])
            : (ranked[0], ranked[1]);

        Emotion[] Rank(IEnumerable<(Emotion Emotion, ValenceArousal Center)> regions) => regions
            .OrderBy(r => va.DistanceTo(r.Center))
            .Select(r => r.Emotion)
            .ToArray();
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
    /// <param name="neutralRadius">
    /// <see cref="Map"/> ile aynı yarıçap kuralı. Verilirse Nötr uzaklıkla yarışmaz: nokta
    /// diskin İÇİNDEYSE Nötr sıfır uzaklıkla (dolayısıyla baskın payla) katılır, DIŞINDAYSA
    /// payı tam olarak 0'dır. Bu, "ilk öğe = <see cref="Map"/>" değişmezini her iki kuralda da
    /// korur — aksi halde UI sütunu ile zaman çizelgesi farklı duygu gösterirdi.
    /// </param>
    /// <remarks>
    /// Dönen ilk öğe her zaman <see cref="Map"/> ile aynı duygudur: pay, uzaklığın monoton
    /// azalan bir fonksiyonudur ve sıralama <see cref="Map"/> ile aynı kararlı (stable) LINQ
    /// sıralamasını kullanır — eşitlik durumunda bildirim sırası korunur.
    /// Sonuç bir olasılık değil, geometrik yakınlık payıdır (bkz. <see cref="EmotionShare"/>).
    /// </remarks>
    public static IReadOnlyList<EmotionShare> Distribute(
        ValenceArousal va, double temperature, double? neutralRadius = null)
    {
        if (temperature <= 0.0)
            throw new ArgumentOutOfRangeException(
                nameof(temperature), temperature, "Dağılım sıcaklığı pozitif olmalıdır.");
        if (neutralRadius is < 0.0)
            throw new ArgumentOutOfRangeException(
                nameof(neutralRadius), neutralRadius, "Nötr yarıçapı negatif olamaz.");

        bool insideNeutral = neutralRadius is { } r && va.DistanceTo(NeutralCenter) <= r;

        var logits = Regions
            .Select(region =>
            {
                if (neutralRadius is not null && region.Emotion == Emotion.Notr)
                    // Yarıçap kuralında Nötr bir uzaklık yarışmacısı değildir: ya sıfır
                    // uzaklıkla baskındır (disk içi) ya da tamamen devre dışıdır (disk dışı).
                    return insideNeutral ? 0.0 : double.NegativeInfinity;

                double d = va.DistanceTo(region.Center);
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
