using System;
using System.Linq;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

public class SpeakerAlignerTests
{
    [Fact]
    public void Align_FragmentedTurns_UsesTotalTimePerSpeaker()
    {
        var result = SpeakerAligner.Align([T(0, 10)],
            [S(0, 0, 2), S(1, 2, 5), S(0, 5, 7), S(1, 7, 8), S(0, 8, 10)], 0.5);
        Assert.Equal(new SpeakerId(0), result[0].Speaker);
        Assert.False(result[0].Uncertain);
    }

    [Fact]
    public void Align_DuplicateRegions_DoNotCountTwice()
    {
        var result = SpeakerAligner.Align([T(0, 10)],
            [S(0, 0, 4), S(0, 0, 4), S(1, 4, 7), S(1, 7, 10)], 0.5);
        Assert.Equal(new SpeakerId(1), result[0].Speaker);
        Assert.False(result[0].Uncertain);
    }

    [Fact]
    public void Align_OverlappingRegionsOfSameSpeaker_UseUnion()
    {
        var result = SpeakerAligner.Align([T(0, 10)], [S(0, 0, 3), S(0, 2, 6)], 0.5);
        Assert.Equal(new SpeakerId(0), result[0].Speaker);
        Assert.False(result[0].Uncertain);
    }

    [Fact]
    public void Align_EqualSpeakers_IsUncertainEvenAtThreshold()
    {
        var result = SpeakerAligner.Align([T(0, 10)], [S(0, 0, 5), S(1, 5, 10)], 0.5);
        Assert.Equal(new SpeakerId(0), result[0].Speaker);
        Assert.True(result[0].Uncertain);
    }

