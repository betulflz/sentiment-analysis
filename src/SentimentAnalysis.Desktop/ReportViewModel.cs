using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Zaman-payı satırı: bir duygunun oturum boyunca kapladığı süre oranı.</summary>
/// <remarks>08d planındaki <see cref="EmotionShareVm"/> (yakınlık payı yüzdesi) ile karışmasın diye
/// bu tip <c>TimeShareVm</c> olarak adlandırıldı.</remarks>
public sealed class TimeShareVm
{
    public string Emotion { get; }
    public double Share { get; }
    public TimeShareVm(Emotion e, double share) { Emotion = EmotionLocalizer.Emotion(e); Share = share; }
}

/// <summary>Toplantı-sonu rapor VM'i: bir oturumu <see cref="IMeetingAggregator"/> ile özetler, lokalize sunar.</summary>
public sealed partial class ReportViewModel : ObservableObject
{
    private readonly IMeetingAggregator _aggregator;

    [ObservableProperty] private bool _hasReport;
    [ObservableProperty] private string _dominantEmotion = "—";
    [ObservableProperty] private string _turkishSummary = string.Empty;
    [ObservableProperty] private double _valenceTrend;
    [ObservableProperty] private double _volatility;
    [ObservableProperty] private double _averageCongruence;

    public IReadOnlyList<TimeShareVm> TimeShare { get; private set; } = System.Array.Empty<TimeShareVm>();
    public IReadOnlyList<ChunkRowVm> PeakMoments { get; private set; } = System.Array.Empty<ChunkRowVm>();

    public ReportViewModel(IMeetingAggregator aggregator) => _aggregator = aggregator;

    public void Load(MeetingSession session)
    {
        var s = _aggregator.Aggregate(session);
        DominantEmotion = EmotionLocalizer.Emotion(s.DominantEmotion);
        TurkishSummary = s.TurkishSummary;
        ValenceTrend = s.ValenceTrend;
        Volatility = s.EmotionalVolatility;
        AverageCongruence = s.AverageCongruence;
        TimeShare = s.TimeShareByEmotion.Select(kv => new TimeShareVm(kv.Key, kv.Value)).ToList();
        PeakMoments = s.PeakMoments.Select(c => new ChunkRowVm(c)).ToList();
        HasReport = true;
    }

    /// <summary>Zaten üretilmiş bir <see cref="MeetingSummary"/>'yi (ör. dosya-analizi koşusundan) yeniden-agregasyon
    /// yapmadan lokalize gösterir.</summary>
    public void LoadSummary(MeetingSummary s)
    {
        DominantEmotion = EmotionLocalizer.Emotion(s.DominantEmotion);
        TurkishSummary = s.TurkishSummary;
        ValenceTrend = s.ValenceTrend;
        Volatility = s.EmotionalVolatility;
        AverageCongruence = s.AverageCongruence;
        TimeShare = s.TimeShareByEmotion.Select(kv => new TimeShareVm(kv.Key, kv.Value)).ToList();
        PeakMoments = s.PeakMoments.Select(c => new ChunkRowVm(c)).ToList();
        HasReport = true;
    }
}
