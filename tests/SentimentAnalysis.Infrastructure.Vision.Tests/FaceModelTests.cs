using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Vision;

namespace SentimentAnalysis.Infrastructure.Vision.Tests;

public class FaceModelPureTests
{
    private static readonly int[] Strides = [8, 16, 32];

    /// <summary>Tüm adımlar için sıfır dolu YuNet çıktıları (skor 0 → hiçbir çapa geçmez).</summary>
    private static Dictionary<string, float[]> EmptyOutputs()
    {
        var o = new Dictionary<string, float[]>();
        foreach (var s in Strides)
        {
            int n = (640 / s) * (640 / s);
            o[$"cls_{s}"] = new float[n];
            o[$"obj_{s}"] = new float[n];
            o[$"bbox_{s}"] = new float[n * 4];
            o[$"kps_{s}"] = new float[n * 10];
        }
        return o;
    }

    private static void SetAnchor(Dictionary<string, float[]> o, int stride, int row, int col, float score, float[] bbox)
    {
        int i = row * (640 / stride) + col;
        o[$"cls_{stride}"][i] = score;
        o[$"obj_{stride}"][i] = score;
        Array.Copy(bbox, 0, o[$"bbox_{stride}"], i * 4, 4);
    }

    [Fact]
    public void Decode_SingleSyntheticAnchor()
    {
        var o = EmptyOutputs();
        SetAnchor(o, 8, 10, 20, 1f, [0, 0, MathF.Log(2), MathF.Log(2)]);

        var faces = YuNetDecoder.Decode(o, 0.6f, 0.3f);

        var f = Assert.Single(faces);
        Assert.Equal(160f, f.Box.CenterX, 3);
        Assert.Equal(80f, f.Box.CenterY, 3);
        Assert.Equal(16f, f.Box.W, 3);
        Assert.Equal(16f, f.Box.H, 3);
        Assert.Equal(1f, f.Score, 3);
        Assert.Equal(10, f.Kps.Length);
        Assert.Equal(160f, f.Kps[0], 3); // kps 0 → çapa noktası (c·s, r·s)
        Assert.Equal(80f, f.Kps[1], 3);
    }

    [Fact]
    public void Decode_BelowThreshold_Empty()
    {
        var o = EmptyOutputs();
        SetAnchor(o, 16, 5, 5, 0.5f, [0, 0, 0, 0]); // sqrt(.5·.5) = .5 < .6
        Assert.Empty(YuNetDecoder.Decode(o, 0.6f, 0.3f));
    }

    [Fact]
    public void Decode_Nms_KeepsHigher()
    {
        var o = EmptyOutputs();
        var big = new[] { 0f, 0f, MathF.Log(4), MathF.Log(4) }; // 32×32 kutu
        SetAnchor(o, 8, 10, 20, 0.8f, big);
        SetAnchor(o, 8, 10, 21, 0.95f, big); // 8 px kaymış, IoU ≈ .6 → bastırılır
        SetAnchor(o, 8, 40, 60, 0.7f, big);  // uzakta, kalır

        var faces = YuNetDecoder.Decode(o, 0.6f, 0.3f);

        Assert.Equal(2, faces.Count);
        Assert.Equal(0.95f, faces[0].Score, 3);
        Assert.Equal(21 * 8f, faces[0].Box.CenterX, 3);
        Assert.Equal(0.7f, faces[1].Score, 3);
    }

    [Fact]
    public void Decode_NonFiniteOutputs_FailClosed()
    {
        var o = EmptyOutputs();
        SetAnchor(o, 8, 10, 20, float.NaN, [0, 0, 0, 0]);
        SetAnchor(o, 8, 30, 30, 1f, [float.NaN, 0, float.PositiveInfinity, 0]);
        Assert.Empty(YuNetDecoder.Decode(o, 0.6f, 0.3f));
    }

    [Fact]
    public void Decode_AllAnchorsAboveThreshold_CapsCandidates()
    {
        var o = EmptyOutputs();
        foreach (var k in o.Keys.Where(k => k.StartsWith("cls_") || k.StartsWith("obj_")).ToList())
            Array.Fill(o[k], 1f);
        var faces = YuNetDecoder.Decode(o, 0.6f, 0.3f);
        Assert.InRange(faces.Count, 1, YuNetDecoder.MaxCandidates);
    }

