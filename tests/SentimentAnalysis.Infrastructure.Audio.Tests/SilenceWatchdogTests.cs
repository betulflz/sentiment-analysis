using System;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class SilenceWatchdogTests
{
    private const int Rate = 16000;

    private static SilenceWatchdog New() => new(Rate, TimeSpan.FromSeconds(2));

    private static float[] Constant(int count, float value)
    {
        var a = new float[count];
        Array.Fill(a, value);
        return a;
    }

    [Fact]
    public void BeforeWindowElapses_IsNotDeclaredDead()
    {
        var w = New();

        w.Observe(new float[Rate]);   // 1 sn dijital sessizlik, pencere 2 sn

        Assert.False(w.WindowElapsed);
        Assert.False(w.LooksDead);
    }

    [Fact]
    public void DigitalSilenceForFullWindow_LooksDead()
    {
        var w = New();

        w.Observe(new float[Rate * 2]);

        Assert.True(w.WindowElapsed);
        Assert.True(w.LooksDead);
    }

    // Spike ölçümü: izin VERİLMİŞ sessiz odanın tabanı 0,0098-0,0168.
    // Bu "ölü akış" DEĞİLDİR — yalnızca sessiz bir oda.
    [Fact]
    public void QuietRoomNoiseFloor_IsNotDead()
    {
        var w = New();

        w.Observe(Constant(Rate * 2, 0.0098f));

        Assert.True(w.WindowElapsed);
        Assert.False(w.LooksDead);
    }

    [Fact]
    public void Speech_IsNotDead()
    {
        var w = New();

        w.Observe(Constant(Rate * 2, 0.0805f));

        Assert.False(w.LooksDead);
    }

    [Fact]
    public void PeakSeen_TracksAbsoluteMaximum()
    {
        var w = New();

        w.Observe(new[] { 0.2f, -0.7f, 0.3f });

        Assert.Equal(0.7, w.PeakSeen, precision: 6);
    }

    [Fact]
    public void SingleLoudSampleInSilentWindow_ClearsTheAlarm()
    {
        var w = New();

        w.Observe(new float[Rate]);
        w.Observe(new[] { 0.5f });
        w.Observe(new float[Rate]);

        Assert.True(w.WindowElapsed);
        Assert.False(w.LooksDead);
    }
}
