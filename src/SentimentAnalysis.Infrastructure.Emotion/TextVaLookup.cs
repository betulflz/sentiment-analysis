using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>XLM-R 11-duygu logit'lerini domain <see cref="TextEmotion"/>'a eşler: softmax →
/// olasılık-ağırlıklı VA ortalaması + entropi-tabanlı güven. VA tablosu etiket ADIYLA anahtarlı
/// (index sırası hatası VA'yı sessizce bozamaz). VA değerleri BAŞLANGIÇ (spec §04b); Alt-Proje 09
/// Türkçe veriyle kalibre eder — keyfi değil.</summary>
public static class TextVaLookup
{
    public static IReadOnlyDictionary<string, ValenceArousal> Table { get; } =
        new Dictionary<string, ValenceArousal>
        {
            ["joy"]         = new(+0.8, 0.7),
            ["love"]        = new(+0.9, 0.5),
            ["gratitude"]   = new(+0.7, 0.4),
            ["surprise"]    = new(+0.2, 0.8),
            ["neutral"]     = new( 0.0, 0.5),
            ["anger"]       = new(-0.8, 0.8),
            ["fear"]        = new(-0.7, 0.8),
            ["sadness"]     = new(-0.7, 0.3),
            ["disgust"]     = new(-0.6, 0.5),
            ["frustration"] = new(-0.5, 0.6),
            ["contempt"]    = new(-0.5, 0.4),
        };

    /// <summary>logits → TextEmotion. labels[i] logits[i]'nin duygusudur (model id2label sırası).</summary>
    public static TextEmotion Map(IReadOnlyList<float> logits, IReadOnlyList<string> labels)
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

        return new TextEmotion(ValenceArousal.Clamp(valence, arousal), confidence);
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
