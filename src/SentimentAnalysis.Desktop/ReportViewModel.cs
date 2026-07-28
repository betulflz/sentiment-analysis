using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Diarization;
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

/// <summary>Konuşmacı etiketli transkript satırı (10b): "Konuşmacı A: merhaba nasılsın".</summary>
public sealed class SpeakerLineVm
{
    public string Label { get; }
    public string Text { get; }
    public string TimeRange { get; }

    public SpeakerLineVm(SpeakerLine line, string? userGivenName = null)
    {
        Label = userGivenName ?? line.Speaker.DefaultLabel;
        Text = line.Text;
        TimeRange = $"{line.Start:mm\\:ss}–{line.End:mm\\:ss}";
    }
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

    /// <summary>Konuşmacı ayrımının kullanıcıya görünen durumu (10b). Boş = özellik kapalı
    /// veya hiç denenmedi → UI şeridi görünmez. 08e dersi: kullanıcı ne gördüğünü ve neye
    /// güvenebileceğini bilmelidir.</summary>
    [ObservableProperty] private string _diarizationStatus = string.Empty;

    /// <summary>Gösterilecek konuşmacı satırı var mı? Yoksa rapor bugünkü düz transkripti
    /// gösterir (davranış değişmez).</summary>
    [ObservableProperty] private bool _hasSpeakers;

    /// <summary>Konuşmacı etiketli transkript. Diarization çalışmadıysa BOŞ kalır —
    /// uydurma etiket üretilmez (master §12.2).</summary>
    public IReadOnlyList<SpeakerLineVm> SpeakerLines { get; private set; } = System.Array.Empty<SpeakerLineVm>();

    public ReportViewModel(IMeetingAggregator aggregator) => _aggregator = aggregator;

    /// <summary>Arka plan geçişi başladı.</summary>
    public void SetDiarizationRunning() => DiarizationStatus = "Konuşmacılar hesaplanıyor…";

    /// <summary>Arka plan geçişinin sonucunu kullanıcıya dürüstçe yansıtır.</summary>
    public void SetDiarizationOutcome(DiarizationOutcome outcome)
    {
        DiarizationStatus = outcome switch
        {
            DiarizationOutcome.Completed when SpeakerLines.Count > 0 => "Konuşmacılar hazır.",
            // Geçiş çalıştı ama kimseyi ayırt edemedi — "başarısız" DEMEYİZ, çünkü doğru
            // cevap "tek kişi konuşuyordu" da olabilir (bkz. DiarizationJob).
            DiarizationOutcome.Completed => "Konuşmacı ayırt edilemedi; transkript konuşmacısız gösteriliyor.",
            DiarizationOutcome.NoAudio => "Konuşmacı ayrımı yapılamadı: bu oturumun sesi saklanmamış.",
            DiarizationOutcome.Failed => "Konuşmacı ayrımı başarısız oldu; transkript konuşmacısız gösteriliyor.",
            _ => string.Empty,
        };

        if (outcome != DiarizationOutcome.Completed)
        {
            SpeakerLines = System.Array.Empty<SpeakerLineVm>();
            HasSpeakers = false;
            OnPropertyChanged(nameof(SpeakerLines));
        }
    }

    /// <summary>Zaman damgalı transkripti konuşmacı bölgeleriyle hizalayıp satırlara indirger.
    /// Hizalama ve birleştirme saf Application koduna (test edilebilir) devredilir; burada
    /// yalnızca sunum yapılır.</summary>
    public void ApplySpeakers(
        IReadOnlyList<TranscriptSegment> transcript,
        IReadOnlyList<SpeakerSegment> speakers,
        double minOverlapRatio)
    {
        var aligned = SpeakerAligner.Align(transcript, speakers, minOverlapRatio);
        SpeakerLines = SpeakerLineBuilder.Build(aligned).Select(l => new SpeakerLineVm(l)).ToList();
        HasSpeakers = SpeakerLines.Count > 0;
        OnPropertyChanged(nameof(SpeakerLines));
    }

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
