using System;
using System.Collections.Generic;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Stt;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Stt.Tests;

public class TranscriptAssemblerTests
{
    private static SttSegment Seg(string text, double startSec, double endSec, double prob, string lang = "tr") =>
        new(text, TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec), prob, lang);

    [Fact]
    public void Assemble_JoinsTextAndMapsLanguage()
    {
        var t = TranscriptAssembler.Assemble(
            new[] { Seg("Merhaba", 0, 1, 0.9), Seg("dünya", 1, 2, 0.8) }, forced: null);
        Assert.Equal("Merhaba dünya", t.Text);
        Assert.Equal(DetectedLanguage.Turkish, t.Language);
    }

    [Fact]
    public void Assemble_AvgLogProb_IsLogOfProbability_AndNegative()
    {
        var t = TranscriptAssembler.Assemble(new[] { Seg("x", 0, 1, 0.5) }, forced: null);
        Assert.Equal(Math.Log(0.5), t.AvgLogProb, precision: 10);
        Assert.True(t.AvgLogProb < 0);
    }

    [Fact]
    public void Assemble_WeightsProbabilityByDuration()
    {
        // Uzun segment (yüksek olasılık) baskın olmalı → ort olasılık 0.9'a yakın.
        var t = TranscriptAssembler.Assemble(
            new[] { Seg("a", 0, 9, 0.9), Seg("b", 9, 10, 0.1) }, forced: null);
        Assert.Equal(Math.Log((9 * 0.9 + 1 * 0.1) / 10.0), t.AvgLogProb, precision: 10);
    }

    [Fact]
    public void Assemble_EmptySegments_YieldsEmptyTextAndLowLogProb()
    {
        var t = TranscriptAssembler.Assemble(Array.Empty<SttSegment>(), forced: DetectedLanguage.Turkish);
        Assert.Equal(string.Empty, t.Text);
        Assert.Equal(DetectedLanguage.Turkish, t.Language);
        Assert.Equal(TranscriptAssembler.EmptyAvgLogProb, t.AvgLogProb);
    }

    [Fact]
    public void Assemble_ForcedLanguage_OverridesDetected()
    {
        var t = TranscriptAssembler.Assemble(
            new[] { Seg("hello", 0, 1, 0.9, lang: "en") }, forced: DetectedLanguage.Turkish);
        Assert.Equal(DetectedLanguage.Turkish, t.Language);
    }

    // --- 10b: konuşmacı hizalaması için zaman damgaları korunmalı ---

    [Fact]
    public void Assemble_ZamanDamgalariniKorur_MetniDegistirmeden()
    {
        var t = TranscriptAssembler.Assemble(
            new[] { Seg("Merhaba", 0, 2, 0.9), Seg("nasılsın", 2, 4, 0.8) }, forced: null);

        Assert.Equal("Merhaba nasılsın", t.Text);          // mevcut davranış korunur
        Assert.Equal(2, t.Segments.Count);
        Assert.Equal("Merhaba", t.Segments[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(0), t.Segments[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(2), t.Segments[0].End);
        Assert.Equal("nasılsın", t.Segments[1].Text);
        Assert.Equal(TimeSpan.FromSeconds(4), t.Segments[1].End);
    }

    [Fact]
    public void Assemble_BosSegmentListesi_BosSegmentDondurur()
    {
        var t = TranscriptAssembler.Assemble(Array.Empty<SttSegment>(), forced: null);

        Assert.Empty(t.Segments);
        Assert.Equal(string.Empty, t.Text);
    }

    [Fact]
    public void Assemble_BosMetinliSegmentiAtar_MetindeDeSegmenttteDe()
    {
        var t = TranscriptAssembler.Assemble(
            new[] { Seg("  ", 0, 1, 0.5), Seg("evet", 1, 2, 0.9) }, forced: null);

        Assert.Equal("evet", t.Text);
        Assert.Single(t.Segments);                          // boş metinli segment taşınmaz
        Assert.Equal("evet", t.Segments[0].Text);
    }

    [Fact]
    public void Assemble_SegmentMetniKirpilir_BastakiSondakiBosluklarGitmeli()
    {
        var t = TranscriptAssembler.Assemble(new[] { Seg("  merhaba  ", 0, 1, 0.9) }, forced: null);

        Assert.Equal("merhaba", t.Segments[0].Text);
    }

    [Theory]
    [InlineData("tr", DetectedLanguage.Turkish)]
    [InlineData("EN", DetectedLanguage.English)]
    [InlineData("de", DetectedLanguage.Other)]
    [InlineData(null, DetectedLanguage.Other)]
    public void LanguageMap_MapsIsoCodes(string? code, DetectedLanguage expected)
    {
        Assert.Equal(expected, LanguageMap.FromIso(code));
    }

    // İt.2: Whisper sessizlikte Türkçe altyazı/YouTube kapanışları uydurur (whisper-beam-01: 30 sn sessizlik → "Altyazı M.K.").
    [Theory]
    [InlineData("Altyazı M.K.")]
    [InlineData("altyazı: M.K")]
    [InlineData("Alt yazı M.K.")]
    [InlineData("İzlediğiniz için teşekkürler.")]
    [InlineData("İzlediğiniz için teşekkür ederim!")]
    [InlineData("Abone olmayı unutmayın.")]
    [InlineData("Bir sonraki videoda görüşmek üzere.")]
    [InlineData("Videoyu beğenmeyi ve abone olmayı unutmayın!")]
    public void Assemble_DropsKnownSilenceHallucination_AsNoSpeech(string hallucination)
    {
        var t = TranscriptAssembler.Assemble(new[] { Seg(hallucination, 0, 29.98, 0.6) }, forced: null);
        Assert.Equal(string.Empty, t.Text);
        Assert.Empty(t.Segments!);
        Assert.Equal(TranscriptAssembler.EmptyAvgLogProb, t.AvgLogProb);
    }

    [Fact]
    public void Assemble_DropsHallucinatedSegment_KeepsRealSpeech()
    {
        var t = TranscriptAssembler.Assemble(
            new[] { Seg("Peki ne zaman başlayabilirsiniz?", 0, 3, 0.9), Seg("Altyazı M.K.", 3, 10, 0.2) }, forced: null);
        Assert.Equal("Peki ne zaman başlayabilirsiniz?", t.Text);
        Assert.Single(t.Segments!);
        Assert.Equal(Math.Log(0.9), t.AvgLogProb, precision: 10);
    }

    [Theory]
    [InlineData("Dinlediğiniz ve izlediğiniz için teşekkürler, şimdi fiyat konusuna geçelim.")]
    [InlineData("Altyazı hazırlama işini biz yapıyoruz.")]
    [InlineData("Abone sayımız geçen ay arttı.")]
    public void Assemble_KeepsRealSentences_ContainingSimilarWords(string real)
    {
        var t = TranscriptAssembler.Assemble(new[] { Seg(real, 0, 4, 0.8) }, forced: null);
        Assert.Equal(real, t.Text);
    }
}
