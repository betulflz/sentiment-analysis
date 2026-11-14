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

/// <summary>"Durdur"a basıldığında konuşmacı geçişinin GERÇEKTEN tetiklendiğini ve sonucunun
/// rapora düştüğünü doğrular. 08e dersi: bileşenlerin tek tek testli olması, aralarındaki telin
/// çekildiği anlamına gelmez — kırılan tam olarak buydu.</summary>
public class MainViewModelSpeakerWiringTests
{
    private sealed class FakeRunner(Guid? sessionId) : IAnalysisRunner
    {
        public Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct)
            => Task.FromResult(Summary());
        public Guid? LastSessionId => sessionId;
    }

    private sealed class FakeAggregator : IMeetingAggregator
    {
        public MeetingSummary Aggregate(MeetingSession s) => Summary();
    }

    private sealed class FakeSpeakers(SpeakerAnalysisResult result, bool enabled = true) : ISpeakerAnalysis
    {
        public bool WasCalled;
        public Guid? SeenSessionId;
        public int SeenExpected = -1;
        public bool Enabled => enabled;

        public Task<SpeakerAnalysisResult> RunAsync(Guid sessionId, int expectedSpeakers, CancellationToken ct)
        {
            WasCalled = true;
            SeenSessionId = sessionId;
            SeenExpected = expectedSpeakers;
            return Task.FromResult(result);
        }

        public bool PrecomputedCalled;
        public IReadOnlyList<SpeakerSegment>? SeenSegments;
        public Task<SpeakerAnalysisResult> RunPrecomputedAsync(Guid sessionId, IReadOnlyList<SpeakerSegment> segments,
            float[] mono16k, CancellationToken ct)
        {
            PrecomputedCalled = true;
            SeenSessionId = sessionId;
            SeenSegments = segments;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingSpeakers : ISpeakerAnalysis
    {
        public bool Enabled => true;
        public Task<SpeakerAnalysisResult> RunAsync(Guid id, int expectedSpeakers, CancellationToken ct)
            => throw new InvalidOperationException("patladı");
    }

    private static MeetingSummary Summary() => new(
        Emotion.Notr, new Dictionary<Emotion, double>(), 0, 0,
        Array.Empty<ChunkResult>(), ValenceArousal.Neutral, 1.0, "özet");

    private static MainViewModel Build(ISpeakerAnalysis? speakers, Guid? sessionId,
        out ReportViewModel report, bool enabled = true, IFileRoleSelection? fileRoles = null)
    {
        var runner = new FakeRunner(sessionId);
        var live = new LiveViewModel(runner);
        report = new ReportViewModel(new FakeAggregator());
        var history = new HistoryViewModel(new NullSessionStore());
        var settings = new SettingsViewModel(AppConfig.Default, _ => { });
        var cfg = AppConfig.Default with
        {
            Diarization = AppConfig.Default.Diarization with { Enabled = enabled }
        };
        return new MainViewModel(live, report, history, settings, speakers, cfg, fileRoles: fileRoles);
    }

    private sealed class NullSessionStore : ISessionStore
    {
        public Task SaveChunkAsync(Guid id, ChunkResult r, CancellationToken ct) => Task.CompletedTask;
        public Task<MeetingSession?> GetSessionAsync(Guid id, CancellationToken ct) => Task.FromResult<MeetingSession?>(null);
        public Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<MeetingSession>>(Array.Empty<MeetingSession>());
        public Task PurgeExpiredAudioAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static TranscriptSegment T(string text, double s, double e) =>
        new(text, TimeSpan.FromSeconds(s), TimeSpan.FromSeconds(e));

    [Fact]
    public async Task KosuBitince_KonusmaciGecisiTetiklenir_VeRaporaDuser()
    {
        var id = Guid.NewGuid();
        var speakers = new FakeSpeakers(new SpeakerAnalysisResult(
            DiarizationOutcome.Completed,
            [T("Merhaba", 0, 2), T("iyiyim", 2, 4)],
            [new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(2)),
             new SpeakerSegment(new SpeakerId(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4))]));

        var vm = Build(speakers, id, out var report);
        vm.Live.StartCommand.Execute(null);
        await vm.Live.WaitForIdleAsync();
        await (vm.SpeakerAnalysisTask ?? Task.CompletedTask);

        Assert.True(speakers.WasCalled, "konuşmacı geçişi hiç tetiklenmedi");
        Assert.Equal(id, speakers.SeenSessionId);
        Assert.Equal(2, speakers.SeenExpected); // varsayılan: 2 kişi
        Assert.Equal(2, report.SpeakerLines.Count);
        Assert.Equal("Konuşmacı A", report.SpeakerLines[0].Label);
        Assert.Equal("Konuşmacılar hazır.", report.DiarizationStatus);
    }

    [Fact]
    public async Task DosyaRolu_AyarKapaliyken_DePrecomputedCalisir_SonraSecimTemizlenir()
    {
        var id = Guid.NewGuid();
        var segs = new[] { new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(2)),
                           new SpeakerSegment(new SpeakerId(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)) };
        var speakers = new FakeSpeakers(new SpeakerAnalysisResult(DiarizationOutcome.Completed,
            [T("Merhaba", 0, 2), T("iyiyim", 2, 4)], segs), enabled: false);
        var roles = new FileRoleSelection();
        roles.Set(new SpeakerPreparationResult(DiarizationOutcome.Completed, segs,
            Array.Empty<SpeakerChoice>(), new float[4 * 16000], false), new SpeakerId(0));
        var vm = Build(speakers, id, out var report, enabled: false, fileRoles: roles);

        vm.Live.StartCommand.Execute(null);
        await vm.Live.WaitForIdleAsync();
        await (vm.SpeakerAnalysisTask ?? Task.CompletedTask);

        Assert.True(speakers.PrecomputedCalled);
        Assert.False(speakers.WasCalled);
        Assert.Equal(id, speakers.SeenSessionId);
        Assert.Equal(segs, speakers.SeenSegments);
        Assert.Equal(2, report.SpeakerLines.Count);
        Assert.Null(roles.Prepared);
    }

    [Fact]
    public async Task SecilenKisiSayisi_GorusmeBasindakiDegerIletilir()
    {
        var speakers = new FakeSpeakers(SpeakerAnalysisResult.Disabled);
        var vm = Build(speakers, Guid.NewGuid(), out _);
        vm.Live.SelectedSpeakerCount = vm.Live.SpeakerCountOptions.Single(o => o.Value == 3);
        vm.Live.StartCommand.Execute(null);
        vm.Live.SelectedSpeakerCount = vm.Live.SpeakerCountOptions.Single(o => o.Value == 0); // görüşme sürerken değişse de
        await vm.Live.WaitForIdleAsync();
        await (vm.SpeakerAnalysisTask ?? Task.CompletedTask);

        Assert.Equal(3, speakers.SeenExpected);
    }

    [Fact]
    public void KisiSayisiSecenekleri_Varsayilan2_BilmiyorumDahil()
    {
        var vm = Build(null, null, out _);
        Assert.Equal(new[] { 2, 3, 4, 5, 0 }, vm.Live.SpeakerCountOptions.Select(o => o.Value));
        Assert.Equal("Bilmiyorum", vm.Live.SpeakerCountOptions[^1].Text);
        Assert.Equal(2, vm.Live.SelectedSpeakerCount.Value);
    }

    [Fact]
    public async Task OzellikKapaliysa_HicTetiklenmez_RaporSessizKalir()
    {
        var speakers = new FakeSpeakers(SpeakerAnalysisResult.Disabled, enabled: false);

        var vm = Build(speakers, Guid.NewGuid(), out var report, enabled: false);
        vm.Live.StartCommand.Execute(null);
        await vm.Live.WaitForIdleAsync();
        await (vm.SpeakerAnalysisTask ?? Task.CompletedTask);

        Assert.False(speakers.WasCalled);
        Assert.Equal(string.Empty, report.DiarizationStatus);   // şerit görünmez
    }

    [Fact]
    public async Task OturumKimligiYoksa_Tetiklenmez()
    {
        // Dosya analizinde/kalıcı oturumu olmayan koşuda konuşmacı geçişi denenmez.
        var speakers = new FakeSpeakers(SpeakerAnalysisResult.Disabled);

        var vm = Build(speakers, sessionId: null, out var report);
        vm.Live.StartCommand.Execute(null);
        await vm.Live.WaitForIdleAsync();
        await (vm.SpeakerAnalysisTask ?? Task.CompletedTask);

        Assert.False(speakers.WasCalled);
    }

    [Fact]
    public async Task ServisPatlarsa_UygulamaCokmez_RaporBasarisizDer()
    {
        var vm = Build(new ThrowingSpeakers(), Guid.NewGuid(), out var report);

        vm.Live.StartCommand.Execute(null);
        await vm.Live.WaitForIdleAsync();
        await (vm.SpeakerAnalysisTask ?? Task.CompletedTask);   // fırlatmamalı

        Assert.Contains("başarısız", report.DiarizationStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SesYoksa_NedeniniSoyler_SatirUretmez()
    {
        var speakers = new FakeSpeakers(new SpeakerAnalysisResult(
            DiarizationOutcome.NoAudio, Array.Empty<TranscriptSegment>(), Array.Empty<SpeakerSegment>()));

        var vm = Build(speakers, Guid.NewGuid(), out var report);
        vm.Live.StartCommand.Execute(null);
        await vm.Live.WaitForIdleAsync();
        await (vm.SpeakerAnalysisTask ?? Task.CompletedTask);

        Assert.Contains("ses", report.DiarizationStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(report.SpeakerLines);
    }

    [Fact]
    public async Task RaporOzeti_KonusmaciGecisiniBEKLEMEZ()
    {
        // "Durdur" sonrası rapor ANINDA gelmeli; konuşmacılar sonradan düşer (08e dersi).
        var gate = new TaskCompletionSource();
        var slow = new SlowSpeakers(gate);

        var vm = Build(slow, Guid.NewGuid(), out var report);
        vm.Live.StartCommand.Execute(null);
        await vm.Live.WaitForIdleAsync();

        Assert.True(report.HasReport, "özet, konuşmacı geçişini beklemeden gösterilmeli");
        gate.SetResult();
        await (vm.SpeakerAnalysisTask ?? Task.CompletedTask);
    }

    private sealed class SlowSpeakers(TaskCompletionSource gate) : ISpeakerAnalysis
    {
        public bool Enabled => true;
        public async Task<SpeakerAnalysisResult> RunAsync(Guid id, int expectedSpeakers, CancellationToken ct)
        {
            await gate.Task.ConfigureAwait(false);
            return SpeakerAnalysisResult.Disabled;
        }
    }
}
