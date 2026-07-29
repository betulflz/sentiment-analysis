using SentimentAnalysis.Application.Configuration;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class AppConfigTests
{
    [Fact]
    public void Default_HasSaneFusionAndStorageDefaults()
    {
        var config = AppConfig.Default;

        // Tamamen tona dayalı füzyon: hem arousal hem valence sesten (master §5, tona-dayalı revizyon).
        Assert.True(config.Fusion.AudioArousalWeight > config.Fusion.TextArousalWeight);
        Assert.True(config.Fusion.AudioValenceWeight > config.Fusion.TextValenceWeight);

        // Ses saklama varsayılanı 7 gün (master §D13).
        Assert.Equal(7, config.Storage.AudioRetentionDays);
    }

    [Fact]
    public void Default_DistributionTemperature_IsPositive()
    {
        Assert.True(AppConfig.Default.Fusion.DistributionTemperature > 0.0);
    }
}
