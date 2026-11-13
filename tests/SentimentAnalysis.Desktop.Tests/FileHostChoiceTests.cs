using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

/// <summary>Karşı tarafa odak: dosya modunda önce konuşmacı ayrımı, "Siz hangisiniz?", sonra rol bilgili analiz.</summary>
public class FileHostChoiceTests
{
    private static readonly SpeakerId A = new(0), B = new(1);

    private sealed class Runner(IFileRoleSelection roles) : IAnalysisRunner
    {
        public int Runs;
        public RoleTimeline? SeenTimeline;
        public bool SawRoles;
        public Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct)
        {
            Runs++;
            SawRoles = roles.Roles is not null;
            SeenTimeline = roles.Roles?.Label(new AudioChunk(new float[160000], 16000, TimeSpan.Zero, TimeSpan.FromSeconds(10)));
            return Task.FromResult(new MeetingSummary(Emotion.Notr, new Dictionary<Emotion, double>(), 0, 0,
                Array.Empty<ChunkResult>(), new ValenceArousal(0, 0), 0, ""));
        }
    }

    private sealed class Prep(Func<CancellationToken, Task<SpeakerPreparationResult>> f) : ISpeakerPreparation
    {
        public int Calls;
        public Task<SpeakerPreparationResult> PrepareAsync(string path, int expectedSpeakers, CancellationToken ct) { Calls++; return f(ct); }
    }

    private sealed class Player(bool supported = true) : ISpeakerSamplePlayer
    {
        public readonly List<int> Lengths = new();
        public bool IsSupported => supported;
        public Task PlayAsync(float[] mono16k, CancellationToken ct) { Lengths.Add(mono16k.Length); return Task.CompletedTask; }
    }

    private static SpeakerPreparationResult TwoSpeakers() => new(DiarizationOutcome.Completed,
        new[] { new SpeakerSegment(A, TimeSpan.Zero, TimeSpan.FromSeconds(4)), new SpeakerSegment(B, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(10)) },
        new[] { new SpeakerChoice(B, 6, TimeSpan.FromSeconds(4.5), TimeSpan.FromSeconds(9.5)), new SpeakerChoice(A, 4, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(4)) },
        new float[16000 * 10], false);

    private static (LiveViewModel Vm, Runner Runner, FileRoleSelection Roles, Prep Prep, SelectedAudio Sel) Make(
        Func<CancellationToken, Task<SpeakerPreparationResult>>? prep = null, ISpeakerSamplePlayer? player = null)
    {
        var roles = new FileRoleSelection();
        var runner = new Runner(roles);
        var sel = new SelectedAudio { Path = "/tmp/gorusme.wav" };
        var p = new Prep(prep ?? (_ => Task.FromResult(TwoSpeakers())));
        var vm = new LiveViewModel(runner, null, sel, null, null, null, null, p, player ?? new Player(), roles);
        return (vm, runner, roles, p, sel);
    }

    private static async Task Until(Func<bool> cond)
    {
        for (int i = 0; i < 200 && !cond(); i++) await Task.Delay(10);
        Assert.True(cond());
    }

    [Fact]
    public async Task Dosya_Baslat_OnceHazirlikSonraSecim_RollerRunneraGider()
    {
        var (vm, runner, roles, prep, _) = Make();
        vm.StartCommand.Execute(null);
        await Until(() => vm.HostChoice is not null);
        Assert.Equal(1, prep.Calls);
        Assert.Equal(0, runner.Runs);
        Assert.Equal(new[] { "Konuşmacı B", "Konuşmacı A" }, vm.HostChoice!.Rows.Select(r => r.Label));
        Assert.Equal("6 sn konuştu", vm.HostChoice.Rows[0].TalkText);

        vm.HostChoice.Rows[1].IsSelected = true;   // A = ben
        vm.HostChoice.ConfirmCommand.Execute(null);
        await Until(() => runner.Runs == 1);

        Assert.True(runner.SawRoles);
        Assert.Equal(new[] { SpeakerRole.Yoneten, SpeakerRole.KarsiTaraf }, runner.SeenTimeline!.Spans.Select(s => s.Role));
        Assert.Null(vm.HostChoice);
        Assert.Equal(A, roles.Host);
    }

    [Fact]
    public async Task EminDegilim_KarisikYol()
    {
        var (vm, runner, roles, _, _) = Make();
        vm.StartCommand.Execute(null);
        await Until(() => vm.HostChoice is not null);
        Assert.False(vm.HostChoice!.ConfirmCommand.CanExecute(null));
        vm.HostChoice.NotSure = true;
        vm.HostChoice.ConfirmCommand.Execute(null);
        await Until(() => runner.Runs == 1);
        Assert.False(runner.SawRoles);
        Assert.Null(roles.Prepared);
    }

    [Fact]
    public async Task TekKonusmaci_SecimYok_KarisikYolVeMesaj()
    {
        var one = new SpeakerPreparationResult(DiarizationOutcome.Completed,
            new[] { new SpeakerSegment(A, TimeSpan.Zero, TimeSpan.FromSeconds(9)) },
            new[] { new SpeakerChoice(A, 9, TimeSpan.Zero, TimeSpan.FromSeconds(5)) }, new float[160000], false);
        var (vm, runner, _, _, _) = Make(_ => Task.FromResult(one));
        vm.StartCommand.Execute(null);
        await Until(() => runner.Runs == 1);
        Assert.False(runner.SawRoles);
        Assert.Contains("Konuşmacılar ayrılamadı", vm.CaptureError);
    }

    [Fact]
    public async Task AyrimHatasi_KarisikYol()
    {
        var (vm, runner, _, _, _) = Make(_ => Task.FromResult(SpeakerPreparationResult.Failed(Array.Empty<float>(), false)));
        vm.StartCommand.Execute(null);
        await Until(() => runner.Runs == 1);
        Assert.False(runner.SawRoles);
    }

    [Fact]
    public async Task Hazirlikta_Durdur_AnalizBaslamaz_SecimTemiz()
    {
        var (vm, runner, roles, _, _) = Make(async ct => { await Task.Delay(Timeout.Infinite, ct); return TwoSpeakers(); });
        vm.StartCommand.Execute(null);
        await Until(() => vm.IsPreparingSpeakers);
        vm.StopCommand.Execute(null);
        await Until(() => !vm.IsCapturing);
        Assert.Equal(0, runner.Runs);
        Assert.Null(roles.Roles);
        Assert.False(vm.IsPreparingSpeakers);
    }

    [Fact]
    public async Task SecimEkranindaDurdur_AnalizBaslamaz()
    {
        var (vm, runner, roles, _, _) = Make();
        vm.StartCommand.Execute(null);
        await Until(() => vm.HostChoice is not null);
        vm.StopCommand.Execute(null);
        await Until(() => !vm.IsCapturing);
        Assert.Equal(0, runner.Runs);
        Assert.Null(roles.Roles);
        Assert.Null(vm.HostChoice);
    }

    [Fact]
    public void DosyaDegisince_VeCanliModaGecince_SecimTemizlenir()
    {
        var (vm, _, roles, _, _) = Make();
        roles.Set(TwoSpeakers(), A);
        vm.UseMicrophone = true;
        Assert.Null(roles.Roles);
        roles.Set(TwoSpeakers(), A);
        vm.UseMicrophone = false;
        Assert.Null(roles.Roles);
    }

    [Fact]
    public async Task Dinle_OynaticiyaDogruParca()
    {
        var player = new Player();
        var (vm, _, _, _, _) = Make(player: player);
        vm.StartCommand.Execute(null);
        await Until(() => vm.HostChoice is not null);
        await vm.HostChoice!.Rows[0].PlayCommand.ExecuteAsync(null);
        Assert.Equal(16000 * 5, player.Lengths.Single());
        vm.StopCommand.Execute(null);
    }

    [Fact]
    public async Task OynaticiDesteklenmez_DinleGizli()
    {
        var (vm, _, _, _, _) = Make(player: new Player(supported: false));
        vm.StartCommand.Execute(null);
        await Until(() => vm.HostChoice is not null);
        Assert.False(vm.HostChoice!.CanPlay);
        Assert.All(vm.HostChoice.Rows, r => Assert.False(r.CanPlay));
        vm.StopCommand.Execute(null);
    }

    [Fact]
    public void SureMetni() => Assert.Equal("1 dk 20 sn", HostChoiceRowVm.FormatDuration(80));
}
