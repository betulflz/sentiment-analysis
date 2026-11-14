using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class TranscriptReviewViewModelTests
{
    [Fact]
    public async Task LateSave_PersistsOriginalSession_WithoutPublishingIntoNewReport()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var pending = new TaskCompletionSource();
        var data = new Data { OnSave = () => pending.Task };
        var vm = new TranscriptReviewViewModel(data, data);
        await vm.LoadAsync(first);
        vm.Rows[0].Text = "eski oturum düzeltmesi";
        var saving = vm.SaveCommand.ExecuteAsync(null);
        await vm.LoadAsync(second);
        bool applied = false;
        vm.ReviewApplied += _ => applied = true;
        pending.SetResult(); await saving;
        Assert.Equal("eski oturum düzeltmesi", data.Saved[first][0].Text);
        Assert.Equal(second, vm.SessionId);
        Assert.False(vm.HasSavedChanges);
        Assert.False(applied);
        Assert.Equal("ham metin", vm.Rows[0].Text);
    }

    [Fact]
    public void TextCorrection_DoesNotCertifyUncertainSpeaker()
    {
        var row = new TranscriptReviewRowVm(new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "ham", "Konuşmacı A", true));
        row.Text = "doğru";
        Assert.True(row.Snapshot().Uncertain);
        row.Speaker = "Aylin";
        Assert.False(row.Snapshot().Uncertain);
        row.Speaker = " ";
        Assert.True(row.Snapshot().Uncertain);
        Assert.Equal(string.Empty, row.Snapshot().Speaker);
    }

    private sealed class DelayedSpeakers : SentimentAnalysis.Application.Diarization.ISpeakerAnalysis
    {
        public bool Enabled => true;
        public TaskCompletionSource<SentimentAnalysis.Application.Diarization.SpeakerAnalysisResult> Pending { get; } = new();
        public Task<SentimentAnalysis.Application.Diarization.SpeakerAnalysisResult> RunAsync(Guid id, int count, CancellationToken ct)
            => Pending.Task;
    }

    [Fact]
    public async Task OldSpeakerResult_DoesNotOverwriteOpenedHistoryReport()
    {
        var data = new Data(); var current = Guid.NewGuid();
        var config = SentimentAnalysis.Application.Configuration.AppConfig.Default;
        var report = new ReportViewModel(new SentimentAnalysis.Application.Fusion.MeetingAggregator(config), config, data, data);
        var speakers = new DelayedSpeakers();
        var main = new MainViewModel(new LiveViewModel(new Runner(Guid.NewGuid())), report,
            new HistoryViewModel(data), new SettingsViewModel(config, _ => { }), speakers);
        main.Live.StartCommand.Execute(null); await main.Live.WaitForIdleAsync();
        await main.History.OpenCommand.ExecuteAsync(new SessionRowVm { Id = current });
        var status = report.DiarizationStatus;
        speakers.Pending.SetResult(new SentimentAnalysis.Application.Diarization.SpeakerAnalysisResult(
            SentimentAnalysis.Application.Diarization.DiarizationOutcome.Completed,
            [new("eski görüşme", TimeSpan.Zero, TimeSpan.FromSeconds(3))],
            [new(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(3))],
            Array.Empty<ChunkResult>()));
        await main.SpeakerAnalysisTask!;
        Assert.DoesNotContain(report.SpeakerLines, l => l.Text == "eski görüşme");
        Assert.Equal(status, report.DiarizationStatus);
    }

    [Fact]
    public async Task SavedReview_UpdatesReportAndOpenEvents_AndSurvivesLateAutomaticResult()
    {
        var data = new Data();
        var config = SentimentAnalysis.Application.Configuration.AppConfig.Default;
        var report = new ReportViewModel(new SentimentAnalysis.Application.Fusion.MeetingAggregator(config),
            config, data, data);
        await report.TranscriptReview.LoadAsync(Guid.NewGuid());
        report.TranscriptReview.Rows[0].Text = "Nasılsınız?";
        report.TranscriptReview.Rows[0].Speaker = "Aylin";
        await report.TranscriptReview.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Nasılsınız?", Assert.Single(report.SpeakerLines).Text);
        Assert.Equal("Aylin", report.SpeakerLines[0].Label);
        Assert.Contains(report.OpenEvents, e => e.Source == "Nasılsınız?" && e.Who == "Aylin");
        report.ApplySpeakers([new("ham", TimeSpan.Zero, TimeSpan.FromSeconds(3))],
            [new(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(3))], 0.5);
        Assert.Equal("Nasılsınız?", Assert.Single(report.SpeakerLines).Text);

        report.LoadSummary(new MeetingSummary(Emotion.Notr, new Dictionary<Emotion, double>(), 0, 0,
            Array.Empty<ChunkResult>(), ValenceArousal.Neutral, 1, ""));
        Assert.Empty(report.TranscriptReview.Rows);
        Assert.False(report.TranscriptReview.HasSavedChanges);
        Assert.Empty(report.SpeakerLines);
    }

    private sealed class Data : ITranscriptReviewStore, ISpeakerDataReader, ISessionStore
    {
        public Task SaveChunkAsync(Guid id, ChunkResult r, CancellationToken ct) => Task.CompletedTask;
        public Task<MeetingSession?> GetSessionAsync(Guid id, CancellationToken ct)
            => Task.FromResult<MeetingSession?>(new(id, DateTimeOffset.UnixEpoch, DetectedLanguage.Turkish, Array.Empty<ChunkResult>()));
        public Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<MeetingSession>>(Array.Empty<MeetingSession>());
        public Task PurgeExpiredAudioAsync(CancellationToken ct) => Task.CompletedTask;
        public Dictionary<Guid, IReadOnlyList<ReviewedTranscriptLine>> Saved { get; } = new();
        public Func<Guid, Task<IReadOnlyList<ReviewedTranscriptLine>?>>? OnRead;
        public Func<Task>? OnSave;
        public bool FailSave;
        public Task<IReadOnlyList<ReviewedTranscriptLine>?> GetTranscriptReviewAsync(Guid id, CancellationToken ct)
            => OnRead?.Invoke(id) ?? Task.FromResult(Saved.GetValueOrDefault(id));
        public async Task SaveTranscriptReviewAsync(Guid id, IReadOnlyList<ReviewedTranscriptLine> lines, CancellationToken ct)
        {
            if (OnSave is not null) await OnSave();
            if (FailSave) throw new IOException("disk unavailable");
            Saved[id] = lines.ToArray();
        }
        public Task<IReadOnlyList<TranscriptSegment>> GetTranscriptSegmentsAsync(Guid id, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<TranscriptSegment>>([new("ham metin", TimeSpan.Zero, TimeSpan.FromSeconds(3))]);
        public Task<IReadOnlyList<SpeakerSegment>> GetSpeakerSegmentsAsync(Guid id, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SpeakerSegment>>([new(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(3))]);
        public Task<IReadOnlyList<ChunkResult>> GetChunkResultsAsync(Guid id, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ChunkResult>>(Array.Empty<ChunkResult>());
    }

    private sealed class Runner(Guid id) : IAnalysisRunner
    {
        public Guid? LastSessionId => id;
        public Task<MeetingSummary> RunAsync(IProgress<ChunkResult> p, CancellationToken ct)
            => Task.FromResult(new MeetingSummary(Emotion.Notr, new Dictionary<Emotion, double>(), 0, 0,
                Array.Empty<ChunkResult>(), ValenceArousal.Neutral, 1, ""));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Main_LoadsEditorForHistoricalAndJustCompletedSession(bool history)
    {
        var data = new Data(); var id = Guid.NewGuid();
        var config = SentimentAnalysis.Application.Configuration.AppConfig.Default;
        var report = new ReportViewModel(new SentimentAnalysis.Application.Fusion.MeetingAggregator(config), config, data, data);
        var main = new MainViewModel(new LiveViewModel(new Runner(id)), report,
            new HistoryViewModel(data), new SettingsViewModel(config, _ => { }));
        if (history) await main.History.OpenCommand.ExecuteAsync(new SessionRowVm { Id = id });
        else { main.Live.StartCommand.Execute(null); await main.Live.WaitForIdleAsync(); }
        await (main.TranscriptLoadTask ?? Task.CompletedTask);
        Assert.Equal(id, report.TranscriptReview.SessionId);
        Assert.Single(report.TranscriptReview.Rows);
    }

    [Fact]
    public async Task SaveAndReopen_PreservesEditedTextAndSpeaker()
    {
        var data = new Data(); var id = Guid.NewGuid();
        var vm = new TranscriptReviewViewModel(data, data);
        await vm.LoadAsync(id);
        var row = Assert.Single(vm.Rows);
        Assert.Equal("ham metin", row.Text);
        Assert.False(vm.SaveCommand.CanExecute(null));
        row.Text = "düzeltilmiş metin"; row.Speaker = "Aylin";
        Assert.True(vm.IsDirty);
        Assert.True(vm.SaveCommand.CanExecute(null));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty);
        Assert.True(vm.HasSavedChanges);
        var reopened = new TranscriptReviewViewModel(data, data);
        await reopened.LoadAsync(id);
        Assert.Equal("düzeltilmiş metin", Assert.Single(reopened.Rows).Text);
        Assert.Equal("Aylin", reopened.Rows[0].Speaker);
        Assert.True(reopened.HasSavedChanges);
    }

    [Fact]
    public async Task FailedSave_KeepsDraftAndDoesNotPublishIt()
    {
        var data = new Data { FailSave = true };
        var vm = new TranscriptReviewViewModel(data, data);
        await vm.LoadAsync(Guid.NewGuid());
        vm.Rows[0].Text = "taslak";
        bool applied = false;
        vm.ReviewApplied += _ => applied = true;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.IsDirty);
        Assert.False(applied);
        Assert.Contains("kaydedilemedi", vm.Status);
        Assert.Equal("taslak", vm.Rows[0].Text);
        vm.DiscardCommand.Execute(null);
        Assert.Equal("ham metin", vm.Rows[0].Text);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task LateRead_DoesNotReplaceNewSession()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var pending = new TaskCompletionSource<IReadOnlyList<ReviewedTranscriptLine>?>();
        var data = new Data { OnRead = id => id == first ? pending.Task : Task.FromResult<IReadOnlyList<ReviewedTranscriptLine>?>(null) };
        var vm = new TranscriptReviewViewModel(data, data);
        var loading = vm.LoadAsync(first);
        await vm.LoadAsync(second);
        vm.Rows[0].Text = "ikinci oturum";
        pending.SetResult([new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "eski oturum", "B")]);
        await loading;
        Assert.Equal("ikinci oturum", vm.Rows[0].Text);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(data.Saved.ContainsKey(second));
        Assert.False(data.Saved.ContainsKey(first));
    }

    [Fact]
    public async Task EditDuringSave_RemainsDirty_AndSpeakerRefreshDoesNotEraseDraft()
    {
        var pending = new TaskCompletionSource();
        var data = new Data { OnSave = () => pending.Task };
        var vm = new TranscriptReviewViewModel(data, data); var id = Guid.NewGuid();
        await vm.LoadAsync(id);
        vm.Rows[0].Text = "ilk düzeltme";
        var saving = vm.SaveCommand.ExecuteAsync(null);
        vm.Rows[0].Text = "ikinci düzeltme";
        await vm.RefreshSpeakersAsync(id);
        pending.SetResult(); await saving;
        Assert.Equal("ilk düzeltme", data.Saved[id][0].Text);
        Assert.Equal("ikinci düzeltme", vm.Rows[0].Text);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task FailedRead_DoesNotAllowOverwritingExistingCorrections()
    {
        var data = new Data { OnRead = _ => throw new IOException("read failed") };
        var vm = new TranscriptReviewViewModel(data, data);
        await vm.LoadAsync(Guid.NewGuid());
        Assert.Empty(vm.Rows);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.Contains("yüklenemedi", vm.Status);
    }
}
