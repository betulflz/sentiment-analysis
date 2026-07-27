namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>
/// <c>SeaBenSea/hubert-large-turkish-speech-emotion-recognition</c> çıktı etiketleri,
/// <c>config.json</c> <c>id2label</c> SIRASINDA (logits[i] ↔ Ordered[i]).
/// <para>Bu sıra 2026-07-27'de gerçek model config.json'ından doğrulandı:
/// 0:Angry, 1:Calm, 2:Happy, 3:Sad. <c>tools/export_onnx_models.py</c> export sırasında
/// aynı kontrolü yapar ve ayrışma varsa export'u durdurur — yanlış sıra sessizce
/// "kızgın" yerine "mutlu" üretirdi.</para>
/// </summary>
public static class TurkishSerLabels
{
    public static IReadOnlyList<string> Ordered { get; } = new[] { "angry", "calm", "happy", "sad" };
}
