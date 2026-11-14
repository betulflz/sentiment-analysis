using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

public class SpeakerPreparationTests
{
    private sealed class FakeDiarizer(Func<IReadOnlyList<SpeakerSegment>> f) : IDiarizer
    {
        public Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(float[] mono16k, int expectedSpeakers, CancellationToken ct) => Task.FromResult(f());
    }
    private static SpeakerSegment Seg(int s, double a, double b) => new(s < 0 ? SpeakerId.Unknown : new SpeakerId(s), TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b));
    private static Task<SpeakerPreparationResult> Run(params SpeakerSegment[] segs) =>
        new SpeakerPreparation(new FakeDiarizer(() => segs)).RunAsync(new float[16000 * 60], 2, false, default);

    [Fact]
    public async Task IkiKonusmaci_SecenekleriSureSirali()
    {
        var r = await Run(Seg(0, 0, 10), Seg(1, 10, 40), Seg(0, 40, 50));
        Assert.Equal(DiarizationOutcome.Completed, r.Outcome);
        Assert.Equal(new[] { 1, 0 }, r.Choices.Select(c => c.Speaker.Index));
        Assert.Equal(30, r.Choices[0].TalkSeconds, 3);
    }

    [Fact]
    public async Task Unknown_SeceneklereGirmez()
    {
        var r = await Run(Seg(0, 0, 20), Seg(-1, 20, 22), Seg(1, 22, 40));
        Assert.DoesNotContain(r.Choices, c => c.Speaker == SpeakerId.Unknown);
    }

    [Fact]
    public async Task Ornek_EnUzunBolgeOrtasi_En5Sn()
    {
        var r = await Run(Seg(0, 0, 3), Seg(0, 20, 40), Seg(1, 3, 20));
        var a = r.Choices.Single(c => c.Speaker.Index == 0);
        Assert.Equal(TimeSpan.FromSeconds(27.5), a.SampleStart);
        Assert.Equal(TimeSpan.FromSeconds(32.5), a.SampleEnd);
    }

    [Fact]
    public async Task TekKonusmaci_IkidenAzSecenek()
    {
        var r = await Run(Seg(0, 0, 50));
        Assert.Equal(DiarizationOutcome.Completed, r.Outcome);
        Assert.True(r.Choices.Count < 2);
    }

    [Fact]
    public async Task DiarizerHatasi_Failed()
    {
        var r = await new SpeakerPreparation(new FakeDiarizer(() => throw new InvalidOperationException("model yok")))
            .RunAsync(new float[16000], 2, false, default);
        Assert.Equal(DiarizationOutcome.Failed, r.Outcome);
        Assert.Empty(r.Choices);
    }

    [Fact]
    public async Task Iptal_Firlatir()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SpeakerPreparation(new FakeDiarizer(() => Array.Empty<SpeakerSegment>())).RunAsync(new float[16000], 2, false, cts.Token));
    }
}
