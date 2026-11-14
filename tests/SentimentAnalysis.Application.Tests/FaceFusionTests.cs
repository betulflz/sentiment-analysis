using System;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class FaceFusionTests
{
    private static readonly FusionConfig Cfg = new();

    private static FusedResult Speech(double v, double a = 0.5, double rAudio = 1, double rText = 1, bool incongruent = false) =>
        new(new ValenceArousal(v, a), Emotion.Uzgun, Emotion.Notr, Confidence.Orta, true, incongruent,
            IncongruencePattern.Yok, 1.0, new ValenceArousal(v, a), new ValenceArousal(v, a), 0.7, 0.7, rAudio, rText);

    private static FaceWindowSummary Face(int i, double shift, double rel = 1, double visible = 1) =>
        new(i, visible, 0, shift, 0, 0, rel);

    [Fact]
    public void NoFaces_ReturnsSpeechUnchanged()
    {
        var s = Speech(-0.4);
        Assert.Same(s, FaceFusion.Apply(s, Array.Empty<FaceWindowSummary>(), Cfg));
    }

    [Fact]
    public void UnreliableFace_ReturnsSpeechUnchanged()
    {
        var s = Speech(-0.4);
        Assert.Same(s, FaceFusion.Apply(s, new[] { Face(0, 0.9, rel: 0) }, Cfg));
    }

    [Fact]
    public void SmilingFace_PullsValenceUp_AndRelabels()
    {
        var s = Speech(-0.4);
        var r = FaceFusion.Apply(s, new[] { Face(0, 0.9) }, Cfg);

        // ses ağırlığı 1 (AudioValenceWeight·rAudio), yüz ağırlığı FaceValenceWeight(1)·1
        Assert.Equal((-0.4 + 0.9) / 2, r.Combined.Valence, 1e-9);
        Assert.Equal(0.5, r.Combined.Arousal, 1e-9);
        var (primary, secondary) = EmotionWheel.MapWithSecondary(r.Combined, Cfg.NeutralRadius);
        Assert.Equal(primary, r.PrimaryEmotion);
        Assert.Equal(secondary, r.SecondaryEmotion);
        Assert.NotEqual(Emotion.Uzgun, r.PrimaryEmotion);
        Assert.Equal(-0.4, r.SpeechValence, 1e-9);
        Assert.Equal(0.9, r.FaceValence!.Value, 1e-9);
        Assert.Equal(1.0, r.FaceWeight, 1e-9);
    }

    [Fact]
    public void HalfReliableFace_HalfWeight()
    {
        var r = FaceFusion.Apply(Speech(-0.4), new[] { Face(0, 0.9, rel: 0.5) }, Cfg);
        Assert.Equal((-0.4 + 0.5 * 0.9) / 1.5, r.Combined.Valence, 1e-9);
    }

    [Fact]
    public void TwoFaces_UsesMostProminentFace_NotAverage()
    {
        // İki kişilik görüşmede dinleyenin ifadesi konuşanın duygusuna karışmasın: en güvenilir/görünür yüz.
        var r = FaceFusion.Apply(Speech(0), new[] { Face(0, -0.4, visible: 0.5), Face(1, 0.8) }, Cfg);
        Assert.Equal(0.8, r.FaceValence!.Value, 1e-9);
        Assert.Equal(0.4, r.Combined.Valence, 1e-9);
    }

    [Fact]
    public void UnreliableSpeech_FaceDominates()
    {
        var r = FaceFusion.Apply(Speech(-0.4, rAudio: 0, rText: 0), new[] { Face(0, 0.7) }, Cfg);
        Assert.Equal(0.7, r.Combined.Valence, 1e-9);
    }

    [Fact]
    public void SpeechOnlyResult_SpeechValenceIsCombined()
    {
        var s = Speech(-0.3);
        Assert.Equal(-0.3, s.SpeechValence, 1e-9);
        Assert.Null(s.FaceValence);
    }

    // İt.5: yüz (kamera karşı tarafı çeker) yalnız karşı tarafın sonucuna katılır; rol yoksa eski davranış.
    private static ChunkResult Chunk(FusedResult fused, FusedResult? counterpart, bool roles) =>
        new ChunkResult(TimeSpan.Zero, TimeSpan.FromSeconds(10), fused, "metin")
        {
            Roles = roles ? new RoleTimeline(Array.Empty<RoleSpan>(), true, false) : null,
            Counterpart = counterpart,
        };

    [Fact]
    public void ApplyToChunk_RolYok_GenelSonucaKatilir()
    {
        var r = FaceFusion.ApplyToChunk(Chunk(Speech(0), null, roles: false), new[] { Face(0, 0.9) }, Cfg);
        Assert.NotNull(r.Fused.FaceValence);
    }

    [Fact]
    public void ApplyToChunk_KarsiTarafKonusmadi_YuzEklenmez()
    {
        var c = Chunk(Speech(0), null, roles: true);
        var r = FaceFusion.ApplyToChunk(c, new[] { Face(0, 0.9) }, Cfg);
        Assert.Null(r.Fused.FaceValence);
        Assert.Null(r.Counterpart);
    }

    [Fact]
    public void ApplyToChunk_KarisikParca_YalnizKarsiTarafa()
    {
        var r = FaceFusion.ApplyToChunk(Chunk(Speech(0), Speech(-0.2), roles: true), new[] { Face(0, 0.9) }, Cfg);
        Assert.Null(r.Fused.FaceValence);
        Assert.NotNull(r.Counterpart!.FaceValence);
    }

    [Fact]
    public void ApplyToChunk_ParcaTamamenKarsiTarafin_IkisineDe()
    {
        var s = Speech(0);
        var r = FaceFusion.ApplyToChunk(Chunk(s, s, roles: true), new[] { Face(0, 0.9) }, Cfg);
        Assert.NotNull(r.Fused.FaceValence);
        Assert.Equal(r.Fused, r.Counterpart);
    }
}
