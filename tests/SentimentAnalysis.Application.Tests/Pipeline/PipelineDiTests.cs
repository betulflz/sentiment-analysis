using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Pipeline;

public class PipelineDiTests
{
    private sealed class DummyStt : ISpeechToText { public Task<Transcript> TranscribeAsync(AudioChunk c, CancellationToken ct) => Task.FromResult(new Transcript("", DetectedLanguage.Other, 0)); }
    private sealed class DummyProsody : IProsodyEmotionModel { public Task<ProsodyEmotion> AnalyzeAsync(AudioChunk c, CancellationToken ct) => Task.FromResult(new ProsodyEmotion(ValenceArousal.Neutral, 0)); }
    private sealed class DummyText : ITextEmotionModel { public Task<TextEmotion> AnalyzeAsync(Transcript t, CancellationToken ct) => Task.FromResult(new TextEmotion(ValenceArousal.Neutral, 0)); }
    private sealed class DummyVad : IVadGate { public bool HasSpeech(AudioChunk c) => true; }

    [Fact]
    public void AddPipeline_ResolvesEmotionPipelineAndStore()
    {
        var provider = new ServiceCollection()
            .AddApplicationCore()
            .AddFusion()
            .AddSingleton<ISpeechToText, DummyStt>()
            .AddSingleton<IProsodyEmotionModel, DummyProsody>()
            .AddSingleton<ITextEmotionModel, DummyText>()
            .AddSingleton<IVadGate, DummyVad>()
            .AddPipeline()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<EmotionPipeline>());
        Assert.NotNull(provider.GetRequiredService<ISessionStore>());
        Assert.NotNull(provider.GetRequiredService<IChunkProcessor>());
    }
}
