using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Vision;

/// <summary>FER+ olasılıklarından ve YuNet noktalarından türeyen saf yüz sinyalleri.
/// FER+ sınıf sırası: nötr, mutlu, şaşkın, üzgün, kızgın, iğrenmiş, korkmuş, küçümseyen.</summary>
public static class FaceSignals
{
    /// <summary>Sınıf başına valence (Russell circumplex yaklaşığı). KALİBRE EDİLMEMİŞ.</summary>
    public static readonly double[] ClassValence = { 0, 0.8, 0.2, -0.6, -0.7, -0.6, -0.6, -0.5 };

    public static double Valence(float[] p)
    {
        double v = 0;
        for (int i = 0; i < ClassValence.Length && i < p.Length; i++) v += p[i] * ClassValence[i];
        return v;
    }

    /// <summary>Olumsuz sınıfların toplam payı (üzgün + kızgın + iğrenmiş + korkmuş + küçümseyen).</summary>
    public static double Negative(float[] p) => p[3] + p[4] + p[5] + p[6] + p[7];

    public static bool Smiling(float[] p) => p[1] > 0.5f;

    /// <summary>Kaba baş yönü: (burun_x − göz_orta_x) / göz_arası. 11a: düz ≤ 0,05, dönük medyan 0,37.</summary>
    public static double Yaw(float[] kps)
    {
        float rightEye = kps[0], leftEye = kps[2], nose = kps[4];
        float eyeDist = Math.Abs(leftEye - rightEye);
        return eyeDist < 1 ? 0 : (nose - (rightEye + leftEye) / 2) / eyeDist;
    }

    /// <summary>Yüz boyuna göre ifade güvenilirliği: MinUsable'da 0, MinReliable'da 1, arası doğrusal.</summary>
    public static double Reliability(FaceBox box, VisionConfig cfg)
    {
        double span = cfg.MinReliableFaceHeight - cfg.MinUsableFaceHeight;
        return span <= 0 ? 1 : Math.Clamp((box.H - cfg.MinUsableFaceHeight) / span, 0, 1);
    }
}
