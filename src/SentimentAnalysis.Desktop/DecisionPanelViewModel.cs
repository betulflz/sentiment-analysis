using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Decision;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Canlı ekranın karar paneli (spec 11 §6.5): profil, ağırlık kaydırıcıları, öneri kartı ve
/// gösterge çubukları. Her analiz penceresi <see cref="OnWindow"/> ile gelir → <see cref="IndicatorCalculator"/>
/// → <see cref="LiveAdvisor"/>. Kaydırıcı değişince son pencere histerezissiz yeniden sıralanır.
/// UI-agnostik (headless test).</summary>
public sealed partial class DecisionPanelViewModel : ObservableObject
{
    private const string Dash = "—";

    private readonly DecisionConfig _cfg;
    private readonly List<DecisionWindow> _history = new();
    private readonly List<double> _valences = new();
    private LiveAdvisor _advisor;
    private bool _applyingProfile;

    [ObservableProperty] private string _actionText = Dash;
    [ObservableProperty] private string _reasonText = string.Empty;
    [ObservableProperty] private string _confidenceText = string.Empty;
    [ObservableProperty] private string _alternativeText = string.Empty;
    [ObservableProperty] private bool _isLowConfidence;
    [ObservableProperty] private IReadOnlyList<IndicatorBarVm> _indicators = EmptyBars();

    /// <summary>Koşu sırasında true: profil görüşme başında seçilir (spec §2), kaydırıcılar serbesttir.</summary>
    [ObservableProperty] private bool _isLocked;

    public DecisionPanelViewModel(DecisionConfig cfg)
    {
        _cfg = cfg;
        _advisor = new LiveAdvisor(_selectedProfile, cfg);
        Weights = Enum.GetValues<Criterion>().Select(c => new WeightSliderVm(c, OnWeightChanged)).ToArray();
        ApplyProfileWeights();
    }

    public IReadOnlyList<MeetingProfile> Profiles { get; } = Enum.GetValues<MeetingProfile>();

    private MeetingProfile _selectedProfile = MeetingProfile.Satis;

    /// <summary>Değişince kaydırıcılar profilin AHP ağırlıklarına döner. Kilitliyken yok sayılır.</summary>
    public MeetingProfile SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (IsLocked || value == _selectedProfile) return;
            _selectedProfile = value;
            OnPropertyChanged();
            _advisor = new LiveAdvisor(value, _cfg);
            ApplyProfileWeights();
        }
    }

    public IReadOnlyList<WeightSliderVm> Weights { get; }

    public ObservableCollection<FaceBarsVm> Faces { get; } = new();

    public IReadOnlyList<DecisionWindow> History => _history;

    public IReadOnlyList<double> CurrentWeights => Weights.Select(w => w.Value).ToArray();

    [RelayCommand]
    private void ResetWeights() => ApplyProfileWeights();

    /// <summary>Yeni oturum: geçmiş, eğilim serisi, kart ve histerezis durumu sıfırlanır.</summary>
    public void Start()
    {
        _history.Clear();
        _valences.Clear();
        _advisor = new LiveAdvisor(SelectedProfile, _cfg);
        Faces.Clear();
        Indicators = EmptyBars();
        ActionText = Dash;
        ReasonText = ConfidenceText = AlternativeText = string.Empty;
        IsLowConfidence = false;
    }

    public void OnWindow(ChunkResult chunk, IReadOnlyList<FaceWindowSummary> faces)
    {
        var indicators = IndicatorCalculator.Compute(chunk, faces, _valences, _cfg);
        _valences.Add(chunk.Fused.Combined.Valence);
        var rec = _advisor.Next(indicators, CurrentWeights);
        _history.Add(new DecisionWindow(chunk.TimeStart, chunk.TimeEnd, indicators, rec));

        Faces.Clear();
        foreach (var f in faces.OrderBy(f => f.FaceIndex))
            Faces.Add(new FaceBarsVm(DecisionText.Subject(f.FaceIndex), f.LookAwayRatio, f.NegativeExpression, f.SmileRatio));
        Indicators = Enum.GetValues<Criterion>()
            .Select(c => new IndicatorBarVm(Capitalize(DecisionText.Name(c)), indicators.Values[(int)c],
                indicators.SourceFace[(int)c] is int i ? DecisionText.Subject(i) : string.Empty))
            .ToArray();
        Show(rec);
    }

    private void OnWeightChanged()
    {
        foreach (var w in Weights) w.UpdateShare(Weights);
        if (_applyingProfile || _history.Count == 0) return;
        var last = _history[^1];
        var rec = _advisor.Rerank(last.Indicators, CurrentWeights);
        _history[^1] = last with { Recommendation = rec };
        Show(rec);
    }

    private void ApplyProfileWeights()
    {
        var defaults = DecisionKnowledgeBase.DefaultWeights(SelectedProfile);
        _applyingProfile = true;
        try
        {
            foreach (var w in Weights) w.Value = defaults[(int)w.Criterion];
        }
        finally { _applyingProfile = false; }
        OnWeightChanged();
    }

    private void Show(Recommendation r)
    {
        ActionText = DecisionText.Name(r.Action);
        ReasonText = r.Reason;
        ConfidenceText = $"Güven: {EmotionLocalizer.Confidence(r.Confidence)}";
        IsLowConfidence = r.Confidence == Confidence.Dusuk;
        var alternative = r.Ranking.FirstOrDefault(x => x.Alternative != r.Action);
        AlternativeText = alternative is null ? string.Empty : $"Alternatif: {DecisionText.Name(alternative.Alternative)}";
    }

    private static IReadOnlyList<IndicatorBarVm> EmptyBars() =>
        Enum.GetValues<Criterion>().Select(c => new IndicatorBarVm(Capitalize(DecisionText.Name(c)), 0, string.Empty)).ToArray();

    internal static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpper(s[0], new System.Globalization.CultureInfo("tr-TR")) + s[1..];
}

/// <summary>Bir kriterin ağırlık kaydırıcısı. <see cref="Value"/> 0..1 (normalize edilmemiş);
/// <see cref="ShareText"/> normalize edilmiş payıdır (hepsi 0 ise eşit pay — motor da öyle davranır).</summary>
public sealed partial class WeightSliderVm : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty] private double _value;
    [ObservableProperty] private string _shareText = string.Empty;

    public WeightSliderVm(Criterion criterion, Action changed)
    {
        Criterion = criterion;
        Name = DecisionPanelViewModel.Capitalize(DecisionText.Name(criterion));
        _changed = changed;
    }

    public Criterion Criterion { get; }
    public string Name { get; }

    partial void OnValueChanged(double value) => _changed();

    internal void UpdateShare(IReadOnlyList<WeightSliderVm> all)
    {
        double sum = all.Sum(w => Math.Max(0, w.Value));
        double share = sum <= 0 ? 1.0 / all.Count : Math.Max(0, Value) / sum;
        ShareText = $"%{Math.Round(share * 100, MidpointRounding.AwayFromZero)}";
    }
}

/// <summary>Bir gösterge çubuğu. <paramref name="Subject"/>: değeri sürükleyen yüz ("Yüz 2") ya da boş.</summary>
public sealed record IndicatorBarVm(string Name, double Value, string Subject);

/// <summary>Bir yüzün son penceredeki çubukları (0..1).</summary>
public sealed record FaceBarsVm(string Label, double LookAway, double Negative, double Smile);
