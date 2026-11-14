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

    /// <summary>Aynı yüzleri iki karede gösterir — yeni yüz ancak tekrar görülünce numara alır.</summary>
    private static System.Collections.Generic.IReadOnlyList<(int Index, DetectedFace Face)> Confirm(FaceTracker t, params DetectedFace[] faces)
    {
        t.Assign(faces);
        return t.Assign(faces);
    }

    private static int IndexOf(System.Collections.Generic.IReadOnlyList<(int Index, DetectedFace Face)> r, DetectedFace f) =>
        r.Single(x => ReferenceEquals(x.Face, f)).Index;

    [Fact]
    public void FirstFrame_LeftToRight()
    {
        var t = new FaceTracker();
        var right = F(430); var left = F(130);
        var r = Confirm(t, right, left);
        Assert.Equal(0, IndexOf(r, left));
        Assert.Equal(1, IndexOf(r, right));
    }

    [Fact]
    public void SmallMovement_KeepsIndex()
    {
        var t = new FaceTracker();
        Confirm(t, F(130), F(430));
        var movedLeft = F(140); var movedRight = F(425);
        var r = t.Assign(new[] { movedRight, movedLeft });
        Assert.Equal(0, IndexOf(r, movedLeft));
        Assert.Equal(1, IndexOf(r, movedRight));
    }

    [Fact]
    public void Reappears_AfterGap_KeepsIndex()
    {
        var t = new FaceTracker();
        Confirm(t, F(130), F(430));
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
        Confirm(t, F(130), F(430));
        var stranger = F(280, y: 250, size: 40);
        var r = Confirm(t, F(130), F(430), stranger);
        Assert.Equal(2, IndexOf(r, stranger));
    }

    [Fact]
    public void TwoFacesNeverShareIndex()
    {
        var t = new FaceTracker();
        Confirm(t, F(130));
        var r = Confirm(t, F(132), F(150)); // ikisi de aynı ize yakın
        Assert.Equal(2, r.Select(x => x.Index).Distinct().Count());
    }

    // --- Canlı kamerada 2 kişiye 15 yüz numarası (2026-10-27) ---

    [Fact]
    public void IlkKare_YeniYuzHenuzRaporlanmaz()
    {
        var t = new FaceTracker();
        Assert.Empty(t.Assign(new[] { F(130), F(430) }));
    }

    [Fact]
    public void TekKarelikSahteTespit_NumaraAlmaz_SonrakiGercekYuzSiradakiNumarayiAlir()
    {
        var t = new FaceTracker();
        Confirm(t, F(130), F(430));
        var ghost = F(600, y: 20, size: 25);
        Assert.DoesNotContain(t.Assign(new[] { F(130), F(430), ghost }), x => ReferenceEquals(x.Face, ghost));
        t.Assign(new[] { F(130), F(430) });
        t.Assign(new[] { F(130), F(430) });

        var third = F(280, y: 250, size: 60);
        t.Assign(new[] { F(130), F(430), third });
        var r = t.Assign(new[] { F(130), F(430), third });
        Assert.Equal(2, IndexOf(r, third));
    }

    [Fact]
    public void AdayAradaKaybolursa_YenidenIkiKareGerekir()
    {
        var t = new FaceTracker();
        var face = F(130);
        t.Assign(new[] { face });
        t.Assign(System.Array.Empty<DetectedFace>());
        t.Assign(System.Array.Empty<DetectedFace>());
        Assert.Empty(t.Assign(new[] { face }));
        Assert.Single(t.Assign(new[] { face }));
    }

    [Fact]
    public void GercekYuzHizliOynarsa_AyniNumarayiKorur()
    {
        // Yarım kutu genişliğinden (45 px) fazla, 1,5 genişlikten (135 px) az sıçrama.
        var t = new FaceTracker();
        Confirm(t, F(130), F(430));
        var jumped = F(530);
        var r = t.Assign(new[] { F(130), jumped });
        Assert.Equal(1, IndexOf(r, jumped));
    }

    [Fact]
    public void KucukSahteTespit_KayipYuzunNumarasiniCalmaz()
    {
        var t = new FaceTracker();
        Confirm(t, F(130), F(430));
        var tiny = F(462.5f, y: 132.5f, size: 25); // merkezi kayıp yüzün merkeziyle aynı
        Assert.DoesNotContain(t.Assign(new[] { F(130), tiny }), x => x.Index == 1);
        var back = F(432);
        Assert.Equal(1, IndexOf(t.Assign(new[] { F(130), back }), back));
    }

    [Fact]
    public void AppConfig_Default_HasVisionDefaults()
    {
        Assert.Equal(2, AppConfig.Default.Vision.AnalyzeEvery);
        Assert.Equal(5, AppConfig.Default.Vision.CaptureFps);
        Assert.Equal(0.2, AppConfig.Default.Vision.LookAwayYaw);
    }
}
