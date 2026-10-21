using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Decision;

/// <summary>Görüşme sırasında pencere başına eylem önerisi (spec 11 §6.4). Kural + ihtiyaç-ağırlıklı
/// TOPSIS + histerezis. Deterministiktir; tek durumu "şu an gösterilen öneri"dir — öneri her pencerede
/// zıplamasın diye yeni aday mevcudu belirgin bir farkla (<see cref="DecisionConfig.SwitchMargin"/>)
/// geçmeli ya da art arda <see cref="DecisionConfig.SwitchPersistence"/> pencere önde kalmalıdır.</summary>
public sealed class LiveAdvisor
{
    private readonly ProfileDefinition _profile;
    private readonly DecisionConfig _cfg;
    private LiveAction? _current;
    private LiveAction? _pending;
    private int _pendingCount;

    public LiveAdvisor(MeetingProfile profile, DecisionConfig cfg)
    {
        _profile = DecisionKnowledgeBase.Get(profile);
        _cfg = cfg;
    }

    /// <param name="weights">Profil ya da kaydırıcı ağırlıkları; normalize edilmemiş olabilir.</param>
    public Recommendation Next(Indicators indicators, IReadOnlyList<double> weights)
    {
        if (indicators.DataQuality < _cfg.InsufficientQuality)
        {
            Reset();
            return new Recommendation(LiveAction.DevamEt, 0, Array.Empty<Ranked<LiveAction>>(),
                Array.Empty<Criterion>(), null, Confidence.Dusuk,
                "Veri yetersiz (yüz/ses kalitesi düşük) — öneri yapılmadı.");
        }

        var confidence = indicators.DataQuality < _cfg.LowQuality ? Confidence.Dusuk
            : indicators.DataQuality >= _cfg.HighQuality ? Confidence.Yuksek
            : Confidence.Orta;

        if (indicators.Values.Max() < _cfg.InterventionThreshold)
        {
            Commit(LiveAction.DevamEt);
            return new Recommendation(LiveAction.DevamEt, 1, Array.Empty<Ranked<LiveAction>>(),
                Array.Empty<Criterion>(), null, confidence, "Belirgin bir sorun yok.");
        }

        var ranking = ActionRanker.Rank(_profile.Interventions, DecisionKnowledgeBase.LiveImpact, weights, indicators.Values);
        var chosen = Choose(ranking);
        var entry = ranking.First(r => r.Alternative == chosen);
        var face = indicators.SourceFace[(int)entry.TopCriteria[0]];
        return new Recommendation(chosen, entry.Closeness, ranking, entry.TopCriteria, face, confidence,
            DecisionText.Reason(indicators, entry.TopCriteria));
    }

    public void Reset()
    {
        _current = null;
        _pending = null;
        _pendingCount = 0;
    }

    private LiveAction Choose(IReadOnlyList<Ranked<LiveAction>> ranking)
    {
        var candidate = ranking[0];
        if (_current is null || candidate.Alternative == _current)
            return Commit(candidate.Alternative);

        // Mevcut öneri "Devam et" ise sıralamada yoktur → yakınlığı 0 sayılır, aday hemen geçer.
        var currentCloseness = ranking.FirstOrDefault(r => r.Alternative == _current)?.Closeness ?? 0;
        if (candidate.Closeness - currentCloseness >= _cfg.SwitchMargin)
            return Commit(candidate.Alternative);

        if (_pending == candidate.Alternative) _pendingCount++;
        else { _pending = candidate.Alternative; _pendingCount = 1; }

        return _pendingCount >= _cfg.SwitchPersistence ? Commit(candidate.Alternative) : _current.Value;
    }

    private LiveAction Commit(LiveAction action)
    {
        _current = action;
        _pending = null;
        _pendingCount = 0;
        return action;
    }
}
