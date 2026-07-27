using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Application.Tests;

/// <summary>
/// Dil ayarının Whisper'a nasıl aktarıldığı. Varsayılanın "tr" olması, İngilizce konuşmayı
/// zorla Türkçeye çözdürüp anlamsız transkript üretiyordu; varsayılan otomatik algılamaya alındı.
/// </summary>
public class SttLanguageConfigTests
{
    [Fact]
    public void Default_IsAutoDetect()
    {
        Assert.Null(AppConfig.Default.Stt.Language);
        Assert.Null(AppConfig.Default.Stt.WhisperLanguage);
    }

    [Theory]
    [InlineData("tr", "tr")]
    [InlineData("en", "en")]
    public void PinnedLanguage_IsPassedThrough(string configured, string expected)
    {
        var cfg = new SttConfig(Language: configured);
        Assert.Equal(expected, cfg.WhisperLanguage);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("AUTO")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AutoOrBlank_MeansNoPinnedLanguage(string? configured)
    {
        Assert.Null(new SttConfig(Language: configured).WhisperLanguage);
    }

    [Fact]
    public void ExecutionProvider_DefaultsByPlatform()
    {
        var resolved = AppConfig.Default.Emotion.ResolvedExecutionProvider;
        var expected = OperatingSystem.IsMacOS() ? "coreml" : "cpu";
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void ExecutionProvider_ExplicitValueWins()
    {
        var cfg = AppConfig.Default.Emotion with { ExecutionProvider = "cpu" };
        Assert.Equal("cpu", cfg.ResolvedExecutionProvider);
    }
}
