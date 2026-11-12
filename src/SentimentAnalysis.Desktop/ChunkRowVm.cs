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
        var role = DominantRole(r);
        // 12: gösterilen duygu etiketle aynı karardan gelir. "Siz" satırında karşı tarafın verisi gösterilmez;
        // karışık satırda karşı tarafın duygusu gösterilir ve etiket bunu açıkça söyler.
        var shown = role == SpeakerRole.Yoneten ? r.Fused : r.Counterpart ?? r.Fused;
        bool mixedShowsOther = role == SpeakerRole.Belirsiz && r.Counterpart is not null && r.Roles is not null;
        Who = role switch
        {
            SpeakerRole.Yoneten => "Siz",
            SpeakerRole.KarsiTaraf => "Karşı taraf",
            _ when r.Roles is null || !HasSpeech(r) => string.Empty,
            _ => mixedShowsOther ? "İkiniz · duygu: karşı taraf" : "İkiniz",
        };
        // Tanıtma şüpheliyse (kayıtta iki ses) rol etiketi kesin gibi gösterilmez.
        if (Who.Length > 0 && r.Roles is { EnrollmentDoubtful: true }) Who += " (ses tanıtması şüpheli)";
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

    /// <summary>Satırda baskın konuşan: payı ≥ %80 olan rol; karışıksa ya da rol yoksa <see cref="SpeakerRole.Belirsiz"/>.</summary>
    private static SpeakerRole DominantRole(ChunkResult r)
    {
        if (r.Roles is not { Enrolled: true } roles) return SpeakerRole.Belirsiz;
        double host = roles.Seconds(SpeakerRole.Yoneten, r.TimeStart, r.TimeEnd);
        double other = roles.Seconds(SpeakerRole.KarsiTaraf, r.TimeStart, r.TimeEnd);
        if (host + other <= 0) return SpeakerRole.Belirsiz;
        double share = host / (host + other);
        return share >= 0.8 ? SpeakerRole.Yoneten : share <= 0.2 ? SpeakerRole.KarsiTaraf : SpeakerRole.Belirsiz;
    }

    private static bool HasSpeech(ChunkResult r) =>
        r.Roles is { Enrolled: true } roles && roles.Spans.Count > 0;

    private static int Percent(double reliability) =>
        (int)Math.Round(Math.Clamp(reliability, 0.0, 1.0) * 100, MidpointRounding.AwayFromZero);
}
