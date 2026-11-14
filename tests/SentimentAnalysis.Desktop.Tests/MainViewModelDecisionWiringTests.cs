using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

/// <summary>Koşu sonunda canlı karar geçmişinin rapora aktarıldığını ve veritabanına yazıldığını doğrular (11e).</summary>
public class MainViewModelDecisionWiringTests
{
    private static ChunkResult Chunk(double t, double valence, double arousal) =>
        new(TimeSpan.FromSeconds(t), TimeSpan.FromSeconds(t + 10),
            new FusedResult(new ValenceArousal(valence, arousal), Emotion.Notr, Emotion.Notr, Confidence.Orta,
                true, false, IncongruencePattern.Yok, 0.9,
                TextVa: new ValenceArousal(valence, arousal), ProsodyVa: new ValenceArousal(valence, arousal),
                TextConfidence: 0.7, ProsodyConfidence: 0.7),
            "metin");

    private sealed class FakeRunner(Guid? sessionId, params ChunkResult[] chunks) : IAnalysisRunner
    {
        public Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct)
        {
            foreach (var c in chunks) progress.Report(c);
            return Task.FromResult(Summary());
        }
        public Guid? LastSessionId => sessionId;
    }

    private sealed class FakeAggregator : IMeetingAggregator
    {
        public MeetingSummary Aggregate(MeetingSession s) => Summary();
    }

    private sealed class FakeDecisionStore(Exception? error = null) : IDecisionStore
    {
        public readonly List<(Guid Id, DecisionSessionRecord Record)> Saved = new();

        public Task SaveDecisionSessionAsync(Guid sessionId, DecisionSessionRecord record, CancellationToken ct)
        {
            if (error is not null) throw error;
            Saved.Add((sessionId, record));
            return Task.CompletedTask;
        }

        public Task<DecisionSessionRecord?> GetDecisionSessionAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<DecisionSessionRecord?>(null);
    }

    private sealed class NullSessionStore : ISessionStore
    {
        public Task SaveChunkAsync(Guid id, ChunkResult r, CancellationToken ct) => Task.CompletedTask;
        public Task<MeetingSession?> GetSessionAsync(Guid id, CancellationToken ct) => Task.FromResult<MeetingSession?>(null);
        public Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<MeetingSession>>(Array.Empty<MeetingSession>());
        public Task PurgeExpiredAudioAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static MeetingSummary Summary() => new(
        Emotion.Notr, new Dictionary<Emotion, double>(), 0, 0,
        Array.Empty<ChunkResult>(), ValenceArousal.Neutral, 1.0, "özet");

    private static readonly ChunkResult[] ThreeChunks = { Chunk(0, 0.5, 0.5), Chunk(10, -0.8, 0.9), Chunk(20, 0.5, 0.5) };

    private static MainViewModel Build(Guid? sessionId, IDecisionStore? store, params ChunkResult[] chunks)
    {
        var live = new LiveViewModel(new FakeRunner(sessionId, chunks));
        var report = new ReportViewModel(new FakeAggregator());
        var history = new HistoryViewModel(new NullSessionStore());
        var settings = new SettingsViewModel(AppConfig.Default, _ => { });
        return new MainViewModel(live, report, history, settings, decisions: store);
    }

    private static async Task RunToEnd(MainViewModel vm)
    {
        vm.Live.StartCommand.Execute(null);
        await vm.Live.WaitForIdleAsync();
        await (vm.DecisionSaveTask ?? Task.CompletedTask);
    }

    [Fact]
    public async Task KosuBitince_RaporKararVerisiniAlir_VeKaydedilir()
    {
        var id = Guid.NewGuid();
        var store = new FakeDecisionStore();
        var vm = Build(id, store, ThreeChunks);
        await RunToEnd(vm);

        Assert.True(vm.Report.HasDecisionData);
        Assert.Equal(3, vm.Report.IndicatorTimeline[0].Values.Count);
        var (savedId, record) = Assert.Single(store.Saved);
        Assert.Equal(id, savedId);
        Assert.Equal(vm.Report.FollowUp.Current!.Best, record.FollowUp);
        Assert.Equal(vm.Report.FollowUp.Current.Reason, record.FollowUpReason);
        Assert.Equal(3, record.Session.Windows.Count);
        Assert.Equal(string.Empty, vm.Report.DecisionSaveStatus);
    }

    [Fact]
    public async Task RaporAcikkenYeniKosu_RaporEskiGorusmeyiKorur()
    {
        var vm = Build(Guid.NewGuid(), new FakeDecisionStore(), ThreeChunks);
        await RunToEnd(vm);
        var timeline = vm.Report.IndicatorTimeline;
        var current = vm.Report.FollowUp.Current;

        vm.Live.Decision.Start();

        Assert.Same(timeline, vm.Report.IndicatorTimeline);
        Assert.Same(current, vm.Report.FollowUp.Current);
        Assert.Equal(3, vm.Report.IndicatorTimeline[0].Values.Count);
    }

    [Fact]
    public async Task KayitHatasi_Cokmez_UyariGosterir()
    {
        var vm = Build(Guid.NewGuid(), new FakeDecisionStore(new InvalidOperationException("veritabanı kilitli")), ThreeChunks);
        await RunToEnd(vm);

        Assert.True(vm.Report.HasDecisionData);
        Assert.StartsWith("Karar kaydı saklanamadı", vm.Report.DecisionSaveStatus);
        Assert.DoesNotContain("veritabanı kilitli", vm.Report.DecisionSaveStatus); // ham teknik mesaj kullanıcıya gösterilmez
    }

    /// <summary>İlk çağrıyı dışarıdan bitirilene kadar bekletir, sonrakileri hemen kaydeder.</summary>
    private sealed class SlowFirstStore : IDecisionStore
    {
        public readonly TaskCompletionSource First = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public Task SaveDecisionSessionAsync(Guid sessionId, DecisionSessionRecord record, CancellationToken ct) =>
            ++_calls == 1 ? First.Task : Task.CompletedTask;

        public Task<DecisionSessionRecord?> GetDecisionSessionAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<DecisionSessionRecord?>(null);
    }

    [Fact]
    public async Task GecBitenKayitHatasi_SonrakiGorusmeninRaporunaDusmez()
    {
        var store = new SlowFirstStore();
        var vm = Build(Guid.NewGuid(), store, ThreeChunks);
        vm.Live.StartCommand.Execute(null);
        await vm.Live.WaitForIdleAsync();
        var firstSave = vm.DecisionSaveTask!;

        await RunToEnd(vm); // ikinci görüşme biter ve kaydedilir; rapor artık onu gösterir
        store.First.SetException(new InvalidOperationException("veritabanı kilitli"));
        await firstSave;

        Assert.Equal(string.Empty, vm.Report.DecisionSaveStatus);
    }

    [Fact]
    public async Task SessionIdYok_VeyaStoreYok_KayitDenenmez()
    {
        var store = new FakeDecisionStore();
        var noId = Build(null, store, ThreeChunks);
        await RunToEnd(noId);
        Assert.Empty(store.Saved);
        Assert.True(noId.Report.HasDecisionData);

        var noStore = Build(Guid.NewGuid(), null, ThreeChunks);
        await RunToEnd(noStore);
        Assert.Null(noStore.DecisionSaveTask);
        Assert.True(noStore.Report.HasDecisionData);
    }

    [Fact]
    public async Task PencereYok_KayitDenenmez()
    {
        var store = new FakeDecisionStore();
        var vm = Build(Guid.NewGuid(), store);
        await RunToEnd(vm);
        Assert.Empty(store.Saved);
        Assert.False(vm.Report.HasDecisionData);
    }
}
