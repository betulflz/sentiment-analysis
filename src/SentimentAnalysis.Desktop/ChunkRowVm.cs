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

    /// <summary>Girdi kalitesinin iki kanal için yüzde özeti. Model güveni DEĞİLDİR: "ses/metin
    /// ne kadar analiz edilebilirdi" sorusunun cevabıdır. Sonuç beklenmedik biçimde nötr
    /// çıktığında bakılacak ilk yer burasıdır (düşük değer → kayıt kısa, sessiz veya gürültülü).</summary>
    public string QualityNote { get; }

    /// <summary>Girdi kanallarından biri zayıf (&lt; %30) — UI bu satıra temkinli yaklaşmalı.</summary>
    public bool WeakInput { get; }

    private const double WeakInputThreshold = 0.3;

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
        QualityNote =
            $"Ses girdisi %{Percent(r.Fused.AudioReliability)} · Metin girdisi %{Percent(r.Fused.TextReliability)}";
        WeakInput = r.Fused.AudioReliability < WeakInputThreshold
                 || r.Fused.TextReliability < WeakInputThreshold;
    }

    private static int Percent(double reliability) =>
        (int)Math.Round(Math.Clamp(reliability, 0.0, 1.0) * 100, MidpointRounding.AwayFromZero);
}
