using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class TextEmotionDiTests
{
    [Fact]
    public void AddTextEmotion_ResolvesTextModelAndProvider()
    {
        var provider = new ServiceCollection()
            .AddApplicationCore()
            .AddTextEmotion()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ITextEmotionModel>());
        Assert.NotNull(provider.GetRequiredService<IEmotionModelProvider>());
    }

    [Fact]
    public void AddProsodyAndText_BothResolve_SingleProvider()
    {
        var provider = new ServiceCollection()
            .AddApplicationCore()
            .AddProsodyEmotion()
            .AddTextEmotion()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IProsodyEmotionModel>());
        Assert.NotNull(provider.GetRequiredService<ITextEmotionModel>());
        // IEmotionModelProvider iki çağrıda da kaydedilir ama tek instance çözülür (TryAdd).
        Assert.Single(provider.GetServices<IEmotionModelProvider>());
    }
}
