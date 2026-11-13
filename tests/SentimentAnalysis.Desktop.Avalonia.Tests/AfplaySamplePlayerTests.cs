using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Desktop.Avalonia;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Desktop.Avalonia.Tests;

public sealed class AfplaySamplePlayerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("player-test").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private sealed class Platform(bool mac) : IRuntimePlatform
    {
        public bool IsWindows => !mac; public bool IsLinux => false; public bool IsMacOS => mac;
    }

    private sealed class Runner : IExternalProcessRunner
    {
        public readonly List<string> Played = new();
        public readonly List<bool> FileExistedDuringPlay = new();
        public TaskCompletionSource? Gate;
        public async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken ct)
        {
            var p = arguments.Single();
            Played.Add(p);
            FileExistedDuringPlay.Add(File.Exists(p));
            if (Gate is not null) await Gate.Task.WaitAsync(ct);
            return new ProcessResult(0, "", "");
        }
    }

    [Fact]
    public async Task Calma_SonrasiDosyaSilinir()
    {
        var r = new Runner();
        await new AfplaySamplePlayer(r, new Platform(true), _dir).PlayAsync(new float[1600], default);
        Assert.True(r.FileExistedDuringPlay.Single());
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Iptal_DosyaSilinir()
    {
        var r = new Runner { Gate = new TaskCompletionSource() };
        using var cts = new CancellationTokenSource();
        var play = new AfplaySamplePlayer(r, new Platform(true), _dir).PlayAsync(new float[1600], cts.Token);
        cts.Cancel();
        await play;
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task IkinciCagri_OncekiniIptalEder()
    {
        var r = new Runner { Gate = new TaskCompletionSource() };
        var player = new AfplaySamplePlayer(r, new Platform(true), _dir);
        var first = player.PlayAsync(new float[1600], default);
        var second = player.PlayAsync(new float[1600], default);
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        r.Gate.SetResult();
        await second;
        Assert.Equal(2, r.Played.Count);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task MacDisi_Desteklenmez_HicCalmaz()
    {
        var r = new Runner();
        var player = new AfplaySamplePlayer(r, new Platform(false), _dir);
        Assert.False(player.IsSupported);
        await player.PlayAsync(new float[1600], default);
        Assert.Empty(r.Played);
    }

    [Fact]
    public void Wav_BasligiVeUzunlugu()
    {
        var b = AfplaySamplePlayer.Wav(new float[100]);
        Assert.Equal(44 + 200, b.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(b, 0, 4));
    }
}
