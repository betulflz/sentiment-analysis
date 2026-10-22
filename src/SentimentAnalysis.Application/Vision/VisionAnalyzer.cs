using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Vision;

/// <summary>Görüntü hattı: kaynak → (her N'inci kare) yüz bul → takip et → ifade + baş yönü →
/// <see cref="VisionTimeline"/>. Ses hattına paralel koşar. Bir karedeki model hatası yalnızca o kareyi
/// atlar (<see cref="ErrorCount"/>); kaynak hatası (izin reddi, ffmpeg yok) yukarı fırlar. İptal,
/// normal bitiştir — "Durdur" görüntü hattını bekletmez.</summary>
public sealed class VisionAnalyzer
{
    private readonly IFaceDetector _detector;
    private readonly IFaceExpressionModel _expression;
    private readonly VisionTimeline _timeline;
    private readonly VisionConfig _cfg;
    private readonly FaceTracker _tracker = new();
    private volatile VideoFrame? _latestFrame;
    private volatile IReadOnlyList<(int Index, FaceBox Box)> _latestFaces = Array.Empty<(int, FaceBox)>();
    private int _errors;

    public VisionAnalyzer(IFaceDetector detector, IFaceExpressionModel expression, VisionTimeline timeline, VisionConfig cfg)
    {
        _detector = detector;
        _expression = expression;
        _timeline = timeline;
        _cfg = cfg;
    }

    /// <summary>Son alınan kare (11d önizlemesi için).</summary>
    public VideoFrame? LatestFrame => _latestFrame;

    /// <summary>Son analiz edilen karedeki yüzler.</summary>
    public IReadOnlyList<(int Index, FaceBox Box)> LatestFaces => _latestFaces;

    public int ErrorCount => Volatile.Read(ref _errors);

    public VisionTimeline Timeline => _timeline;

    public async Task RunAsync(IVideoCaptureSource source, CancellationToken ct)
    {
        // Önceki oturumun izleri ve gözlemleri yeni oturuma SIZMAMALI (aynı VisionAnalyzer/VisionTimeline
        // birden çok oturumda yeniden kullanılabilir — composition root singleton deseni).
        _tracker.Reset();
        _timeline.Clear();
        int n = 0;
        try
        {
            await foreach (var frame in source.CaptureAsync(ct).WithCancellation(ct).ConfigureAwait(false))
            {
                _latestFrame = frame;
                if (n++ % Math.Max(1, _cfg.AnalyzeEvery) != 0) continue;
                Analyze(frame);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // "Durdur": normal bitiş.
        }
        finally
        {
            // Son kare, oturum bittikten sonra bellekte sızıntı gibi durmasın — görüntü diske
            // yazılmıyor (spec §4), bitmiş bir oturumun karesini bellekte tutmak da aynı ilkeyi ihlal eder.
            _latestFrame = null;
            _latestFaces = Array.Empty<(int, FaceBox)>();
        }
    }

    private void Analyze(VideoFrame frame)
    {
        try
        {
            var tracked = _tracker.Assign(_detector.Detect(frame));
            var observations = tracked
                .Select(t => new FaceObservation(t.Index, frame.Time, t.Face.Box,
                    FaceSignals.Yaw(t.Face.Kps), _expression.Classify(frame, t.Face.Box)))
                .ToArray();
            _timeline.AddFrame(frame.Time, observations);
            _latestFaces = tracked.Select(t => (t.Index, t.Face.Box)).ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Interlocked.Increment(ref _errors);
        }
    }
}
