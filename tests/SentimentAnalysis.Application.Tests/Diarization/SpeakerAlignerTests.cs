using System;
using System.Linq;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

public class SpeakerAlignerTests
{
    private static TranscriptSegment T(double a, double b, string text = "x") =>
        new(text, TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b));

    private static SpeakerSegment S(int spk, double a, double b) =>
        new(new SpeakerId(spk), TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b));

    [Fact]
    public void Align_TamOrtusme_DogruKonusmaciyaAtar()
    {
        var r = SpeakerAligner.Align([T(0, 5), T(6, 10)], [S(0, 0, 5), S(1, 6, 10)], minOverlapRatio: 0.5);

        Assert.Equal(new SpeakerId(0), r[0].Speaker);
        Assert.Equal(new SpeakerId(1), r[1].Speaker);
    }

    [Fact]
    public void Align_IkiKonusmaciyaYayilanSegment_EnCokOrtusenKazanir()
    {
        // segment 0-10; konuşmacı 0 ile 3 sn, konuşmacı 1 ile 7 sn örtüşüyor
        var r = SpeakerAligner.Align([T(0, 10)], [S(0, 0, 3), S(1, 3, 10)], minOverlapRatio: 0.5);

        Assert.Equal(new SpeakerId(1), r[0].Speaker);
    }

    [Fact]
    public void Align_UcKonusmaciyaYayilan_EnCokOrtusenKazanir()
    {
        var r = SpeakerAligner.Align([T(0, 12)], [S(0, 0, 3), S(1, 3, 8), S(2, 8, 12)], minOverlapRatio: 0.3);

        Assert.Equal(new SpeakerId(1), r[0].Speaker);   // 5 sn > 4 sn > 3 sn
    }

    [Fact]
    public void Align_HicOrtusmeYok_BilinmeyenDondurur()
    {
        var r = SpeakerAligner.Align([T(0, 5)], [S(0, 100, 110)], minOverlapRatio: 0.5);

        Assert.Equal(SpeakerId.Unknown, r[0].Speaker);
    }

    [Fact]
    public void Align_OrtusmeEsikAltinda_BilinmeyenDondurur()
    {
        // segment 0-10; yalnızca 2 sn örtüşme = %20 < %50 eşik
        var r = SpeakerAligner.Align([T(0, 10)], [S(0, 8, 12)], minOverlapRatio: 0.5);

        Assert.Equal(SpeakerId.Unknown, r[0].Speaker);
    }

    [Fact]
    public void Align_OrtusmeTamEsikte_AtanirEsikDahil()
    {
        // 5 sn örtüşme / 10 sn segment = tam 0.5 → eşik DAHİL sayılır
        var r = SpeakerAligner.Align([T(0, 10)], [S(0, 5, 15)], minOverlapRatio: 0.5);

        Assert.Equal(new SpeakerId(0), r[0].Speaker);
    }

    [Fact]
    public void Align_KonusmaciBolgesiYok_HepsiBilinmeyen()
    {
        var r = SpeakerAligner.Align([T(0, 5), T(5, 10)], [], minOverlapRatio: 0.5);

        Assert.Equal(2, r.Count);
        Assert.All(r, x => Assert.Equal(SpeakerId.Unknown, x.Speaker));
    }

    [Fact]
    public void Align_BosTranskript_BosDondurur()
    {
        Assert.Empty(SpeakerAligner.Align([], [S(0, 0, 10)], minOverlapRatio: 0.5));
    }

    [Fact]
    public void Align_SifirUzunlukluSegment_BilinmeyenDondurur_BolmeHatasiYok()
    {
        var r = SpeakerAligner.Align([T(5, 5)], [S(0, 0, 10)], minOverlapRatio: 0.5);

        Assert.Equal(SpeakerId.Unknown, r[0].Speaker);
    }

    [Fact]
    public void Align_MetinVeSirayiKorur()
    {
        var r = SpeakerAligner.Align(
            [T(0, 5, "merhaba"), T(6, 10, "dünya")],
            [S(0, 0, 5), S(1, 6, 10)], minOverlapRatio: 0.5);

        Assert.Equal("merhaba", r[0].Segment.Text);
        Assert.Equal("dünya", r[1].Segment.Text);
        Assert.Equal(TimeSpan.FromSeconds(0), r[0].Segment.Start);
    }

    [Fact]
    public void Align_SiralanmamisKonusmaciBolgeleri_YineDogruAtar()
    {
        // Diarization çıktısının sıralı geleceğine güvenmeyiz.
        var r = SpeakerAligner.Align([T(6, 10)], [S(1, 6, 10), S(0, 0, 5)], minOverlapRatio: 0.5);

        Assert.Equal(new SpeakerId(1), r[0].Speaker);
    }

    [Fact]
    public void Align_EsitOrtusme_IlkKonusmaciyiSecer_Deterministik()
    {
        // Beraberlikte davranış TANIMLI olmalı: ilk gelen kazanır (rastgelelik yok).
        var r1 = SpeakerAligner.Align([T(0, 10)], [S(0, 0, 5), S(1, 5, 10)], minOverlapRatio: 0.5);
        var r2 = SpeakerAligner.Align([T(0, 10)], [S(0, 0, 5), S(1, 5, 10)], minOverlapRatio: 0.5);

        Assert.Equal(new SpeakerId(0), r1[0].Speaker);
        Assert.Equal(r1[0].Speaker, r2[0].Speaker);
    }

    [Fact]
    public void Align_TumSegmentleriDondurur_HicbiriDusmez()
    {
        var r = SpeakerAligner.Align(
            [T(0, 2), T(2, 4), T(50, 52)],          // sonuncusu hiçbir bölgeye denk gelmiyor
            [S(0, 0, 4)], minOverlapRatio: 0.5);

        Assert.Equal(3, r.Count);                    // atanamayan segment SESSİZCE kaybolmaz
        Assert.Equal(SpeakerId.Unknown, r[2].Speaker);
    }
}
