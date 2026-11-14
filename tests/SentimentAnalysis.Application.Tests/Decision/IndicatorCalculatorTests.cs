using System;
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Decision;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Decision;

public class IndicatorCalculatorTests
{
    private static readonly DecisionConfig Cfg = new();
    private static readonly double[] NoHistory = Array.Empty<double>();
    private static readonly FaceWindowSummary[] NoFaces = Array.Empty<FaceWindowSummary>();

    private static ChunkResult Chunk(double v, double a, double congruence = 1.0, double aRel = 1.0,
        double tRel = 0.5, string transcript = "bir iki üç dört beş altı yedi sekiz dokuz on on bir on iki on üç on dört on beş on altı on yedi on sekiz on dokuz yirmi", double seconds = 10)
    {
        var va = new ValenceArousal(v, a);
        var fused = new FusedResult(va, Emotion.Notr, Emotion.Notr, Confidence.Orta, true, false,
            IncongruencePattern.Yok, congruence, va, va, 0.8, 0.8, aRel, tRel);
        return new ChunkResult(TimeSpan.Zero, TimeSpan.FromSeconds(seconds), fused, transcript);
    }

    private static FaceWindowSummary Face(int i, double visible = 1, double lookAway = 0, double shift = 0,
        double neg = 0, double rel = 1) => new(i, visible, lookAway, shift, neg, 0, rel);

    private static double K(Indicators ind, Criterion c) => ind.Values[(int)c];

    [Fact]
    public void FaceFusedChunk_IndicatorsUseSpeechValence_NoDoubleCounting()
    {
        // Yüz göstergelere zaten FaceWindowSummary ile girer; üç kaynaklı Combined kullanılırsa iki kez sayılır.
        var speech = Chunk(-0.6, 0.9);
        var fused = speech with
        {
            Fused = speech.Fused with
            {
                Combined = new ValenceArousal(0.2, 0.9), SpeechVa = speech.Fused.Combined, FaceValence = 1.0, FaceWeight = 1.0,
            },
        };
        var faces = new[] { Face(0, shift: 1.0) };
        var expected = IndicatorCalculator.Compute(speech, faces, new[] { -0.2, -0.4 }, Cfg);
        var actual = IndicatorCalculator.Compute(fused, faces, new[] { -0.2, -0.4 }, Cfg);
        Assert.Equal(expected.Values, actual.Values);
    }

    [Fact]
    public void PhantomFace_DoesNotDragDataQualityDown()
    {
        // Gürültülü ortam: ses/metin kalitesi ~0. Net bir yüzün yanında anlık yanlış algılanan
        // ikinci yüz, kaliteyi "veri yetersiz" eşiğinin altına çekmemeli.
        var chunk = Chunk(0, 0.5, aRel: 0.03, tRel: 0);
        var alone = IndicatorCalculator.Compute(chunk, new[] { Face(0) }, NoHistory, Cfg);
        var withPhantom = IndicatorCalculator.Compute(chunk, new[] { Face(0), Face(1, visible: 0.05, rel: 0.3) }, NoHistory, Cfg);
        Assert.Equal(alone.DataQuality, withPhantom.DataQuality, 1e-9);
        Assert.True(withPhantom.DataQuality >= Cfg.InsufficientQuality);
    }

    [Fact]
    public void NoFaces_UsesAudioOnly_NoNaN()
    {
        var ind = IndicatorCalculator.Compute(Chunk(-0.6, 0.9), NoFaces, NoHistory, Cfg);
        Assert.Equal(0.48, K(ind, Criterion.Gerilim), 1e-9);
        Assert.Equal(0.0, K(ind, Criterion.IlgiKaybi), 1e-9);
        Assert.Equal(0.6, K(ind, Criterion.OlumsuzDuygu), 1e-9);
        Assert.All(ind.SourceFace, s => Assert.Null(s));
        Assert.Equal(0.6, ind.DataQuality, 1e-9);
        Assert.All(ind.Values, v => Assert.True(double.IsFinite(v)));
    }

