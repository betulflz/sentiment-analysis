using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Decision;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Zaman-payı satırı: bir duygunun oturum boyunca kapladığı süre oranı.</summary>
/// <remarks>08d planındaki <see cref="EmotionShareVm"/> (yakınlık payı yüzdesi) ile karışmasın diye
/// bu tip <c>TimeShareVm</c> olarak adlandırıldı.</remarks>
public sealed class TimeShareVm
{
    public Emotion Key { get; }
    public string Emotion { get; }
    public double Share { get; }

    /// <summary>"%42 · yaklaşık 1 dk 53 sn"; toplam süre bilinmiyorsa yalnız yüzde. Süre paydan
    /// türetildiği için "yaklaşık" denir.</summary>
    public string Text { get; }

    public TimeShareVm(Emotion e, double share, System.TimeSpan total = default)
    {
        Key = e;
        Emotion = EmotionLocalizer.Emotion(e);
        Share = share;
        var pct = $"%{System.Math.Round(100 * share, System.MidpointRounding.AwayFromZero)}";
        Text = total > System.TimeSpan.Zero ? $"{pct} · yaklaşık {ReportViewModel.Duration(total * share)}" : pct;
    }
}

/// <summary>Konuşmacı etiketli transkript satırı (10b): "Konuşmacı A: merhaba nasılsın".</summary>
public sealed class SpeakerLineVm
{
    public string Label { get; }

    /// <summary>Renk seçimi için konuşmacı sırası (−1 = bilinmeyen) ve rozet harfi ("A", "?").</summary>
    public int SpeakerIndex { get; }
    public string Initial { get; }

    /// <summary>Sohbet görünümü: tek sıradaki konuşmacılar (B, D…) sağda.</summary>
    public bool IsRight => SpeakerIndex % 2 == 1;
    public string Text { get; }
    public string TimeRange { get; }

    /// <summary>Atama eşik-altı örtüşmeye dayanıyor: UI bunu soluk renk + "?" rozetiyle
    /// gösterir. Gizlenirse sistem emin olmadığı yerde emin görünür.</summary>
    public bool Uncertain { get; }

    public SpeakerLineVm(SpeakerLine line, string? userGivenName = null)
    {
        Label = userGivenName ?? line.Speaker.DefaultLabel;
        SpeakerIndex = line.Speaker.Index;
        Initial = SpeakerBadge.Initial(line.Speaker);
        Text = line.Text;
        TimeRange = $"{line.Start:mm\\:ss}–{line.End:mm\\:ss}";
        Uncertain = line.Uncertain;
    }
}

/// <summary>Konuşmacı-başına duygu özeti (10c): metin/ses/nihai üç sütun, "Sözlere göre" /
/// "Ses tonuna göre" / "Genel sonuç" panelleriyle aynı görünüm modeli (<see cref="SourceAnalysisVm"/>)
/// yeniden kullanılır. Üç panel de <c>neutralRadius</c>'u canlı ekrandan alır — aksi halde aynı
/// VA noktası rapor ile canlı ekranda farklı duygu etiketiyle görünürdü.
/// <para><b>"Genel sonuç" neden ham prozodi DEĞİL:</b> <c>s.Combined</c>, <c>EmotionFusion</c>'ın
/// ürettiği füzyon sonucudur ve ses güvenilirliği düşükken (düşük SNR, 3 sn altı süre, sessizlik)
/// bilinçli olarak nötr merkeze çekilir — "bu sese güvenmiyorum" demenin yoludur (düzeltme turu
/// 1 / bulgu 1). "Genel sonuç" panelini ölçülmüş ham prozodiyle (<c>audioVa</c>) beslemek bu
/// korumayı atlayıp güvenilmez seste uydurma bir sonuç gösterirdi; ayrıca canlı ekran
/// <c>Fused.Combined</c> gösterdiği için rapor başka şey gösterirse iki yüzey ayrışırdı. "Ses
/// Analizi" paneli ise KASITLI olarak ham ölçülmüş prozodiyi (<c>audioVa</c>) göstermeye devam
/// eder — 10d'nin getirdiği iyileştirme (kişinin KENDİ sesinden ölçüm) tam burada görünür
/// olmalı.</para></summary>
public sealed class SpeakerEmotionVm
{
    public string Label { get; }
    public int SpeakerIndex { get; }
    public string Initial { get; }

    /// <summary>Karşı tarafa odak: dosya modunda görüşmeyi yöneten kişinin kartı — duygu gösterilmez, yalnız davranış.</summary>
    public bool IsHost { get; init; }
    public string HostBehaviorText { get; init; } = string.Empty;
    public bool ShowEmotion => !IsHost;

    /// <summary>Kartın başlığındaki tek satır: "Genel olarak: sakin" (füzyon sonucu).</summary>
    public string OverallText { get; }
    public SourceAnalysisVm TextAnalysis { get; }
    public SourceAnalysisVm AudioAnalysis { get; }
    public SourceAnalysisVm CombinedAnalysis { get; }

