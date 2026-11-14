using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Abstractions;

public interface ITranscriptReviewStore
{
    /// <summary>Henüz kullanıcı kaydı yoksa null; otomatik metin ayrı depoda korunur.</summary>
    Task<IReadOnlyList<ReviewedTranscriptLine>?> GetTranscriptReviewAsync(Guid sessionId, CancellationToken ct);
    /// <summary>Var olan oturumun düzeltmelerini atomik olarak değiştirir.</summary>
    Task SaveTranscriptReviewAsync(Guid sessionId, IReadOnlyList<ReviewedTranscriptLine> lines, CancellationToken ct);
}
