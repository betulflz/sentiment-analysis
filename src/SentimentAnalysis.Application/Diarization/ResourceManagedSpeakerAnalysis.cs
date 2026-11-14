using SentimentAnalysis.Core;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Runtime;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Speaker models cannot overlap live models and are closed after each job.</summary>
public sealed class ResourceManagedSpeakerAnalysis(
    IDiarizationState state, ModelWorkQueue queue,
    Func<OwnedModelSession<ISpeakerAnalysis>> createSession) : ISpeakerAnalysis
{
    public bool Enabled => state.Enabled;

    public async Task<SpeakerAnalysisResult> RunAsync(Guid sessionId, int expectedSpeakers, CancellationToken ct)
    {
        if (!Enabled) return SpeakerAnalysisResult.Disabled;
        using var lease = await queue.AcquireAsync(ct).ConfigureAwait(false);
        lease.Token.ThrowIfCancellationRequested();
        if (!Enabled) return SpeakerAnalysisResult.Disabled;
        await using var session = createSession();
        return await Task.Run(() => session.Service.RunAsync(sessionId, expectedSpeakers, lease.Token))
            .ConfigureAwait(false);
    }

    public async Task<SpeakerAnalysisResult> RunPrecomputedAsync(Guid sessionId, IReadOnlyList<SpeakerSegment> segments,
        float[] mono16k, CancellationToken ct)
    {
        using var lease = await queue.AcquireAsync(ct).ConfigureAwait(false);
        lease.Token.ThrowIfCancellationRequested();
        await using var session = createSession();
        return await Task.Run(() => session.Service.RunPrecomputedAsync(sessionId, segments, mono16k, lease.Token))
            .ConfigureAwait(false);
    }
}
