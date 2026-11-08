using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Vision;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Vision;

public class VisionAnalyzerTests
{
    private static readonly VisionConfig Cfg = new();

    private static VideoFrame Frame(int i) => new(new byte[3], 1, 1, TimeSpan.FromSeconds(i / 5.0));

    private sealed class ListSource(int count) : IVideoCaptureSource
    {
        public async IAsyncEnumerable<VideoFrame> CaptureAsync([EnumeratorCancellation] CancellationToken ct)
        {
            for (int i = 0; i < count; i++)
            {
                await Task.Yield();
                yield return Frame(i);
            }
        }
    }

    private sealed class EndlessSource : IVideoCaptureSource
    {
        public async IAsyncEnumerable<VideoFrame> CaptureAsync([EnumeratorCancellation] CancellationToken ct)
        {
            for (int i = 0; ; i++)
            {
                await Task.Delay(10, ct);
                yield return Frame(i);
            }
        }
    }

    private sealed class ThrowingSource : IVideoCaptureSource
    {
        public async IAsyncEnumerable<VideoFrame> CaptureAsync([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return Frame(0);
            throw new InvalidOperationException("kamera izni reddedildi");
        }
    }

    private sealed class FakeDetector(Func<int, IReadOnlyList<DetectedFace>>? faces = null) : IFaceDetector
    {
        public int Calls;
        public IReadOnlyList<DetectedFace> Detect(VideoFrame frame)
        {
            Calls++;
            return faces?.Invoke(Calls) ?? new[] { new DetectedFace(new FaceBox(10, 10, 90, 90), 0.9f, new float[10]) };
        }
    }

    private sealed class FakeExpression : IFaceExpressionModel
    {
        public float[] Classify(VideoFrame frame, FaceBox box) => new float[] { 1, 0, 0, 0, 0, 0, 0, 0 };
    }

    [Fact]
    public async Task ProcessesEveryNthFrame()
    {
        var detector = new FakeDetector();
        var analyzer = new VisionAnalyzer(detector, new FakeExpression(), new VisionTimeline(Cfg), Cfg);
        await analyzer.RunAsync(new ListSource(10), CancellationToken.None);
        Assert.Equal(5, detector.Calls);
    }

    [Fact]
    public async Task WritesObservationsToTimeline()
    {
        var timeline = new VisionTimeline(Cfg);
        var analyzer = new VisionAnalyzer(new FakeDetector(), new FakeExpression(), timeline, Cfg);
        await analyzer.RunAsync(new ListSource(10), CancellationToken.None);
        var s = Assert.Single(timeline.Summarize(TimeSpan.Zero, TimeSpan.FromSeconds(10)));
        // 5 analiz karesi; yeni yüz ikinci görülüşünde doğrulanır (FaceTracker), ilk kare sayılmaz → 4/5.
        Assert.Equal(0.8, s.VisibleRatio, 1e-9);
    }

    [Fact]
    public async Task LatestFrameAndFaces_PopulatedWhileCapturing()
    {
        // 11d önizlemesi koşu SIRASINDA LatestFrame/LatestFaces'i okur; bu test o sözleşmeyi
        // RunAsync tamamlanmadan ayrı bir eşzamanlı okuyucuyla doğrular.
        var analyzer = new VisionAnalyzer(new FakeDetector(), new FakeExpression(), new VisionTimeline(Cfg), Cfg);
        using var cts = new CancellationTokenSource();
        var run = analyzer.RunAsync(new EndlessSource(), cts.Token);
        VideoFrame? seenFrame = null;
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (seenFrame is null && DateTime.UtcNow < deadline)
        {
            seenFrame = analyzer.LatestFrame;
            await Task.Delay(5);
        }
        cts.Cancel();
        await run;
        Assert.NotNull(seenFrame);
    }

    [Fact]
    public async Task RunAsync_ClearsPreviousSessionTimeline()
    {
        var timeline = new VisionTimeline(Cfg);
        var analyzer = new VisionAnalyzer(new FakeDetector(), new FakeExpression(), timeline, Cfg);
        await analyzer.RunAsync(new ListSource(5), CancellationToken.None);
        Assert.NotEmpty(timeline.Summarize(TimeSpan.Zero, TimeSpan.FromSeconds(5)));

        // Yeni oturum, 0. saniyede hiç kare yok → eski oturumun gözlemleri sızmamalı.
        await analyzer.RunAsync(new ListSource(0), CancellationToken.None);
        Assert.Empty(timeline.Summarize(TimeSpan.Zero, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task RunAsync_ClearsLatestFrameAndFacesWhenCaptureEnds()
    {
        var analyzer = new VisionAnalyzer(new FakeDetector(), new FakeExpression(), new VisionTimeline(Cfg), Cfg);
        await analyzer.RunAsync(new ListSource(5), CancellationToken.None);
        Assert.Null(analyzer.LatestFrame);
        Assert.Empty(analyzer.LatestFaces);
    }

    [Fact]
    public async Task DetectorThrows_SkipsFrame_CountsError()
    {
        var detector = new FakeDetector(call => call == 2 ? throw new InvalidOperationException("onnx") : null!);
        var analyzer = new VisionAnalyzer(detector, new FakeExpression(), new VisionTimeline(Cfg), Cfg);
        await analyzer.RunAsync(new ListSource(10), CancellationToken.None);
        Assert.Equal(1, analyzer.ErrorCount);
        Assert.Equal(5, detector.Calls);
    }

    [Fact]
    public async Task Cancellation_StopsPromptly()
    {
        var analyzer = new VisionAnalyzer(new FakeDetector(), new FakeExpression(), new VisionTimeline(Cfg), Cfg);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var sw = Stopwatch.StartNew();
        await analyzer.RunAsync(new EndlessSource(), cts.Token);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"iptal {sw.Elapsed} sürdü");
    }

    [Fact]
    public async Task SourceThrows_Propagates()
    {
        var analyzer = new VisionAnalyzer(new FakeDetector(), new FakeExpression(), new VisionTimeline(Cfg), Cfg);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => analyzer.RunAsync(new ThrowingSource(), CancellationToken.None));
        Assert.Contains("izni", ex.Message);
    }
}
