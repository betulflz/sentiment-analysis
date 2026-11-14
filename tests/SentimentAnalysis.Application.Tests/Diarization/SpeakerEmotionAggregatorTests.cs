using System;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

public class SpeakerEmotionAggregatorTests
{
    private static SpeakerSegment S(int spk, double a, double b) =>
        new(new SpeakerId(spk), TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b));

    private static ChunkResult C(double a, double b, double textV, double audioV, double combinedV) =>
        new(TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b),
            new FusedResult(
                Combined: new ValenceArousal(combinedV, 0.5),
                PrimaryEmotion: Emotion.Notr,
                SecondaryEmotion: Emotion.Notr,
                Confidence: Confidence.Orta,
                Agreement: true,
                Incongruent: false,
                Pattern: IncongruencePattern.Yok,
                Congruence: 1.0,
                TextVa: new ValenceArousal(textV, 0.5),
                ProsodyVa: new ValenceArousal(audioV, 0.5),
                TextConfidence: 0.8,
                ProsodyConfidence: 0.8),
            Transcript: "x");

    [Fact]
    public void Aggregate_IkiKonusmaci_HerBirininOrtalamasiniAyriHesaplar()
    {
        var chunks = new[]
        {
            C(0, 2, textV: 0.8, audioV: 0.6, combinedV: 0.7),   // konuşmacı 0
            C(2, 4, textV: 0.4, audioV: 0.4, combinedV: 0.4),   // konuşmacı 0
            C(4, 6, textV: -0.6, audioV: -0.8, combinedV: -0.7), // konuşmacı 1
        };
        var speakers = new[] { S(0, 0, 4), S(1, 4, 6) };

        var r = SpeakerEmotionAggregator.Aggregate(chunks, speakers, minOverlapRatio: 0.5);

        Assert.Equal(2, r.Count);
        var spk0 = r[0];
        Assert.Equal(new SpeakerId(0), spk0.Speaker);
        Assert.Equal(0.6, spk0.Text.Valence, precision: 10);   // (0.8+0.4)/2
        Assert.Equal(0.5, spk0.Audio.Valence, precision: 10);  // (0.6+0.4)/2
        Assert.Equal(0.55, spk0.Combined.Valence, precision: 10);
        Assert.Equal(2, spk0.ChunkCount);

        var spk1 = r[1];
        Assert.Equal(new SpeakerId(1), spk1.Speaker);
        Assert.Equal(-0.6, spk1.Text.Valence, precision: 10);
        Assert.Equal(1, spk1.ChunkCount);
    }

    [Fact]
    public void Aggregate_AtanamayanChunk_BilinmeyenGrubundaKalir_SessizceAtilmaz()
    {
        var chunks = new[] { C(0, 2, 0.5, 0.5, 0.5) };
        var speakers = Array.Empty<SpeakerSegment>(); // hiç konuşmacı bölgesi yok → Unknown

        var r = SpeakerEmotionAggregator.Aggregate(chunks, speakers, minOverlapRatio: 0.5);

        Assert.Single(r);
        Assert.Equal(SpeakerId.Unknown, r[0].Speaker);
    }

    [Fact]
    public void Aggregate_BelirsizAtamaliChunklariSayar_OrtalamayaYineDeKatar()
    {
        // Final inceleme / bulgu 3: SpeakerAligner artık eşik-altı chunk'ları elemek yerine en iyi
        // adaya atayıp Uncertain işaretliyor. Bu bayrak okunmazsa, eskiden düşen chunk'lar
        // ortalamaya hiçbir iz bırakmadan giriyor — kullanıcı bunu göremiyordu.
        var chunks = new[]
        {
            C(0, 2, textV: 1.0, audioV: 1.0, combinedV: 1.0),    // 0–2: tamamen konuşmacı 0 → kesin
            C(2, 12, textV: 0.0, audioV: 0.0, combinedV: 0.0),   // 2–12: yalnızca 1 sn örtüşme → belirsiz
        };
        var speakers = new[] { S(0, 0, 3) };

        var r = SpeakerEmotionAggregator.Aggregate(chunks, speakers, minOverlapRatio: 0.5);

        var spk0 = Assert.Single(r);
        Assert.Equal(new SpeakerId(0), spk0.Speaker);
        Assert.Equal(2, spk0.ChunkCount);              // ChunkCount semantiği değişmez: tüm chunk'lar
        Assert.Equal(1, spk0.UncertainChunkCount);     // bunlardan biri belirsiz atamaya dayanıyor
        Assert.Equal(0.5, spk0.Combined.Valence, precision: 10);   // ortalama hâlâ İKİ chunk'ı kapsıyor
    }

    [Fact]
    public void Aggregate_TumAtamalarKesinse_BelirsizSayisiSifirdir()
    {
        var chunks = new[] { C(0, 2, 0.5, 0.5, 0.5), C(2, 4, 0.5, 0.5, 0.5) };
        var speakers = new[] { S(0, 0, 4) };

        var r = SpeakerEmotionAggregator.Aggregate(chunks, speakers, minOverlapRatio: 0.5);

        Assert.Equal(0, r[0].UncertainChunkCount);
    }

    [Fact]
    public void Aggregate_BosChunkListesi_BosDoner()
    {
        var r = SpeakerEmotionAggregator.Aggregate(
            Array.Empty<ChunkResult>(), Array.Empty<SpeakerSegment>(), minOverlapRatio: 0.5);

        Assert.Empty(r);
    }
}