    [Fact]
    public void ToInput_BgrOrderAndPadding()
    {
        var rgb = new byte[640 * 360 * 3];
        int x = 100, y = 50, p = (y * 640 + x) * 3;
        rgb[p] = 255; // kırmızı
        for (int i = (359 * 640) * 3; i < rgb.Length; i++) rgb[i] = 200; // son satır dolu

        var input = YuNetDecoder.ToInput(new VideoFrame(rgb, 640, 360, TimeSpan.Zero));

        const int plane = 640 * 640;
        Assert.Equal(3 * plane, input.Length);
        Assert.Equal(0f, input[0 * plane + y * 640 + x]);   // B
        Assert.Equal(0f, input[1 * plane + y * 640 + x]);   // G
        Assert.Equal(255f, input[2 * plane + y * 640 + x]); // R
        Assert.Equal(200f, input[0 * plane + 359 * 640 + 5]);
        Assert.Equal(0f, input[0 * plane + 400 * 640 + 5]);
        Assert.Equal(0f, input[2 * plane + 639 * 640 + 639]);
    }

    [Fact]
    public void FerPreprocess_UniformGray_ConstantInput()
    {
        var rgb = new byte[640 * 360 * 3];
        Array.Fill(rgb, (byte)100);
        var frame = new VideoFrame(rgb, 640, 360, TimeSpan.Zero);

        var input = FerPlusPreprocess.ToInput(frame, new FaceBox(200, 100, 60, 80));

        Assert.Equal(64 * 64, input.Length);
        Assert.All(input, v => Assert.Equal(100f, v, 2));
    }

    [Fact]
    public void FerPreprocess_BoxAtEdge_DoesNotThrow()
    {
        var frame = new VideoFrame(new byte[640 * 360 * 3], 640, 360, TimeSpan.Zero);
        var input = FerPlusPreprocess.ToInput(frame, new FaceBox(610, 340, 40, 40));
        Assert.Equal(64 * 64, input.Length);
    }

    [Fact]
    public void CorruptModel_ThrowsVideoSourceException()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var cfg = new VisionConfig(ModelDirectory: dir);
            File.WriteAllText(Path.Combine(dir, cfg.YuNetModelFile), "yarım indirme");
            var ex = Assert.Throws<VideoSourceException>(() => new YuNetFaceDetector(cfg));
            Assert.Contains("Yüz modeli yüklenemedi", ex.Message);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void MissingModel_ThrowsVideoSourceException()
    {
        var cfg = new VisionConfig(ModelDirectory: "/olmayan/klasor");
        var ex = Assert.Throws<VideoSourceException>(() => new YuNetFaceDetector(cfg));
        Assert.Contains("Yüz modeli bulunamadı", ex.Message);
        Assert.Throws<VideoSourceException>(() => new FerPlusExpressionModel(cfg));
    }
}

/// <summary>Gerçek YuNet + FER+ ile. Model ya da SENTIMENT_TEST_VIDEO (iki kişilik mülakat) yoksa atlanır.</summary>
public class FaceModelIntegrationTests
{
    private static readonly VisionConfig Cfg = new();
    private static string? Video => Environment.GetEnvironmentVariable("SENTIMENT_TEST_VIDEO");

    private static bool ModelsPresent() =>
        File.Exists(VisionModels.Resolve(Cfg, Cfg.YuNetModelFile)) &&
        File.Exists(VisionModels.Resolve(Cfg, Cfg.FerPlusModelFile));

    private static async Task<List<VideoFrame>> AnalysisFrames(int count)
    {
        var frames = new List<VideoFrame>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        int n = 0;
        await foreach (var f in FfmpegVideoCaptureSource.ForFile(Video!, Cfg).CaptureAsync(cts.Token))
        {
            if (n++ % Cfg.AnalyzeEvery == 0) frames.Add(f);
            if (frames.Count == count) break;
        }
        return frames;
    }

    [SkippableFact]
    public async Task RealModels_InterviewVideo_TwoFaces()
    {
        Skip.If(string.IsNullOrEmpty(Video) || !File.Exists(Video), "SENTIMENT_TEST_VIDEO yok");
        Skip.IfNot(ModelsPresent(), "yüz modelleri yok");
        using var detector = new YuNetFaceDetector(Cfg);

        var frames = await AnalysisFrames(20);

        Assert.Equal(20, frames.Count);
        foreach (var f in frames)
        {
            var faces = detector.Detect(f);
            Assert.Equal(2, faces.Count);
            Assert.All(faces, d => Assert.True(d.Score >= 0.6f));
        }
    }

    [SkippableFact]
    public async Task RealModels_Classify_SumsToOne()
    {
        Skip.If(string.IsNullOrEmpty(Video) || !File.Exists(Video), "SENTIMENT_TEST_VIDEO yok");
        Skip.IfNot(ModelsPresent(), "yüz modelleri yok");
        using var detector = new YuNetFaceDetector(Cfg);
        using var fer = new FerPlusExpressionModel(Cfg);

        var frame = (await AnalysisFrames(1))[0];
        var face = detector.Detect(frame)[0];
        var probs = fer.Classify(frame, face.Box);

        Assert.Equal(8, probs.Length);
        Assert.Equal(1f, probs.Sum(), 3);
        Assert.All(probs, p => Assert.InRange(p, 0f, 1f));
    }
}
