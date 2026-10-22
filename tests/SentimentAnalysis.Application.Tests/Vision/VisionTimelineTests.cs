using System;
using System.Linq;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Vision;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Vision;

public class FaceSignalsTests
{
    private static readonly VisionConfig Cfg = new();

    [Fact]
    public void Valence_KnownVectors()
    {
        Assert.Equal(0.8, FaceSignals.Valence(new float[] { 0, 1, 0, 0, 0, 0, 0, 0 }), 1e-6);
        Assert.Equal(-0.156, FaceSignals.Valence(new float[] { .73f, 0, 0, .26f, 0, 0, 0, 0 }), 1e-6);
    }

    [Fact]
    public void Negative_SumsNegativeClasses() =>
        Assert.Equal(0.5, FaceSignals.Negative(new float[] { .5f, 0, 0, .1f, .1f, .1f, .1f, .1f }), 1e-6);

    [Fact]
    public void Smiling_Threshold()
    {
        Assert.True(FaceSignals.Smiling(new float[] { .2f, .75f, 0, 0, 0, 0, 0, .05f }));
        Assert.False(FaceSignals.Smiling(new float[] { .5f, .5f, 0, 0, 0, 0, 0, 0 }));
    }

    [Fact]
    public void Yaw_Frontal_NearZero()
    {
        // sağ göz 100, sol göz 140, burun 120
        var kps = new float[] { 100, 50, 140, 50, 120, 70, 105, 90, 135, 90 };
        Assert.Equal(0.0, FaceSignals.Yaw(kps), 1e-6);
    }

    [Fact]
    public void Yaw_Turned()
    {
        var kps = new float[] { 100, 50, 140, 50, 135, 70, 105, 90, 135, 90 };
        Assert.Equal(0.375, FaceSignals.Yaw(kps), 1e-6);
    }

    [Fact]
    public void Yaw_DegenerateEyes_Zero() =>
        Assert.Equal(0.0, FaceSignals.Yaw(new float[] { 100, 50, 100.5f, 50, 130, 70, 0, 0, 0, 0 }));

    [Theory]
    [InlineData(20, 0.0)]
    [InlineData(24, 0.0)]
    [InlineData(36, 0.5)]
    [InlineData(48, 1.0)]
    [InlineData(90, 1.0)]
    public void Reliability_Ramp(float h, double expected) =>
        Assert.Equal(expected, FaceSignals.Reliability(new FaceBox(0, 0, h, h), Cfg), 1e-6);
}

public class VisionTimelineTests
{
    private static readonly VisionConfig Cfg = new();
    private static readonly float[] Neutral = { .73f, 0, 0, .26f, 0, 0, 0, .01f };
    private static readonly float[] Happy = { 0, 1, 0, 0, 0, 0, 0, 0 };

    private static FaceObservation Obs(int face, double t, float[] expr, double yaw = 0, float h = 90) =>
        new(face, TimeSpan.FromSeconds(t), new FaceBox(0, 0, h, h), yaw, expr);

    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void Summarize_VisibleAndLookAway()
    {
        var tl = new VisionTimeline(Cfg);
        tl.AddFrame(S(1), new[] { Obs(0, 1, Neutral) });
        tl.AddFrame(S(2), new[] { Obs(0, 2, Neutral, yaw: -0.5) });
        tl.AddFrame(S(3), Array.Empty<FaceObservation>());
        tl.AddFrame(S(4), Array.Empty<FaceObservation>());
        var s = Assert.Single(tl.Summarize(S(0), S(10)));
        Assert.Equal(0, s.FaceIndex);
        Assert.Equal(0.5, s.VisibleRatio, 1e-9);
        Assert.Equal(0.5, s.LookAwayRatio, 1e-9);
        Assert.Equal(1.0, s.Reliability, 1e-9);
    }

    [Fact]
    public void Summarize_BaselineRemovesRestingSadness()
    {
        var tl = new VisionTimeline(Cfg);
        for (double t = 0; t < 20; t += 0.4) tl.AddFrame(S(t), new[] { Obs(0, t, Neutral) });
        var s = Assert.Single(tl.Summarize(S(10), S(20)));
        Assert.Equal(0.0, s.ValenceShift, 1e-6);
        Assert.Equal(0.0, s.NegativeExpression, 1e-6);
    }

    [Fact]
    public void Summarize_SmileAfterBaseline_PositiveShift()
    {
        var tl = new VisionTimeline(Cfg);
        for (double t = 0; t < 10; t += 0.4) tl.AddFrame(S(t), new[] { Obs(0, t, Neutral) });
        for (double t = 10; t < 20; t += 0.4) tl.AddFrame(S(t), new[] { Obs(0, t, Happy) });
        var s = Assert.Single(tl.Summarize(S(10), S(20)));
        Assert.Equal(0.8 - FaceSignals.Valence(Neutral), s.ValenceShift, 1e-6);
        Assert.Equal(1.0, s.SmileRatio, 1e-9);
    }

    [Fact]
    public void Summarize_BeforeBaseline_UsesDefault_NoNaN()
    {
        var tl = new VisionTimeline(Cfg);
        for (double t = 0; t < 4; t += 0.4) tl.AddFrame(S(t), new[] { Obs(0, t, Happy) });
        var s = Assert.Single(tl.Summarize(S(0), S(4)));
        Assert.Equal(0.8 - (-0.15), s.ValenceShift, 1e-6);
        Assert.Equal(0.0, s.NegativeExpression, 1e-9);
        Assert.True(double.IsFinite(s.ValenceShift) && double.IsFinite(s.NegativeExpression));
    }

    [Fact]
    public void Summarize_EmptyWindow_EmptyList()
    {
        var tl = new VisionTimeline(Cfg);
        tl.AddFrame(S(1), new[] { Obs(0, 1, Neutral) });
        Assert.Empty(tl.Summarize(S(5), S(10)));
    }

    [Fact]
    public void Summarize_OrderedByFaceIndex()
    {
        var tl = new VisionTimeline(Cfg);
        tl.AddFrame(S(1), new[] { Obs(1, 1, Neutral), Obs(0, 1, Neutral) });
        Assert.Equal(new[] { 0, 1 }, tl.Summarize(S(0), S(2)).Select(x => x.FaceIndex));
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var tl = new VisionTimeline(Cfg);
        tl.AddFrame(S(1), new[] { Obs(0, 1, Neutral) });
        tl.Clear();
        Assert.Empty(tl.Summarize(S(0), S(10)));
    }

    [Fact]
    public async Task Summarize_ConcurrentAddAndRead_NoException()
    {
        var tl = new VisionTimeline(Cfg);
        var writer = Task.Run(() =>
        {
            for (int i = 0; i < 1000; i++) tl.AddFrame(S(i * 0.4), new[] { Obs(0, i * 0.4, Neutral) });
        });
        var reader = Task.Run(() =>
        {
            for (int i = 0; i < 1000; i++) tl.Summarize(S(0), S(400));
        });
        await Task.WhenAll(writer, reader);
    }
}
