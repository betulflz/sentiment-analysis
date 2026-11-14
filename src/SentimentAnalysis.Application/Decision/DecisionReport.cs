using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Decision;

/// <summary>Görüşme sonrası raporun karar bölümleri (spec 11 §6.6): kritik anlar ve verilen öneriler.
/// Saklanmış pencere göstergelerinden çalışır — göstergeler YENİDEN HESAPLANMAZ. Saf.</summary>
public static class DecisionReport
{
    /// <summary>İhtiyacın (değer × veri kalitesi) <see cref="DecisionConfig.InterventionThreshold"/>'u
    /// aştığı ardışık pencereler bir bölümdür. Tepeye göre en fazla <paramref name="max"/> bölüm seçilir,
    /// zaman sırasıyla döner.</summary>
    public static IReadOnlyList<CriticalMoment> CriticalMoments(IReadOnlyList<DecisionWindow> windows,
        DecisionConfig cfg, int max = 5)
    {
        var moments = new List<CriticalMoment>();
        int i = 0;
        while (i < windows.Count)
        {
            if (Peak(windows[i]).Need < cfg.InterventionThreshold) { i++; continue; }

            int first = i, peakAt = i;
            for (; i < windows.Count && Peak(windows[i]).Need >= cfg.InterventionThreshold; i++)
                if (Peak(windows[i]).Need > Peak(windows[peakAt]).Need) peakAt = i;

            var w = windows[peakAt];
            var (c, need) = Peak(w);
            moments.Add(new CriticalMoment(windows[first].Start, windows[i - 1].End, c, need,
                w.Indicators.SourceFace[(int)c], w.Recommendation.Action));
        }
        return moments
            .Select((m, k) => (m, k))
            .OrderByDescending(x => x.m.Peak).ThenBy(x => x.k)
            .Take(max)
            .Select(x => x.m)
            .OrderBy(m => m.Start)
            .ToArray();
    }

    /// <summary>Gösterilen önerinin değiştiği pencereler (ilk pencere dahil).</summary>
    public static IReadOnlyList<RecommendationChange> Changes(IReadOnlyList<DecisionWindow> windows)
    {
        var changes = new List<RecommendationChange>();
        LiveAction? previous = null;
        foreach (var w in windows)
        {
            var r = w.Recommendation;
            if (r.Action == previous) continue;
            changes.Add(new RecommendationChange(w.Start, r.Action, r.Reason, r.Confidence));
            previous = r.Action;
        }
        return changes;
    }

    /// <summary>Pencerenin en yüksek ihtiyaçlı kriteri; eşitlikte enum sırasıyla ilki.</summary>
    private static (Criterion Criterion, double Need) Peak(DecisionWindow w)
    {
        var ind = w.Indicators;
        int best = 0;
        for (int j = 1; j < ind.Values.Count; j++)
            if (ind.Values[j] > ind.Values[best]) best = j;
        return ((Criterion)best, ind.Values[best] * ind.DataQuality);
    }
}
