using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Infrastructure.Vad;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Vad.Tests;

public class VadDiTests
{
    [Fact]
    public void AddSileroVad_ResolvesGateAndProvider()
    {
        var provider = new ServiceCollection()
            .AddApplicationCore()
            .AddSileroVad()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IVadGate>());
        Assert.NotNull(provider.GetRequiredService<IVadModelProvider>());
    }
}