    /// <summary>Ses ölçümünün kaç saniyelik konuşmaya dayandığı (10d). 0 = konuşmacının
    /// prozodi için yeterli sesi yoktu; UI bunu göstermelidir.</summary>
    public double AnalyzedSeconds { get; }

    /// <summary>Ses ölçümü gerçekten yapıldı mı? UI bu bayrağa bağlanır (XAML'de
    /// "sayı > 0" karşılaştırması yapılamaz).</summary>
    public bool HasAudioMeasurement => AnalyzedSeconds > 0;

    /// <summary>"Ses tonuna göre" panelinin altındaki açıklama satırı — HER İKİ durumda da doludur.
    /// <para><b>Neden gizlenmiyor:</b> ölçüm yokken panel sessizce eski chunk-ortalaması değerine
    /// düşüyor ve açıklama satırı gizleniyordu; kullanıcı için "kişinin kendi sesinden ölçüldü"
    /// ile "ölçülemedi, tahmine düşüldü" tıpatıp aynı görünüyordu. Tasarım §3 bunu açıkça
    /// "rapor bunu 'yeterli ses yok' olarak gösterir" diye şart koşar.</para></summary>
    public string AudioMeasurementNote { get; }

    /// <summary>Ortalamaya giren chunk'lardan kaçının konuşmacı ataması belirsiz (eşik-altı
    /// örtüşme) olduğu — 0 ise UI hiçbir şey göstermez.</summary>
    public int UncertainChunkCount { get; }

    /// <summary>Gösterilecek belirsizlik uyarısı var mı? (XAML'de "sayı > 0" yapılamaz.)</summary>
    public bool HasUncertainChunks => UncertainChunkCount > 0;

    /// <summary>Belirsiz atamaya dayanan ölçüm sayısını kullanıcıya bildiren satır. Yalnızca
    /// <see cref="HasUncertainChunks"/> true iken gösterilir.</summary>
    public string UncertainChunkNote { get; }

    /// <summary>"Bu kişinin yüzü" seçenekleri (ilki "Eşleştirme yok") ve seçili olan.</summary>
    public IReadOnlyList<FaceOptionVm> FaceOptions { get; }

    private readonly System.Action<int, int?>? _onFaceSelected;

    /// <summary>Kullanıcının bu konuşmacıya eşlediği yüz. Değişince rapor, kişiye özel duyguyu yeniden kurar.</summary>
    public FaceOptionVm? SelectedFace
    {
        get => _selectedFace;
        set
        {
            if (value is null || value.FaceIndex == _selectedFace?.FaceIndex) return;
            _selectedFace = value;
            _onFaceSelected?.Invoke(SpeakerIndex, value.FaceIndex);
        }
    }
    private FaceOptionVm? _selectedFace;

    /// <summary>Eşleştirilmişse "yüz kaç bölümde katıldı" açıklaması.</summary>
    public string FaceNote { get; } = string.Empty;
    public bool HasFaceNote => FaceNote.Length > 0;

    /// <summary>Seçilebilecek bir yüz var mı ("Eşleştirme yok"tan başka)? Yoksa seçim kutusu gizlenir.</summary>
    public bool HasFaceChoices => FaceOptions.Count > 1;

    private const double SmallEffect = 0.05;

    /// <summary>Yüzün ifadesini ve duyguya etkisini açıkça söyler; etki küçükse bunu da — kullanıcı "hiçbir şey olmadı" sanmasın.</summary>
    private static string FaceNoteText(PersonEmotion p)
    {
        if (p.AverageFace is not { } face || p.FaceChunkCount == 0)
            return "Yüzü bu bölümlerde görünmedi; yalnızca ses ve sözlere göre hesaplandı";
        var tr = new System.Globalization.CultureInfo("tr-TR");
        double delta = p.Combined.Valence - p.WithoutFace.Valence;
        string effect = System.Math.Abs(delta) < SmallEffect
            ? $"çok küçük ({delta.ToString("+0.00;-0.00;0.00", tr)})"
            : delta.ToString("+0.00;-0.00;0.00", tr);
        return $"Sesi, sözleri ve yüzü birlikte hesaplandı ({p.FaceChunkCount}/{p.ChunkCount} bölümde yüz görüldü). "
             + $"Yüzü çoğunlukla {FacePanelVm.EmotionText(face).ToLower(tr)} "
             + $"(gülümseme %{System.Math.Round(100 * face.SmileRatio, System.MidpointRounding.AwayFromZero)}, "
             + $"olumsuz ifade %{System.Math.Round(100 * face.NegativeExpression, System.MidpointRounding.AwayFromZero)}); "
             + $"duyguya etkisi {effect}.";
    }

