using System.Runtime.CompilerServices;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Tests.Pipeline;

public class PipelineDiagnosticsTests
{
    private sealed class Source(int count, TaskCompletionSource? produced = null) : IAudioCaptureSource
    {
        public bool Closed;
        public async IAsyncEnumerable<AudioChunk> CaptureAsync(CaptureOptions options,
            [EnumeratorCancellation] CancellationToken ct)
        {
            try
            {
                for (int i = 0; i < count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    yield return new(new float[16000], 16000, TimeSpan.FromSeconds(i * 5), TimeSpan.FromSeconds(i * 5 + 10));
                    await Task.Yield();
                }
                produced?.TrySetResult();
            }
            finally { Closed = true; }
        }
    }

    private sealed class Processor(Func<AudioChunk, CancellationToken, Task<ChunkResult?>> process) : IChunkProcessor
    {
        public Task<ChunkResult?> ProcessAsync(AudioChunk chunk, CancellationToken ct) => process(chunk, ct);
    }

    private sealed class BrokenStore : ISessionStore
    {
        public Task SaveChunkAsync(Guid id, ChunkResult result, CancellationToken ct) => throw new IOException("private path");
        public Task<MeetingSession?> GetSessionAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task PurgeExpiredAudioAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static ChunkResult Speech(AudioChunk c) => new(c.Start, c.End,
        new FusedResult(ValenceArousal.Neutral, Emotion.Notr, Emotion.Notr, Confidence.Orta,
            true, false, IncongruencePattern.Yok, 1, ValenceArousal.Neutral, ValenceArousal.Neutral, 0, 0), "merhaba");

    private static EmotionPipeline Pipeline(IChunkProcessor processor, ISessionStore? store = null) => new(
        processor, store ?? new InMemorySessionStore(), new MeetingAggregator(AppConfig.Default),
        AppConfig.Default with { Pipeline = new PipelineConfig(ChannelCapacity: 1, DrainTimeBudgetSeconds: 1) });

    private static readonly CaptureOptions Options = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));

    [Fact]
    public async Task Offline_WaitsInsteadOfDropping_AndUnionsCoverage()
    {
        var pipeline = Pipeline(new Processor(async (c, ct) => { await Task.Delay(5, ct); return Speech(c); }));
        var summary = await pipeline.RunAsync(new Source(20), Options, Guid.NewGuid(), DateTimeOffset.UnixEpoch,
            DetectedLanguage.Turkish, null, CancellationToken.None, mode: PipelineExecutionMode.Offline);
        Assert.Equal(20, pipeline.Timeline.Count);
        Assert.Equal(0, pipeline.DroppedCount);
        Assert.Equal(20, summary.Diagnostics!.Chunks.Count);
        Assert.Equal(105, summary.Diagnostics.CapturedSeconds);
        Assert.Equal(105, summary.Diagnostics.EvaluatedSeconds);
        Assert.Equal(0, summary.Diagnostics.UnevaluatedSeconds);
        Assert.Equal("completed", summary.Diagnostics.SourceStatus);
    }

    [Fact]
    public async Task Silence_Failure_AndEnrichmentFailure_RemainDistinctWithoutSensitiveMessages()
    {
        var pipeline = Pipeline(new Processor((c, _) => c.Start.TotalSeconds switch
        {
            0 => Task.FromResult<ChunkResult?>(null),
            5 => throw new InvalidOperationException("secret transcript and path"),
            _ => Task.FromResult<ChunkResult?>(Speech(c))
        }));
        var summary = await pipeline.RunAsync(new Source(3), Options, Guid.NewGuid(), DateTimeOffset.UnixEpoch,
            DetectedLanguage.Turkish, null, CancellationToken.None,
            enrich: _ => throw new IOException("private face data"), mode: PipelineExecutionMode.Offline);
        var diagnostics = summary.Diagnostics!;
        Assert.Equal(new[] { ChunkDisposition.NoSpeech, ChunkDisposition.ProcessingFailed, ChunkDisposition.Speech },
            diagnostics.Chunks.Select(c => c.Disposition));
        Assert.Equal("InvalidOperationException", diagnostics.Chunks[1].FailureType);
        Assert.Equal("IOException", diagnostics.Chunks[2].EnrichmentFailureType);
        Assert.Equal(20, diagnostics.EvaluatedSeconds); // the failed interval is covered by its neighbours
        Assert.True(diagnostics.HasIssues);            // failure must remain visible anyway
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(diagnostics));
    }

    [Fact]
    public async Task Live_OverflowIsRecorded_AndRunResetsItsJournal()
    {
        var produced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipeline = Pipeline(new Processor(async (c, _) => { await gate.Task; return Speech(c); }));
        var run = pipeline.RunAsync(new Source(20, produced), Options, Guid.NewGuid(), DateTimeOffset.UnixEpoch,
            DetectedLanguage.Turkish, null, CancellationToken.None);
        await produced.Task.WaitAsync(TimeSpan.FromSeconds(2));
        gate.SetResult();
        var summary = await run;
        Assert.True(pipeline.DroppedCount > 0);
        Assert.Equal(pipeline.DroppedCount, summary.Diagnostics!.Chunks.Count(c => c.Disposition == ChunkDisposition.QueueDropped));
        Assert.Equal(20, summary.Diagnostics.Chunks.Count);
        await pipeline.RunAsync(new Source(1), Options, Guid.NewGuid(), DateTimeOffset.UnixEpoch,
            DetectedLanguage.Turkish, null, CancellationToken.None);
        Assert.Single(pipeline.Diagnostics.Chunks);
    }

    [Fact]
    public async Task Offline_StoreFailure_CancelsBlockedProducer()
    {
        var source = new Source(100);
        var pipeline = Pipeline(new Processor((c, _) => Task.FromResult<ChunkResult?>(Speech(c))), new BrokenStore());
        await Assert.ThrowsAsync<IOException>(async () => await pipeline.RunAsync(source, Options,
            Guid.NewGuid(), DateTimeOffset.UnixEpoch, DetectedLanguage.Turkish, null,
            CancellationToken.None, mode: PipelineExecutionMode.Offline).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(source.Closed);
        Assert.Equal("IOException", pipeline.Diagnostics.StorageFailureType);
    }

    [Fact]
    public async Task DrainBudget_RecordsInFlightCancellationAndQueuedRemainder()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var produced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();
        var pipeline = Pipeline(new Processor(async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        }));
        var run = pipeline.RunAsync(new Source(10, produced), Options, Guid.NewGuid(), DateTimeOffset.UnixEpoch,
            DetectedLanguage.Turkish, null, stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await produced.Task.WaitAsync(TimeSpan.FromSeconds(2));
        stop.Cancel();
        var summary = await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Contains(summary.Diagnostics!.Chunks, c => c.Disposition == ChunkDisposition.Cancelled);
        Assert.Contains(summary.Diagnostics.Chunks, c => c.Disposition == ChunkDisposition.DrainSkipped);
        Assert.DoesNotContain(summary.Diagnostics.Chunks, c => c.Disposition == ChunkDisposition.Pending);
    }
}
