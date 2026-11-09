using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

/// <summary>Canlı ekranda yazılan yüz adı ve parçalar, görüşme bitince rapora ulaşıp konuşmacı kartından
/// yüz seçilebilmeli (08e dersi: parçalar tek tek testli olsa da tel çekilmemiş olabilir).</summary>
public class MainViewModelFaceWiringTests
{
    private sealed class FaceRunner : IAnalysisRunner
    {
        public Action<IProgress<ChunkResult>>? OnRun;
        public IReadOnlyList<FaceWindowSummary> Summaries = Array.Empty<FaceWindowSummary>();
        public VideoFrame? Frame = new(new byte[4 * 2 * 3], 4, 2, TimeSpan.Zero);
        public IReadOnlyList<(int Index, FaceBox Box)> Boxes = new[] { (0, new FaceBox(1, 1, 2, 2)), (1, new FaceBox(5, 1, 2, 2)) };
        public Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct)
        {
            OnRun?.Invoke(progress);
            return Task.FromResult(new MeetingSummary(Emotion.Notr, new Dictionary<Emotion, double>(), 0, 0,
                Array.Empty<ChunkResult>(), ValenceArousal.Neutral, 1.0, "özet"));
        }
        public Guid? LastSessionId => Guid.NewGuid();
        public VideoFrame? LatestFrame => Frame;
        public IReadOnlyList<(int Index, FaceBox Box)> LatestFaces => Boxes;
        public IReadOnlyList<FaceWindowSummary> FacesFor(ChunkResult r) => Summaries;
    }

    private sealed class FakeAggregator : IMeetingAggregator
    {
        public MeetingSummary Aggregate(MeetingSession s) => throw new NotSupportedException();
    }

    private sealed class FakeSpeakers(SpeakerAnalysisResult result) : ISpeakerAnalysis
    {
        public bool Enabled => true;
        public Task<SpeakerAnalysisResult> RunAsync(Guid id, int expected, CancellationToken ct) => Task.FromResult(result);
    }

    private sealed class NullSessionStore : ISessionStore
    {
        public Task SaveChunkAsync(Guid id, ChunkResult r, CancellationToken ct) => Task.CompletedTask;
        public Task<MeetingSession?> GetSessionAsync(Guid id, CancellationToken ct) => Task.FromResult<MeetingSession?>(null);
        public Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<MeetingSession>>(Array.Empty<MeetingSession>());
        public Task PurgeExpiredAudioAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static ChunkResult Chunk(double a, double b) =>
        new(TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b),
            new FusedResult(new ValenceArousal(0, 0.5), Emotion.Notr, Emotion.Notr, Confidence.Orta,
                true, false, IncongruencePattern.Yok, 1.0,
                TextVa: new ValenceArousal(0, 0.5), ProsodyVa: new ValenceArousal(0, 0.5),
                TextConfidence: 0.8, ProsodyConfidence: 0.8),
            "merhaba");

    private static (MainViewModel Main, ReportViewModel Report) Build(FaceRunner runner, ISpeakerAnalysis speakers)
    {
        var live = new LiveViewModel(runner);
        live.Decision.SelectedProfile = MeetingProfile.Mulakat;
        var report = new ReportViewModel(new FakeAggregator());
        var cfg = AppConfig.Default;
        var main = new MainViewModel(live, report, new HistoryViewModel(new NullSessionStore()),
            new SettingsViewModel(cfg, _ => { }), speakers, cfg);
        return (main, report);
    }

    [Fact]
    public async Task PaneldeYazilanAdVeYuzler_RaporunKonusmaciKartinaUlasir()
    {
        var runner = new FaceRunner
        {
            Summaries = new[] { new FaceWindowSummary(0, 1, 0, 0.9, 0, 0, 1), new FaceWindowSummary(1, 1, 0, -0.9, 0, 0, 1) },
        };
        var speakers = new FakeSpeakers(new SpeakerAnalysisResult(
            DiarizationOutcome.Completed,
            [new TranscriptSegment("merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(10))],
            [new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(10))],
            [Chunk(0, 10)]));
        var (main, report) = Build(runner, speakers);
        runner.OnRun = progress =>
        {
            main.Live.RefreshPreview();
            main.Live.FacePanels[0].Name = "Ayşe";
            progress.Report(Chunk(0, 10));
        };

        main.Live.StartCommand.Execute(null);
        await main.Live.WaitForIdleAsync();
        await (main.SpeakerAnalysisTask ?? Task.CompletedTask);

        var card = report.SpeakerEmotions[0];
        Assert.Equal(new[] { "Eşleştirme yok", "Ayşe", "Yüz 2" }, card.FaceOptions.Select(o => o.Text));

        card.SelectedFace = card.FaceOptions.Single(o => o.FaceIndex == 0);

        var after = report.SpeakerEmotions[0];
        Assert.Equal("Ayşe", after.Label);
        Assert.True(after.CombinedAnalysis.Valence > 0.2);
        Assert.Contains("1/1", after.FaceNote);
    }

    [Fact]
    public void UserFaceNames_YalnizKullanicininYazdigiAdlar()
    {
        var runner = new FaceRunner();
        var vm = new LiveViewModel(runner);
        vm.Decision.SelectedProfile = MeetingProfile.Mulakat;
        vm.RefreshPreview();

        vm.FacePanels[0].Name = "Ayşe";

        Assert.Equal(new Dictionary<int, string> { [0] = "Ayşe" }, vm.UserFaceNames);
    }

    [Fact]
    public void RunChunks_RaporlananParcalariTutar_YeniKosudaTemizlenir()
    {
        var vm = new LiveViewModel(new FaceRunner());
        vm.Report(Chunk(0, 10));
        Assert.Single(vm.RunChunks);

        vm.StartCommand.Execute(null);

        Assert.Empty(vm.RunChunks);
    }
}
