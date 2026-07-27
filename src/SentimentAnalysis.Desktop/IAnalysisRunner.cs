using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Canlı analiz koşusunu soyutlar (VM `EmotionPipeline`'ı doğrudan bilmez — MVVM sınırı).
/// App katmanı bunu `EmotionPipeline` + seçili `IAudioCaptureSource` üzerine implemente eder.</summary>
public interface IAnalysisRunner
{
    Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct);
}