    [Fact]
    public void TenseFace_RaisesTension_AndNamesFace()
    {
        var ind = IndicatorCalculator.Compute(Chunk(-0.6, 0.9), new[] { Face(0), Face(1, neg: 1) }, NoHistory, Cfg);
        Assert.Equal(0.7 * 0.48 + 0.3, K(ind, Criterion.Gerilim), 1e-9);
        Assert.Equal(1, ind.SourceFace[(int)Criterion.Gerilim]);
    }

    [Fact]
    public void AudioOnlyTension_NotPinnedOnNeutralFaces()
    {
        // Sesten gelen gerilim, yüzünde hiçbir şey görünmeyen bir kişiye yazılmamalı (spec §4).
        var ind = IndicatorCalculator.Compute(Chunk(-0.6, 0.9), new[] { Face(0), Face(1) }, NoHistory, Cfg);
        Assert.Null(ind.SourceFace[(int)Criterion.Gerilim]);
        Assert.Null(ind.SourceFace[(int)Criterion.OlumsuzDuygu]);
        Assert.Equal(0.7 * 0.48, K(ind, Criterion.Gerilim), 1e-9);
    }

    [Fact]
    public void AudioOnlyTension_NotPinnedOnSingleNeutralFace()
    {
        var ind = IndicatorCalculator.Compute(Chunk(-0.6, 0.9), new[] { Face(0) }, NoHistory, Cfg);
        Assert.Null(ind.SourceFace[(int)Criterion.Gerilim]);
    }

    [Fact]
    public void LookingAway_OnlyWhenVisible()
    {
        var hidden = IndicatorCalculator.Compute(Chunk(0, 0.5), new[] { Face(0, visible: 0, lookAway: 1) }, NoHistory, Cfg);
        var seen = IndicatorCalculator.Compute(Chunk(0, 0.5), new[] { Face(0, visible: 1, lookAway: 1) }, NoHistory, Cfg);
        Assert.Equal(0.0, K(hidden, Criterion.IlgiKaybi), 1e-9);
        Assert.Equal(1.0, K(seen, Criterion.IlgiKaybi), 1e-9);
        Assert.Equal(0, seen.SourceFace[(int)Criterion.IlgiKaybi]);
    }

    [Fact]
    public void NegativeFace_PullsFusedValenceDown()
    {
        // aq = 1, yüz Rel 1 → V3 = (1·0 + 0.5·(−0.6)) / 1.5 = −0.2
        var ind = IndicatorCalculator.Compute(Chunk(0, 0.5), new[] { Face(0, shift: -0.6) }, NoHistory, Cfg);
        Assert.Equal(0.2, K(ind, Criterion.OlumsuzDuygu), 1e-9);
    }

    [Fact]
    public void FaceVoiceGap_RaisesIncongruence()
    {
        var ind = IndicatorCalculator.Compute(Chunk(0.5, 0.5, congruence: 0.9), new[] { Face(0, shift: -0.3) }, NoHistory, Cfg);
        Assert.Equal(0.8, K(ind, Criterion.Uyumsuzluk), 1e-9);
    }

    [Fact]
    public void Trend_FewerThanThreePoints_IsZero()
    {
        var ind = IndicatorCalculator.Compute(Chunk(-1, 0.5), NoFaces, new[] { 1.0 }, Cfg);
        Assert.Equal(0.0, K(ind, Criterion.Kotulesme), 1e-9);
    }

    [Fact]
    public void Trend_FallingValence()
    {
        var ind = IndicatorCalculator.Compute(Chunk(0.0, 0.5), NoFaces, new[] { 0.4, 0.2 }, Cfg);
        Assert.Equal(1.0, K(ind, Criterion.Kotulesme), 1e-9);
        Assert.Null(ind.SourceFace[(int)Criterion.Kotulesme]);
    }

    [Fact]
    public void Trend_UsesOnlyLastTrendWindows()
    {
        // eski düşüş pencere dışında kalır; son 6 nokta düz → 0
        var history = new[] { 1.0, 0.5, 0.0, 0.0, 0.0, 0.0, 0.0 };
        var ind = IndicatorCalculator.Compute(Chunk(0.0, 0.5), NoFaces, history, Cfg);
        Assert.Equal(0.0, K(ind, Criterion.Kotulesme), 1e-9);
    }

