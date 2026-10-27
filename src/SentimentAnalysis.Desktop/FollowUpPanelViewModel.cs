using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Decision;
using SentimentAnalysis.Core;
using SensitivityAnalysis = SentimentAnalysis.Application.Decision.Sensitivity;

namespace SentimentAnalysis.Desktop;

/// <summary>Raporun takip kararı paneli (spec 11 §6.6): görüşme düzeyi göstergeler üzerinden
/// <see cref="FollowUpAdvisor"/> + duyarlılık analizi. Kaydırıcılar görüşme sonundaki değerlerle başlar;
/// değişince karar, sıralama ve duyarlılık tablosu anında yeniden hesaplanır ("ya şöyle olsaydı" —
/// saklanmaz). UI-agnostik (headless test).</summary>
public sealed partial class FollowUpPanelViewModel : ObservableObject
{
    private static readonly System.Globalization.CultureInfo Tr = new("tr-TR");

    private readonly DecisionConfig _cfg;
    private DecisionSession? _session;
    private Indicators? _meeting;
    private bool _loading;

    [ObservableProperty] private bool _hasDecision;
    [ObservableProperty] private string _profileText = string.Empty;
    [ObservableProperty] private string _bestText = string.Empty;
    [ObservableProperty] private string _reasonText = string.Empty;
    [ObservableProperty] private IReadOnlyList<IndicatorBarVm> _meetingIndicators = Array.Empty<IndicatorBarVm>();
    [ObservableProperty] private IReadOnlyList<RankingRowVm> _ranking = Array.Empty<RankingRowVm>();
    [ObservableProperty] private IReadOnlyList<SensitivityRowVm> _sensitivity = Array.Empty<SensitivityRowVm>();
    [ObservableProperty] private string _sensitivitySummary = string.Empty;
    [ObservableProperty] private FollowUpDecision? _current;

    public FollowUpPanelViewModel(DecisionConfig cfg)
    {
        _cfg = cfg;
        Weights = Enum.GetValues<Criterion>().Select(c => new WeightSliderVm(c, OnWeightChanged)).ToArray();
    }

    public IReadOnlyList<WeightSliderVm> Weights { get; }

    public IReadOnlyList<double> CurrentWeights => Weights.Select(w => w.Value).ToArray();

    public void Load(DecisionSession session)
    {
        _session = session;
        HasDecision = session.Windows.Count > 0;
        if (!HasDecision)
        {
            _meeting = null;
            Current = null;
            BestText = ReasonText = ProfileText = SensitivitySummary = string.Empty;
            MeetingIndicators = Array.Empty<IndicatorBarVm>();
            Ranking = Array.Empty<RankingRowVm>();
            Sensitivity = Array.Empty<SensitivityRowVm>();
            return;
        }

        _meeting = FollowUpAdvisor.AggregateMeeting(session.Windows.Select(w => w.Indicators).ToArray(), _cfg);
        var cr = Ahp.Compute(DecisionKnowledgeBase.Get(session.Profile).Pairwise).ConsistencyRatio;
        ProfileText = $"Profil: {DecisionText.Name(session.Profile)} · AHP tutarlılık oranı CR = {cr.ToString("0.00", Tr)}";
        MeetingIndicators = Enum.GetValues<Criterion>()
            .Select(c => new IndicatorBarVm(DecisionPanelViewModel.Capitalize(DecisionText.Name(c)), _meeting.Values[(int)c],
                _meeting.SourceFace[(int)c] is int i ? DecisionText.Subject(i) : string.Empty))
            .ToArray();
        ApplyWeights(session.FinalWeights);
    }

    [RelayCommand]
    private void ResetWeights()
    {
        if (_session is not null) ApplyWeights(_session.FinalWeights);
    }

    private void ApplyWeights(IReadOnlyList<double> weights)
    {
        _loading = true;
        try
        {
            foreach (var w in Weights) w.Value = weights[(int)w.Criterion];
        }
        finally { _loading = false; }
        OnWeightChanged();
    }

    private void OnWeightChanged()
    {
        foreach (var w in Weights) w.UpdateShare(Weights);
        if (_loading || _session is null || _meeting is null) return;

        var profile = _session.Profile;
        var meeting = _meeting;
        var weights = CurrentWeights;
        var decision = FollowUpAdvisor.Decide(profile, meeting, weights, _cfg);
        Current = decision;
        BestText = DecisionText.Name(decision.Best);
        ReasonText = decision.Reason;
        Ranking = decision.Ranking
            .Select((r, i) => new RankingRowVm($"{i + 1}. {DecisionText.Name(r.Alternative)}", r.Closeness,
                $"%{Math.Round(r.Closeness * 100, MidpointRounding.AwayFromZero)}"))
            .ToArray();

        var rows = SensitivityAnalysis.Analyze(weights, w => FollowUpAdvisor.Decide(profile, meeting, w, _cfg).Best);
        Sensitivity = rows
            .GroupBy(r => r.Criterion)
            .Select(g => new SensitivityRowVm(DecisionPanelViewModel.Capitalize(DecisionText.Name(g.Key)),
                g.Select(r => new SensitivityCellVm(r.Changed ? DecisionText.Name(r.Best) : "aynı", r.Changed)).ToArray()))
            .ToArray();

        int changed = rows.Count(r => r.Changed);
        if (changed == 0)
        {
            SensitivitySummary = $"{rows.Count} senaryonun hepsinde en iyi seçenek aynı kaldı — karar ağırlıklara duyarlı değil.";
            return;
        }
        var most = rows.Where(r => r.Changed).GroupBy(r => r.Criterion)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        SensitivitySummary = $"{rows.Count} senaryonun {changed} tanesinde en iyi seçenek değişti — karar en çok {DecisionText.Name(most)} ağırlığına duyarlı.";
    }
}

/// <summary>Duyarlılık tablosunun bir satırı: kriter ve −%20, −%10, +%10, +%20 hücreleri.</summary>
public sealed record SensitivityRowVm(string Criterion, IReadOnlyList<SensitivityCellVm> Cells);

/// <summary><paramref name="Text"/>: değişmediyse "aynı", değiştiyse yeni en iyi seçeneğin adı.</summary>
public sealed record SensitivityCellVm(string Text, bool Changed);
