using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class OnnxTextEmotionModelTests
{
    private sealed class FixedModelProvider(string dir) : IEmotionModelProvider
    {
        public string ResolveModelPath(string fileName) => Path.Combine(dir, fileName);
    }

    private sealed class UnusedModelProvider : IEmotionModelProvider
    {
        public string ResolveModelPath(string fileName) =>
            throw new InvalidOperationException("dispose sonrası model çözülmemeliydi");
    }

    [Fact]
    public async Task AnalyzeAsync_AfterDispose_Throws()
    {
        var model = new OnnxTextEmotionModel(AppConfig.Default, new UnusedModelProvider());
        model.Dispose();
        var t = new Transcript("merhaba", DetectedLanguage.Turkish, -0.2);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.AnalyzeAsync(t, CancellationToken.None));
    }

    [SkippableFact]
    public async Task AnalyzeAsync_RealModel_ProducesValenceArousal()
    {
        // TEXT_MODEL_DIR = hem text-emotion-xlmr.onnx hem sentencepiece.bpe.model'i içeren klasör.
        var dir = Environment.GetEnvironmentVariable("TEXT_MODEL_DIR");
        Skip.If(string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir),
            "TEXT_MODEL_DIR ayarlı değil; gerçek-model testi atlandı.");
        Skip.If(!File.Exists(Path.Combine(dir!, "text-emotion-xlmr.onnx")) ||
                !File.Exists(Path.Combine(dir!, "sentencepiece.bpe.model")),
            "text-emotion-xlmr.onnx / sentencepiece.bpe.model bulunamadı.");

        var cfg = AppConfig.Default with { Emotion = AppConfig.Default.Emotion with { ExecutionProvider = "cpu" } };
        using var model = new OnnxTextEmotionModel(cfg, new FixedModelProvider(dir!));

        var positive = new Transcript("Bugün harika bir gündü, çok mutluyum!", DetectedLanguage.Turkish, -0.15);
        var negative = new Transcript("Bu durumdan gerçekten çok korkuyorum ve üzgünüm.", DetectedLanguage.Turkish, -0.15);

        var pe = await model.AnalyzeAsync(positive, CancellationToken.None);
        var ne = await model.AnalyzeAsync(negative, CancellationToken.None);

        Assert.InRange(pe.Va.Valence, -1.0, 1.0);
        Assert.InRange(pe.Va.Arousal, 0.0, 1.0);
        Assert.InRange(pe.ModelConfidence, 0.0, 1.0);
        // Yön kontrolü: olumlu metin olumsuzdan daha yüksek valence vermeli (model çalışıyor).
        Assert.True(pe.Va.Valence > ne.Va.Valence,
            $"olumlu ({pe.Va.Valence:F3}) > olumsuz ({ne.Va.Valence:F3}) beklenir");
    }
}
