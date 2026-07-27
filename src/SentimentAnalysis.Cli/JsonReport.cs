using System.Text.Json;
using System.Text.Json.Serialization;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;

namespace SentimentAnalysis.Cli;

/// <summary>
/// Analiz sonucunu makine okunur JSON'a çevirir — eval/kalibrasyon araçlarının girdisi.
/// Enum'lar ada göre yazılır (sayısal değer sırası değişirse eski raporlar bozulmasın).
/// </summary>
public static class JsonReport
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // Türkçe karakterler okunur kalsın
    };

    public static string Build(
        MeetingSummary s, IReadOnlyList<ChunkResult> timeline, DetectedLanguage language,
        AppConfig config, TimeSpan elapsed)
    {
        var shares = EmotionWheel.Distribute(s.AverageVa, config.Fusion.DistributionTemperature);
        var percents = PercentFormatter.ToPercents(shares.Select(x => x.Share).ToList());

        var report = new
        {
            schemaVersion = 1,
            analysis = new
            {
                elapsedSeconds = Math.Round(elapsed.TotalSeconds, 2),
                chunkCount = timeline.Count,
                detectedLanguage = language.ToString(),
                executionProvider = config.Emotion.ResolvedExecutionProvider,
                pinnedLanguage = config.Stt.WhisperLanguage,
            },
            summary = new
            {
                dominantEmotion = s.DominantEmotion.ToString(),
                averageValence = Round(s.AverageVa.Valence),
                averageArousal = Round(s.AverageVa.Arousal),
                valenceTrend = Round(s.ValenceTrend),
                emotionalVolatility = Round(s.EmotionalVolatility),
                averageCongruence = Round(s.AverageCongruence),
                turkishSummary = s.TurkishSummary,
                timeShareByEmotion = s.TimeShareByEmotion
                    .OrderByDescending(kv => kv.Value)
                    .ToDictionary(kv => kv.Key.ToString(), kv => Round(kv.Value)),
                // Yakınlık payı — OLASILIK DEĞİL (VA uzayındaki geometrik yakınlık).
                emotionProximityShares = shares
                    .Select((x, i) => new { emotion = x.Emotion.ToString(), share = Round(x.Share), percent = percents[i] })
                    .ToArray(),
                peakMoments = s.PeakMoments.Select(ToChunkDto).ToArray(),
            },
            flagged = new
            {
                count = timeline.Count(c => c.Fused.Flagged),
                chunks = timeline.Where(c => c.Fused.Flagged).Select(ToChunkDto).ToArray(),
            },
            timeline = timeline.Select(ToChunkDto).ToArray(),
        };

        return JsonSerializer.Serialize(report, Options);
    }

    private static object ToChunkDto(ChunkResult c)
    {
        var f = c.Fused;
        return new
        {
            startSeconds = Round(c.TimeStart.TotalSeconds),
            endSeconds = Round(c.TimeEnd.TotalSeconds),
            language = c.Language.ToString(),
            transcript = c.Transcript,
            primaryEmotion = f.PrimaryEmotion.ToString(),
            secondaryEmotion = f.SecondaryEmotion.ToString(),
            confidence = f.Confidence.ToString(),
            valence = Round(f.Combined.Valence),
            arousal = Round(f.Combined.Arousal),
            // İki kanalın ham çıktısı + onlara verilen ağırlık — kararın "nasıl" oluştuğu.
            textValence = Round(f.TextValence),
            textArousal = Round(f.TextArousal),
            textConfidence = Round(f.TextConfidence),
            textReliability = Round(f.TextReliability),
            textChannelSilent = f.TextChannelSilent,
            audioValence = Round(f.AudioValence),
            audioArousal = Round(f.AudioArousal),
            audioConfidence = Round(f.ProsodyConfidence),
            audioReliability = Round(f.AudioReliability),
            audioChannelSilent = f.AudioChannelSilent,
            congruence = Round(f.Congruence),
            agreement = f.Agreement,
            incongruent = f.Incongruent,
            pattern = f.Pattern.ToString(),
            flagged = f.Flagged,
        };
    }

    private static double Round(double v) => Math.Round(v, 4);
}
