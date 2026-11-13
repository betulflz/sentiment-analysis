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
    private readonly TalkMeter _talk;
    private LiveAdvisor _advisor;
    private bool _applyingProfile;

    [ObservableProperty] private string _actionText = Dash;

    /// <summary>12: son dakikanın konuşma payı ("Siz %62 · Karşı taraf %38"); bilinmiyorsa boş.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasTalkShare))] private string _talkShareText = string.Empty;

    /// <summary>12: yönetenin konuşma payı (0..1) — çubuk için.</summary>
    [ObservableProperty] private double _hostShare;

    public bool HasTalkShare => TalkShareText.Length > 0;
    [ObservableProperty] private string _reasonText = string.Empty;

    /// <summary>"Neden bu öneri?": en çok sürükleyen sorunun açıklaması ve eylemin o soruna etkisi (bilgi tabanı).
    /// Öneri kara kutu olmasın: hangi ölçüm, hangi eylem, ne kadar yardım. Sorun yoksa boş.</summary>
    [ObservableProperty] private string _whyText = string.Empty;
    [ObservableProperty] private string _confidenceText = string.Empty;
    [ObservableProperty] private string _alternativeText = string.Empty;
    [ObservableProperty] private bool _isLowConfidence;
    [ObservableProperty] private IReadOnlyList<IndicatorBarVm> _indicators = EmptyBars();

    /// <summary>Son pencerenin ilk 3 eylemi (TOPSIS yakınlığı). Kart "Devam et" derken de hesaplanır —
    /// müdahale eşiğinin altında kaydırıcıların etkisi böylece görünür kalır. Veri yetersizse boş.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRanking))]
    private IReadOnlyList<RankingRowVm> _ranking = Array.Empty<RankingRowVm>();

    public bool HasRanking => Ranking.Count > 0;

    /// <summary>Koşu sırasında true: profil görüşme başında seçilir (spec §2), kaydırıcılar serbesttir.</summary>
    [ObservableProperty] private bool _isLocked;

    public DecisionPanelViewModel(DecisionConfig cfg)
    {
        _cfg = cfg;
        _talk = new TalkMeter(cfg);
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

    /// <summary>Son pencerenin konuşma payı (12); ses tanıtılmadıysa <see cref="TalkSnapshot.Unknown"/>.</summary>
    public TalkSnapshot LastTalk { get; private set; } = TalkSnapshot.Unknown;

    public IReadOnlyList<double> CurrentWeights => Weights.Select(w => w.Value).ToArray();

    [RelayCommand]
    private void ResetWeights() => ApplyProfileWeights();

    /// <summary>Yeni oturum: geçmiş, eğilim serisi, kart ve histerezis durumu sıfırlanır.</summary>
    public void Start()
    {
        _history.Clear();
        _valences.Clear();
        _talk.Reset();
        LastTalk = TalkSnapshot.Unknown;
        TalkShareText = string.Empty;
        HostShare = 0;
        _advisor = new LiveAdvisor(SelectedProfile, _cfg);
        Faces.Clear();
        Indicators = EmptyBars();
        ActionText = Dash;
        ReasonText = ConfidenceText = AlternativeText = WhyText = string.Empty;
        IsLowConfidence = false;
        Ranking = Array.Empty<RankingRowVm>();
        foreach (var w in Weights) w.IdleNote = string.Empty;
    }

    /// <summary>Bitmiş görüşmenin rapora (11e) giden kopyası: sonraki <see cref="Start"/> geçmişi temizlese de
    /// rapor eski görüşmeyi göstermeye devam eder.</summary>
    public DecisionSession Snapshot() =>
        new(SelectedProfile, DecisionKnowledgeBase.DefaultWeights(SelectedProfile), CurrentWeights, _history.ToArray());

    public void OnWindow(ChunkResult chunk, IReadOnlyList<FaceWindowSummary> faces)
    {
        var talk = _talk.Add(chunk);
        LastTalk = talk;
        if (talk.HostShare is double hs)
        {
            HostShare = hs;
            int pct = (int)Math.Round(100 * hs, MidpointRounding.AwayFromZero);
            TalkShareText = $"Siz %{pct} · Karşı taraf %{100 - pct}";
        }
        else
        {
            // Son dakikada yeterli konuşma yok (ya da rol yok): eski pay ekranda asılı kalmasın.
            HostShare = 0;
            TalkShareText = string.Empty;
        }
        var indicators = IndicatorCalculator.Compute(chunk, faces, _valences, _cfg, talk);
        if (IndicatorCalculator.TrendValence(chunk) is double tv) _valences.Add(tv);
        var rec = _advisor.Next(indicators, CurrentWeights);
        _history.Add(new DecisionWindow(chunk.TimeStart, chunk.TimeEnd, indicators, rec) { Faces = faces.ToArray() });

        Faces.Clear();
        foreach (var f in faces.OrderBy(f => f.FaceIndex))
            Faces.Add(new FaceBarsVm(DecisionText.Subject(f.FaceIndex), f.LookAwayRatio, f.NegativeExpression, f.SmileRatio));
        Indicators = Enum.GetValues<Criterion>()
            .Select(c => new IndicatorBarVm(Capitalize(DecisionText.Name(c)), indicators.Values[(int)c],
                indicators.SourceFace[(int)c] is int i ? DecisionText.Subject(i) : string.Empty))
            .ToArray();
        Show(rec);
        UpdateRanking();
    }

    private void OnWeightChanged()
    {
        foreach (var w in Weights) w.UpdateShare(Weights);
        if (_applyingProfile || _history.Count == 0) return;
        var last = _history[^1];
        var rec = _advisor.Rerank(last.Indicators, CurrentWeights);
        // Geçmiş yalnızca koşu sırasında güncellenir: Durdur sonrası profil/kaydırıcı oynatmak bitmiş
        // görüşmede hiç gösterilmemiş bir öneriyi rapora (11e) yazmasın — kart yine "ya şöyle olsaydı"yı gösterir.
        if (IsLocked) _history[^1] = last with { Recommendation = rec };
        Show(rec);
        UpdateRanking();
    }

    private void UpdateRanking()
    {
        var ind = _history[^1].Indicators;
        if (ind.DataQuality < _cfg.InsufficientQuality)
        {
            Ranking = Array.Empty<RankingRowVm>();
            foreach (var w in Weights) w.IdleNote = string.Empty;
            return;
        }
        var needs = ind.Values.Select(v => v * ind.DataQuality).ToArray();
        // İhtiyaç-ağırlıklı TOPSIS'te (w·n) ihtiyacı 0 olan kriterin ağırlığı sıralamayı etkilemez;
        // tüm ihtiyaçlar 0 ise motor saf ağırlıklara, tüm ağırlıklar 0 ise eşit ağırlığa döner — ikisinde de hepsi etkilidir.
        bool anyNeed = needs.Any(n => n > 0) && Weights.Sum(w => Math.Max(0, w.Value)) > 0;
        foreach (var w in Weights)
            w.IdleNote = anyNeed && needs[(int)w.Criterion] <= 0 ? "şu an sorun yok" : string.Empty;
        Ranking = ActionRanker.Rank(DecisionKnowledgeBase.Get(SelectedProfile).Interventions,
                DecisionKnowledgeBase.LiveImpact, CurrentWeights, needs)
            .Take(3)
            .Select((r, i) => new RankingRowVm($"{i + 1}. {DecisionText.Name(r.Alternative)}", r.Closeness,
                $"%{Math.Round(r.Closeness * 100, MidpointRounding.AwayFromZero)}"))
            .ToArray();
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
        WhyText = WhyFor(r);
        ConfidenceText = $"Güven: {EmotionLocalizer.Confidence(r.Confidence)}";
        IsLowConfidence = r.Confidence == Confidence.Dusuk;
        var alternative = r.Ranking.FirstOrDefault(x => x.Alternative != r.Action);
        AlternativeText = alternative is null ? string.Empty : $"Alternatif: {DecisionText.Name(alternative.Alternative)}";
    }

    private static string WhyFor(Recommendation r)
    {
        if (r.Action is LiveAction.BirAdimIlerlet or LiveAction.OlumluyuPekistir)
            return "Neden bu öneri? Konuşma ve yüz ifadesi olumlu; bu iyi havayı bir sonraki adıma taşımak için uygun bir an.";
        if (r.Action == LiveAction.SusVeDinle && r.TopCriteria.Count == 0)
            return "Neden bu öneri? Konuşmanın çoğu sizde. Karşı taraf konuşmadıkça ne düşündüğü ölçülemez ve ilgisi dağılabilir; ona soru sorup alan açın.";
        if (r.TopCriteria.Count == 0 || !DecisionKnowledgeBase.LiveImpact.TryGetValue(r.Action, out var impact)) return string.Empty;
        var top = r.TopCriteria[0];
        return $"Neden bu öneri? {Capitalize(DecisionText.Description(top))}. "
             + $"Bu eylemin \"{DecisionText.Name(top)}\" sorununa etkisi: %{Math.Round(100 * impact[(int)top], MidpointRounding.AwayFromZero)} (uzman varsayımı).";
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

    /// <summary>Son pencerede bu kriterin ihtiyacı 0 ise söyler: kaydırıcı o an sıralamayı etkilemez.</summary>
    [ObservableProperty] private string _idleNote = string.Empty;

    public WeightSliderVm(Criterion criterion, Action changed)
    {
        Criterion = criterion;
        Name = DecisionPanelViewModel.Capitalize(DecisionText.Name(criterion));
        Description = DecisionText.Description(criterion);
        _changed = changed;
    }

    public Criterion Criterion { get; }
    public string Name { get; }
    public string Description { get; }

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

/// <summary>Eylem sıralamasında bir satır: "1. Empati kur", yakınlık (0..1) ve yüzde metni.</summary>
public sealed record RankingRowVm(string Label, double Closeness, string PercentText);

/// <summary>Takip kartındaki "İlk 3 seçenek" satırı; kural yolunda <paramref name="PercentText"/> ve
/// <paramref name="WhyText"/> boştur.</summary>
public sealed record TopOptionRowVm(string Label, string PercentText, string WhyText);

/// <summary>Bir yüzün son penceredeki çubukları (0..1).</summary>
public sealed record FaceBarsVm(string Label, double LookAway, double Negative, double Smile);
