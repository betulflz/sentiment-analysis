using System;
using System.Linq;
using SentimentAnalysis.Infrastructure.Vad;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Vad.Tests;

public class VadWindowerTests
{
    [Fact]
    public void ToFrames_ExactMultiple_NoPadding()
    {
        var frames = VadWindower.ToFrames(new float[1024]);
        Assert.Equal(2, frames.Count);
        Assert.All(frames, f => Assert.Equal(512, f.Length));
    }

    [Fact]
    public void ToFrames_PartialTail_ZeroPadded()
    {
        var x = Enumerable.Repeat(0.7f, 600).ToArray(); // 512 + 88
        var frames = VadWindower.ToFrames(x);
        Assert.Equal(2, frames.Count);
        Assert.Equal(512, frames[1].Length);
        Assert.Equal(0.7f, frames[1][87], precision: 5); // son gerçek örnek
        Assert.Equal(0f, frames[1][88], precision: 5);   // pad başlangıcı
        Assert.Equal(0f, frames[1][511], precision: 5);
    }

    [Fact]
    public void ToFrames_Empty_ReturnsEmpty()
    {
        Assert.Empty(VadWindower.ToFrames(Array.Empty<float>()));
    }

    [Fact]
    public void ToFrames_ShorterThanFrame_SinglePaddedFrame()
    {
        var frames = VadWindower.ToFrames(new float[] { 1f, 2f, 3f });
        Assert.Single(frames);
        Assert.Equal(512, frames[0].Length);
        Assert.Equal(3f, frames[0][2], precision: 5);
        Assert.Equal(0f, frames[0][3], precision: 5);
    }
}
