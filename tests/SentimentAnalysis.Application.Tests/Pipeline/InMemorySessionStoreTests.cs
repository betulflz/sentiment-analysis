using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Pipeline;

public class InMemorySessionStoreTests
{
    private static ChunkResult Result(double start) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(start + 1),
            new FusedResult(ValenceArousal.Neutral, Emotion.Notr, Emotion.Notr,
                Confidence.Orta, true, false, IncongruencePattern.Yok, 1.0,
                TextVa: ValenceArousal.Neutral, ProsodyVa: ValenceArousal.Neutral,
                TextConfidence: 0.0, ProsodyConfidence: 0.0),
            $"chunk-{start}");

    [Fact]
    public async Task SaveAndGet_ReturnsChunksInOrder()
    {
        var store = new InMemorySessionStore();
        var id = Guid.NewGuid();
        await store.SaveChunkAsync(id, Result(0), CancellationToken.None);
        await store.SaveChunkAsync(id, Result(1), CancellationToken.None);

        var session = await store.GetSessionAsync(id, CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal(id, session!.Id);
        Assert.Equal(2, session.Chunks.Count);
        Assert.Equal("chunk-0", session.Chunks[0].Transcript);
        Assert.Equal("chunk-1", session.Chunks[1].Transcript);
    }

    [Fact]
    public async Task GetSession_UnknownId_ReturnsNull()
    {
        var store = new InMemorySessionStore();
        Assert.Null(await store.GetSessionAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ListSessions_ReturnsAllSaved()
    {
        var store = new InMemorySessionStore();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        await store.SaveChunkAsync(a, Result(0), CancellationToken.None);
        await store.SaveChunkAsync(b, Result(0), CancellationToken.None);

        var list = await store.ListSessionsAsync(CancellationToken.None);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, s => s.Id == a);
        Assert.Contains(list, s => s.Id == b);
    }

    [Fact]
    public async Task PurgeExpiredAudio_NoThrow()
    {
        var store = new InMemorySessionStore();
        await store.PurgeExpiredAudioAsync(CancellationToken.None); // no-op (gerçek retention 07)
    }
}
