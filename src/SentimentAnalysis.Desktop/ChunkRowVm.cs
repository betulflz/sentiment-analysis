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

    /// <summary>12: bu aralıkta kim konuştu — "Siz", "Karşı taraf", "İkiniz"; ses tanıtılmadıysa boş.</summary>
    public string Who { get; }

    public bool HasWho => Who.Length > 0;

    /// <summary>Valence/Arousal'ın jargonsuz, düz Türkçe özeti. <see cref="SourceAnalysisVm.AxisSummary"/>
    /// ile aynı üretici metotları kullanır — iki yüzey asla ayrışmasın diye.</summary>
    public string AxisSummary { get; }

    /// <summary>Girdi kalitesinin iki kanal için yüzde özeti. Model güveni DEĞİLDİR: "ses/metin
    /// ne kadar analiz edilebilirdi" sorusunun cevabıdır. Sonuç beklenmedik biçimde nötr
    /// çıktığında bakılacak ilk yer burasıdır (düşük değer → kayıt kısa, sessiz veya gürültülü).</summary>
    public string QualityNote { get; }

    /// <summary>Girdi kanallarından biri zayıf (&lt; %30) — UI bu satıra temkinli yaklaşmalı.</summary>
    public bool WeakInput { get; }

    /// <summary>Bu aralıkta görülen yüzlerin kısa özeti (11b): "Yüz 1: dönük %20, olumsuz ifade %10,
    /// gülümseme %0 · Yüz 2: …". Görüntü kanalı yoksa ya da yüz görülmediyse boş.</summary>
    public string FaceNote { get; }

    private const double WeakInputThreshold = 0.3;

    public ChunkRowVm(ChunkResult r, IReadOnlyList<FaceWindowSummary>? faces = null)
    {
        Time = r.TimeStart;
        Who = WhoText(r);
        // 12: karşı taraf konuştuysa satırın duygusu ONUN duygusudur (öneriler de ona göre); aksi halde konuşanın.
        var shown = r.Counterpart ?? r.Fused;
        Emotion = EmotionLocalizer.Emotion(shown.PrimaryEmotion);
        Valence = shown.Combined.Valence;
        Arousal = shown.Combined.Arousal;
        Flagged = shown.Flagged;
        Badge = shown.Flagged ? EmotionLocalizer.Pattern(shown.Pattern) : string.Empty;
        Transcript = r.Transcript;
        AxisSummary = $"{EmotionLocalizer.ValenceText(Valence)} · {EmotionLocalizer.ArousalText(Arousal)}";
        QualityNote =
            $"Ses girdisi %{Percent(r.Fused.AudioReliability)} · Metin girdisi %{Percent(r.Fused.TextReliability)}";
        WeakInput = r.Fused.AudioReliability < WeakInputThreshold
                 || r.Fused.TextReliability < WeakInputThreshold;
        FaceNote = string.Join(" · ", (faces ?? Array.Empty<FaceWindowSummary>()).Select(f =>
            $"Yüz {f.FaceIndex + 1}: dönük %{Percent(f.LookAwayRatio)}, " +
            $"olumsuz ifade %{Percent(f.NegativeExpression)}, gülümseme %{Percent(f.SmileRatio)}"));
    }

    private static string WhoText(ChunkResult r)
    {
        if (r.Roles is not { Enrolled: true } roles) return string.Empty;
        double host = roles.Seconds(SpeakerRole.Yoneten, r.TimeStart, r.TimeEnd);
        double other = roles.Seconds(SpeakerRole.KarsiTaraf, r.TimeStart, r.TimeEnd);
        if (host + other <= 0) return string.Empty;
        double share = host / (host + other);
        return share >= 0.8 ? "Siz" : share <= 0.2 ? "Karşı taraf" : "İkiniz";
    }

    private static int Percent(double reliability) =>
        (int)Math.Round(Math.Clamp(reliability, 0.0, 1.0) * 100, MidpointRounding.AwayFromZero);
}
