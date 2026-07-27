using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Infrastructure.Stt;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Stt.Tests;

public class SttDiTests
{
    [Fact]
    public void AddStt_ResolvesSpeechToTextAndModelProvider()
    {
        var provider = new ServiceCollection()
            .AddApplicationCore()
            .AddStt()
            .BuildServiceProvider();

        // Model yüklemeden çözülebilmeli (factory lazy).
        Assert.NotNull(provider.GetRequiredService<ISpeechToText>());
        Assert.NotNull(provider.GetRequiredService<ISttModelProvider>());
    }
}
