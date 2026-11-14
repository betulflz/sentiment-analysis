using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Report metadata that can outlive the native model graph.</summary>
public sealed record AnalysisRunSnapshot(Guid? SessionId, int SkippedChunkCount,
    Func<ChunkResult, IReadOnlyList<FaceWindowSummary>>? Faces = null);
