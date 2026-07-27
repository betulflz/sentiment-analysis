namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>tabularisai/multilingual-emotion-classification'ın çıktı etiketleri, `config.json`
/// `id2label` SIRASINDA (logits[i] ↔ Ordered[i]). Bu sıra 2026-07-26'da gerçek model config.json'ından
/// DOĞRULANDI (alfabetik): 0:anger … 10:surprise. (Önceki başlangıç sırası spec taslağındandı ve YANLIŞTI —
/// gerçek modelle düzeltildi.) VA tablosu ada göre anahtarlı olduğundan sıra bu listeyle kilitlenir.</summary>
public static class XlmrEmotionLabels
{
    public static IReadOnlyList<string> Ordered { get; } = new[]
    {
        "anger", "contempt", "disgust", "fear", "frustration",
        "gratitude", "joy", "love", "neutral", "sadness", "surprise",
    };
}
