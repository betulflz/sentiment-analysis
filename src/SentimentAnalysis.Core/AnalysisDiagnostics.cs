namespace SentimentAnalysis.Core;

public enum ChunkDisposition { Pending, Speech, NoSpeech, ProcessingFailed, QueueDropped, Cancelled, DrainSkipped, Unprocessed }

/// <summary>Operational evidence only: no audio, transcript, exception message or stack trace.</summary>
public sealed record ChunkObservation(int Index, double StartSeconds, double EndSeconds,
    ChunkDisposition Disposition, double ProcessingSeconds = 0, string? FailureType = null,
    string? EnrichmentFailureType = null);

/// <summary>Coverage describes evaluated input, NOT transcript or emotion accuracy.
/// Durations are unions of intervals; an overlapping failed chunk can still be covered by neighbours.</summary>
public sealed record AnalysisDiagnostics(IReadOnlyList<ChunkObservation> Chunks,
    double CapturedSeconds, double EvaluatedSeconds, double UnevaluatedSeconds,
    string SourceStatus, string? SourceFailureType = null, string? StorageFailureType = null)
{
    public double? CoverageRatio => CapturedSeconds > 0 ? EvaluatedSeconds / CapturedSeconds : null;
    public bool HasIssues => SourceFailureType is not null || StorageFailureType is not null ||
        Chunks.Any(c => c.Disposition is not (ChunkDisposition.Speech or ChunkDisposition.NoSpeech)
                        || c.EnrichmentFailureType is not null);
}
