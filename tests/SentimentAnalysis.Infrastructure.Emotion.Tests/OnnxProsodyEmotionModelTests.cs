using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class OnnxProsodyEmotionModelTests
{
    private sealed class UnusedModelProvider : IEmotionModelProvider
    {
        public string ResolveModelPath(string fileName) =>
            throw new InvalidOperationException("guard'dan önce model çözülmemeliydi");
    }

    private sealed class FixedModelProvider(string path) : IEmotionModelProvider
    {
        public string ResolveModelPath(string fileName) => path;
    }

    [Fact]
    public async Task AnalyzeAsync_Throws_ForNon16kHz()
    {
        using var model = new OnnxProsodyEmotionModel(AppConfig.Default, new UnusedModelProvider());
        var chunk = new AudioChunk(new float[8000], SampleRate: 8000, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<ArgumentException>(() => model.AnalyzeAsync(chunk, CancellationToken.None));
    }

    [Fact]
    public async Task AnalyzeAsync_AfterDispose_Throws()
    {
        var model = new OnnxProsodyEmotionModel(AppConfig.Default, new UnusedModelProvider());
        model.Dispose();
        var chunk = new AudioChunk(new float[16000], SampleRate: 16000, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.AnalyzeAsync(chunk, CancellationToken.None));
    }

    [SkippableFact]
    public async Task AnalyzeAsync_RealModel_ProducesValenceArousal()
    {
        var modelPath = Environment.GetEnvironmentVariable("PROSODY_MODEL_PATH");
        var wavPath = Environment.GetEnvironmentVariable("PROSODY_SAMPLE_WAV");
        Skip.If(string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath),
            "PROSODY_MODEL_PATH ayarlı değil; gerçek-model testi atlandı.");
        Skip.If(string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath),
            "PROSODY_SAMPLE_WAV ayarlı değil; örnek ses yok.");

        var samples = WavReader.ReadMono16(wavPath!);
        // CPU EP ile deterministik test (CoreML EP makineye bağlı).
        var cfg = AppConfig.Default with { Emotion = AppConfig.Default.Emotion with { ExecutionProvider = "cpu" } };
        using var model = new OnnxProsodyEmotionModel(cfg, new FixedModelProvider(modelPath!));
        var chunk = new AudioChunk(samples, 16000, TimeSpan.Zero, TimeSpan.FromSeconds(samples.Length / 16000.0));

        var p = await model.AnalyzeAsync(chunk, CancellationToken.None);

        Assert.InRange(p.Va.Valence, -1.0, 1.0);
        Assert.InRange(p.Va.Arousal, 0.0, 1.0);
        Assert.InRange(p.ModelConfidence, 0.0, 1.0);
        // Spike referansı: sakin-olumlu Türkçe örnekte valence hafif pozitif (~0.24), arousal ~0.59.
        Assert.True(p.Va.Valence > 0.0, $"örnek hafif olumlu beklenir, {p.Va.Valence}");
    }

    /// <summary>Kazancın KONFİGÜRASYONDAN motora kadar bağlı olduğunu uçtan uca doğrular.
    /// <c>ProsodyOutputMapper</c>'ın birim testleri matematiği kanıtlar; bu test motorun o
    /// matematiği gerçekten config'ten beslediğini kanıtlar (kablo kopukluğu sessiz kalmasın).</summary>
    [SkippableFact]
    public async Task AnalyzeAsync_RealModel_KazancCiktiyiGenisletir()
    {
        var modelPath = Environment.GetEnvironmentVariable("PROSODY_MODEL_PATH");
        var wavPath = Environment.GetEnvironmentVariable("PROSODY_SAMPLE_WAV");
        Skip.If(string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath),
            "PROSODY_MODEL_PATH ayarlı değil; gerçek-model testi atlandı.");
        Skip.If(string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath),
            "PROSODY_SAMPLE_WAV ayarlı değil; örnek ses yok.");

        var samples = WavReader.ReadMono16(wavPath!);
        var chunk = new AudioChunk(samples, 16000, TimeSpan.Zero, TimeSpan.FromSeconds(samples.Length / 16000.0));

        var flat = await AnalyzeWithGain(modelPath!, chunk, valenceGain: 1.0, arousalGain: 1.0);
        var expanded = await AnalyzeWithGain(modelPath!, chunk, valenceGain: 2.5, arousalGain: 2.5);

        Assert.True(Math.Abs(expanded.Va.Valence) > Math.Abs(flat.Va.Valence),
            $"kazançlı valence daha yayılmış olmalı: {expanded.Va.Valence} vs {flat.Va.Valence}");
        Assert.True(Math.Abs(expanded.Va.Arousal - 0.5) > Math.Abs(flat.Va.Arousal - 0.5),
            $"kazançlı arousal daha yayılmış olmalı: {expanded.Va.Arousal} vs {flat.Va.Arousal}");
    }

    private static async Task<ProsodyEmotion> AnalyzeWithGain(
        string modelPath, AudioChunk chunk, double valenceGain, double arousalGain)
    {
        var cfg = AppConfig.Default with
        {
            Emotion = AppConfig.Default.Emotion with
            {
                ExecutionProvider = "cpu",
                ProsodyValenceGain = valenceGain,
                ProsodyArousalGain = arousalGain,
            },
        };
        using var model = new OnnxProsodyEmotionModel(cfg, new FixedModelProvider(modelPath));
        return await model.AnalyzeAsync(chunk, CancellationToken.None);
    }
}
