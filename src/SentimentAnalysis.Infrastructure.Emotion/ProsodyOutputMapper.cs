using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>audeering çıktısını (`logits` = [arousal, dominance, valence], 0..1) domain
/// <see cref="ProsodyEmotion"/>'a eşler. valence 0..1 → −1..1; arousal direkt.
/// modelConfidence = sabit, ayarlanabilir model güveni. Regresyon modeli belirsizlik vermez; eskiden kullanılan
/// "nötr merkeze uzaklık" ŞİDDETTİ, güvenilirlik değil — nötr sesi düşük güvenli sayıyor, kazanç artınca
/// güveni de şişiriyordu (İt.3). Ses kalitesi füzyonda ayrı (rAudio); kalibrasyon etiketli veriyle yapılır.
/// <para><b>Kazanç (gain):</b> ham model çıktısı nötr merkez etrafında sıkışıktır (msp-dim
/// valence'ı pratikte 0,35–0,65 bandında kalır → domain'de −0,3…+0,3). Bu aralık
/// <see cref="EmotionWheel"/>'in nötr havzasının içinde olduğundan, kazanç uygulanmazsa
/// prozodi kanalı etiketi hiçbir zaman Nötr'ün dışına taşıyamaz. Kazanç merkez etrafında
/// SİMETRİK doğrusal genişletmedir: işareti değil yayılımı düzeltir, nötr girdiyi nötr bırakır.
/// Kazanç POLİTİKASI <c>EmotionConfig</c>'te yaşar; burada varsayılan kimliktir (1,0).</para></summary>
public static class ProsodyOutputMapper
{
    public const double DefaultModelConfidence = 0.6;

    public static ProsodyEmotion Map(float[] logits, double valenceGain = 1.0, double arousalGain = 1.0,
        double modelConfidence = DefaultModelConfidence)
    {
        double arousal01 = logits.Length > 0 ? logits[0] : 0.5;
        double valence01 = logits.Length > 2 ? logits[2] : 0.5;

        // Merkez etrafında genişlet: valence merkezi 0,5 (domain 0), arousal merkezi 0,5.
        double valence = (valence01 - 0.5) * 2.0 * valenceGain;
        double arousal = 0.5 + (arousal01 - 0.5) * arousalGain;

        return new ProsodyEmotion(ValenceArousal.Clamp(valence, arousal), Math.Clamp(modelConfidence, 0.0, 1.0));
    }
}
