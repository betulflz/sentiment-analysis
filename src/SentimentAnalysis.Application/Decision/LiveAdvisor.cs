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
    private bool _skipHysteresis;
    private LiveAction? _lastShown;
    private int _streak;   // _lastShown art arda kaç pencere gösterildi

    public LiveAdvisor(MeetingProfile profile, DecisionConfig cfg)
    {
        _profile = DecisionKnowledgeBase.Get(profile);
        _cfg = cfg;
    }

    /// <param name="weights">Profil ya da kaydırıcı ağırlıkları; normalize edilmemiş olabilir.</param>
    public Recommendation Next(Indicators indicators, IReadOnlyList<double> weights)
    {
        var rec = NextCore(indicators, weights);
        _streak = rec.Action == _lastShown ? _streak + 1 : 1;
        _lastShown = rec.Action;
        return rec;
    }

    private Recommendation NextCore(Indicators indicators, IReadOnlyList<double> weights)
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
        var intervening = _current is { } shownNow && shownNow != LiveAction.DevamEt && !IsPositive(shownNow);
        if (needs.Max() < (intervening ? _cfg.InterventionExitThreshold : _cfg.InterventionThreshold))
        {
            bool lowQualitySignal = indicators.Values.Max() >= _cfg.InterventionThreshold;
            // Sorun yok ve görüşme gerçekten olumluysa olumlu bir öneri (sinyal düşük kaliteden gelmiyorsa).
            if (!lowQualitySignal && indicators.Positivity >= _cfg.PositiveThreshold)
            {
                var positive = ChoosePositive();
                Commit(positive);
                return new Recommendation(positive, 1, Array.Empty<Ranked<LiveAction>>(),
                    Array.Empty<Criterion>(), null, confidence, "Görüşme olumlu gidiyor.");
            }
            Commit(LiveAction.DevamEt);
            var reason = lowQualitySignal
                ? "Sinyal var ama veri kalitesi düşük — öneri yapılmadı."
                : "Belirgin bir sorun yok.";
            return new Recommendation(LiveAction.DevamEt, 1, Array.Empty<Ranked<LiveAction>>(),
                Array.Empty<Criterion>(), null, confidence, reason);
        }

        var pool = indicators.Cues is null
            ? _profile.Interventions
            : _profile.Interventions.Concat(DecisionKnowledgeBase.ContextActions).ToArray();
        var ranking = ApplyContext(ActionRanker.Rank(pool, DecisionKnowledgeBase.LiveImpact, weights, needs), indicators);
        var chosen = Choose(ranking);
        var entry = ranking.First(r => r.Alternative == chosen);
        var face = indicators.SourceFace[(int)entry.TopCriteria[0]];
        return new Recommendation(chosen, entry.Closeness, ranking, entry.TopCriteria, face, confidence,
            DecisionText.Reason(indicators, entry.TopCriteria));
    }

    /// <summary>TOPSIS yakınlığına duygu/tempo/hareket uyumunu ekler ve yeniden sıralar (bağlam yoksa dokunmaz).</summary>
    private IReadOnlyList<Ranked<LiveAction>> ApplyContext(IReadOnlyList<Ranked<LiveAction>> ranking, Indicators ind)
    {
        if (ind.Cues is not { } cues || _cfg.ContextWeight <= 0) return ranking;
        double incongruence = ind.Values[(int)Criterion.Uyumsuzluk];
        return ranking
            .Select((r, i) => (r: r with { Closeness = Math.Clamp(
                r.Closeness + _cfg.ContextWeight * ContextAffinity.Of(r.Alternative, cues, incongruence), 0, 1) }, i))
            .OrderByDescending(x => x.r.Closeness).ThenBy(x => x.i)
            .Select(x => x.r)
            .ToArray();
    }

    /// <summary>Kullanıcı ağırlıkları değiştirdi: histerezis atlanır, en iyi eylem hemen gösterilir ve
    /// mevcut öneri kabul edilir (spec §6.5: "değişince sıralama anında yeniden hesaplanır"). Kalite ve
    /// müdahale bantları KORUNUR — ağırlık değişimi veri kalitesini ya da ihtiyacı değiştirmez.</summary>
    public Recommendation Rerank(Indicators indicators, IReadOnlyList<double> weights)
    {
        _pending = null;
        _pendingCount = 0;
        _skipHysteresis = true;
        try { return Next(indicators, weights); }
        finally { _skipHysteresis = false; }
    }

    public void Reset()
    {
        _insufficient = false;
        _lastShown = null;
        _streak = 0;
        _current = null;
        _pending = null;
        _pendingCount = 0;
    }

    private LiveAction Choose(IReadOnlyList<Ranked<LiveAction>> ranking)
    {
        var candidate = ranking[0];

        // Çeşitlilik: aynı öneri art arda RepeatLimit pencere gösterildiyse ve birinciye yakın bir alternatif
        // varsa ona geçilir; ekran hep aynı 2–3 öneriyi döndürmesin. Kaydırıcı oynatılınca (Rerank) uygulanmaz.
        if (!_skipHysteresis && _current is { } cur && _lastShown == cur && _streak >= _cfg.RepeatLimit
            && candidate.Alternative == cur)
        {
            var alternative = ranking.Skip(1).FirstOrDefault(r => candidate.Closeness - r.Closeness <= _cfg.DiversityMargin);
            if (alternative is not null) return Commit(alternative.Alternative);
        }

        if (_skipHysteresis || _current is null || candidate.Alternative == _current)
            return Commit(candidate.Alternative);

        // Mevcut öneri "Devam et" ise sıralamada yoktur → yakınlığı 0 sayılır, aday hemen geçer.
        var currentCloseness = ranking.FirstOrDefault(r => r.Alternative == _current)?.Closeness ?? 0;
        if (candidate.Closeness - currentCloseness >= _cfg.SwitchMargin)
            return Commit(candidate.Alternative);

        if (_pending == candidate.Alternative) _pendingCount++;
        else { _pending = candidate.Alternative; _pendingCount = 1; }

        return _pendingCount >= _cfg.SwitchPersistence ? Commit(candidate.Alternative) : _current.Value;
    }

    private static bool IsPositive(LiveAction a) => a is LiveAction.BirAdimIlerlet or LiveAction.OlumluyuPekistir;

    /// <summary>İki olumlu öneri arasında dönüşümlü: biri art arda <see cref="DecisionConfig.RepeatLimit"/> pencere gösterildiyse diğeri.</summary>
    private LiveAction ChoosePositive()
    {
        var shown = _lastShown is { } l && IsPositive(l) ? l : LiveAction.BirAdimIlerlet;
        if (_lastShown == shown && _streak >= _cfg.RepeatLimit)
            return shown == LiveAction.BirAdimIlerlet ? LiveAction.OlumluyuPekistir : LiveAction.BirAdimIlerlet;
        return shown;
    }

    private LiveAction Commit(LiveAction action)
    {
        _current = action;
        _pending = null;
        _pendingCount = 0;
        return action;
    }
}
