using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Fusion;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class FusionDiTests
{
    [Fact]
    public void AddFusion_ResolvesFusionAndAggregator()
    {
        var provider = new ServiceCollection()
            .AddApplicationCore()
            .AddFusion()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IEmotionFusion>());
        Assert.NotNull(provider.GetRequiredService<IMeetingAggregator>());
    }

    [Fact]
    public void ResolvedFusion_IsUsable()
    {
        var provider = new ServiceCollection()
            .AddApplicationCore()
            .AddFusion()
            .BuildServiceProvider();

        var fusion = provider.GetRequiredService<IEmotionFusion>();
        var r = fusion.Fuse(
            new Core.TextEmotion(new Core.ValenceArousal(0.8, 0.5), 0.9),
            new Core.ProsodyEmotion(new Core.ValenceArousal(0.8, 0.5), 0.9),
            new Core.QualitySignals(20, 1.0, System.TimeSpan.FromSeconds(10), 20, 0.0));
        Assert.Equal(Core.Emotion.Mutlu, r.PrimaryEmotion);
    }
}