    public SpeakerEmotionVm(
        SpeakerEmotionSummary s, double temperature, double? neutralRadius,
        SpeakerProsodySummary? prosody = null, string? userGivenName = null,
        PersonEmotion? person = null, IReadOnlyList<FaceOptionVm>? faceOptions = null,
        int? selectedFace = null, System.Action<int, int?>? onFaceSelected = null)
    {
        FaceOptions = faceOptions ?? new[] { FaceOptionVm.None };
        _selectedFace = FaceOptions.FirstOrDefault(o => o.FaceIndex == selectedFace) ?? FaceOptions[0];
        _onFaceSelected = onFaceSelected;
        if (person is not null)
        {
            s = s with { Combined = person.Combined };
            FaceNote = FaceNoteText(person);
        }
        Label = userGivenName ?? s.Speaker.DefaultLabel;
        var audioVa = prosody?.Va ?? s.Audio;
        AnalyzedSeconds = prosody?.AnalyzedSeconds ?? 0;
        SpeakerIndex = s.Speaker.Index;
        Initial = SpeakerBadge.Initial(s.Speaker);
        AudioMeasurementNote = prosody is not null
            ? $"Ses tonu bu kişinin {prosody.AnalyzedSeconds:F0} sn konuşmasından ölçüldü"
            : "Bu kişinin kendi sesinden ölçüm için yeterli ses yok; genel ortalama kullanıldı";
        UncertainChunkCount = s.UncertainChunkCount;
        UncertainChunkNote = $"{s.UncertainChunkCount} bölümde konuşanın bu kişi olduğu kesin değil";
        TextAnalysis = new SourceAnalysisVm("Sözlere göre", s.Text, temperature, neutralRadius);
        AudioAnalysis = new SourceAnalysisVm("Ses tonuna göre", audioVa, temperature, neutralRadius);
        // Bulgu 1: Nihai Karar füzyon sonucunu (s.Combined) gösterir, ham prozodiyi değil —
        // bkz. sınıf-üstü açıklama.
        CombinedAnalysis = new SourceAnalysisVm("Genel sonuç", s.Combined, temperature, neutralRadius);
        OverallText = $"Genel olarak: {CombinedAnalysis.Emotion.ToLower(new System.Globalization.CultureInfo("tr-TR"))}";
    }
}

/// <summary>"Kim hangi yüz?" satırı: konuşma payındaki HER konuşmacı için bir yüz seçimi. Duygu kartından bağımsızdır —
/// az konuşan kişi hiçbir parçayı kazanamayıp kart almasa da yüzü ve adı seçilebilir.</summary>
public sealed class SpeakerFaceRowVm
{
    private readonly System.Action<int, int?>? _onSelected;
    private FaceOptionVm? _selectedFace;

    public SpeakerFaceRowVm(int speakerIndex, string label, IReadOnlyList<FaceOptionVm> faceOptions,
                            int? selectedFace, System.Action<int, int?>? onSelected)
    {
        SpeakerIndex = speakerIndex;
        Label = label;
        Initial = SpeakerBadge.Initial(new SpeakerId(speakerIndex));
        FaceOptions = faceOptions;
        _selectedFace = faceOptions.FirstOrDefault(o => o.FaceIndex == selectedFace) ?? faceOptions[0];
        _onSelected = onSelected;
    }

    public int SpeakerIndex { get; }
    public string Label { get; }
    public string Initial { get; }
    public IReadOnlyList<FaceOptionVm> FaceOptions { get; }

    public FaceOptionVm? SelectedFace
    {
        get => _selectedFace;
        set
        {
            if (value is null || value.FaceIndex == _selectedFace?.FaceIndex) return;
            _selectedFace = value;
            _onSelected?.Invoke(SpeakerIndex, value.FaceIndex);
        }
    }
}

/// <summary>Konuşmacı kartındaki yüz seçeneği. <see cref="FaceIndex"/> null = eşleştirme yok.</summary>
public sealed record FaceOptionVm(int? FaceIndex, string Text)
{
    public static FaceOptionVm None { get; } = new(null, "Eşleştirme yok");
    public override string ToString() => Text;
}

/// <summary>Konuşmacı rozetinin harfi: "Konuşmacı A" → "A", 26'dan sonra sayı, bilinmeyen → "?".</summary>
public static class SpeakerBadge
{
    public static string Initial(SpeakerId id) =>
        id.Index < 0 ? "?" : id.Index < 26 ? ((char)('A' + id.Index)).ToString() : (id.Index + 1).ToString();
}

/// <summary>Toplantı-sonu rapor VM'i: bir oturumu <see cref="IMeetingAggregator"/> ile özetler, lokalize sunar.</summary>
public sealed partial class ReportViewModel : ObservableObject
{
    private readonly IMeetingAggregator _aggregator;
    private readonly double _temperature;
    private readonly double? _neutralRadius;

    [ObservableProperty] private bool _hasReport;
    [ObservableProperty] private string _dominantEmotion = "—";
    [ObservableProperty] private string _turkishSummary = string.Empty;
    [ObservableProperty] private double _valenceTrend;
    [ObservableProperty] private double _volatility;
    [ObservableProperty] private double _averageCongruence;

    public IReadOnlyList<TimeShareVm> TimeShare { get; private set; } = System.Array.Empty<TimeShareVm>();

    [ObservableProperty] private string _emotionMapSummary = string.Empty;

