using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Pipeline;

/// <summary>Tek bir chunk'ı duygu boru hattından geçirir. VAD sessiz derse <c>null</c> döner
/// (chunk timeline'a düşmez).</summary>
public interface IChunkProcessor
{
    Task<ChunkResult?> ProcessAsync(AudioChunk chunk, CancellationToken ct);

    /// <summary>Rol etiketleyiciyle (12): sonuca <see cref="ChunkResult.Roles"/> ve <see cref="ChunkResult.Counterpart"/> eklenir.
    /// Varsayılan: rolsüz işleme (rolü bilmeyen uygulamalar için).</summary>
    Task<ChunkResult?> ProcessAsync(AudioChunk chunk, IRoleLabeler? roles, CancellationToken ct) => ProcessAsync(chunk, ct);
    Task WarmUpAsync(CancellationToken ct) => Task.CompletedTask;
}