    [Fact]
    public void LowSpeech_RaisesLowEnergy()
    {
        var ind = IndicatorCalculator.Compute(Chunk(0, 0.5, transcript: "", seconds: 10), NoFaces, NoHistory, Cfg);
        Assert.Equal(0.5, K(ind, Criterion.DusukEnerji), 1e-9);
    }

    [Fact]
    public void DataQuality_BlendsAudioAndFace()
    {
        var ind = IndicatorCalculator.Compute(Chunk(0, 0.5, aRel: 0.5, tRel: 0.2),
            new[] { Face(0, visible: 1, rel: 1), Face(1, visible: 0.5, rel: 0.5) }, NoHistory, Cfg);
        Assert.Equal(0.6 * 0.5 + 0.4 * 1.0, ind.DataQuality, 1e-9); // en iyi görülen yüz
    }

    public static IEnumerable<object[]> Extremes()
    {
        foreach (var v in new[] { -1.0, 1.0 })
            foreach (var a in new[] { 0.0, 1.0 })
                yield return new object[] { v, a };
    }

    [Theory]
    [MemberData(nameof(Extremes))]
    public void AllValuesInUnitRange(double v, double a)
    {
        var faces = new[] { Face(0, lookAway: 1, shift: -1, neg: 1), Face(1, shift: 1) };
        var ind = IndicatorCalculator.Compute(Chunk(v, a, congruence: 0, transcript: "", seconds: 0), faces, new[] { 1.0, -1.0 }, Cfg);
        Assert.Equal(6, ind.Values.Count);
        Assert.All(ind.Values, x => Assert.InRange(x, 0.0, 1.0));
        Assert.InRange(ind.DataQuality, 0.0, 1.0);
    }

    // ---- Olumluluk: sorun yokken "olumlu öneri" için; ses+söz valence'ı ve gülümseme ----

    [Fact]
    public void Olumluluk_YuzYoksa_KonusmaValenceidir()
    {
        var ind = IndicatorCalculator.Compute(Chunk(0.8, 0.6), NoFaces, NoHistory, Cfg);

        Assert.Equal(0.8, ind.Positivity, 3);
    }

    [Fact]
    public void Olumluluk_NegatifKonusma_Sifir()
    {
        var ind = IndicatorCalculator.Compute(Chunk(-0.6, 0.6), NoFaces, NoHistory, Cfg);

        Assert.Equal(0.0, ind.Positivity, 3);
    }

    [Fact]
    public void Olumluluk_GulumsemeKatkiVerir_VeEnGulenYuzAlinir()
    {
        var faces = new[]
        {
            new FaceWindowSummary(0, 1, 0, 0, 0, SmileRatio: 0.2, Reliability: 1),
            new FaceWindowSummary(1, 1, 0, 0, 0, SmileRatio: 0.8, Reliability: 1),
        };

        var ind = IndicatorCalculator.Compute(Chunk(0.5, 0.6), faces, NoHistory, Cfg);

        Assert.Equal(0.6 * 0.5 + 0.4 * 0.8, ind.Positivity, 3);
    }

    [Fact]
    public void Olumluluk_GuvenilmezYuzdeGulumseme_Sayilmaz()
    {
        var faces = new[] { new FaceWindowSummary(0, 1, 0, 0, 0, SmileRatio: 1.0, Reliability: 0) };

        var ind = IndicatorCalculator.Compute(Chunk(0.5, 0.6), faces, NoHistory, Cfg);

        Assert.Equal(0.6 * 0.5, ind.Positivity, 3);
    }

