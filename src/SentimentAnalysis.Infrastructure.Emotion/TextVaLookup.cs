using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>XLM-R 11-duygu logit'lerini domain <see cref="TextEmotion"/>'a eşler. Model ÇOK ETİKETLİDİR
/// (config.json problem_type=multi_label_classification; model kartı: bağımsız sigmoid, τ=0,5) — 2026-11-14'te
/// yerel ONNX'in ham logitleriyle doğrulandı (etkin olmayan duygular -5…-8). Bu yüzden softmax DEĞİL sigmoid:
/// VA = sigmoid-ağırlıklı ortalama; güven = en olası duygunun olasılığı (&lt;0,5 → model hiçbir duyguya "var" demiyor). VA tablosu etiket ADIYLA anahtarlı
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

        double valence = 0, arousal = 0, weight = 0, top = 0;
        for (int i = 0; i < logits.Count; i++)
        {
            if (!Table.TryGetValue(labels[i], out var va))
                throw new ArgumentException($"VA tablosunda etiket yok: '{labels[i]}'.", nameof(labels));
            double p = 1.0 / (1.0 + Math.Exp(-logits[i]));   // bağımsız sigmoid (çok etiketli model)
            valence += p * va.Valence;
            arousal += p * va.Arousal;
            weight += p;
            top = Math.Max(top, p);
        }

        return new TextEmotion(ValenceArousal.Clamp(valence / weight, arousal / weight), Math.Clamp(top, 0.0, 1.0));
    }
}
