using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>
/// Türkçe SER'in 4 ayrık sınıf logit'ini boyutsal <see cref="ProsodyEmotion"/>'a çevirir:
/// softmax → olasılık-ağırlıklı VA ortalaması + entropi tabanlı güven.
/// (<see cref="TextVaLookup"/> ile aynı desen; VA tablosu etiket ADIYLA anahtarlı, böylece
/// index sırası hatası VA'yı sessizce bozamaz.)
/// <para><b>Çapa noktaları keyfi değildir:</b> her sınıf, <see cref="EmotionWheel"/>'in kendi
/// merkezine bağlanmıştır. Böylece saf bir "angry" tahmini çarkta tam olarak Kızgın'a geri
/// döner — iki temsil arasında sessiz bir kayma olmaz. <c>calm</c> → Nötr eşlemesi, aynı
/// modeli kullanan Python projesindeki <c>SER_LABEL_TR_MAP</c> kararıyla aynıdır.</para>
/// <para><b>Kalibre edilmemiştir</b> (eval-09): TurEV-DB oyunculu konuşmayla eğitilmiştir,
/// kendiliğinden konuşmada sınıf olasılıkları farklı dağılabilir.</para>
/// </summary>
public static class TurkishSerVaLookup
{
    public static IReadOnlyDictionary<string, ValenceArousal> Table { get; } =
        new Dictionary<string, ValenceArousal>
        {
            ["angry"] = new(-0.8, 0.9),   // EmotionWheel: Kizgin
            ["calm"]  = new( 0.0, 0.5),   // EmotionWheel: Notr
            ["happy"] = new( 0.8, 0.5),   // EmotionWheel: Mutlu
            ["sad"]   = new(-0.6, 0.3),   // EmotionWheel: Uzgun
        };

    /// <summary>logits → ProsodyEmotion. labels[i] logits[i]'nin sınıfıdır (model id2label sırası).</summary>
    public static ProsodyEmotion Map(IReadOnlyList<float> logits, IReadOnlyList<string> labels)
    {
        if (logits.Count != labels.Count)
            throw new ArgumentException(
                $"logits ({logits.Count}) ile labels ({labels.Count}) uzunluğu eşleşmiyor.", nameof(logits));
        if (logits.Count == 0)
            throw new ArgumentException("Boş logits.", nameof(logits));

        var probs = Softmax(logits);

        double valence = 0, arousal = 0, entropy = 0;
        for (int i = 0; i < probs.Length; i++)
        {
            if (!Table.TryGetValue(labels[i], out var va))
                throw new ArgumentException($"VA tablosunda etiket yok: '{labels[i]}'.", nameof(labels));
            valence += probs[i] * va.Valence;
            arousal += probs[i] * va.Arousal;
            if (probs[i] > 0) entropy -= probs[i] * Math.Log(probs[i]);
        }

        double maxEntropy = Math.Log(probs.Length);          // düzgün dağılımın entropisi
        double confidence = maxEntropy > 0
            ? Math.Clamp(1.0 - entropy / maxEntropy, 0.0, 1.0)
            : 1.0;

        return new ProsodyEmotion(ValenceArousal.Clamp(valence, arousal), confidence);
    }

    private static double[] Softmax(IReadOnlyList<float> logits)
    {
        double max = double.NegativeInfinity;
        for (int i = 0; i < logits.Count; i++) if (logits[i] > max) max = logits[i];

        var exp = new double[logits.Count];
        double sum = 0;
        for (int i = 0; i < logits.Count; i++)
        {
            exp[i] = Math.Exp(logits[i] - max);              // sayısal kararlılık için max çıkar
            sum += exp[i];
        }
        for (int i = 0; i < exp.Length; i++) exp[i] /= sum;
        return exp;
    }
}
