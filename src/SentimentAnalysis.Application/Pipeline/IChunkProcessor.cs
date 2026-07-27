using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Pipeline;

/// <summary>Tek bir chunk'ı duygu boru hattından geçirir. VAD sessiz derse <c>null</c> döner
/// (chunk timeline'a düşmez).</summary>
public interface IChunkProcessor
{
    Task<ChunkResult?> ProcessAsync(AudioChunk chunk, CancellationToken ct);
}
