using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Zaman çizelgesi satırı (lokalize, salt-okunur).</summary>
public sealed class ChunkRowVm
{
    public TimeSpan Time { get; }
    public string Emotion { get; }
    public double Valence { get; }
    public double Arousal { get; }
    public bool Flagged { get; }
    public string Badge { get; }
    public string Transcript { get; }

    /// <summary>Valence/Arousal'ın jargonsuz, düz Türkçe özeti. <see cref="SourceAnalysisVm.AxisSummary"/>
    /// ile aynı üretici metotları kullanır — iki yüzey asla ayrışmasın diye.</summary>
    public string AxisSummary { get; }

    public ChunkRowVm(ChunkResult r)
    {
        Time = r.TimeStart;
        Emotion = EmotionLocalizer.Emotion(r.Fused.PrimaryEmotion);
        Valence = r.Fused.Combined.Valence;
        Arousal = r.Fused.Combined.Arousal;
        Flagged = r.Fused.Flagged;
        Badge = r.Fused.Flagged ? EmotionLocalizer.Pattern(r.Fused.Pattern) : string.Empty;
        Transcript = r.Transcript;
        AxisSummary = $"{EmotionLocalizer.ValenceText(Valence)} · {EmotionLocalizer.ArousalText(Arousal)}";
    }
}
