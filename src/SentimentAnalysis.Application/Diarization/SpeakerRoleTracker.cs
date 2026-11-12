using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Canlı "Yöneten / Karşı taraf" ayrımı (12, ölçüm 12a). Görüşmenin ilk konuşma saniyeleri yönetenin
/// tanıtma örneği olur; sonra her ses parçası kısa pencerelere bölünür ve her konuşma penceresi örneğe kosinüs
/// benzerliğiyle etiketlenir. Kümeleme ve bölütleme YOKTUR — pyannote'un karşılıklı konuşmayı tek parça kesme
/// sorunu (2026-11-09 ölçümü) bu yolda oluşmaz.
/// <para><b>Durumludur, koşu başına bir örnek gibi kullanılır:</b> her koşunun başında <see cref="Reset"/>.
/// Tek tüketici iş parçacığından sırayla çağrılmalıdır (boru hattı bunu sağlar).</para>
/// <para><b>Örtüşme:</b> ses parçaları birbirine ~2 sn biner. Tanıtma örneği toplanırken aynı ses iki kez
/// sayılmaz (imleç); etiketleme ise her parçanın tamamını kapsar.</para>
/// <para><b>Hata:</b> model eksik/bozuksa özellik bu koşu için kapanır (<see cref="Failure"/>), analiz sürer.</para></summary>
public sealed class SpeakerRoleTracker
{
    private const int Sr = 16000;
    private readonly ISpeakerEmbedder _embedder;
    private readonly IVadGate _vad;
    private readonly RoleConfig _cfg;

    private readonly List<float> _enrollAudio = new();
    private TimeSpan _enrollCursor = TimeSpan.MinValue;
    private float[]? _enrollment;
    private bool _doubtful;

    public SpeakerRoleTracker(ISpeakerEmbedder embedder, IVadGate vad, AppConfig config)
    {
        _embedder = embedder;
        _vad = vad;
        _cfg = config.Role;
    }

    /// <summary>Özellik bu koşuda kapandıysa nedeni (ör. model eksik); aksi halde null.</summary>
    public string? Failure { get; private set; }

    public bool Enrolled => _enrollment is not null;

    public void Reset()
    {
        _enrollAudio.Clear();
        _enrollCursor = TimeSpan.MinValue;
        _enrollment = null;
        _doubtful = false;
        Failure = null;
    }

    public RoleTimeline Label(AudioChunk chunk)
    {
        if (!_cfg.Enabled || Failure is not null || chunk.SampleRate != Sr || chunk.Samples.Length == 0)
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

        if (_enrollment is null)
        {
            Collect(chunk, windows);
            if (_enrollment is null) return RoleTimeline.NotEnrolled;
        }

        if (windows.Count == 0) return new RoleTimeline(Array.Empty<RoleSpan>(), true, _doubtful);

        var sims = windows.Select(w => Cos(_embedder.Embed(Slice(chunk, w.From, w.To).Samples), _enrollment)).ToArray();
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

    /// <summary>Tanıtma örneği: konuşma pencerelerinin temsil dilimleri (uç uca döşenir, örtüşmez). İmleçten önce
    /// başlayan dilimler (önceki parçayla örtüşen, zaten toplanmış ses) atlanır.</summary>
    private void Collect(AudioChunk chunk, List<Window> windows)
    {
        foreach (var w in windows)
        {
            var start = chunk.Start + Seconds(w.A);
            if (start < _enrollCursor) continue;
            _enrollAudio.AddRange(chunk.Samples.AsSpan(w.A, w.B - w.A).ToArray());
            _enrollCursor = chunk.Start + Seconds(w.B);
            if (_enrollAudio.Count >= _cfg.EnrollSeconds * Sr) break;
        }
        if (_enrollAudio.Count < _cfg.EnrollSeconds * Sr) return;

        var audio = _enrollAudio.ToArray();
        _enrollment = _embedder.Embed(audio);
        int half = audio.Length / 2;
        _doubtful = Cos(_embedder.Embed(audio[..half]), _embedder.Embed(audio[half..])) < _cfg.DoubtThreshold;
        _enrollAudio.Clear();
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
