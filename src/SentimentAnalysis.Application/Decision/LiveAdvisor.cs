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
    private bool _insufficient;

    public LiveAdvisor(MeetingProfile profile, DecisionConfig cfg)
    {
        _profile = DecisionKnowledgeBase.Get(profile);
        _cfg = cfg;
    }

    /// <param name="weights">Profil ya da kaydırıcı ağırlıkları; normalize edilmemiş olabilir.</param>
    public Recommendation Next(Indicators indicators, IReadOnlyList<double> weights)
    {
        // Bant: yetersiz duruma 0,25'in altında girilir, ancak 0,30'un üstünde çıkılır.
        _insufficient = indicators.DataQuality < (_insufficient ? _cfg.QualityRecovery : _cfg.InsufficientQuality);
        if (_insufficient)
        {
            _current = null;
            _pending = null;
            _pendingCount = 0;
            return new Recommendation(LiveAction.DevamEt, 0, Array.Empty<Ranked<LiveAction>>(),
                Array.Empty<Criterion>(), null, Confidence.Dusuk,
                "Veri yetersiz (yüz/ses kalitesi düşük) — öneri yapılmadı.");
        }

        var confidence = indicators.DataQuality < _cfg.LowQuality ? Confidence.Dusuk
            : indicators.DataQuality >= _cfg.HighQuality ? Confidence.Yuksek
            : Confidence.Orta;

        // Spec §6.4.1: ihtiyaç = gösterge × veri kalitesi. TOPSIS sıralamasını değiştirmez (sabit çarpan
        // ağırlık normalizasyonunda sadeleşir), yalnızca "müdahale gerekli mi" kuralını etkiler.
        var needs = indicators.Values.Select(v => v * indicators.DataQuality).ToArray();
        // Bant: müdahale 0,35'te başlar, ama gösterilen bir müdahale ancak 0,30'un altında "Devam et"e döner.
        var intervening = _current is not null && _current != LiveAction.DevamEt;
        if (needs.Max() < (intervening ? _cfg.InterventionExitThreshold : _cfg.InterventionThreshold))
        {
            Commit(LiveAction.DevamEt);
            var reason = indicators.Values.Max() >= _cfg.InterventionThreshold
                ? "Sinyal var ama veri kalitesi düşük — öneri yapılmadı."
                : "Belirgin bir sorun yok.";
            return new Recommendation(LiveAction.DevamEt, 1, Array.Empty<Ranked<LiveAction>>(),
                Array.Empty<Criterion>(), null, confidence, reason);
        }

        var ranking = ActionRanker.Rank(_profile.Interventions, DecisionKnowledgeBase.LiveImpact, weights, needs);
        var chosen = Choose(ranking);
        var entry = ranking.First(r => r.Alternative == chosen);
        var face = indicators.SourceFace[(int)entry.TopCriteria[0]];
        return new Recommendation(chosen, entry.Closeness, ranking, entry.TopCriteria, face, confidence,
            DecisionText.Reason(indicators, entry.TopCriteria));
    }

    public void Reset()
    {
        _insufficient = false;
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
