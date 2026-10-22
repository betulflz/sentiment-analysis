using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class PreviewTests
{
    private sealed class PreviewRunner : IAnalysisRunner
    {
        public VideoFrame? Frame;
        public IReadOnlyList<(int Index, FaceBox Box)> Boxes = Array.Empty<(int, FaceBox)>();
        public Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct) =>
            Task.FromResult(MeetingSummary.CreateEmpty(AnalysisOutcome.NoResult));
        public VideoFrame? LatestFrame => Frame;
        public IReadOnlyList<(int Index, FaceBox Box)> LatestFaces => Boxes;
    }

    private static VideoFrame Frame() => new(new byte[4 * 2 * 3], 4, 2, TimeSpan.Zero);

    [Fact]
    public void ToBgra_SwapsChannels()
    {
        var rgb = new byte[2 * 1 * 3];
        rgb[0] = 10; rgb[1] = 20; rgb[2] = 30;
        var bgra = PreviewPixels.ToBgra(new VideoFrame(rgb, 2, 1, TimeSpan.Zero));
        Assert.Equal(2 * 1 * 4, bgra.Length);
        Assert.Equal(new byte[] { 30, 20, 10, 255 }, bgra[..4]);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, bgra[4..]);
    }

    [Fact]
    public void RefreshPreview_ShowsFrameAndLabels()
    {
        var runner = new PreviewRunner { Frame = Frame(), Boxes = new[] { (1, new FaceBox(10, 20, 30, 40)) } };
        var vm = new LiveViewModel(runner);

        vm.RefreshPreview();

        Assert.True(vm.HasPreview);
        Assert.Same(runner.Frame, vm.PreviewFrame);
        var box = Assert.Single(vm.PreviewFaces);
        Assert.Equal(new FaceBoxVm(10, 20, 30, 40, "Yüz 2"), box);
    }

    [Fact]
    public void RefreshPreview_NoFrame_HidesPreview()
    {
        var runner = new PreviewRunner { Frame = Frame(), Boxes = new[] { (0, new FaceBox(1, 1, 5, 5)) } };
        var vm = new LiveViewModel(runner);
        vm.RefreshPreview();

        runner.Frame = null;
        runner.Boxes = Array.Empty<(int, FaceBox)>();
        vm.RefreshPreview();

        Assert.False(vm.HasPreview);
        Assert.Null(vm.PreviewFrame);
        Assert.Empty(vm.PreviewFaces);
    }

    [Fact]
    public void RefreshPreview_SameFrame_NoPropertyChange()
    {
        var runner = new PreviewRunner { Frame = Frame() };
        var vm = new LiveViewModel(runner);
        vm.RefreshPreview();

        int changes = 0;
        vm.PropertyChanged += (_, _) => changes++;
        vm.RefreshPreview();

        Assert.Equal(0, changes);
    }
}
