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
}
