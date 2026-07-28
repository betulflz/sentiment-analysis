using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Stt;

/// <summary>Whisper segmentlerini tek bir <see cref="Transcript"/>'e indirger. Saf, deterministik.
/// AvgLogProb = süreyle ağırlıklı ortalama token olasılığının logaritması (negatif; kalite sinyali).</summary>
public static class TranscriptAssembler
{
    /// <summary>Boş transkript (konuşma yok) için avg-logprob tabanı — güçlü "düşük kalite" sinyali.</summary>
    public const double EmptyAvgLogProb = -10.0;

    private const double MinProbability = 1e-6;

    public static Transcript Assemble(IReadOnlyList<SttSegment> segments, DetectedLanguage? forced)
    {
        if (segments.Count == 0)
            return new Transcript(string.Empty, forced ?? DetectedLanguage.Other, EmptyAvgLogProb);

        // Metin ve segmentler AYNI süzgeçten geçer: boş metinli segment ikisinde de yer almaz.
        // (Ayrı süzülselerdi transkript ile segment listesi sessizce çelişebilirdi.)
        var kept = segments
            .Select(s => new TranscriptSegment(s.Text.Trim(), s.Start, s.End))
            .Where(s => s.Text.Length > 0)
            .ToList();

        var text = string.Join(" ", kept.Select(s => s.Text));
        var language = forced ?? DominantLanguage(segments);
        var avgProb = WeightedAverageProbability(segments);
        var avgLogProb = Math.Log(Math.Clamp(avgProb, MinProbability, 1.0));

        return new Transcript(text, language, avgLogProb, kept);
    }

    /// <summary>En uzun (süreye göre) segmentin dili baskın kabul edilir.</summary>
    private static DetectedLanguage DominantLanguage(IReadOnlyList<SttSegment> segments)
    {
        var code = segments
            .OrderByDescending(s => (s.End - s.Start).TotalSeconds)
            .First().LanguageCode;
        return LanguageMap.FromIso(code);
    }

    /// <summary>Olasılıkları segment süresiyle ağırlıklandırır (sıfır/negatif süre → ağırlık 1).</summary>
    private static double WeightedAverageProbability(IReadOnlyList<SttSegment> segments)
    {
        double weightSum = 0, weighted = 0;
        foreach (var s in segments)
        {
            double w = (s.End - s.Start).TotalSeconds;
            if (w <= 0) w = 1.0;
            weightSum += w;
            weighted += w * Math.Clamp(s.Probability, 0.0, 1.0);
        }
        return weightSum <= 1e-9 ? 0.0 : weighted / weightSum;
    }
}
