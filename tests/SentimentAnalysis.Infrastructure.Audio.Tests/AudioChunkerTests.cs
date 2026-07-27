using System;
using System.Linq;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class AudioChunkerTests
{
    private static CaptureOptions Opts(double chunkSec, double overlapSec) =>
        new(TimeSpan.FromSeconds(chunkSec), TimeSpan.FromSeconds(overlapSec));

    [Fact]
    public void Chunk_OverlappingChunks_CoverSignal()
    {
        // 3 sn @16k = 48000 örnek; chunk 2 sn (32000), overlap 1 sn → step 16000.
        var mono = new float[48000];
        var chunks = AudioChunker.Chunk(mono, Opts(2, 1));

        Assert.Equal(2, chunks.Count);
        Assert.Equal(32000, chunks[0].Samples.Length);
        Assert.Equal(TimeSpan.Zero, chunks[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(2), chunks[0].End);
        Assert.Equal(TimeSpan.FromSeconds(1), chunks[1].Start);   // 1 sn kaydı (örtüşme)
        Assert.Equal(TimeSpan.FromSeconds(3), chunks[1].End);
        Assert.All(chunks, c => Assert.Equal(16000, c.SampleRate));
    }

    [Fact]
    public void Chunk_ShorterThanOneChunk_ReturnsSingleShortChunk()
    {
        var mono = new float[8000]; // 0.5 sn
        var chunks = AudioChunker.Chunk(mono, Opts(2, 0));
        Assert.Single(chunks);
        Assert.Equal(8000, chunks[0].Samples.Length);
        Assert.Equal(TimeSpan.Zero, chunks[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(0.5), chunks[0].End);
    }

    [Fact]
    public void Chunk_NoOverlap_TilesExactly()
    {
        var mono = new float[48000]; // 3 sn
        var chunks = AudioChunker.Chunk(mono, Opts(1, 0)); // 1 sn'lik diliml → 3 chunk
        Assert.Equal(3, chunks.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), chunks[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(2), chunks[2].Start);
    }

    [Fact]
    public void Chunk_Empty_ReturnsEmpty()
    {
        Assert.Empty(AudioChunker.Chunk(Array.Empty<float>(), Opts(2, 1)));
    }

    [Fact]
    public void Chunk_OverlapGeChunkLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => AudioChunker.Chunk(new float[16000], Opts(1, 1)));
    }

    [Fact]
    public void Chunk_LastChunkEndsAtSignalEnd()
    {
        var mono = new float[40000]; // 2.5 sn; chunk 2 sn, overlap 1 sn, step 1 sn
        var chunks = AudioChunker.Chunk(mono, Opts(2, 1));
        var last = chunks[^1];
        Assert.Equal(TimeSpan.FromSeconds(2.5), last.End); // son chunk sinyal sonunda biter
    }
}
