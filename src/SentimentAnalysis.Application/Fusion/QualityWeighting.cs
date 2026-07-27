using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Fusion;

/// <summary>
/// Girdi kalite sinyallerini, füzyon ağırlıklarını modüle eden 0..1 güvenilirlik
/// faktörlerine çevirir. Saf ve deterministik. Her faktör, zayıf herhangi bir
/// boyutun sonucu aşağı çekmesi için bileşenlerin ÇARPIMIdır (master §5).
/// </summary>
public static class QualityWeighting
{
    /// <summary>Ses güvenilirliği: SNR, konuşma oranı ve süre birlikte.</summary>
    public static double AudioReliability(QualitySignals q, FusionConfig cfg)
    {
        double snrRange = cfg.SnrHighDb - cfg.SnrLowDb;
        double snrF = snrRange <= 1e-9 ? 0.0 : Clamp01((q.AudioSnr - cfg.SnrLowDb) / snrRange);
        double speechF = Clamp01(q.SpeechRatio);
        double durF = cfg.MinReliableSeconds <= 1e-9 ? 0.0 : Clamp01(q.Duration.TotalSeconds / cfg.MinReliableSeconds);
        return Clamp01(snrF * speechF * durF);
    }

    /// <summary>Metin güvenilirliği: kelime sayısı ve Whisper avg-logprob birlikte.</summary>
    public static double TextReliability(QualitySignals q, FusionConfig cfg)
    {
        double wordF = cfg.MinReliableWords <= 1e-9 ? 0.0 : Clamp01(q.TranscriptWordCount / cfg.MinReliableWords);
        double logRange = 0.0 - cfg.LogProbFloor;
        double logF = logRange <= 1e-9 ? 0.0 : Clamp01((q.TranscriptAvgLogProb - cfg.LogProbFloor) / logRange);
        return Clamp01(wordF * logF);
    }

    private static double Clamp01(double x) => Math.Clamp(x, 0.0, 1.0);
}