    /// <summary>Özet kartının renkli etiketleri ("Hava: olumluya döndü" …) — sayı yerine.</summary>
    public IReadOnlyList<SummaryTagVm> SummaryTags { get; private set; } = System.Array.Empty<SummaryTagVm>();
    public IReadOnlyList<ChunkRowVm> PeakMoments { get; private set; } = System.Array.Empty<ChunkRowVm>();

    /// <summary>Konuşmacı ayrımının kullanıcıya görünen durumu (10b). Boş = özellik kapalı
    /// veya hiç denenmedi → UI şeridi görünmez. 08e dersi: kullanıcı ne gördüğünü ve neye
    /// güvenebileceğini bilmelidir.</summary>
    [ObservableProperty] private string _diarizationStatus = string.Empty;
    [ObservableProperty] private string _analysisQualityNotice = string.Empty;

    /// <summary>Gösterilecek konuşmacı satırı var mı? Yoksa rapor bugünkü düz transkripti
    /// gösterir (davranış değişmez).</summary>
    [ObservableProperty] private bool _hasSpeakers;

    /// <summary>Konuşmacı etiketli transkript. Diarization çalışmadıysa BOŞ kalır —
    /// uydurma etiket üretilmez (master §12.2).</summary>
    public IReadOnlyList<SpeakerLineVm> SpeakerLines { get; private set; } = System.Array.Empty<SpeakerLineVm>();

    /// <summary>Konuşmacı-başına metin/ses/nihai duygu ortalaması (10c). Diarization çalışmadıysa
    /// veya konuşmacı ayırt edilemediyse BOŞ kalır — uydurma özet üretilmez (master §12.2).</summary>
    public IReadOnlyList<SpeakerEmotionVm> SpeakerEmotions { get; private set; } = System.Array.Empty<SpeakerEmotionVm>();

    /// <summary>Takip kararı + duyarlılık paneli (11e).</summary>
    public FollowUpPanelViewModel FollowUp { get; }

    /// <summary>Koşunun karar verisi var mı (en az bir pencere)? Yoksa karar bölümleri gizlenir.</summary>
    [ObservableProperty] private bool _hasDecisionData;

    /// <summary>Kriter başına pencere değerleri (0..1) — ısı şeridi.</summary>
    public IReadOnlyList<IndicatorRowVm> IndicatorTimeline { get; private set; } = System.Array.Empty<IndicatorRowVm>();

    /// <summary>Zaman çizelgesinin kapsadığı aralık ("00:00–04:30").</summary>
    [ObservableProperty] private string _timelineRange = string.Empty;

    /// <summary>Grafiğin altındaki zaman ekseninin iki ucu.</summary>
    [ObservableProperty] private string _timelineStart = string.Empty;
    [ObservableProperty] private string _timelineEnd = string.Empty;

    /// <summary>Görüşme sırasında gösterilen önerinin değiştiği anlar.</summary>
    public IReadOnlyList<RecommendationRowVm> Recommendations { get; private set; } = System.Array.Empty<RecommendationRowVm>();

    public IReadOnlyList<CriticalMomentVm> CriticalMoments { get; private set; } = System.Array.Empty<CriticalMomentVm>();

    /// <summary>XAML dizinin <c>Count</c>'una bağlanamaz (dizide açık arayüz üyesi) — "kritik an yok" metni buradan.</summary>
    public bool HasCriticalMoments => CriticalMoments.Count > 0;

    /// <summary>Konuşmacı başına konuşma süresi ve payı (11e). Diarization çalışmadıysa BOŞ.</summary>
    public IReadOnlyList<TalkShareVm> TalkBalance { get; private set; } = System.Array.Empty<TalkShareVm>();

    /// <summary>Karar kaydı saklanamadıysa kısa uyarı; boşsa gösterilmez.</summary>
    [ObservableProperty] private string _decisionSaveStatus = string.Empty;

    private readonly DecisionConfig _decision;
    private readonly FusionConfig _fusion;

    // Konuşmacı–yüz eşlemesi (kullanıcı seçer): yüz numarası → panelde yazdığı ad; canlı parçalar yüzsüz
    // değerleriyle (SpeakerVa) kişiye özel duygu için; pencereler yüz özetleriyle.
    private IReadOnlyList<ChunkResult> _liveChunks = System.Array.Empty<ChunkResult>();
    private IReadOnlyDictionary<int, string> _faceNames = new Dictionary<int, string>();
    private IReadOnlyList<FaceWindow> _faceWindows = System.Array.Empty<FaceWindow>();
    private readonly Dictionary<SpeakerId, int> _faceBySpeaker = new();
    private (IReadOnlyList<TranscriptSegment> Transcript, IReadOnlyList<SpeakerSegment> Speakers, double MinOverlap)? _lastSpeakerInput;
    private (IReadOnlyList<ChunkResult> Chunks, IReadOnlyList<SpeakerSegment> Speakers, double MinOverlap, IReadOnlyList<SpeakerProsodySummary> Prosody)? _lastEmotionInput;

