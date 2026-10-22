using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Vision;

/// <summary>Görüntü modellerinin dosya yolu: <c>ModelDirectory ?? ~/Library/Application Support/SentimentAnalysis/models</c>.</summary>
public static class VisionModels
{
    public static string Resolve(VisionConfig cfg, string file) =>
        Path.Combine(cfg.ModelDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "SentimentAnalysis", "models"), file);

    internal static InferenceSession Open(VisionConfig cfg, string file)
    {
        var path = Resolve(cfg, file);
        if (!File.Exists(path)) throw new VideoSourceException($"Yüz modeli bulunamadı: {path}");
        using var options = new SessionOptions { IntraOpNumThreads = cfg.OnnxThreads };
        return new InferenceSession(path, options);
    }
}

/// <summary>YuNet (2023mar) girdi hazırlığı ve çıktı çözümlemesi — 11a §4'te kare üstüne çizilerek
/// doğrulanmış tarif. Girdi sabit 1×3×640×640; 640×360 kare üste yerleşir, altı sıfır kalır.</summary>
public static class YuNetDecoder
{
    public const int Size = 640;
    private static readonly int[] Strides = [8, 16, 32];

    /// <summary>NMS öncesi en yüksek skorlu aday sayısı (OpenCV FaceDetectorYN top_k ile aynı). 8400 çapanın
    /// hepsi eşiği geçse bile NMS karesel patlamasın.</summary>
    public const int MaxCandidates = 5000;

    public static IReadOnlyList<DetectedFace> Decode(IReadOnlyDictionary<string, float[]> outputs, float minScore, float nmsIou)
    {
        var candidates = new List<DetectedFace>();
        foreach (var s in Strides)
        {
            var cls = outputs[$"cls_{s}"];
            var obj = outputs[$"obj_{s}"];
            var bbox = outputs[$"bbox_{s}"];
            var kps = outputs[$"kps_{s}"];
            int cols = Size / s;
            for (int i = 0; i < cls.Length; i++)
            {
                // Kapalı kapı: NaN skor "eşiği geçmedi" sayılır (NaN < eşik false döndüğü için ters koşul).
                float score = MathF.Sqrt(Math.Clamp(cls[i], 0f, 1f) * Math.Clamp(obj[i], 0f, 1f));
                if (!(score >= minScore)) continue;
                int r = i / cols, c = i % cols;
                float cx = (c + bbox[i * 4]) * s, cy = (r + bbox[i * 4 + 1]) * s;
                float w = MathF.Exp(bbox[i * 4 + 2]) * s, h = MathF.Exp(bbox[i * 4 + 3]) * s;
                if (!float.IsFinite(cx) || !float.IsFinite(cy) || !(w > 0 && w <= 2 * Size) || !(h > 0 && h <= 2 * Size))
                    continue;
                var points = new float[10];
                for (int k = 0; k < 5; k++)
                {
                    points[2 * k] = (c + kps[i * 10 + 2 * k]) * s;
                    points[2 * k + 1] = (r + kps[i * 10 + 2 * k + 1]) * s;
                }
                candidates.Add(new DetectedFace(new FaceBox(cx - w / 2, cy - h / 2, w, h), score, points));
            }
        }

        // Skora göre azalan NMS.
        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (candidates.Count > MaxCandidates) candidates.RemoveRange(MaxCandidates, candidates.Count - MaxCandidates);
        var kept = new List<DetectedFace>();
        foreach (var f in candidates)
            if (kept.TrueForAll(k => IoU(k.Box, f.Box) <= nmsIou)) kept.Add(f);
        return kept;
    }

    public static float[] ToInput(VideoFrame frame)
    {
        if (frame.Width > Size || frame.Height > Size)
            throw new ArgumentException($"YuNet girdisi en çok {Size}×{Size}; gelen {frame.Width}×{frame.Height}.", nameof(frame));
        const int plane = Size * Size;
        var input = new float[3 * plane];
        var rgb = frame.Rgb24;
        for (int y = 0; y < frame.Height; y++)
        for (int x = 0; x < frame.Width; x++)
        {
            int p = (y * frame.Width + x) * 3, q = y * Size + x;
            input[q] = rgb[p + 2];             // B
            input[plane + q] = rgb[p + 1];     // G
            input[2 * plane + q] = rgb[p];     // R
        }
        return input;
    }

