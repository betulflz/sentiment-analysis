using SentimentAnalysis.Benchmark;
using Xunit;

namespace SentimentAnalysis.Benchmark.Tests;

public class BenchmarkOptionsTests
{
    [Fact]
    public void Quantization_CanSelectOfficialSmallModelFormat()
    {
        var options = BenchmarkOptions.Parse(Args("--model", "base", "--quantization", "q5_1"));
        Assert.Equal("q5_1", options.Quantization);
    }

    [Fact]
    public void Quantization_RejectsUnknownFormat() => Assert.Throws<ArgumentException>(() =>
        BenchmarkOptions.Parse(Args("--quantization", "../model")));

    private static string[] Args(params string[] extra) =>
        ["diarize", "--input", "sample.wav", "--output", "result.json", "--models", "models", .. extra];

    [Fact]
    public void Defaults_AreSequentialPilotWithTwoWhisperThreadsAndUnknownCount()
    {
        var options = BenchmarkOptions.Parse(Args());
        Assert.Equal(2, options.Threads);
        Assert.Equal(0, options.ExpectedSpeakers);
        Assert.Equal("large-v3-turbo", options.Model);
    }

    [Theory]
    [InlineData("--threads", "0")]
    [InlineData("--threads", "8")]
    [InlineData("--speakers", "9")]
    [InlineData("--speakers", "-1")]
    [InlineData("--threads", "two")]
    [InlineData("--model", "../untrusted")]
    [InlineData("--unknown", "1")]
    public void InvalidOptions_AreRejected(string key, string value) =>
        Assert.Throws<ArgumentException>(() => BenchmarkOptions.Parse(Args(key, value)));

    [Fact]
    public void DuplicateOptions_AreRejected() =>
        Assert.Throws<ArgumentException>(() => BenchmarkOptions.Parse(Args("--input", "other.wav")));

    [Fact]
    public void ModelsDirectoryWithSpaces_IsPreserved()
    {
        var options = BenchmarkOptions.Parse(["transcribe", "--input", "a.wav", "--output", "b.json",
                                              "--models", "/a folder/models"]);
        Assert.Equal("/a folder/models", options.Models);
    }
}
