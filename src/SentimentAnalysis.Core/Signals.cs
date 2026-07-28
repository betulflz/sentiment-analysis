using System;
using System.Collections.Generic;

namespace SentimentAnalysis.Core;

/// <summary>Nihai sonucun güven düzeyi.</summary>
public enum Confidence { Yuksek, Orta, Dusuk }

/// <summary>Söz-ton uyumsuzluğunun örüntüsü.</summary>
public enum IncongruencePattern { Yok, Maskeleme, Saka, OlasiIroni }

/// <summary>Algılanan konuşma dili (çok-dilli-hazır).</summary>
public enum DetectedLanguage { Turkish, English, Other }

/// <summary>Ham ses parçası: 16 kHz mono float PCM.</summary>
public sealed record AudioChunk(float[] Samples, int SampleRate, TimeSpan Start, TimeSpan End);

/// <summary>Transkriptin zaman damgalı bir parçası. Konuşmacı ayrımı (alt-proje 10b) bu
/// zamanları konuşmacı bölgeleriyle hizalar; onsuz transkript, kime ait olduğu bilinemeyen
/// düz bir metindir.</summary>
public sealed record TranscriptSegment(string Text, TimeSpan Start, TimeSpan End);

/// <summary>STT çıktısı. <paramref name="Segments"/> additive'dir (varsayılan boş liste) —
/// 10b öncesinden gelen çağıranlar etkilenmez.
/// <para><b>Zaman ekseni uyarısı:</b> STT motoru segmentleri CHUNK'A GÖRELİ üretir (her chunk'ta
/// 0'dan başlar). Mutlak zamana çevirme <c>ChunkProcessor</c>'ın işidir; bu tipte taşınan
/// zamanların hangi eksende olduğu üretenin sözleşmesine bağlıdır.</para></summary>
public sealed record Transcript(
    string Text,
    DetectedLanguage Language,
    double AvgLogProb,
    IReadOnlyList<TranscriptSegment>? Segments = null)
{
    public IReadOnlyList<TranscriptSegment> Segments { get; init; } = Segments ?? Array.Empty<TranscriptSegment>();
}

/// <summary>Metin modelinin ham çıktısı.</summary>
public sealed record TextEmotion(ValenceArousal Va, double ModelConfidence);

/// <summary>Prozodi modelinin ham çıktısı.</summary>
public sealed record ProsodyEmotion(ValenceArousal Va, double ModelConfidence);

/// <summary>Füzyon ağırlığını besleyen girdi kalite sinyalleri.</summary>
public sealed record QualitySignals(
    double AudioSnr,
    double SpeechRatio,
    TimeSpan Duration,
    int TranscriptWordCount,
    double TranscriptAvgLogProb);
