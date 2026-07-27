using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class MainViewModelTests
{
    private sealed class FakeRunner : IAnalysisRunner
    { public Task<MeetingSummary> RunAsync(IProgress<ChunkResult> p, CancellationToken ct) =>
        Task.FromResult(new MeetingSummary(Emotion.Notr, new Dictionary<Emotion,double>(), 0,0,
            Array.Empty<ChunkResult>(), new ValenceArousal(0,0.5), 1, "")); }
    private sealed class FakeStore : ISessionStore
    { public Task SaveChunkAsync(Guid i, ChunkResult r, CancellationToken c)=>Task.CompletedTask;
      public Task<MeetingSession?> GetSessionAsync(Guid i, CancellationToken c)=>Task.FromResult<MeetingSession?>(null);
      public Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken c)=>Task.FromResult<IReadOnlyList<MeetingSession>>(Array.Empty<MeetingSession>());
      public Task PurgeExpiredAudioAsync(CancellationToken c)=>Task.CompletedTask; }
    private sealed class FakeAgg : IMeetingAggregator
    { public MeetingSummary Aggregate(MeetingSession s)=>new(Emotion.Notr,new Dictionary<Emotion,double>(),0,0,Array.Empty<ChunkResult>(),new ValenceArousal(0,0.5),1,""); }

    private static MainViewModel New()
    {
        var live = new LiveViewModel(new FakeRunner());
        var report = new ReportViewModel(new FakeAgg());
        var history = new HistoryViewModel(new FakeStore());
        var settings = new SettingsViewModel(AppConfig.Default, _ => { });
        return new MainViewModel(live, report, history, settings);
    }

    [Fact]
    public void DefaultView_IsLive()
    {
        var vm = New();
        Assert.IsType<LiveViewModel>(vm.CurrentView);
    }

    [Fact]
    public void ShowHistory_SwitchesCurrentView()
    {
        var vm = New();
        vm.ShowHistoryCommand.Execute(null);
        Assert.IsType<HistoryViewModel>(vm.CurrentView);
    }
}
