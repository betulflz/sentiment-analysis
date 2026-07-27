using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class EmotionDiTests
{
    [Fact]
    public void AddProsodyEmotion_ResolvesModelAndProvider()
    {
        var provider = new ServiceCollection()
            .AddApplicationCore()
            .AddProsodyEmotion()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IProsodyEmotionModel>());
        Assert.NotNull(provider.GetRequiredService<IEmotionModelProvider>());
    }
}
