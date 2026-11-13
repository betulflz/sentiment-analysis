using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Application.Runtime;

namespace SentimentAnalysis.Desktop.Tests;

public sealed class SpeakerModelLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Speaker_job_waits_for_live_models_and_releases_its_models(bool fail)
    {
        await using var queue = new ModelWorkQueue();
        using var live = await queue.AcquireAsync(default);
        var state = new DiarizationState(SentimentAnalysis.Application.Configuration.AppConfig.Default) { Enabled = true };
        var model = new FakeSpeaker { Fail = fail };
        var created = false;
        var speaker = new ResourceManagedSpeakerAnalysis(state, queue, () =>
        {
            created = true;
            return new OwnedModelSession<ISpeakerAnalysis>(model, model);
        });
        var run = speaker.RunAsync(Guid.NewGuid(), 2, default);
        Assert.False(created);
        live.Dispose();
        if (fail) await Assert.ThrowsAsync<InvalidOperationException>(() => run);
        else await run;
        Assert.True(model.Disposed);
        using var next = await queue.AcquireAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Disabled_speaker_does_not_create_models()
    {
        await using var queue = new ModelWorkQueue();
        var speaker = new ResourceManagedSpeakerAnalysis(new DiarizationState(SentimentAnalysis.Application.Configuration.AppConfig.Default), queue,
            () => throw new Exception("Must not create model"));
        Assert.Equal(SpeakerAnalysisResult.Disabled, await speaker.RunAsync(Guid.NewGuid(), 0, default));
    }

    private sealed class FakeSpeaker : ISpeakerAnalysis, IDisposable
    {
        public bool Fail, Disposed;
        public bool Enabled => true;
        public Task<SpeakerAnalysisResult> RunAsync(Guid id, int count, CancellationToken ct) =>
            Fail ? Task.FromException<SpeakerAnalysisResult>(new InvalidOperationException())
                : Task.FromResult(SpeakerAnalysisResult.Disabled);
        public void Dispose() => Disposed = true;
    }
}
