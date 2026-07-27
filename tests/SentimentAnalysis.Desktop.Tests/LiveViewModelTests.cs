using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class LiveViewModelTests
{
    private static ChunkResult Chunk(double t, Emotion e, double v, bool flagged = false) =>
        new(TimeSpan.FromSeconds(t), TimeSpan.FromSeconds(t + 1),
            new FusedResult(new ValenceArousal(v, 0.6), e, Emotion.Notr, Confidence.Orta,
                !flagged, flagged, flagged ? IncongruencePattern.Maskeleme : IncongruencePattern.Yok,
                flagged ? 0.2 : 0.9,
                TextVa: new ValenceArousal(v, 0.6), ProsodyVa: new ValenceArousal(v, 0.6),
                TextConfidence: 0.7, ProsodyConfidence: 0.7),
            $"metin-{t}");

    private sealed class FakeRunner : IAnalysisRunner
    {
        private readonly ChunkResult[] _chunks;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FakeRunner(params ChunkResult[] chunks) => _chunks = chunks;
        public async Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct)
        {
            Started.TrySetResult();
            await Task.Yield();
            foreach (var c in _chunks) { ct.ThrowIfCancellationRequested(); progress.Report(c); }
            return new MeetingSummary(Emotion.Mutlu, new System.Collections.Generic.Dictionary<Emotion,double>(),
                0, 0, Array.Empty<ChunkResult>(), new ValenceArousal(0.3, 0.5), 0.8, "özet");
        }
    }

    [Fact]
    public void Report_AddsRowAndUpdatesCurrent()
    {
        var vm = new LiveViewModel(new FakeRunner());
        vm.Report(Chunk(0, Emotion.Kizgin, -0.7, flagged: true));

        Assert.Single(vm.Timeline);
        Assert.Equal("Kızgın", vm.Timeline[0].Emotion);
        Assert.Equal("Kızgın", vm.CurrentEmotion);
        Assert.Equal(-0.7, vm.CurrentValence, precision: 5);
        Assert.True(vm.Timeline[0].Flagged);
        Assert.Contains("Maskeleme", vm.CongruenceBadge); // uyumsuzluk rozeti lokalize
    }

    [Fact]
    public async Task Start_RunsRunner_FeedsTimeline_ThenStops()
    {
        var runner = new FakeRunner(Chunk(0, Emotion.Mutlu, 0.5), Chunk(1, Emotion.Sakin, 0.3));
        var vm = new LiveViewModel(runner);

        Assert.False(vm.IsCapturing);
        vm.StartCommand.Execute(null);
        await runner.Started.Task;            // koşu başladı
        // StartCommand async void olabilir → kısa bekleme yerine tamamlanmayı VM üzerinden bekle:
        await vm.WaitForIdleAsync();          // test yardımcısı: koşu görevini bekler

        Assert.Equal(2, vm.Timeline.Count);
        Assert.False(vm.IsCapturing);         // koşu bitince kapanır
    }

    [Fact]
    public async Task Stop_CancelsCapture()
    {
        var runner = new FakeRunner(Chunk(0, Emotion.Mutlu, 0.5));
        var vm = new LiveViewModel(runner);
        vm.StartCommand.Execute(null);
        await runner.Started.Task;            // koşu başladı
        vm.StopCommand.Execute(null);         // iptal
        // İptal sonrası IsCapturing false'a döner (WaitForIdle ile deterministik).
        await vm.WaitForIdleAsync();
        Assert.False(vm.IsCapturing); // çökme olmamalı
    }

    /// <summary>Metin ve ses birbirinden AYRI VA taşıyan chunk — panellerin karışmadığını gösterir.</summary>
    private static ChunkResult SplitChunk(ValenceArousal text, ValenceArousal prosody, ValenceArousal combined) =>
        new(TimeSpan.Zero, TimeSpan.FromSeconds(1),
            new FusedResult(combined, EmotionWheel.Map(combined), Emotion.Notr, Confidence.Dusuk,
                false, true, IncongruencePattern.Maskeleme, 0.1,
                TextVa: text, ProsodyVa: prosody, TextConfidence: 0.8, ProsodyConfidence: 0.6),
            "harika, çok memnunum");

    [Fact]
    public void Panels_BeforeAnyChunk_AreEmpty()
    {
        var vm = new LiveViewModel(new FakeRunner());

        Assert.False(vm.TextAnalysis.HasData);
        Assert.False(vm.AudioAnalysis.HasData);
        Assert.False(vm.FinalAnalysis.HasData);
        Assert.Equal("Metin Analizi", vm.TextAnalysis.Title);
        Assert.Equal("Ses Analizi", vm.AudioAnalysis.Title);
        Assert.Equal("Nihai Karar", vm.FinalAnalysis.Title);
    }

    [Fact]
    public void Report_FeedsEachPanelFromItsOwnSource()
    {
        var vm = new LiveViewModel(new FakeRunner());

        vm.Report(SplitChunk(
            text:     new ValenceArousal(0.80, 0.50),   // "Mutlu"
            prosody:  new ValenceArousal(-0.50, 0.80),  // "Gergin"
            combined: new ValenceArousal(0.00, 0.50))); // "Nötr"

        Assert.Equal("Mutlu", vm.TextAnalysis.Emotion);
        Assert.Equal("Gergin", vm.AudioAnalysis.Emotion);
        Assert.Equal("Nötr", vm.FinalAnalysis.Emotion);

        // Eksenler de kendi kaynağından gelir — karışmıyor.
        Assert.Equal(0.80, vm.TextAnalysis.Valence, precision: 6);
        Assert.Equal(-0.50, vm.AudioAnalysis.Valence, precision: 6);
    }

    [Fact]
    public void Report_PanelsExposeFullDistribution()
    {
        var vm = new LiveViewModel(new FakeRunner());

        vm.Report(Chunk(0, Emotion.Mutlu, 0.5));

        Assert.Equal(8, vm.FinalAnalysis.Shares.Count);
        Assert.Equal(100, vm.FinalAnalysis.Shares.Sum(s => s.Percent));
        Assert.True(vm.FinalAnalysis.HasData);
    }

    [Fact]
    public void Report_SetsLocalizedConfidence()
    {
        var vm = new LiveViewModel(new FakeRunner());

        vm.Report(Chunk(0, Emotion.Mutlu, 0.5));   // Confidence.Orta

        Assert.Equal("Orta", vm.CurrentConfidence);
    }

    [Fact]
    public void CanStart_FileMode_RequiresSelectedFile()
    {
        var selected = new SelectedAudio();
        var mode = new CaptureModeState { Mode = CaptureMode.Dosya };
        var vm = new LiveViewModel(new FakeRunner(), null, selected, null, mode);

        Assert.False(vm.StartCommand.CanExecute(null));

        selected.Path = "/tmp/ses.wav";
        vm.UseMicrophone = false;   // CanExecute yeniden değerlendirilsin

        Assert.True(vm.StartCommand.CanExecute(null));
    }

    [Fact]
    public void CanStart_MicrophoneMode_DoesNotRequireFile()
    {
        var mode = new CaptureModeState();
        var vm = new LiveViewModel(new FakeRunner(), null, new SelectedAudio(), null, mode);

        vm.UseMicrophone = true;

        Assert.True(vm.StartCommand.CanExecute(null));
        Assert.Equal(CaptureMode.Mikrofon, mode.Mode);
    }

    [Fact]
    public void UseMicrophone_WritesThroughToSharedState()
    {
        var mode = new CaptureModeState();
        var vm = new LiveViewModel(new FakeRunner(), null, new SelectedAudio(), null, mode);

        vm.UseMicrophone = true;
        Assert.Equal(CaptureMode.Mikrofon, mode.Mode);

        vm.UseMicrophone = false;
        Assert.Equal(CaptureMode.Dosya, mode.Mode);
    }

    private sealed class ThrowingRunner : IAnalysisRunner
    {
        private readonly Exception _ex;
        public ThrowingRunner(Exception ex) => _ex = ex;
        public Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct)
            => throw _ex;
    }

    [Fact]
    public async Task Start_OnPermissionError_SurfacesFullMessageInCaptureError()
    {
        const string uzunMesaj = "Mikrofon izni reddedildi. Sistem Ayarları > Gizlilik ve Güvenlik > " +
            "Mikrofon bölümünden bu uygulamaya izin verin ve tekrar deneyin.";
        var runner = new ThrowingRunner(new InvalidOperationException(uzunMesaj));
        var vm = new LiveViewModel(runner, null, new SelectedAudio { Path = "/tmp/ses.wav" });

        vm.StartCommand.Execute(null);
        await vm.WaitForIdleAsync();

        Assert.Contains(uzunMesaj, vm.CaptureError);
        Assert.False(vm.IsCapturing);
    }
}
