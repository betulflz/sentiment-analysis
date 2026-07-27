using System.Collections.Generic;

namespace SentimentAnalysis.Core;

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
    string TurkishSummary);
