using System;
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

public class PersonEmotionComposerTests
{
    private static readonly FusionConfig Cfg = new();
    private static readonly SpeakerId A = new(0), B = new(1);

    private static SpeakerSegment Seg(SpeakerId spk, double a, double b) =>
        new(spk, TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b));

    /// <summary>Sesten+sözden gelen valence <paramref name="speechV"/>; yüz daha önce (yanlış kişiden) karıştıysa
    /// <paramref name="combinedV"/> ona göre farklıdır ve SpeechVa özgün değeri taşır.</summary>
    private static ChunkResult Chunk(double a, double b, double speechV, double? combinedV = null) =>
        new(TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b),
            new FusedResult(new ValenceArousal(combinedV ?? speechV, 0.5), Emotion.Notr, Emotion.Notr, Confidence.Orta,
                true, false, IncongruencePattern.Yok, 1.0,
                TextVa: new ValenceArousal(speechV, 0.5), ProsodyVa: new ValenceArousal(speechV, 0.5),
                TextConfidence: 0.8, ProsodyConfidence: 0.8,
                SpeechVa: combinedV is null ? null : new ValenceArousal(speechV, 0.5)),
            "x");

    private static FaceWindowSummary Face(int index, double valenceShift) =>
        new(index, VisibleRatio: 1, LookAwayRatio: 0, ValenceShift: valenceShift, NegativeExpression: 0, SmileRatio: 0, Reliability: 1);

    private static FaceWindow Window(double a, double b, params FaceWindowSummary[] faces) =>
        new(TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b), faces);

    private static readonly Dictionary<SpeakerId, int> Both = new() { [A] = 0, [B] = 1 };

    [Fact]
    public void HerKonusmaciKendiYuzunuKullanir()
    {
        // Aynı sakin ses; A'nın yüzü olumlu, B'nin yüzü olumsuz.
        var chunks = new[] { Chunk(0, 10, 0.0), Chunk(10, 20, 0.0) };
        var speakers = new[] { Seg(A, 0, 10), Seg(B, 10, 20) };
        var windows = new[] { Window(0, 10, Face(0, 0.8), Face(1, -0.8)), Window(10, 20, Face(0, 0.8), Face(1, -0.8)) };

        var result = PersonEmotionComposer.Compose(chunks, speakers, windows, Both, Cfg, 0.5)
            .ToDictionary(p => p.Speaker);

        Assert.True(result[A].Combined.Valence > 0.2);
        Assert.True(result[B].Combined.Valence < -0.2);
        Assert.Equal(1, result[A].FaceChunkCount);
    }

    [Fact]
    public void EslesmeyenKonusmaciSonuctaYok()
    {
        var chunks = new[] { Chunk(0, 10, 0.0), Chunk(10, 20, 0.0) };
        var speakers = new[] { Seg(A, 0, 10), Seg(B, 10, 20) };
        var windows = new[] { Window(0, 10, Face(0, 0.8)), Window(10, 20, Face(0, 0.8)) };

        var result = PersonEmotionComposer.Compose(chunks, speakers, windows,
            new Dictionary<SpeakerId, int> { [A] = 0 }, Cfg, 0.5);

        Assert.Equal(A, Assert.Single(result).Speaker);
    }

    [Fact]
    public void EskiYuzKarisimiGeriAlinir_YuzGorunmuyorsaYalnizSesVeSoz()
    {
        // Canlıda en belirgin yüz (yanlış kişi) valence'ı −0,6'ya çekmişti; özgün ses+söz +0,4.
        var chunks = new[] { Chunk(0, 10, speechV: 0.4, combinedV: -0.6) };
        var speakers = new[] { Seg(A, 0, 10) };
        var windows = new[] { Window(0, 10, Face(1, -0.9)) };   // A'nın eşleşen yüzü (0) bu pencerede yok

        var result = Assert.Single(PersonEmotionComposer.Compose(chunks, speakers, windows,
            new Dictionary<SpeakerId, int> { [A] = 0 }, Cfg, 0.5));

        Assert.Equal(0.4, result.Combined.Valence, 3);
        Assert.Equal(0, result.FaceChunkCount);
        Assert.Equal(1, result.ChunkCount);
    }

    [Fact]
    public void OrtalamaKonusmacininButunParcalarindanGelir()
    {
        var chunks = new[] { Chunk(0, 10, 0.0), Chunk(10, 20, 0.0) };
        var speakers = new[] { Seg(A, 0, 20) };
        var windows = new[] { Window(0, 10, Face(0, 0.8)), Window(10, 20) };   // ikinci pencerede yüz yok

        var result = Assert.Single(PersonEmotionComposer.Compose(chunks, speakers, windows,
            new Dictionary<SpeakerId, int> { [A] = 0 }, Cfg, 0.5));

        Assert.Equal(2, result.ChunkCount);
        Assert.Equal(1, result.FaceChunkCount);
        Assert.True(result.Combined.Valence > 0 && result.Combined.Valence < 0.4);
    }

    [Fact]
    public void YuzPenceresiYoksa_YalnizSesVeSoz()
    {
        var chunks = new[] { Chunk(0, 10, 0.3) };
        var speakers = new[] { Seg(A, 0, 10) };

        var result = Assert.Single(PersonEmotionComposer.Compose(chunks, speakers, Array.Empty<FaceWindow>(),
            new Dictionary<SpeakerId, int> { [A] = 0 }, Cfg, 0.5));

        Assert.Equal(0.3, result.Combined.Valence, 3);
        Assert.Equal(0, result.FaceChunkCount);
    }

    [Fact]
    public void YuzsuzDeger_VeYuzunOrtalamaIfadesiTasinir()
    {
        var chunks = new[] { Chunk(0, 10, 0.0), Chunk(10, 20, 0.0) };
        var speakers = new[] { Seg(A, 0, 20) };
        var windows = new[]
        {
            Window(0, 10, new FaceWindowSummary(0, 1, 0.0, 0.1, 0.0, 0.2, 1)),
            Window(10, 20, new FaceWindowSummary(0, 1, 0.2, 0.3, 0.2, 0.4, 1)),
        };

        var result = Assert.Single(PersonEmotionComposer.Compose(chunks, speakers, windows,
            new Dictionary<SpeakerId, int> { [A] = 0 }, Cfg, 0.5));

        Assert.Equal(0.0, result.WithoutFace.Valence, 3);          // yüz hiç katılmasaydı
        Assert.True(result.Combined.Valence > result.WithoutFace.Valence);
        var face = result.AverageFace!;
        Assert.Equal(0.2, face.ValenceShift, 3);
        Assert.Equal(0.3, face.SmileRatio, 3);
        Assert.Equal(0.1, face.NegativeExpression, 3);
    }

    [Fact]
    public void YuzKatilmadiysa_OrtalamaYuzYok()
    {
        var chunks = new[] { Chunk(0, 10, 0.2) };
        var result = Assert.Single(PersonEmotionComposer.Compose(chunks, new[] { Seg(A, 0, 10) },
            Array.Empty<FaceWindow>(), new Dictionary<SpeakerId, int> { [A] = 0 }, Cfg, 0.5));

        Assert.Null(result.AverageFace);
        Assert.Equal(result.Combined.Valence, result.WithoutFace.Valence, 3);
    }
}
