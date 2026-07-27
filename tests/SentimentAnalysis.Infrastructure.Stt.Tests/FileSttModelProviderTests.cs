using System;
using System.IO;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Infrastructure.Stt;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Stt.Tests;

public class FileSttModelProviderTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-stt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ResolveModelPath_ReturnsPath_WhenFileExists()
    {
        var dir = NewTempDir();
        var file = Path.Combine(dir, "ggml-large-v3-turbo-q5_0.bin");
        File.WriteAllText(file, "stub");
        var cfg = AppConfig.Default with { Stt = AppConfig.Default.Stt with { ModelDirectory = dir } };

        var provider = new FileSttModelProvider(cfg);
        Assert.Equal(file, provider.ResolveModelPath("large-v3-turbo"));
    }

    [Fact]
    public void ResolveModelPath_Throws_WhenFileMissing()
    {
        var dir = NewTempDir(); // boş
        var cfg = AppConfig.Default with { Stt = AppConfig.Default.Stt with { ModelDirectory = dir } };
        var provider = new FileSttModelProvider(cfg);

        var ex = Assert.Throws<FileNotFoundException>(() => provider.ResolveModelPath("large-v3-turbo"));
        Assert.Contains("ggml-large-v3-turbo-q5_0.bin", ex.Message);
    }

    [Fact]
    public void DefaultModelDirectory_IsUnderApplicationData()
    {
        var def = FileSttModelProvider.DefaultModelDirectory();
        Assert.Contains("SentimentAnalysis", def);
        Assert.EndsWith("models", def);
    }
}
