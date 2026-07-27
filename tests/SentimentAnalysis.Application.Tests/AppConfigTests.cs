using SentimentAnalysis.Application.Configuration;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class AppConfigTests
{
    [Fact]
    public void Default_HasSaneFusionAndStorageDefaults()
    {
        var config = AppConfig.Default;

        // Arousal ağırlıkla sesten, valence ağırlıkla metinden (master §5).
        Assert.True(config.Fusion.AudioArousalWeight > config.Fusion.TextArousalWeight);
        Assert.True(config.Fusion.TextValenceWeight > config.Fusion.AudioValenceWeight);

        // Ses saklama varsayılanı 7 gün (master §D13).
        Assert.Equal(7, config.Storage.AudioRetentionDays);
    }
}
