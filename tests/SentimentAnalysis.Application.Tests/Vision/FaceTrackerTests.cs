using System.Linq;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Vision;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Vision;

public class FaceTrackerTests
{
    private static DetectedFace F(float x, float y = 100, float size = 90) =>
        new(new FaceBox(x, y, size, size), 0.95f, new float[10]);

    private static int IndexOf(System.Collections.Generic.IReadOnlyList<(int Index, DetectedFace Face)> r, DetectedFace f) =>
        r.Single(x => ReferenceEquals(x.Face, f)).Index;

    [Fact]
    public void FirstFrame_LeftToRight()
    {
        var t = new FaceTracker();
        var right = F(430); var left = F(130);
        var r = t.Assign(new[] { right, left });
        Assert.Equal(0, IndexOf(r, left));
        Assert.Equal(1, IndexOf(r, right));
    }

    [Fact]
    public void SmallMovement_KeepsIndex()
    {
        var t = new FaceTracker();
        t.Assign(new[] { F(130), F(430) });
        var movedLeft = F(140); var movedRight = F(425);
        var r = t.Assign(new[] { movedRight, movedLeft });
        Assert.Equal(0, IndexOf(r, movedLeft));
        Assert.Equal(1, IndexOf(r, movedRight));
    }

    [Fact]
    public void Reappears_AfterGap_KeepsIndex()
    {
        var t = new FaceTracker();
        t.Assign(new[] { F(130), F(430) });
        t.Assign(new[] { F(130) });                 // sağdaki kayboldu
        t.Assign(System.Array.Empty<DetectedFace>()); // kimse yok
        var back = F(435);
        var r = t.Assign(new[] { back });
        Assert.Equal(1, IndexOf(r, back));
    }

    [Fact]
    public void NewFaceFarAway_GetsNextIndex()
    {
        var t = new FaceTracker();
        t.Assign(new[] { F(130), F(430) });
        var stranger = F(280, y: 250, size: 40);
        var r = t.Assign(new[] { F(130), F(430), stranger });
        Assert.Equal(2, IndexOf(r, stranger));
    }

    [Fact]
    public void TwoFacesNeverShareIndex()
    {
        var t = new FaceTracker();
        t.Assign(new[] { F(130) });
        var r = t.Assign(new[] { F(132), F(150) }); // ikisi de aynı ize yakın
        Assert.Equal(2, r.Select(x => x.Index).Distinct().Count());
    }

    [Fact]
    public void AppConfig_Default_HasVisionDefaults()
    {
        Assert.Equal(2, AppConfig.Default.Vision.AnalyzeEvery);
        Assert.Equal(5, AppConfig.Default.Vision.CaptureFps);
        Assert.Equal(0.2, AppConfig.Default.Vision.LookAwayYaw);
    }
}