    private static float IoU(FaceBox a, FaceBox b)
    {
        float ix = Math.Max(0, Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X));
        float iy = Math.Max(0, Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y));
        float inter = ix * iy, union = a.W * a.H + b.W * b.H - inter;
        return union <= 0 ? 0 : inter / union;
    }
}

/// <summary>FER+ girdisi (11a §4): kutu merkezinde kenarı max(w,h)·1.15 olan kare, gri
/// (0.299R+0.587G+0.114B), 64×64 bilinear, 0–255 (normalize yok). Kare dışına taşan kısım kenar pikseliyle dolar.</summary>
public static class FerPlusPreprocess
{
    public const int Size = 64;

    public static float[] ToInput(VideoFrame frame, FaceBox box)
    {
        float side = Math.Max(box.W, box.H) * 1.15f;
        float left = box.CenterX - side / 2, top = box.CenterY - side / 2, step = side / Size;
        var input = new float[Size * Size];
        for (int j = 0; j < Size; j++)
        for (int i = 0; i < Size; i++)
        {
            float sx = left + (i + 0.5f) * step - 0.5f, sy = top + (j + 0.5f) * step - 0.5f;
            int x0 = (int)MathF.Floor(sx), y0 = (int)MathF.Floor(sy);
            float fx = sx - x0, fy = sy - y0;
            input[j * Size + i] =
                Gray(frame, x0, y0) * (1 - fx) * (1 - fy) + Gray(frame, x0 + 1, y0) * fx * (1 - fy) +
                Gray(frame, x0, y0 + 1) * (1 - fx) * fy + Gray(frame, x0 + 1, y0 + 1) * fx * fy;
        }
        return input;
    }

    private static float Gray(VideoFrame f, int x, int y)
    {
        x = Math.Clamp(x, 0, f.Width - 1);
        y = Math.Clamp(y, 0, f.Height - 1);
        int p = (y * f.Width + x) * 3;
        return 0.299f * f.Rgb24[p] + 0.587f * f.Rgb24[p + 1] + 0.114f * f.Rgb24[p + 2];
    }
}

/// <summary>YuNet ONNX yüz algılayıcı. Session kurucuda bir kez açılır (model yoksa anlamlı hata).</summary>
public sealed class YuNetFaceDetector : IFaceDetector, IDisposable
{
    private readonly VisionConfig _cfg;
    private readonly InferenceSession _session;

    public YuNetFaceDetector(VisionConfig cfg)
    {
        _cfg = cfg;
        _session = VisionModels.Open(cfg, cfg.YuNetModelFile);
    }

    public IReadOnlyList<DetectedFace> Detect(VideoFrame frame)
    {
        var input = new DenseTensor<float>(YuNetDecoder.ToInput(frame), [1, 3, YuNetDecoder.Size, YuNetDecoder.Size]);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor("input", input)]);
        var outputs = results.ToDictionary(r => r.Name, r => r.AsTensor<float>().ToArray());
        return YuNetDecoder.Decode(outputs, _cfg.MinFaceScore, _cfg.NmsIou);
    }

    public void Dispose() => _session.Dispose();
}

/// <summary>FER+ ONNX ifade modeli: 8 logit → softmax (FER+ sınıf sırası).</summary>
public sealed class FerPlusExpressionModel : IFaceExpressionModel, IDisposable
{
    private readonly InferenceSession _session;

    public FerPlusExpressionModel(VisionConfig cfg) => _session = VisionModels.Open(cfg, cfg.FerPlusModelFile);

    public float[] Classify(VideoFrame frame, FaceBox box)
    {
        var input = new DenseTensor<float>(FerPlusPreprocess.ToInput(frame, box), [1, 1, FerPlusPreprocess.Size, FerPlusPreprocess.Size]);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor("Input3", input)]);
        var logits = results.First().AsTensor<float>().ToArray();
        float max = logits.Max();
        var e = logits.Select(l => MathF.Exp(l - max)).ToArray();
        float sum = e.Sum();
        return e.Select(v => v / sum).ToArray();
    }

    public void Dispose() => _session.Dispose();
}