    [Fact]
    public void Align_UnknownRegions_DoNotOutvoteKnownSpeaker()
    {
        var result = SpeakerAligner.Align([T(0, 10)], [S(-1, 0, 10), S(0, 0, 6)], 0.5);
        Assert.Equal(new SpeakerId(0), result[0].Speaker);
    }

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
    public void Align_OrtusmeEsikAltinda_EnIyiAdayaAtanirVeBelirsizIsaretlenir()
    {
        // segment 0-10; yalnızca 2 sn örtüşme = %20 < %50 eşik.
        // "Bilinmeyen" demek bu bilgiyi çöpe atmaktı; doğrusu adaya ata + belirsizliği işaretle.
        var r = SpeakerAligner.Align([T(0, 10)], [S(0, 8, 12)], minOverlapRatio: 0.5);

        Assert.Equal(new SpeakerId(0), r[0].Speaker);
        Assert.True(r[0].Uncertain);
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

    [Fact]
    public void EsikAlti_AtanirAmaBelirsizIsaretlenir()
    {
        // Örtüşme var ama %50'nin altında: "Bilinmeyen" demek bilgiyi çöpe atmaktı;
        // uydurma atama da yanlış. Doğrusu: en iyi adayı ata, belirsizliği İŞARETLE.
        var transcript = new[] { new TranscriptSegment("merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(10)) };
        var speakers = new[] { new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(3)) };

        var result = SpeakerAligner.Align(transcript, speakers, minOverlapRatio: 0.5);

        Assert.Equal(new SpeakerId(0), result[0].Speaker);
        Assert.True(result[0].Uncertain);
    }

    [Fact]
    public void EsikUstu_KesinAtanir()
    {
        var transcript = new[] { new TranscriptSegment("merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(10)) };
        var speakers = new[] { new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(9)) };

        var result = SpeakerAligner.Align(transcript, speakers, minOverlapRatio: 0.5);

        Assert.Equal(new SpeakerId(0), result[0].Speaker);
        Assert.False(result[0].Uncertain);
    }

    [Fact]
    public void HicOrtusmeYok_BilinmeyenKalir()
    {
        // Aday bile yok: burada atamak uydurmak olurdu.
        var transcript = new[] { new TranscriptSegment("merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(2)) };
        var speakers = new[] { new SpeakerSegment(new SpeakerId(0), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(9)) };

        var result = SpeakerAligner.Align(transcript, speakers, minOverlapRatio: 0.5);

        Assert.Equal(SpeakerId.Unknown, result[0].Speaker);
        Assert.True(result[0].Uncertain);
    }

    [Fact]
    public void HicKonusmaciYok_BilinmeyenDoner()
    {
        var transcript = new[] { new TranscriptSegment("merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(2)) };

        var result = SpeakerAligner.Align(transcript, Array.Empty<SpeakerSegment>(), minOverlapRatio: 0.5);

        Assert.Single(result);
        Assert.Equal(SpeakerId.Unknown, result[0].Speaker);
    }

    [Fact]
    public void AlignChunks_EsikAlti_AtanirAmaBelirsizIsaretlenir()
    {
        var chunk = new ChunkResult(TimeSpan.Zero, TimeSpan.FromSeconds(10),
            new FusedResult(ValenceArousal.Neutral, Emotion.Notr, Emotion.Notr, Confidence.Orta,
                true, false, IncongruencePattern.Yok, 1,
                TextVa: ValenceArousal.Neutral, ProsodyVa: ValenceArousal.Neutral,
                TextConfidence: 0, ProsodyConfidence: 0),
            "metin");
        var speakers = new[] { new SpeakerSegment(new SpeakerId(1), TimeSpan.Zero, TimeSpan.FromSeconds(2)) };

        var result = SpeakerAligner.AlignChunks(new[] { chunk }, speakers, minOverlapRatio: 0.5);

        Assert.Equal(new SpeakerId(1), result[0].Speaker);
        Assert.True(result[0].Uncertain);
    }

    // --- İt.2: iki kişiye yayılan uzun parça bölünür (kelime oranıyla, yaklaşık) ---
    private static TranscriptSegment W(double s, double e, string text) => new(text, TimeSpan.FromSeconds(s), TimeSpan.FromSeconds(e));

    [Fact]
    public void Split_IkiKisiyeYayilanParca_SurelerineGoreBolunur()
    {
        var r = SpeakerAligner.AlignSplitting(
            [W(0, 10, "bir iki üç dört beş altı yedi sekiz dokuz on")],
            [S(0, 0, 6), S(1, 6, 10)], 0.5);
        Assert.Equal(2, r.Count);
        Assert.Equal(new SpeakerId(0), r[0].Speaker);
        Assert.Equal("bir iki üç dört beş altı", r[0].Segment.Text);
        Assert.Equal(TimeSpan.Zero, r[0].Segment.Start);
        Assert.Equal(TimeSpan.FromSeconds(6), r[0].Segment.End);
        Assert.Equal(new SpeakerId(1), r[1].Speaker);
        Assert.Equal("yedi sekiz dokuz on", r[1].Segment.Text);
        Assert.Equal(TimeSpan.FromSeconds(6), r[1].Segment.Start);
        Assert.Equal(TimeSpan.FromSeconds(10), r[1].Segment.End);
        Assert.All(r, a => Assert.False(a.Uncertain));
    }

    [Fact]
    public void Split_KisaAraSoz_BolmeYapilmaz()
    {
        // 0,6 sn'lik araya giriş (ör. "hı hı") parçayı bölmez: tek kişiye, eski kuralla.
        var r = SpeakerAligner.AlignSplitting([W(0, 10, "bir iki üç dört beş")], [S(0, 0, 5), S(1, 5, 5.6), S(0, 5.6, 10)], 0.5);
        var a = Assert.Single(r);
        Assert.Equal(new SpeakerId(0), a.Speaker);
        Assert.Equal("bir iki üç dört beş", a.Segment.Text);
    }

    [Fact]
    public void Split_TekKelime_Bolunmez()
    {
        var r = SpeakerAligner.AlignSplitting([W(0, 4, "evet")], [S(0, 0, 2), S(1, 2, 4)], 0.5);
        Assert.Single(r);
    }

    [Fact]
    public void Split_UcSira_ABA()
    {
        var r = SpeakerAligner.AlignSplitting([W(0, 9, "a1 a2 a3 b1 b2 b3 c1 c2 c3")], [S(0, 0, 3), S(1, 3, 6), S(0, 6, 9)], 0.5);
        Assert.Equal(new[] { "a1 a2 a3", "b1 b2 b3", "c1 c2 c3" }, r.Select(x => x.Segment.Text));
        Assert.Equal(new[] { 0, 1, 0 }, r.Select(x => x.Speaker.Index));
    }

    [Fact]
    public void Split_HerParcaEnAzBirKelime_MetinKaybiYok()
    {
        // Kelime sayısı az, süre oranı çok dengesiz: hiçbir taraf boş kalmaz, tüm kelimeler korunur.
        var r = SpeakerAligner.AlignSplitting([W(0, 20, "uzun kısa")], [S(0, 0, 18), S(1, 18, 20)], 0.5);
        Assert.Equal(new[] { "uzun", "kısa" }, r.Select(x => x.Segment.Text));
    }

    [Fact]
    public void Split_BilinmeyenBolgeYoksayilir_TekKisiyeKalir()
    {
        var r = SpeakerAligner.AlignSplitting([W(0, 10, "bir iki üç dört")], [S(0, 0, 6), S(-1, 6, 10)], 0.5);
        Assert.Single(r);
    }
}
