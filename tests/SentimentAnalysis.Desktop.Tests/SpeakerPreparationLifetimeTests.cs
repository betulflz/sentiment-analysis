using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Application.Runtime;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public sealed class SpeakerPreparationLifetimeTests
{
    private sealed class Decoder(float[] audio) : IAudioFileDecoder
    {
        public Task<float[]> DecodeMono16kAsync(string path, TimeSpan maxDuration, CancellationToken ct) => Task.FromResult(audio);
    }
    private sealed class Diarizer(Func<CancellationToken, Task<IReadOnlyList<SpeakerSegment>>> f) : IDiarizer
    {
        public Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(float[] mono16k, int expectedSpeakers, CancellationToken ct) => f(ct);
    }
    private sealed class Owner : IDisposable { public bool Disposed; public void Dispose() => Disposed = true; }

    private static (ResourceManagedSpeakerPreparation Prep, List<Owner> Owners) Make(Func<CancellationToken, Task<IReadOnlyList<SpeakerSegment>>> f, ModelWorkQueue q)
    {
        var owners = new List<Owner>();
        var prep = new ResourceManagedSpeakerPreparation(new Decoder(new float[16000 * 5]), q, () =>
        {
            var o = new Owner(); owners.Add(o);
            return new OwnedModelSession<SpeakerPreparation>(new SpeakerPreparation(new Diarizer(f)), o);
        }, AppConfig.Default);
        return (prep, owners);
    }

    [Fact]
    public async Task Basari_OturumKapanir()
    {
        await using var q = new ModelWorkQueue();
        var (prep, owners) = Make(_ => Task.FromResult<IReadOnlyList<SpeakerSegment>>(new[] {
            new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(2)),
            new SpeakerSegment(new SpeakerId(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)) }), q);
        var r = await prep.PrepareAsync("x.wav", 2, default);
        Assert.Equal(DiarizationOutcome.Completed, r.Outcome);
        Assert.True(Assert.Single(owners).Disposed);
    }

    [Fact]
    public async Task Hata_OturumKapanir_Failed()
    {
        await using var q = new ModelWorkQueue();
        var (prep, owners) = Make(_ => throw new InvalidOperationException(), q);
        var r = await prep.PrepareAsync("x.wav", 2, default);
        Assert.Equal(DiarizationOutcome.Failed, r.Outcome);
        Assert.True(Assert.Single(owners).Disposed);
    }

    [Fact]
    public async Task Iptal_OturumKapanir()
    {
        await using var q = new ModelWorkQueue();
        using var cts = new CancellationTokenSource();
        var (prep, owners) = Make(async ct => { cts.Cancel(); await Task.Delay(Timeout.Infinite, ct); return Array.Empty<SpeakerSegment>(); }, q);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prep.PrepareAsync("x.wav", 2, cts.Token));
        Assert.True(Assert.Single(owners).Disposed);
    }
}