    public ReportViewModel(IMeetingAggregator aggregator, AppConfig? config = null)
    {
        _aggregator = aggregator;
        var cfg = config ?? AppConfig.Default;
        _decision = cfg.Decision;
        _fusion = cfg.Fusion;
        FollowUp = new FollowUpPanelViewModel(cfg.Decision);
        _temperature = cfg.Fusion.DistributionTemperature;
        // Nötr yarıçapı canlı ekrandan geçirilir; buradan da geçmezsek rapor ile canlı ekran
        // farklı duygu etiketi gösterir (aynı VA'ya farklı yorum).
        _neutralRadius = cfg.Fusion.NeutralRadius;
    }

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

        if (outcome != DiarizationOutcome.Completed) ClearSpeakers();
    }

    private void ClearSpeakers()
    {
        SpeakerLines = System.Array.Empty<SpeakerLineVm>();
        HasSpeakers = false;
        OnPropertyChanged(nameof(SpeakerLines));

        SpeakerEmotions = System.Array.Empty<SpeakerEmotionVm>();
        OnPropertyChanged(nameof(SpeakerEmotions));
        OnPropertyChanged(nameof(FaceMappingHint));

        TalkBalance = System.Array.Empty<TalkShareVm>();
        OnPropertyChanged(nameof(TalkBalance));

        FaceMappings = System.Array.Empty<SpeakerFaceRowVm>();
        OnPropertyChanged(nameof(FaceMappings));
        OnPropertyChanged(nameof(HasFaceMappings));
        OnPropertyChanged(nameof(FaceMappingHint));
    }

    /// <summary>Zaman damgalı transkripti konuşmacı bölgeleriyle hizalayıp satırlara indirger.
    /// Hizalama ve birleştirme saf Application koduna (test edilebilir) devredilir; burada
    /// yalnızca sunum yapılır.</summary>
    public void ApplySpeakers(
        IReadOnlyList<TranscriptSegment> transcript,
        IReadOnlyList<SpeakerSegment> speakers,
        double minOverlapRatio)
    {
        _lastSpeakerInput = (transcript, speakers, minOverlapRatio);
        BuildSpeakerLines();
    }

    private void BuildSpeakerLines()
    {
        if (_lastSpeakerInput is not var (transcript, speakers, minOverlapRatio)) return;
        var aligned = SpeakerAligner.Align(transcript, speakers, minOverlapRatio);
        SpeakerLines = SpeakerLineBuilder.Build(aligned).Select(l => new SpeakerLineVm(l, GivenName(l.Speaker))).ToList();
        HasSpeakers = SpeakerLines.Count > 0;
        OnPropertyChanged(nameof(SpeakerLines));

        TalkBalance = Application.Diarization.TalkBalance.Compute(speakers)
            .Select(t => new TalkShareVm(GivenName(t.Speaker) ?? t.Speaker.DefaultLabel, t.Share, $"{Duration(t.Duration)} · {Percent(t.Share)}", t.Speaker.Index))
            .ToList();
        OnPropertyChanged(nameof(TalkBalance));
        BuildFaceMappings(speakers);
    }

    /// <summary>Her konuşmacı için yüz seçimi (konuşma payındaki kişiler). Yüz hiç görülmediyse boş.</summary>
    public IReadOnlyList<SpeakerFaceRowVm> FaceMappings { get; private set; } = System.Array.Empty<SpeakerFaceRowVm>();

    public bool HasFaceMappings => FaceMappings.Count > 0;

    private void BuildFaceMappings(IReadOnlyList<SpeakerSegment> speakers)
    {
        var options = FaceOptions();
        FaceMappings = options.Count <= 1 ? System.Array.Empty<SpeakerFaceRowVm>()
            : speakers.Select(sp => sp.Speaker).Where(id => id.Index >= 0).Distinct().OrderBy(id => id.Index)
                .Select(id => new SpeakerFaceRowVm(id.Index, GivenName(id) ?? id.DefaultLabel, options,
                    _faceBySpeaker.TryGetValue(id, out var f) ? f : null, SetSpeakerFace))
                .ToList();
        OnPropertyChanged(nameof(FaceMappings));
        OnPropertyChanged(nameof(HasFaceMappings));
        OnPropertyChanged(nameof(FaceMappingHint));
    }

    /// <summary>Konuşmacıya eşlenen yüze kullanıcı bir ad yazdıysa o ad; yoksa null (varsayılan "Konuşmacı A").</summary>
    private string? GivenName(SpeakerId speaker) =>
        _faceBySpeaker.TryGetValue(speaker, out var face) && _faceNames.TryGetValue(face, out var name)
        && !string.IsNullOrWhiteSpace(name) ? name.Trim() : null;

    /// <summary>Görüşme bitince çağrılır: canlı parçalar (yüzsüz değerleriyle) ve panelde yazılan adlar.
    /// Yeni görüşmede önceki eşleme geçersizdir.</summary>
    public void SetFaceContext(IReadOnlyList<ChunkResult> liveChunks, IReadOnlyDictionary<int, string> faceNames)
    {
        _liveChunks = liveChunks;
        _faceNames = faceNames;
        _faceBySpeaker.Clear();
    }

    /// <summary>Yüz görüldü ama hiçbir konuşmacıya eşlenmedi: kullanıcıyı seçim kutusuna yönlendirir; aksi halde boş.</summary>
    public string FaceMappingHint =>
        HasFaceMappings && _faceBySpeaker.Count == 0
            ? "Adları görmek için aşağıda her kişinin \"Bu kişinin yüzü\" kutusunu seç."
            : string.Empty;

    private IReadOnlyList<FaceOptionVm> FaceOptions()
    {
        var indexes = _faceWindows.SelectMany(w => w.Faces.Select(f => f.FaceIndex)).Concat(_faceNames.Keys).Distinct().OrderBy(i => i);
        return new[] { FaceOptionVm.None }
            .Concat(indexes.Select(i => new FaceOptionVm(i,
                _faceNames.TryGetValue(i, out var n) && !string.IsNullOrWhiteSpace(n) ? n.Trim() : $"Yüz {i + 1}")))
            .ToList();
    }

    /// <summary>Kullanıcı bir konuşmacıya yüz eşledi (ya da kaldırdı): satırlar, paylar ve duygu kartları yeniden kurulur.</summary>
    public void SetSpeakerFace(int speakerIndex, int? faceIndex)
    {
        var id = new SpeakerId(speakerIndex);
        if (faceIndex is int f) _faceBySpeaker[id] = f; else _faceBySpeaker.Remove(id);
        BuildSpeakerLines();
        BuildSpeakerEmotions();
    }

    /// <summary>Bitmiş görüşmenin karar verisini (11e) gösterir: zaman çizelgesi, verilen öneriler, kritik
    /// anlar ve takip kararı. Saklanmış pencere göstergelerinden çalışır — yeniden hesaplamaz.</summary>
    public void LoadDecisions(DecisionSession session)
    {
        var windows = session.Windows;
        _faceWindows = windows.Select(w => new FaceWindow(w.Start, w.End, w.Faces)).ToArray();
        HasDecisionData = windows.Count > 0;
        DecisionSaveStatus = string.Empty;
        // Karşı tarafa odak: karşı tarafın sustuğu pencereler çizelgeye ve kritik anlara girmez (nötr görünmesin).
        var spoken = windows.Where(w => !w.CounterpartSilent).ToArray();
        IndicatorTimeline = System.Enum.GetValues<Criterion>()
            .Select(c => new IndicatorRowVm(DecisionPanelViewModel.Capitalize(DecisionText.Name(c)),
                spoken.Select(w => w.Indicators.Values[(int)c]).ToArray()))
            .ToArray();
        TimelineStart = windows.Count == 0 ? string.Empty : Clock(windows[0].Start);
        TimelineEnd = windows.Count == 0 ? string.Empty : Clock(windows[^1].End);
        TimelineRange = windows.Count == 0 ? string.Empty : $"{TimelineStart}–{TimelineEnd}";
        Recommendations = DecisionReport.Changes(windows)
            .Select(c => new RecommendationRowVm(Clock(c.At), DecisionText.Name(c.Action), c.Reason, c.Confidence == Confidence.Dusuk))
            .ToArray();
        CriticalMoments = DecisionReport.CriticalMoments(spoken, _decision)
            .Select(m => new CriticalMomentVm($"{Clock(m.Start)}–{Clock(m.End)}",
                $"{DecisionPanelViewModel.Capitalize(DecisionText.Name(m.Criterion))} {Percent(m.Peak)} — {DecisionText.Subject(m.Face)} · gösterilen öneri: {DecisionText.Name(m.Shown)}"))
            .ToArray();
        OnPropertyChanged(nameof(IndicatorTimeline));
        OnPropertyChanged(nameof(Recommendations));
        OnPropertyChanged(nameof(CriticalMoments));
        OnPropertyChanged(nameof(HasCriticalMoments));
        FollowUp.Load(session);
    }

    private static string Clock(System.TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";

    private static string Percent(double x) => $"%{System.Math.Round(100 * x, System.MidpointRounding.AwayFromZero)}";

    internal static string Duration(System.TimeSpan t)
    {
        int total = (int)System.Math.Round(t.TotalSeconds, System.MidpointRounding.AwayFromZero);
        return total < 60 ? $"{total} sn" : $"{total / 60} dk {total % 60} sn";
    }

    /// <summary>Chunk-düzeyi füzyon sonuçlarını konuşmacı bölgeleriyle hizalayıp konuşmacı-başına
    /// metin/ses/nihai duygu ortalamasına indirger (10c). Hizalama ve ortalama saf Application
    /// koduna (test edilebilir) devredilir; burada yalnızca sunum yapılır.</summary>
    public void ApplySpeakerEmotions(
        IReadOnlyList<ChunkResult> chunks,
        IReadOnlyList<SpeakerSegment> speakers,
        double minOverlapRatio,
        IReadOnlyList<SpeakerProsodySummary> speakerProsody)
    {
        _lastEmotionInput = (chunks, speakers, minOverlapRatio, speakerProsody);
        BuildSpeakerEmotions();
    }

    private void BuildSpeakerEmotions()
    {
        if (_lastEmotionInput is not var (chunks, speakers, minOverlapRatio, speakerProsody)) return;
        var bySpeaker = speakerProsody.ToDictionary(p => p.Speaker);
        var summaries = SpeakerEmotionAggregator.Aggregate(chunks, speakers, minOverlapRatio);
        // Kişiye özel üç kaynaklı duygu yalnızca yüz eşlenen konuşmacılar için; canlı parçalar yüzsüz değeri taşır.
        var people = _faceBySpeaker.Count == 0
            ? new Dictionary<SpeakerId, PersonEmotion>()
            : PersonEmotionComposer.Compose(_liveChunks.Count > 0 ? _liveChunks : chunks, speakers, _faceWindows,
                _faceBySpeaker, _fusion, minOverlapRatio).ToDictionary(p => p.Speaker);
        var options = FaceOptions();
        SpeakerEmotions = summaries
            .Select(s => new SpeakerEmotionVm(
                s, _temperature, _neutralRadius,
                bySpeaker.TryGetValue(s.Speaker, out var p) ? p : null,
                GivenName(s.Speaker),
                people.TryGetValue(s.Speaker, out var person) ? person : null,
                options,
                _faceBySpeaker.TryGetValue(s.Speaker, out var face) ? face : null,
                SetSpeakerFace)
            {
                IsHost = _host == s.Speaker,
                HostBehaviorText = _host == s.Speaker ? HostBehavior(s.Speaker, speakers) : string.Empty,
            })
            .ToList();
        OnPropertyChanged(nameof(SpeakerEmotions));
        OnPropertyChanged(nameof(FaceMappingHint));
    }

    private SpeakerId? _host;

    /// <summary>Dosya modunda "Siz hangisiniz?" seçimi (karşı tarafa odak); null = yöneten kartı yok.</summary>
    public void SetHost(SpeakerId? host)
    {
        _host = host;
        BuildSpeakerEmotions();
    }

    private string HostBehavior(SpeakerId host, IReadOnlyList<SpeakerSegment> speakers)
    {
        double total = speakers.Where(x => x.Speaker != SpeakerId.Unknown).Sum(x => (x.End - x.Start).TotalSeconds);
        double mine = speakers.Where(x => x.Speaker == host).Sum(x => (x.End - x.Start).TotalSeconds);
        int words = SpeakerLines.Where(l => l.SpeakerIndex == host.Index)
            .Sum(l => l.Text.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries).Length);
        var parts = new List<string> { "Görüşmeyi yöneten (siz)" };
        if (total > 0) parts.Add($"konuşma payı %{System.Math.Round(100 * mine / total, System.MidpointRounding.AwayFromZero)}");
        if (mine > 0 && words > 0) parts.Add($"dakikada {System.Math.Round(words / (mine / 60), System.MidpointRounding.AwayFromZero)} kelime");
        return string.Join(" · ", parts);
    }

    public const string MixedRoleNotice = "Yöneten ayrılmadı — sonuçlar iki tarafın karışık sesinden.";
    public const string PartialRoleNotice = "Yöneten yalnız kısmen ayrıldı — bazı bölümler karışık sesten.";

    /// <summary>Rapor başındaki rol notu (karşı tarafa odak): rolsüz → karışık, kısmen rolsüz → kısmi, tam → boş.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasRoleNotice))] private string _roleNotice = string.Empty;
    public bool HasRoleNotice => !string.IsNullOrEmpty(RoleNotice);

    public void SetRoleContext(IReadOnlyList<ChunkResult> chunks)
    {
        var speech = chunks.Where(c => !string.IsNullOrWhiteSpace(c.Transcript)).ToArray();
        int withRoles = speech.Count(c => c.Roles is { Enrolled: true });
        RoleNotice = speech.Length == 0 || withRoles == speech.Length ? string.Empty
            : withRoles == 0 ? MixedRoleNotice
            : PartialRoleNotice;
    }

    /// <summary>Duygu haritası: paylar büyükten küçüğe, süre ve tek cümlelik özet.</summary>
    private void LoadTimeShare(MeetingSummary s)
    {
        TimeShare = s.TimeShareByEmotion
            .OrderByDescending(kv => kv.Value).ThenBy(kv => (int)kv.Key)
            .Select(kv => new TimeShareVm(kv.Key, kv.Value, s.AnalyzedDuration))
            .ToList();
        EmotionMapSummary = TimeShare.Count == 0 ? string.Empty
            : $"Konuşmanın en büyük kısmı (%{System.Math.Round(100 * TimeShare[0].Share, System.MidpointRounding.AwayFromZero)}) "
              + $"{TimeShare[0].Emotion.ToLower(new System.Globalization.CultureInfo("tr-TR"))} bir havada geçti.";
        OnPropertyChanged(nameof(TimeShare));
    }

    public void Load(MeetingSession session)
    {
        var s = _aggregator.Aggregate(session);
        AnalysisQualityNotice = AnalysisQualityText.Format(s.Diagnostics);
        DominantEmotion = EmotionLocalizer.Emotion(s.DominantEmotion);
        TurkishSummary = s.TurkishSummary;
        ValenceTrend = s.ValenceTrend;
        Volatility = s.EmotionalVolatility;
        AverageCongruence = s.AverageCongruence;
        SummaryTags = Application.Fusion.TurkishSummaryWriter.Tags(s).Select(t => new SummaryTagVm(t.Text, t.Tone)).ToList();
        OnPropertyChanged(nameof(SummaryTags));
        LoadTimeShare(s);
        PeakMoments = s.PeakMoments.Select(c => new ChunkRowVm(c)).ToList();
        OnPropertyChanged(nameof(PeakMoments));
        HasReport = true;
    }

    /// <summary>Zaten üretilmiş bir <see cref="MeetingSummary"/>'yi (ör. dosya-analizi koşusundan) yeniden-agregasyon
    /// yapmadan lokalize gösterir.</summary>
    public void LoadSummary(MeetingSummary s)
    {
        AnalysisQualityNotice = AnalysisQualityText.Format(s.Diagnostics);
        DominantEmotion = EmotionLocalizer.Emotion(s.DominantEmotion);
        TurkishSummary = s.TurkishSummary;
        ValenceTrend = s.ValenceTrend;
        Volatility = s.EmotionalVolatility;
        AverageCongruence = s.AverageCongruence;
        SummaryTags = Application.Fusion.TurkishSummaryWriter.Tags(s).Select(t => new SummaryTagVm(t.Text, t.Tone)).ToList();
        OnPropertyChanged(nameof(SummaryTags));
        LoadTimeShare(s);
        PeakMoments = s.PeakMoments.Select(c => new ChunkRowVm(c)).ToList();
        OnPropertyChanged(nameof(PeakMoments));
        // Yeni görüşme: önceki koşunun konuşmacıları sarkmasın (ayrım bu koşuda kapalıysa geçiş hiç çalışmaz).
        DiarizationStatus = string.Empty;
        ClearSpeakers();
        HasReport = true;
    }
}

