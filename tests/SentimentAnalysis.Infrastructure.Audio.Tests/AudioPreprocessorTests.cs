using System;
using System.Linq;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class AudioPreprocessorTests
{
    [Fact]
    public void ToMono16k_AlreadyMono16k_PassesThrough()
    {
        var x = new float[] { 0.1f, -0.2f, 0.3f, 0.4f };
        var y = AudioPreprocessor.ToMono16k(x, 16000, 1);
        Assert.Equal(x, y);
    }

    [Fact]
    public void ToMono16k_Stereo_DownmixesByAveraging()
    {
        // interleaved L,R: (1,-1)->0 ; (0.5,0.5)->0.5
        var x = new float[] { 1f, -1f, 0.5f, 0.5f };
        var y = AudioPreprocessor.ToMono16k(x, 16000, 2);
        Assert.Equal(2, y.Length);
        Assert.Equal(0f, y[0], precision: 5);
        Assert.Equal(0.5f, y[1], precision: 5);
    }

    [Fact]
    public void ToMono16k_Downsample32kTo16k_HalvesLength()
    {
        var x = Enumerable.Range(0, 320).Select(i => (float)Math.Sin(i * 0.05)).ToArray();
        var y = AudioPreprocessor.ToMono16k(x, 32000, 1);
        // 320 örnek @32k → ~160 @16k (lineer resample; ±1 tolerans)
        Assert.InRange(y.Length, 159, 161);
        Assert.InRange(y[0], -1.0f, 1.0f);
    }

    [Fact]
    public void ToMono16k_Upsample8kTo16k_DoublesLength()
    {
        var x = new float[] { 0f, 1f, 0f, -1f };
        var y = AudioPreprocessor.ToMono16k(x, 8000, 1);
        Assert.InRange(y.Length, 7, 8);
        Assert.Equal(0f, y[0], precision: 4); // ilk örnek korunur
    }

    [Fact]
    public void ToMono16k_Empty_ReturnsEmpty()
    {
        Assert.Empty(AudioPreprocessor.ToMono16k(Array.Empty<float>(), 44100, 2));
    }

    [Fact]
    public void ToMono16k_FromDecodedAudio_Works()
    {
        var d = new DecodedAudio(new float[] { 1f, -1f }, 16000, 2);
        var y = AudioPreprocessor.ToMono16k(d);
        Assert.Single(y);
        Assert.Equal(0f, y[0], precision: 5);
    }
}