    [Fact]
    public void BagiranOfkeliKonusma_SesPozitifOkusaBile_MetinOlumsuzsa_GerilimVeKizginDuygu()
    {
        // Gerçek kayıt (oturum 6339…, parça 6): "Sinirliyim" — ses valence +0,67 / enerji 0,95, metin −0,66.
        // Birleşik değer pozitife kayıyordu; gerilim ~%1, olumsuz duygu %0 çıkıp "Devam et" gösteriliyordu.
        var combined = new ValenceArousal(0.40, 0.95);
        var fused = new FusedResult(combined, Emotion.Cosakulu, Emotion.Notr, Confidence.Orta, false, true,
            IncongruencePattern.Yok, 0.34, new ValenceArousal(-0.66, 0.5), new ValenceArousal(0.67, 0.95), 0.9, 0.5, 0.5, 0.9);
        var chunk = new ChunkResult(TimeSpan.Zero, TimeSpan.FromSeconds(10), fused, "devam etmeyeceğim sinirliyim şu an");

        var ind = IndicatorCalculator.Compute(chunk, NoFaces, NoHistory, Cfg);

        Assert.True(K(ind, Criterion.Gerilim) > 0.5, $"gerilim {K(ind, Criterion.Gerilim)}");
        Assert.True(K(ind, Criterion.OlumsuzDuygu) > 0.5);
        Assert.Equal(Emotion.Kizgin, ind.Cues!.Primary);
    }

    // ---- 12: ses tanıtıldıysa göstergeler karşı tarafın sesinden ----

    private static RoleTimeline Roles(params (double A, double B, SpeakerRole R)[] spans) =>
        new(spans.Select(x => new RoleSpan(TimeSpan.FromSeconds(x.A), TimeSpan.FromSeconds(x.B), x.R, 0.5)).ToArray(), true, false);

    [Fact]
    public void Roller_YonetenGerginKarsiTarafSakin_GerilimKarsiTaraftanOkunur()
    {
        var hostAngry = Chunk(-0.8, 0.95);                       // karışık ses: yönetenin öfkesi baskın
        var calm = Chunk(0.3, 0.4).Fused;
        var chunk = hostAngry with
        {
            Roles = Roles((0, 5, SpeakerRole.Yoneten), (5, 10, SpeakerRole.KarsiTaraf)),
            Counterpart = calm,
        };

        var withRoles = IndicatorCalculator.Compute(chunk, NoFaces, NoHistory, Cfg);
        var mixed = IndicatorCalculator.Compute(hostAngry, NoFaces, NoHistory, Cfg);

        Assert.True(K(mixed, Criterion.Gerilim) > 0.5);
        Assert.Equal(0, K(withRoles, Criterion.Gerilim), 3);
        Assert.Equal(0, K(withRoles, Criterion.OlumsuzDuygu), 3);
        Assert.Equal(0.3, IndicatorCalculator.TrendValence(chunk)!.Value, 3);
    }

    [Fact]
    public void Roller_KarsiTarafSustu_SesGostergeleriNotr_YuzSurer_KaliteKorunur()
    {
        var chunk = Chunk(-0.8, 0.95) with { Roles = Roles((0, 10, SpeakerRole.Yoneten)), Counterpart = null };
        var faces = new[] { Face(0, lookAway: 0.8) };

        var ind = IndicatorCalculator.Compute(chunk, faces, new[] { 0.5, 0.2 }, Cfg);

        Assert.Equal(0, K(ind, Criterion.Gerilim), 3);
        Assert.Equal(0, K(ind, Criterion.OlumsuzDuygu), 3);
        Assert.Equal(0, K(ind, Criterion.DusukEnerji), 3);
        Assert.True(K(ind, Criterion.IlgiKaybi) > 0.5);          // yüz göstergesi sürer
        Assert.True(ind.DataQuality > 0.5);                       // kayıt iyi, yalnız karşı taraf susuyor
        Assert.Null(IndicatorCalculator.TrendValence(chunk));     // yönetenin sesi gidişata yazılmaz
    }

    [Fact]
    public void KonusmaPayi_BaglamIpuclarinaGirer()
    {
        var talk = new TalkSnapshot(0.8, 48, 12, 3.4);

        var ind = IndicatorCalculator.Compute(Chunk(0, 0.5), NoFaces, NoHistory, Cfg, talk);

        Assert.Equal(0.8, ind.Cues!.HostShare);
        Assert.Equal(3.4, ind.Cues.HostWordsPerSecond);
    }
}