/// <summary>Özet etiketi; renk sınıfı XAML'de bool'lara bağlanır.</summary>
public sealed record SummaryTagVm(string Text, Application.Fusion.SummaryTone Tone)
{
    public bool Good => Tone == Application.Fusion.SummaryTone.Good;
    public bool Warn => Tone == Application.Fusion.SummaryTone.Warn;
    public bool Bad => Tone == Application.Fusion.SummaryTone.Bad;
    public bool Neutral => Tone == Application.Fusion.SummaryTone.Neutral;
}

/// <summary>Zaman çizelgesinde bir kriterin pencere değerleri (0..1) ve baştan sona eğilimi.
/// Eğilim ilk ve son üçte birin ORTALAMASIYLA ölçülür — tek pencerelik sıçrama "arttı" demesin.
/// Kriterlerin hepsi sorun olduğundan artış kötü (kırmızı), azalış iyi (yeşil) gösterilir.</summary>
public sealed record IndicatorRowVm(string Name, IReadOnlyList<double> Values)
{
    private const double FlatBand = 0.1;

    private (double Start, double End) Ends
    {
        get
        {
            int n = System.Math.Max(1, Values.Count / 3);
            return (Values.Take(n).Average(), Values.Skip(Values.Count - n).Average());
        }
    }

