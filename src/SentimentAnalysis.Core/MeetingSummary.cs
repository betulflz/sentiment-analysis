using System;
using System.Collections.Generic;

namespace SentimentAnalysis.Core;

public enum AnalysisOutcome
{
    Success,
    NoResult,
    Failed
}

/// <summary>
/// Toplantı-düzeyi agregasyon çıktısı (master §6). Chunk zaman çizelgesinden
/// hesaplanır. Türkçe özet cümlesi Application katmanında üretilir; burada yalnızca
/// taşınır (Core dil-nötr kalır, yalnızca hazır string tutar).
/// </summary>
public sealed record MeetingSummary(
    Emotion DominantEmotion,
    IReadOnlyDictionary<Emotion, double> TimeShareByEmotion, // duygu → 0..1 süre oranı
    double ValenceTrend,          // regresyon eğimi: + ısındı, − soğudu
    double EmotionalVolatility,   // valence standart sapması (oynaklık)
    IReadOnlyList<ChunkResult> PeakMoments, // en kritik anlar (yüksek arousal / düşük valence)
    ValenceArousal AverageVa,
    double AverageCongruence,     // ortalama söz-ton örtüşmesi (0..1)
    string TurkishSummary,
    AnalysisOutcome Outcome = AnalysisOutcome.Success)
{
    /// <summary>Null for old/unmeasured summaries; absence must not be presented as full coverage.</summary>
    public AnalysisDiagnostics? Diagnostics { get; init; }

    /// <summary>Analiz edilen konuşmanın süresi: parça aralıklarının BİRLEŞİMİ (örtüşme iki kez
    /// sayılmaz, parçalar arası sessizlik sayılmaz). Duygu paylarını süreye çevirmek için.</summary>
    public TimeSpan AnalyzedDuration { get; init; }

    /// <summary>Özete giren parça sayısı (karşı tarafa odakta: karşı tarafın konuştuğu parçalar).</summary>
    public int SummarizedChunks { get; init; }

    /// <summary>Bunlardan model emin olmadığı için "belirsiz" sayılanlar (<see cref="FusedResult.Uncertain"/>).</summary>
    public int UncertainChunks { get; init; }

    /// <summary>Belirsiz parçaların süre payı (0..1). <see cref="TimeShareByEmotion"/> payları tüm süreye
    /// göredir; toplamları 1 − bu değerdir (kapsam kaybı gizlenmez).</summary>
    public double UncertainShare { get; init; }

    public static MeetingSummary CreateEmpty(AnalysisOutcome outcome, string summaryText = "") => new(
        DominantEmotion: Emotion.Notr,
        TimeShareByEmotion: new Dictionary<Emotion, double>(),
        ValenceTrend: 0.0,
        EmotionalVolatility: 0.0,
        PeakMoments: new List<ChunkResult>(),
        AverageVa: ValenceArousal.Neutral,
        AverageCongruence: 1.0,
        TurkishSummary: summaryText,
        Outcome: outcome);
}
