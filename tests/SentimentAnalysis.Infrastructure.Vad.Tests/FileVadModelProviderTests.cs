using System;
using System.IO;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Infrastructure.Vad;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Vad.Tests;

public class FileVadModelProviderTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-vad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ResolveModelPath_ReturnsPath_WhenExists()
    {
        var dir = NewTempDir();
        var file = Path.Combine(dir, "silero_vad.onnx");
        File.WriteAllText(file, "stub");
        var cfg = AppConfig.Default with { Vad = AppConfig.Default.Vad with { ModelDirectory = dir } };

        var provider = new FileVadModelProvider(cfg);
        Assert.Equal(file, provider.ResolveModelPath("silero_vad.onnx"));
    }

    [Fact]
    public void ResolveModelPath_Throws_WhenMissing()
    {
        var dir = NewTempDir();
        var cfg = AppConfig.Default with { Vad = AppConfig.Default.Vad with { ModelDirectory = dir } };
        var provider = new FileVadModelProvider(cfg);
        var ex = Assert.Throws<FileNotFoundException>(() => provider.ResolveModelPath("silero_vad.onnx"));
        Assert.Contains("silero_vad.onnx", ex.Message);
    }

    [Fact]
    public void DefaultModelDirectory_UnderApplicationData()
    {
        var def = FileVadModelProvider.DefaultModelDirectory();
        Assert.Contains("SentimentAnalysis", def);
        Assert.EndsWith("models", def);
    }
}
