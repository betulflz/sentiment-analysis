using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Fusion;

/// <summary>Üç kaynaklı duygu (spec 11 §3.2): metin + ses füzyonunun valence'ına yüzü kalite ağırlıklı
/// üçüncü kaynak olarak katar ve duygu etiketini yeniden belirler. Birden çok yüz varsa en belirgini kullanılır. Yüz yalnızca valence'a girer —
/// FER+ ifadesi uyarılmışlık için zayıf bir sinyaldir, arousal sesten kalır. Yüz görünmüyor ya da
/// güvenilmezse sonuç aynen döner (dürüst belirsizlik korunur). Saf, deterministik.</summary>
public static class FaceFusion
{
    public static FusedResult Apply(FusedResult speech, IReadOnlyList<FaceWindowSummary> faces, FusionConfig cfg)
    {
        // Konuşmacı–yüz eşleşmesi yok: ortalama, dinleyenin ifadesini konuşanın duygusuna karıştırırdı.
        // Bu yüzden pencerenin en belirgin (en güvenilir × en görünür) yüzü alınır.
        double best = 0, faceValence = 0;
        foreach (var f in faces)
        {
            double w = Math.Clamp(f.Reliability, 0, 1) * Math.Clamp(f.VisibleRatio, 0, 1);
            if (w > best) { best = w; faceValence = f.ValenceShift; }
        }
        if (best <= 1e-9) return speech;

        double faceWeight = cfg.FaceValenceWeight * best;
        double speechWeight = speech.Incongruent
            ? cfg.IncongruentValenceProsodyWeight * speech.AudioReliability
              + (1.0 - cfg.IncongruentValenceProsodyWeight) * speech.TextReliability
            : cfg.TextValenceWeight * speech.TextReliability + cfg.AudioValenceWeight * speech.AudioReliability;
        if (faceWeight <= 1e-9) return speech;

        double valence = (speechWeight * speech.Combined.Valence + faceWeight * faceValence) / (speechWeight + faceWeight);
        var combined = ValenceArousal.Clamp(valence, speech.Combined.Arousal);
        var (primary, secondary) = EmotionWheel.MapWithSecondary(combined, cfg.NeutralRadius);
        return speech with
        {
            Combined = combined,
            PrimaryEmotion = primary,
            SecondaryEmotion = secondary,
            SpeechVa = speech.Combined,
            FaceValence = faceValence,
            FaceWeight = faceWeight,
        };
    }
}
