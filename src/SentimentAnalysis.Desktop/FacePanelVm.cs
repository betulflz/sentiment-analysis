using CommunityToolkit.Mvvm.ComponentModel;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Mülakat/toplantıda bir yüzün paneli: kırpılacak kutu, kullanıcının verdiği ad, o anki duygu
/// ve üç gösterge (başını çevirme, olumsuz ifade, gülümseme). Yüz numarası kişi değil sıradır; biri çıkıp
/// girerse numara karışabilir — ad kutusu bunu kullanıcıya düzeltme imkânı verir.</summary>
public sealed partial class FacePanelVm : ObservableObject
{
    private const double PositiveShift = 0.15, NegativeShift = -0.15, SmileShare = 0.3, NegativeShare = 0.4;

    public FacePanelVm(int index)
    {
        Index = index;
        _name = DefaultName;
    }

    public int Index { get; }

    public string DefaultName => $"Yüz {Index + 1}";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayName))] private string _name;

    /// <summary>Boş bırakılan ad varsayılan etikete döner.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? DefaultName : Name.Trim();

    [ObservableProperty] private FaceBoxVm _box = new(0, 0, 0, 0, string.Empty);
    [ObservableProperty] private string _emotion = "—";
    [ObservableProperty] private double _lookAway;
    [ObservableProperty] private double _negative;
    [ObservableProperty] private double _smile;

    public void Apply(FaceWindowSummary s)
    {
        LookAway = s.LookAwayRatio;
        Negative = s.NegativeExpression;
        Smile = s.SmileRatio;
        Emotion = EmotionText(s);
    }

    /// <summary>Pencere özetinden günlük dilde duygu: olumlu / olumsuz / karışık / sakin.</summary>
    public static string EmotionText(FaceWindowSummary s)
    {
        bool positive = s.ValenceShift > PositiveShift || s.SmileRatio >= SmileShare;
        bool negative = s.ValenceShift < NegativeShift || s.NegativeExpression >= NegativeShare;
        return positive && negative ? "Karışık" : positive ? "Olumlu" : negative ? "Olumsuz" : "Sakin";
    }
}
