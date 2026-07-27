using System;
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class StreamingChunkBufferTests
{
    private const int Rate = 16000;

    private static StreamingChunkBuffer New(double chunkSeconds = 1.0, double overlapSeconds = 0.25) =>
        new(Rate, TimeSpan.FromSeconds(chunkSeconds), TimeSpan.FromSeconds(overlapSeconds));

    private static float[] Ramp(int count, int startValue = 0)
    {
        var a = new float[count];
        for (int i = 0; i < count; i++) a[i] = startValue + i;
        return a;
    }

    [Fact]
    public void Append_BelowChunkLength_EmitsNothing()
    {
        var buffer = New();

        Assert.Empty(buffer.Append(new float[Rate - 1]));
    }

    [Fact]
    public void Append_ExactlyChunkLength_EmitsOneChunk()
    {
        var buffer = New();

        var chunks = buffer.Append(new float[Rate]);

        var chunk = Assert.Single(chunks);
        Assert.Equal(Rate, chunk.Samples.Length);
        Assert.Equal(Rate, chunk.SampleRate);
        Assert.Equal(TimeSpan.Zero, chunk.Start);
        Assert.Equal(TimeSpan.FromSeconds(1), chunk.End);
    }

    [Fact]
    public void Append_AccumulatesAcrossCalls()
    {
        var buffer = New();

        Assert.Empty(buffer.Append(new float[Rate / 2]));
        var chunks = buffer.Append(new float[Rate / 2]);

        Assert.Single(chunks);
    }

    [Fact]
    public void Append_SecondChunkStartsAfterStep_NotAfterFullChunk()
    {
        // chunk 1.0 sn, overlap 0.25 sn → adım 0.75 sn
        var buffer = New();
        buffer.Append(new float[Rate]);              // 1. chunk: 0.00 - 1.00

        var chunks = buffer.Append(new float[(int)(Rate * 0.75)]);

        var chunk = Assert.Single(chunks);
        Assert.Equal(TimeSpan.FromSeconds(0.75), chunk.Start);
        Assert.Equal(TimeSpan.FromSeconds(1.75), chunk.End);
    }

    [Fact]
    public void Append_OverlapRegion_IsActuallyRepeated()
    {
        var buffer = New();
        var first = buffer.Append(Ramp(Rate))[0];
        var second = buffer.Append(Ramp((int)(Rate * 0.75), startValue: Rate))[0];

        // 1. chunk'ın son 0.25 sn'si = 2. chunk'ın ilk 0.25 sn'si
        int overlapCount = (int)(Rate * 0.25);
        var tailOfFirst = first.Samples.Skip(first.Samples.Length - overlapCount).ToArray();
        var headOfSecond = second.Samples.Take(overlapCount).ToArray();

        Assert.Equal(tailOfFirst, headOfSecond);
    }

    [Fact]
    public void Append_LongInput_EmitsMultipleChunksAtOnce()
    {
        var buffer = New();

        // 2.5 sn veri, 0.75 sn adım → 0.00, 0.75, 1.50 başlangıçlı 3 tam chunk
        var chunks = buffer.Append(new float[(int)(Rate * 2.5)]);

        Assert.Equal(3, chunks.Count);
        Assert.Equal(TimeSpan.Zero, chunks[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(0.75), chunks[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(1.50), chunks[2].Start);
    }

    [Fact]
    public void Flush_EmitsRemainderNotYetCovered()
    {
        var buffer = New();
        buffer.Append(new float[Rate]);                    // kapsanan: 0.00 - 1.00
        buffer.Append(new float[(int)(Rate * 0.5)]);       // toplam 1.5 sn, yeni chunk yok

        var tail = buffer.Flush();

        Assert.NotNull(tail);
        Assert.Equal(TimeSpan.FromSeconds(1.5), tail!.End);
    }

    [Fact]
    public void Flush_WhenEverythingAlreadyCovered_ReturnsNull()
    {
        var buffer = New();
        buffer.Append(new float[Rate]);   // tam olarak 1 chunk, artık yok

        Assert.Null(buffer.Flush());
    }

    [Fact]
    public void Flush_OnEmptyBuffer_ReturnsNull()
    {
        Assert.Null(New().Flush());
    }

    [Fact]
    public void Ctor_OverlapNotLessThanChunk_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new StreamingChunkBuffer(Rate, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Ctor_NonPositiveChunkLength_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new StreamingChunkBuffer(Rate, TimeSpan.Zero, TimeSpan.Zero));
    }

    [Fact]
    public void NoSampleIsLost_AcrossManyRandomSizedAppends()
    {
        var buffer = New();
        var emitted = new List<AudioChunk>();
        int total = 0;
        // 100 × 700 örnek = 70.000 örnek (4,375 sn)
        for (int i = 0; i < 100; i++)
        {
            emitted.AddRange(buffer.Append(Ramp(700, startValue: total)));
            total += 700;
        }
        var tail = buffer.Flush();
        if (tail is not null) emitted.Add(tail);

        // Her chunk'ın kapsadığı aralıkların birleşimi [0, total) olmalı — boşluk yok.
        double lastEnd = 0;
        foreach (var chunk in emitted)
        {
            Assert.True(chunk.Start.TotalSeconds <= lastEnd + 1e-9,
                $"boşluk var: {chunk.Start.TotalSeconds} > {lastEnd}");
            lastEnd = Math.Max(lastEnd, chunk.End.TotalSeconds);
        }
        Assert.Equal(total / (double)Rate, lastEnd, precision: 6);
    }
}
