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
    /// <summary>"Önerilen yön: Netleştirin" — kararın kategorisi.</summary>
    [ObservableProperty] private string _directionText = string.Empty;
    /// <summary>Ekrandaki "İlk 3 seçenek"; kural yolunda puansız.</summary>
    [ObservableProperty] private IReadOnlyList<TopOptionRowVm> _topOptions = Array.Empty<TopOptionRowVm>();
    [ObservableProperty] private IReadOnlyList<IndicatorBarVm> _meetingIndicators = Array.Empty<IndicatorBarVm>();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRanking))]
    private IReadOnlyList<RankingRowVm> _ranking = Array.Empty<RankingRowVm>();
    [ObservableProperty] private IReadOnlyList<SensitivityRowVm> _sensitivity = Array.Empty<SensitivityRowVm>();
    [ObservableProperty] private string _sensitivitySummary = string.Empty;

    /// <summary>Duyarlılığın tek satırlık hükmü ("Karar sağlam" …); tablo yalnız "Ayrıntı"da görünür.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SensitivityStable), nameof(SensitivityMostlyStable), nameof(SensitivityFragile))]
    private string _sensitivityVerdict = string.Empty;

    public bool SensitivityStable => SensitivityVerdict == Stable;
    public bool SensitivityMostlyStable => SensitivityVerdict == MostlyStable;
    public bool SensitivityFragile => SensitivityVerdict == Fragile;

    /// <summary>"Neden" sütununa yazılacak sorunun görüşmedeki en düşük değeri — seçeneğin hedeflediği ama
    /// görüşmede belirgin olmayan sorun yazılmaz (<see cref="DecisionText.Reason"/> ile aynı 0,2 eşiği).</summary>
    private const double WhyMinValue = 0.2;

    private const string Stable = "Karar sağlam";
    private const string MostlyStable = "Karar büyük ölçüde sağlam";
    private const string Fragile = "Karar kırılgan";

    /// <summary>Hiç değişmiyorsa sağlam, denemelerin en çok dörtte birinde değişiyorsa büyük ölçüde sağlam, fazlası kırılgan.</summary>
    public static string Verdict(int changed, int total) =>
        changed == 0 ? Stable : changed * 4 <= total ? MostlyStable : Fragile;
    [ObservableProperty] private FollowUpDecision? _current;

    public FollowUpPanelViewModel(DecisionConfig cfg)
    {
        _cfg = cfg;
        Weights = Enum.GetValues<Criterion>().Select(c => new WeightSliderVm(c, OnWeightChanged)).ToArray();
    }

    public IReadOnlyList<WeightSliderVm> Weights { get; }

    /// <summary>Kural yolunda (sakin görüşme vb.) sıralama boştur. XAML dizinin <c>Count</c>'una bağlanamaz
    /// (dizide açık arayüz üyesi) — görünürlük buradan okunur.</summary>
    public bool HasRanking => Ranking.Count > 0;

    public IReadOnlyList<double> CurrentWeights => Weights.Select(w => w.Value).ToArray();

    public const string SilentCounterpartText = "Karşı taraf konuşmadı — takip önerisi üretilmedi.";

    public void Load(DecisionSession session)
    {
        _session = session;
        // Karşı tarafın sustuğu pencereler takip kararına girmez; hepsi sessizse karar yok (sessizlik nötr sayılmaz).
        var spoken = session.Windows.Where(w => !w.CounterpartSilent).ToArray();
        HasDecision = spoken.Length > 0;
        if (!HasDecision)
        {
            _meeting = null;
            Current = null;
            BestText = ReasonText = ProfileText = SensitivitySummary = SensitivityVerdict = DirectionText = string.Empty;
            TopOptions = Array.Empty<TopOptionRowVm>();
            MeetingIndicators = Array.Empty<IndicatorBarVm>();
            Ranking = Array.Empty<RankingRowVm>();
            Sensitivity = Array.Empty<SensitivityRowVm>();
            if (session.Windows.Count > 0) DirectionText = SilentCounterpartText;
            return;
        }

        _meeting = FollowUpAdvisor.AggregateMeeting(spoken.Select(w => w.Indicators).ToArray(), _cfg);
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
        DirectionText = $"Önerilen yön: {DecisionText.Name(decision.Direction)}";
        TopOptions = decision.Ranking.Count > 0
            ? decision.Ranking.Take(decision.Options.Count)
                .Select((r, i) => new TopOptionRowVm($"{i + 1}. {DecisionText.Name(r.Alternative)}",
                    $"%{Math.Round(r.Closeness * 100, MidpointRounding.AwayFromZero)}",
                    string.Join(", ", r.TopCriteria
                        .Where(c => meeting.Values[(int)c] >= WhyMinValue).Select(DecisionText.Name))))
                .ToArray()
            : decision.Options
                .Select((a, i) => new TopOptionRowVm($"{i + 1}. {DecisionText.Name(a)}", string.Empty, string.Empty))
                .ToArray();
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
        SensitivityVerdict = Verdict(changed, rows.Count);
        if (decision.Ranking.Count == 0)
        {
            // Kural yolu (sakin görüşme, düşük veri kalitesi): TOPSIS hiç çalışmadı, ağırlıklar kullanılmadı.
            SensitivitySummary = "Bu öneri önem ayarlarına bağlı değil, görüşmenin genel durumundan geliyor (gerekçeye bakın).";
            return;
        }
        const string tried = "Önem ayarlarını biraz artırıp azalttığımız";
        if (changed == 0)
        {
            SensitivitySummary = $"{tried} {rows.Count} denemenin hepsinde aynı öneri çıktı.";
            return;
        }
        var most = rows.Where(r => r.Changed).GroupBy(r => r.Criterion)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        SensitivitySummary = $"{tried} {rows.Count} denemenin {changed} tanesinde öneri değişti; en çok \"{DecisionText.Name(most)}\" ayarına bağlı.";
    }
}

/// <summary>Duyarlılık tablosunun bir satırı: kriter ve −%20, −%10, +%10, +%20 hücreleri.</summary>
public sealed record SensitivityRowVm(string Criterion, IReadOnlyList<SensitivityCellVm> Cells);

/// <summary><paramref name="Text"/>: değişmediyse "aynı", değiştiyse yeni en iyi seçeneğin adı.</summary>
public sealed record SensitivityCellVm(string Text, bool Changed);
