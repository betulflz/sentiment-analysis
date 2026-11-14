using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Vision;

/// <summary>Analiz edilmiş karelerin bellek içi zaman çizelgesi; ses penceresi geldiğinde aynı aralığın
/// yüz özetini (<see cref="FaceWindowSummary"/>) üretir. Thread-safe: görüntü hattı yazar, UI/karar
/// tarafı okur. Görüntünün kendisi DEĞİL, yalnızca türetilmiş gözlemler tutulur (spec 11 §4).
/// <para><b>Taban çizgisi:</b> FER+ dinlenme yüzünü ~%26 "üzgün" okur (11a §5). Bu yüzden ifade, kişinin
/// KENDİ ilk <see cref="VisionConfig.BaselineSeconds"/> saniyesine göre ölçülür. O süre dolmadan
/// ölçülmüş dinlenme yüzü varsayılanı kullanılır.</para></summary>
public sealed class VisionTimeline
{
    // 11a §5: dinlenme yüzü nötr ~.73 / üzgün ~.26 → valence ≈ −0,15, olumsuz pay ≈ 0,27.
    private const double DefaultBaselineValence = -0.15;
    private const double DefaultBaselineNegative = 0.27;
    // Kişisel taban bir dinlenme yüzüdür, ifade değil: ilk saniyelerde gülümseyen/kaş çatan kişinin
    // ifadesi tabana yazılıp sonradan silinmesin diye taban bu aralıklara sıkıştırılır (11d).
    private const double MinBaselineValence = -0.4, MaxBaselineValence = 0.0;
    private const double MaxBaselineNegative = 0.5;

    private readonly VisionConfig _cfg;
    private readonly object _gate = new();
    private readonly List<TimeSpan> _frames = new();
    private readonly List<FaceObservation> _observations = new();

    public VisionTimeline(VisionConfig cfg) => _cfg = cfg;

    /// <summary>Yüzsüz analiz karesi de eklenmelidir — görünürlük oranının paydasıdır.</summary>
    public void AddFrame(TimeSpan time, IReadOnlyList<FaceObservation> faces)
    {
        lock (_gate)
        {
            _frames.Add(time);
            _observations.AddRange(faces);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _frames.Clear();
            _observations.Clear();
        }
    }

    /// <summary>[start, end) aralığı. Bu aralıkta en az bir kez görülen her yüz için özet, FaceIndex sırasıyla.</summary>
    public IReadOnlyList<FaceWindowSummary> Summarize(TimeSpan start, TimeSpan end)
    {
        FaceObservation[] all;
        int frameCount;
        lock (_gate)
        {
            frameCount = _frames.Count(t => t >= start && t < end);
            all = _observations.ToArray();
        }
        if (frameCount == 0) return Array.Empty<FaceWindowSummary>();

        return all
            .Where(o => o.Time >= start && o.Time < end)
            .GroupBy(o => o.FaceIndex)
            .OrderBy(g => g.Key)
            .Select(g => Summarize(g.Key, g.ToArray(), frameCount, all))
            .ToArray();
    }

    private FaceWindowSummary Summarize(int face, FaceObservation[] window, int frameCount, FaceObservation[] all)
    {
        var (baseValence, baseNegative) = Baseline(all.Where(o => o.FaceIndex == face).OrderBy(o => o.Time).ToArray());
        double valence = window.Average(o => FaceSignals.Valence(o.Expression));
        double negative = window.Average(o => FaceSignals.Negative(o.Expression));
        double negScale = 1 - baseNegative;

        return new FaceWindowSummary(
            FaceIndex: face,
            VisibleRatio: Math.Clamp(window.Length / (double)frameCount, 0, 1),
            LookAwayRatio: window.Count(o => Math.Abs(o.Yaw) > _cfg.LookAwayYaw) / (double)window.Length,
            ValenceShift: Math.Clamp(valence - baseValence, -1, 1),
            NegativeExpression: negScale <= 0 ? 0 : Math.Clamp((negative - baseNegative) / negScale, 0, 1),
            SmileRatio: window.Count(o => FaceSignals.Smiling(o.Expression)) / (double)window.Length,
            Reliability: window.Average(o => FaceSignals.Reliability(o.Box, _cfg)));
    }

    /// <summary>Yüzün gözlemleri en az BaselineSeconds'lık bir süreye yayıldıysa ilk BaselineSeconds'ın
    /// ortalaması; değilse ölçülmüş varsayılan.</summary>
    private (double Valence, double Negative) Baseline(FaceObservation[] history)
    {
        var window = TimeSpan.FromSeconds(_cfg.BaselineSeconds);
        if (history.Length == 0 || history[^1].Time - history[0].Time < window)
            return (DefaultBaselineValence, DefaultBaselineNegative);

        var first = history.TakeWhile(o => o.Time - history[0].Time < window).ToArray();
        return (Math.Clamp(first.Average(o => FaceSignals.Valence(o.Expression)), MinBaselineValence, MaxBaselineValence),
                Math.Min(first.Average(o => FaceSignals.Negative(o.Expression)), MaxBaselineNegative));
    }
}
