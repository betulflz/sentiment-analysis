using System.Runtime.CompilerServices;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Decision;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Benchmark;

public sealed record AudioReportResult(string Transcript, double DurationSeconds, bool Complete,
    MeetingSummary Summary, AnalysisDiagnostics Diagnostics, IReadOnlyList<ChunkResult> Chunks,
    DecisionSession Decisions, FollowUpDecision? FollowUp);

/// <summary>Headless audio-to-report path. Uses production pipeline and decision algorithms;
/// excludes Avalonia, SQLite, camera and optional post-session speaker analysis.</summary>
public static class AudioReportBenchmark
{
    public static async Task<AudioReportResult> RunAsync(EmotionPipeline pipeline, IAudioCaptureSource source,
        double durationSeconds, AppConfig config, CancellationToken ct)
    {
        var summary = await pipeline.RunAsync(source, new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)),
            Guid.NewGuid(), DateTimeOffset.UnixEpoch, DetectedLanguage.Turkish, null, ct,
            mode: PipelineExecutionMode.Offline);
        const MeetingProfile profile = MeetingProfile.Satis;
        var weights = DecisionKnowledgeBase.DefaultWeights(profile);
        var advisor = new LiveAdvisor(profile, config.Decision);
        var previousValences = new List<double>();
        var windows = new List<DecisionWindow>();
        foreach (var chunk in pipeline.Timeline)
        {
            var indicators = IndicatorCalculator.Compute(chunk, [], previousValences, config.Decision);
            previousValences.Add(chunk.Fused.SpeechValence);
            windows.Add(new(chunk.TimeStart, chunk.TimeEnd, indicators, advisor.Next(indicators, weights)));
        }
        var decisions = new DecisionSession(profile, weights, weights, windows);
        var followUp = windows.Count == 0 ? null : FollowUpAdvisor.Decide(profile,
            FollowUpAdvisor.AggregateMeeting(windows.Select(w => w.Indicators).ToArray(), config.Decision), weights, config.Decision);
        var diagnostics = pipeline.Diagnostics;
        bool complete = !diagnostics.HasIssues && diagnostics.SourceStatus == "completed"
            && Math.Abs(diagnostics.CapturedSeconds - durationSeconds) <= 0.05;
        return new(string.Join(" ", pipeline.Timeline.Select(c => c.Transcript)), durationSeconds, complete,
            summary, diagnostics, pipeline.Timeline.ToArray(), decisions, followUp);
    }

    public static IAudioCaptureSource FromSamples(float[] samples) => new SampleSource(samples);

    private sealed class SampleSource(float[] samples) : IAudioCaptureSource
    {
        public async IAsyncEnumerable<AudioChunk> CaptureAsync(CaptureOptions options,
            [EnumeratorCancellation] CancellationToken ct)
        {
            int length = (int)(options.ChunkLength.TotalSeconds * 16000);
            int step = (int)((options.ChunkLength - options.Overlap).TotalSeconds * 16000);
            for (int start = 0; start < samples.Length; start += step)
            {
                ct.ThrowIfCancellationRequested();
                int end = Math.Min(samples.Length, start + length);
                yield return new(samples[start..end], 16000,
                    TimeSpan.FromSeconds(start / 16000.0), TimeSpan.FromSeconds(end / 16000.0));
                if (end == samples.Length) yield break;
                await Task.Yield();
            }
        }
    }
}
