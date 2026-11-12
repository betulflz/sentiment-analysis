using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Tanıtma kaydının sonucu. <paramref name="Doubtful"/>: kayıt kendi içinde tutarsız (iki kişi konuşmuş
/// olabilir) — tanıtma yine yapılır ama kullanıcıya yeniden kaydetmesi önerilir.</summary>
public sealed record EnrollmentResult(bool Success, bool Doubtful, double SpeechSeconds, string Message);

/// <summary>Canlı "Yöneten / Karşı taraf" ayrımı (12, ölçüm 12a). Görüşmeyi yöneten kişi görüşmeden önce kısa bir
/// tanıtma kaydı yapar (<see cref="Enroll"/>); sonra her ses parçası kısa pencerelere bölünür ve her konuşma
/// penceresi tanıtma örneğine kosinüs benzerliğiyle etiketlenir. Kümeleme ve bölütleme YOKTUR — pyannote'un
/// karşılıklı konuşmayı tek parça kesme sorunu (2026-11-09 ölçümü) bu yolda oluşmaz.
/// <para><b>Tanıtma yoksa rol verilmez</b> (davranış 12 öncesiyle aynı). Akış içi "ilk saniyeler" tanıtması gerçek
/// doğrulamada %64'e düştüğü için bilerek yoktur — yanlış rol, rolsüzlükten kötüdür.</para>
/// <para><b>Gizlilik:</b> ses imzası yalnız bellekte tutulur, diske yazılmaz; uygulama kapanınca kaybolur.</para>
/// <para><see cref="Label"/> tek tüketici iş parçacığından sırayla çağrılır; <see cref="Enroll"/> koşu dışında.</para>
/// <para><b>Hata:</b> model eksik/bozuksa özellik bu koşu için kapanır (<see cref="Failure"/>), analiz sürer.</para></summary>
public sealed class SpeakerRoleTracker
{
    private const int Sr = 16000;
    private readonly ISpeakerEmbedder _embedder;
    private readonly IVadGate _vad;
    private readonly RoleConfig _cfg;
    private volatile float[]? _enrollment;
    private volatile bool _doubtful;

    public SpeakerRoleTracker(ISpeakerEmbedder embedder, IVadGate vad, AppConfig config)
    {
        _embedder = embedder;
        _vad = vad;
        _cfg = config.Role;
    }

    /// <summary>Özellik bu koşuda kapandıysa nedeni (ör. model eksik); aksi halde null.</summary>
    public string? Failure { get; private set; }

    public bool Enrolled => _enrollment is not null;

    /// <summary>Yeni koşu: hata durumu sıfırlanır. Tanıtma KORUNUR (aynı kişi sonraki görüşmeleri de yönetir).</summary>
    public void Reset() => Failure = null;

    public void ClearEnrollment()
    {
        _enrollment = null;
        _doubtful = false;
    }

    /// <summary>Tanıtma kaydından ses imzası çıkarır: sessizlik ayıklanır, en az
    /// <see cref="RoleConfig.MinEnrollSpeechSeconds"/> konuşma gerekir. Başarısızsa önceki tanıtma korunur.</summary>
    public EnrollmentResult Enroll(float[] mono16k)
    {
        ArgumentNullException.ThrowIfNull(mono16k);
        var chunk = new AudioChunk(mono16k, Sr, TimeSpan.Zero, Seconds(mono16k.Length));
        var speech = new List<float>();
        foreach (var w in Windows(chunk))
            if (_vad.HasSpeech(Slice(chunk, w.A, w.B)))
                speech.AddRange(mono16k.AsSpan(w.A, w.B - w.A).ToArray());

        double seconds = speech.Count / (double)Sr;
        if (seconds < _cfg.MinEnrollSpeechSeconds)
            return new EnrollmentResult(false, false, seconds,
                $"Yeterince konuşma algılanmadı ({seconds:0.0} sn). Kayıt süresince kesintisiz konuşup tekrar deneyin.");

        var audio = speech.ToArray();
        var enrollment = _embedder.Embed(audio);
        int half = audio.Length / 2;
        bool doubtful = Cos(_embedder.Embed(audio[..half]), _embedder.Embed(audio[half..])) < _cfg.DoubtThreshold;
        _doubtful = doubtful;
        _enrollment = enrollment;
        return new EnrollmentResult(true, doubtful, seconds, doubtful
            ? "Kayıtta birden fazla ses var gibi. Sessiz bir ortamda yalnız siz konuşarak yeniden kaydetmeniz önerilir."
            : "Sesiniz tanındı.");
    }

    public RoleTimeline Label(AudioChunk chunk)
    {
        if (!_cfg.Enabled || _enrollment is null || Failure is not null || chunk.SampleRate != Sr || chunk.Samples.Length == 0)
            return RoleTimeline.NotEnrolled;
        try
        {
            return LabelCore(chunk);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failure = ex.Message;
            return RoleTimeline.NotEnrolled;
        }
    }

    private RoleTimeline LabelCore(AudioChunk chunk)
    {
        // Konuşma kararı pencerenin TEMSİL ETTİĞİ dilimde verilir, 1,5 sn'nin tamamında değil: Silero pencerenin
        // herhangi bir yerinde konuşma görse "konuşma" der; bütün pencereye baksaydık konuşma kenarları sessizliğe
        // ~1 sn taşar, konuşma payı şişerdi.
        var windows = Windows(chunk).Where(w => _vad.HasSpeech(Slice(chunk, w.A, w.B))).ToList();

        if (_enrollment is not { } enrollment) return RoleTimeline.NotEnrolled;
        if (windows.Count == 0) return new RoleTimeline(Array.Empty<RoleSpan>(), true, _doubtful);

        var sims = windows.Select(w => Cos(_embedder.Embed(Slice(chunk, w.From, w.To).Samples), enrollment)).ToArray();
        var smooth = new double[sims.Length];
        for (int i = 0; i < sims.Length; i++)
        {
            // 3 komşu konuşma penceresinin ortancası (12a: +1 puan, tek pencerelik sıçramaları bastırır)
            var a = sims[Math.Max(0, i - 1)];
            var b = sims[i];
            var c = sims[Math.Min(sims.Length - 1, i + 1)];
            smooth[i] = Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
        }
        return new RoleTimeline(Spans(chunk.Start, windows, smooth, _cfg.Threshold), true, _doubtful);
    }

    /// <summary>Embedding penceresi [From, To) ve temsil ettiği dilim [A, B): pencerenin ortasındaki "hop" dilimi;
    /// parçanın ilk/son penceresinde parça kenarına uzanır. Dilimler parçayı boşluksuz ve örtüşmesiz döşer.</summary>
    private readonly record struct Window(int From, int To, int A, int B);

    private IEnumerable<Window> Windows(AudioChunk chunk)
    {
        int len = (int)(_cfg.WindowSeconds * Sr), hop = (int)(_cfg.HopSeconds * Sr), n = chunk.Samples.Length;
        if (n < len)
        {
            if (n >= hop) yield return new Window(0, n, 0, n);   // kısa parça: tek pencere
            yield break;
        }
        int last = (n - len) / hop * hop;
        for (int from = 0; from <= last; from += hop)
        {
            int mid = from + len / 2;
            yield return new Window(from, from + len,
                from == 0 ? 0 : mid - hop / 2,
                from == last ? n : mid + hop / 2);
        }
    }

    /// <summary>Aynı roldeki bitişik dilimler birleşir; sessizlik (atlanan dilim) koşuları ayırır.</summary>
    private static List<RoleSpan> Spans(TimeSpan origin, List<Window> windows, double[] sims, double threshold)
    {
        var spans = new List<RoleSpan>();
        var sums = new List<(double Sum, int Count)>();
        for (int i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            var role = sims[i] >= threshold ? SpeakerRole.Yoneten : SpeakerRole.KarsiTaraf;
            bool contiguous = i > 0 && windows[i - 1].B == w.A;
            if (contiguous && spans[^1].Role == role)
            {
                sums[^1] = (sums[^1].Sum + sims[i], sums[^1].Count + 1);
                spans[^1] = spans[^1] with { End = origin + Seconds(w.B) };
            }
            else
            {
                spans.Add(new RoleSpan(origin + Seconds(w.A), origin + Seconds(w.B), role, sims[i]));
                sums.Add((sims[i], 1));
            }
        }
        return spans.Select((s, i) => s with { Similarity = sums[i].Sum / sums[i].Count }).ToList();
    }

    private static TimeSpan Seconds(int samples) => TimeSpan.FromSeconds(samples / (double)Sr);

    private static AudioChunk Slice(AudioChunk chunk, int from, int to) =>
        new(chunk.Samples[from..to], chunk.SampleRate,
            chunk.Start + TimeSpan.FromSeconds(from / (double)Sr), chunk.Start + TimeSpan.FromSeconds(to / (double)Sr));

    private static double Cos(float[] a, float[] b)
    {
        double d = 0, na = 0, nb = 0;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) { d += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na <= 0 || nb <= 0 ? 0 : d / Math.Sqrt(na * nb);
    }
}