    public bool TrendUp => Values.Count > 1 && Ends.End - Ends.Start >= FlatBand;
    public bool TrendDown => Values.Count > 1 && Ends.Start - Ends.End >= FlatBand;
    public bool TrendFlat => !TrendUp && !TrendDown;

    public string TrendText =>
        Values.Count == 0 ? string.Empty
        : TrendUp ? $"↑ arttı {Pct(Ends.Start)} → {Pct(Ends.End)}"
        : TrendDown ? $"↓ azaldı {Pct(Ends.Start)} → {Pct(Ends.End)}"
        : $"→ belirgin değişim yok (ortalama {Pct(Values.Average())})";

    private static string Pct(double x) => $"%{System.Math.Round(100 * x, System.MidpointRounding.AwayFromZero)}";
}

/// <summary>Verilen öneri satırı: "00:40", eylem, gerekçe; düşük güvenli satır soluk gösterilir.</summary>
public sealed record RecommendationRowVm(string Time, string Action, string Reason, bool IsLowConfidence);

/// <summary>Kritik an: "01:10–01:40" ve "Gerilim %72 — Yüz 2 · gösterilen öneri: …".</summary>
public sealed record CriticalMomentVm(string TimeRange, string Text);

/// <summary>Konuşma dengesi satırı: etiket, pay (0..1), "2 dk 10 sn · %58" ve renk için konuşmacı sırası.</summary>
public sealed record TalkShareVm(string Label, double Share, string Text, int SpeakerIndex = 0);
