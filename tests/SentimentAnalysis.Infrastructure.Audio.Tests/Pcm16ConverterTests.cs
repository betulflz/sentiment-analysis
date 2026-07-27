using System;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class Pcm16ConverterTests
{
    [Fact]
    public void ToFloat_ReturnsSampleCount()
    {
        var bytes = new byte[8];
        var dest = new float[4];

        Assert.Equal(4, Pcm16Converter.ToFloat(bytes, dest));
    }

    [Fact]
    public void ToFloat_ZeroBytes_AreSilence()
    {
        var dest = new float[2];

        Pcm16Converter.ToFloat(new byte[4], dest);

        Assert.All(dest, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void ToFloat_ReadsLittleEndian()
    {
        // 0x0100 little-endian = 256
        var bytes = new byte[] { 0x00, 0x01 };
        var dest = new float[1];

        Pcm16Converter.ToFloat(bytes, dest);

        Assert.Equal(256f / 32768f, dest[0], precision: 6);
    }

    [Fact]
    public void ToFloat_HandlesNegativeSamples()
    {
        // -32768 = 0x8000 little-endian
        var bytes = new byte[] { 0x00, 0x80 };
        var dest = new float[1];

        Pcm16Converter.ToFloat(bytes, dest);

        Assert.Equal(-1.0f, dest[0], precision: 6);
    }

    [Fact]
    public void ToFloat_MaxPositive_IsJustUnderOne()
    {
        // 32767 = 0xFF7F little-endian
        var bytes = new byte[] { 0xFF, 0x7F };
        var dest = new float[1];

        Pcm16Converter.ToFloat(bytes, dest);

        Assert.True(dest[0] > 0.999f && dest[0] < 1.0f);
    }

    [Fact]
    public void ToFloat_OddByteCount_Throws()
    {
        Assert.Throws<ArgumentException>(() => Pcm16Converter.ToFloat(new byte[3], new float[2]));
    }

    [Fact]
    public void ToFloat_DestinationTooSmall_Throws()
    {
        Assert.Throws<ArgumentException>(() => Pcm16Converter.ToFloat(new byte[8], new float[3]));
    }
}
