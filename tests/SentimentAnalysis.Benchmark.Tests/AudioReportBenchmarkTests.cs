using System.Runtime.CompilerServices;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Benchmark.Tests;

public class AudioReportBenchmarkTests
{
    private sealed class Source : IAudioCaptureSource
    {
        public async IAsyncEnumerable<AudioChunk> CaptureAsync(CaptureOptions options,
            [EnumeratorCancellation] CancellationToken ct)
        {
            yield return new(new float[16000], 16000, TimeSpan.Zero, TimeSpan.FromSeconds(1));
            await Task.Yield();
        }
    }

    private sealed class Processor(bool fails) : IChunkProcessor
    {
        public Task<ChunkResult?> ProcessAsync(AudioChunk chunk, CancellationToken ct)
        {
            if (fails) throw new IOException("not for telemetry");
            var result = new ChunkResult(chunk.Start, chunk.End,
                new FusedResult(ValenceArousal.Neutral, Emotion.Notr, Emotion.Notr, Confidence.Orta,
                    true, false, IncongruencePattern.Yok, 1, ValenceArousal.Neutral, ValenceArousal.Neutral, 0, 0),
                "merhaba", [new("merhaba", chunk.Start, chunk.End)]);
            return Task.FromResult<ChunkResult?>(result);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunsProductionPipelineAndDecisionLogic_AndMarksPartialFailures(bool fails)
    {
        var config = AppConfig.Default;
        var pipeline = new EmotionPipeline(new Processor(fails), new InMemorySessionStore(), new MeetingAggregator(config), config);
        var report = await AudioReportBenchmark.RunAsync(pipeline, new Source(), 1, config, CancellationToken.None);
        Assert.Equal(!fails, report.Complete);
        Assert.Equal(fails ? "" : "merhaba", report.Transcript);
        Assert.Equal(fails ? 0 : 1, report.Decisions.Windows.Count);
        Assert.Equal(fails, report.Diagnostics.HasIssues);
        if (fails) Assert.Null(report.FollowUp);
    }

    [Fact]
    public void Options_AcceptAnalyzeTask() =>
        Assert.Equal("analyze", BenchmarkOptions.Parse(["analyze", "--input", "a.wav", "--output", "b.json", "--models", "models"]).Task);
}
