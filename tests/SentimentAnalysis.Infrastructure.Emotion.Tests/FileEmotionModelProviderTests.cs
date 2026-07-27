using System;
using System.IO;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class FileEmotionModelProviderTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-emo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ResolveModelPath_ReturnsPath_WhenFileExists()
    {
        var dir = NewTempDir();
        var file = Path.Combine(dir, "prosody-msp-dim.onnx");
        File.WriteAllText(file, "stub");
        var cfg = AppConfig.Default with { Emotion = AppConfig.Default.Emotion with { ModelDirectory = dir } };

        var provider = new FileEmotionModelProvider(cfg);
        Assert.Equal(file, provider.ResolveModelPath("prosody-msp-dim.onnx"));
    }

    [Fact]
    public void ResolveModelPath_Throws_WhenMissing()
    {
        var dir = NewTempDir();
        var cfg = AppConfig.Default with { Emotion = AppConfig.Default.Emotion with { ModelDirectory = dir } };
        var provider = new FileEmotionModelProvider(cfg);
        var ex = Assert.Throws<FileNotFoundException>(() => provider.ResolveModelPath("prosody-msp-dim.onnx"));
        Assert.Contains("prosody-msp-dim.onnx", ex.Message);
    }

    [Fact]
    public void DefaultModelDirectory_IsUnderApplicationData()
    {
        var def = FileEmotionModelProvider.DefaultModelDirectory();
        Assert.Contains("SentimentAnalysis", def);
        Assert.EndsWith("models", def);
    }
}
