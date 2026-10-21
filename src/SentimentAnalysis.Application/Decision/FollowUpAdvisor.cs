using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Decision;

/// <summary>Görüşme sonrası takip kararı (spec 11 §6.6): görüşme düzeyindeki göstergeler üzerinden
/// canlı danışmanla AYNI motor (kural + ihtiyaç-ağırlıklı TOPSIS), profile özgü seçeneklerle. Saf.</summary>
public static class FollowUpAdvisor
{
    /// <summary>Kriter başına 0,5·ortalama + 0,5·p90 — tek bir gergin an görüşmeyi tek başına
    /// belirlemesin, ama tekrarlayan tepeler de ortalamada kaybolmasın.</summary>
    public static Indicators AggregateMeeting(IReadOnlyList<Indicators> windows, DecisionConfig cfg)
    {
        var values = new double[6];
        var source = new int?[6];
        if (windows.Count == 0) return new Indicators(values, source, 0);

        for (int j = 0; j < 6; j++)
        {
            var xs = windows.Select(w => w.Values[j]).OrderBy(x => x).ToArray();
            var p90 = xs[(int)Math.Ceiling(0.9 * xs.Length) - 1];
            values[j] = Math.Clamp(0.5 * xs.Average() + 0.5 * p90, 0, 1);
            source[j] = windows
                .Where(w => w.Values[j] >= cfg.InterventionThreshold && w.SourceFace[j] is not null)
                .GroupBy(w => w.SourceFace[j]!.Value)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                .Select(g => (int?)g.Key)
                .FirstOrDefault();
        }
        return new Indicators(values, source, windows.Average(w => w.DataQuality));
    }

    public static FollowUpDecision Decide(MeetingProfile profile, Indicators meeting,
        IReadOnlyList<double> weights, DecisionConfig cfg)
    {
        var def = DecisionKnowledgeBase.Get(profile);
        var none = Array.Empty<Ranked<FollowUpAction>>();

        if (profile == MeetingProfile.Mulakat && meeting.DataQuality < cfg.LowQuality)
            return new FollowUpDecision(FollowUpAction.GorusmeyiTekrarla, none,
                "Görüşme verisi yetersiz (yüz/ses kalitesi düşük) — sağlıklı bir değerlendirme için görüşme tekrarlanmalı.");

        if (meeting.Values.Max() < cfg.InterventionThreshold)
            return new FollowUpDecision(def.PositiveFollowUp, none, "Görüşme boyunca belirgin bir sorun görülmedi.");

        var ranking = ActionRanker.Rank(def.FollowUps, DecisionKnowledgeBase.FollowUpImpact, weights, meeting.Values);
        return new FollowUpDecision(ranking[0].Alternative, ranking, DecisionText.Reason(meeting, ranking[0].TopCriteria));
    }
}
