using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Fusion;

/// <summary>
/// Chunk zaman çizelgesini toplantı-düzeyi metriklere indirger (master §6):
/// baskın duygu, süre oranları, valence trendi, oynaklık, tepe anlar, ortalama
/// congruence. Saf ve deterministik.
/// </summary>
public sealed class MeetingAggregator : IMeetingAggregator
{
    private readonly FusionConfig _cfg;

    public MeetingAggregator(AppConfig config) => _cfg = config.Fusion;

    public MeetingSummary Aggregate(MeetingSession session)
    {
        var chunks = session.Chunks;
        if (chunks.Count == 0)
        {
            return new MeetingSummary(
                DominantEmotion: Emotion.Notr,
                TimeShareByEmotion: new Dictionary<Emotion, double>(),
                ValenceTrend: 0.0,
                EmotionalVolatility: 0.0,
                PeakMoments: new List<ChunkResult>(),
                AverageVa: ValenceArousal.Neutral,
                AverageCongruence: 1.0,
                TurkishSummary: string.Empty);
        }

        var timeShare = ComputeTimeShare(chunks, out var dominant);
        var valences = chunks.Select(c => c.Fused.Combined.Valence).ToArray();
        var times = chunks.Select(c => (c.TimeStart + (c.TimeEnd - c.TimeStart) / 2).TotalSeconds).ToArray();
        double trend = Slope(times, valences);
        double volatility = PopulationStdDev(valences);
        var peaks = chunks
            .OrderByDescending(c => Criticality(c.Fused.Combined))
            .Take(_cfg.PeakMomentCount)
            .ToList();
        var averageVa = new ValenceArousal(
            chunks.Average(c => c.Fused.Combined.Valence),
            chunks.Average(c => c.Fused.Combined.Arousal));
        double avgCongruence = chunks.Average(c => c.Fused.Congruence);

        var summary = new MeetingSummary(
            DominantEmotion: dominant,
            TimeShareByEmotion: timeShare,
            ValenceTrend: trend,
            EmotionalVolatility: volatility,
            PeakMoments: peaks,
            AverageVa: averageVa,
            AverageCongruence: avgCongruence,
            TurkishSummary: string.Empty);
        return summary with { TurkishSummary = TurkishSummaryWriter.Write(summary), AnalyzedDuration = Union(chunks) };
    }

    /// <summary>Kritiklik: yüksek arousal + düşük valence = en kritik an.</summary>
    private static double Criticality(ValenceArousal va) => va.Arousal - va.Valence;

    private static IReadOnlyDictionary<Emotion, double> ComputeTimeShare(
        IReadOnlyList<ChunkResult> chunks, out Emotion dominant)
    {
        var weights = new Dictionary<Emotion, double>();
        double total = 0;
        foreach (var c in chunks)
        {
            double w = Weight(c);
            total += w;
            weights[c.Fused.PrimaryEmotion] = weights.GetValueOrDefault(c.Fused.PrimaryEmotion) + w;
        }

        var share = new Dictionary<Emotion, double>();
        foreach (var kv in weights)
            share[kv.Key] = kv.Value / total;

        // Baskın: en yüksek pay; eşitlikte enum sırasına göre deterministik.
        dominant = share
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => (int)kv.Key)
            .First().Key;
        return share;
    }

    /// <summary>Chunk ağırlığı: pozitif süre; sıfır/negatifse 1.0 (dejenere veriyi eşit say).</summary>
    private static TimeSpan Union(IReadOnlyList<ChunkResult> chunks)
    {
        var total = TimeSpan.Zero;
        TimeSpan? start = null, end = null;
        foreach (var c in chunks.OrderBy(c => c.TimeStart))
        {
            if (end is { } e && c.TimeStart <= e)
            {
                if (c.TimeEnd > e) end = c.TimeEnd;
                continue;
            }
            if (start is { } s0) total += end!.Value - s0;
            start = c.TimeStart;
            end = c.TimeEnd;
        }
        if (start is { } s1) total += end!.Value - s1;
        return total;
    }

    private static double Weight(ChunkResult c)
    {
        double seconds = (c.TimeEnd - c.TimeStart).TotalSeconds;
        return seconds > 0 ? seconds : 1.0;
    }

    /// <summary>y'nin x (chunk zaman-ortası, saniye) üzerinden en küçük kareler regresyon
    /// eğimi. Zaman-tabanlı x, eşit olmayan chunk süreleri/boşluklarında trendi doğru tutar
    /// (master §6: toplantı zaman içinde ısındı/soğudu). x sabitse (den≈0) eğim 0.</summary>
    private static double Slope(double[] x, double[] y)
    {
        int n = y.Length;
        if (n < 2) return 0.0;
        double meanX = x.Average();
        double meanY = y.Average();
        double num = 0, den = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = x[i] - meanX;
            num += dx * (y[i] - meanY);
            den += dx * dx;
        }
        return den <= 1e-9 ? 0.0 : num / den;
    }

    /// <summary>Popülasyon standart sapması (oynaklık).</summary>
    private static double PopulationStdDev(double[] y)
    {
        int n = y.Length;
        if (n < 2) return 0.0;
        double mean = y.Average();
        double sumSq = y.Sum(v => (v - mean) * (v - mean));
        return System.Math.Sqrt(sumSq / n);
    }
}
