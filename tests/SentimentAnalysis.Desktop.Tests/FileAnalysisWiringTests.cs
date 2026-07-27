using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class FileAnalysisWiringTests
{
    private sealed class FakePicker(string? path) : IAudioFilePicker
    { public Task<string?> PickAudioFileAsync() => Task.FromResult(path); }

    private sealed class FakeRunner : IAnalysisRunner
    {
        public readonly ChunkResult[] Chunks;
        public FakeRunner(params ChunkResult[] c) => Chunks = c;
        public async Task<MeetingSummary> RunAsync(IProgress<ChunkResult> p, CancellationToken ct)
        {
            await Task.Yield();
            foreach (var c in Chunks) p.Report(c);
            return new MeetingSummary(Emotion.Mutlu, new Dictionary<Emotion,double>{[Emotion.Mutlu]=1.0},
                0.3, 0.1, Array.Empty<ChunkResult>(), new ValenceArousal(0.4,0.5), 0.9, "olumlu geçti");
        }
    }

    private static ChunkResult Chunk(double t) =>
        new(TimeSpan.FromSeconds(t), TimeSpan.FromSeconds(t+1),
            new FusedResult(new ValenceArousal(0.5,0.6), Emotion.Mutlu, Emotion.Notr, Confidence.Orta,
                true, false, IncongruencePattern.Yok, 0.9,
                TextVa: new ValenceArousal(0.5, 0.6), ProsodyVa: new ValenceArousal(0.5, 0.6),
                TextConfidence: 0.5, ProsodyConfidence: 0.5), $"m{t}");

    [Fact]
    public void Start_DisabledUntilFileSelected()
    {
        var sel = new SelectedAudio();
        var vm = new LiveViewModel(new FakeRunner(), new FakePicker(null), sel);
        Assert.False(vm.StartCommand.CanExecute(null)); // dosya yok → başlatılamaz
    }

    [Fact]
    public async Task SelectFile_SetsPathAndEnablesStart()
    {
        var sel = new SelectedAudio();
        var vm = new LiveViewModel(new FakeRunner(), new FakePicker("/tmp/ses.mp3"), sel);
        await vm.SelectFileCommand.ExecuteAsync(null);
        Assert.Equal("/tmp/ses.mp3", sel.Path);
        Assert.Equal("ses.mp3", vm.SelectedFile);
        Assert.True(vm.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task Start_AfterSelect_RunsAndRaisesCompleted()
    {
        var sel = new SelectedAudio();
        var runner = new FakeRunner(Chunk(0), Chunk(1));
        var vm = new LiveViewModel(runner, new FakePicker("/tmp/ses.mp3"), sel);
        await vm.SelectFileCommand.ExecuteAsync(null);

        MeetingSummary? done = null;
        vm.Completed += s => done = s;
        vm.StartCommand.Execute(null);
        await vm.WaitForIdleAsync();

        Assert.Equal(2, vm.Timeline.Count);
        Assert.NotNull(done);
        Assert.Equal("olumlu geçti", done!.TurkishSummary);
        Assert.False(vm.IsCapturing);
    }

    [Fact]
    public void ReportViewModel_LoadSummary_ShowsLocalizedFields()
    {
        var vm = new ReportViewModel(new NoAggregator());
        var s = new MeetingSummary(Emotion.Kizgin, new Dictionary<Emotion,double>{[Emotion.Kizgin]=0.7,[Emotion.Notr]=0.3},
            -0.5, 0.2, Array.Empty<ChunkResult>(), new ValenceArousal(-0.4,0.7), 0.6, "gergin");
        vm.LoadSummary(s);
        Assert.True(vm.HasReport);
        Assert.Equal("Kızgın", vm.DominantEmotion);
        Assert.Equal("gergin", vm.TurkishSummary);
        Assert.Equal(2, vm.TimeShare.Count);
    }

    private sealed class NoAggregator : IMeetingAggregator
    { public MeetingSummary Aggregate(MeetingSession s) => throw new NotImplementedException(); }

    private sealed class FakeStore : ISessionStore
    { public Task SaveChunkAsync(Guid i, ChunkResult r, CancellationToken c)=>Task.CompletedTask;
      public Task<MeetingSession?> GetSessionAsync(Guid i, CancellationToken c)=>Task.FromResult<MeetingSession?>(null);
      public Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken c)=>Task.FromResult<IReadOnlyList<MeetingSession>>(Array.Empty<MeetingSession>());
      public Task PurgeExpiredAudioAsync(CancellationToken c)=>Task.CompletedTask; }

    [Fact]
    public async Task MainViewModel_AfterRun_ShowsReportAndRefreshesHistory()
    {
        var sel = new SelectedAudio();
        var live = new LiveViewModel(new FakeRunner(Chunk(0)), new FakePicker("/tmp/a.wav"), sel);
        var report = new ReportViewModel(new NoAggregator());
        var history = new HistoryViewModel(new FakeStore());
        var settings = new SettingsViewModel(SentimentAnalysis.Application.Configuration.AppConfig.Default, _ => { });
        var main = new MainViewModel(live, report, history, settings);

        await live.SelectFileCommand.ExecuteAsync(null);
        live.StartCommand.Execute(null);
        await live.WaitForIdleAsync();

        Assert.True(report.HasReport);              // koşu sonrası rapor doldu
        Assert.IsType<ReportViewModel>(main.CurrentView); // rapora geçildi
    }
}
