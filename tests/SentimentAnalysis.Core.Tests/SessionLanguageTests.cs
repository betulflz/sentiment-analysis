using SentimentAnalysis.Core;

namespace SentimentAnalysis.Core.Tests;

public class SessionLanguageTests
{
    private static readonly FusedResult Fused = new(
        ValenceArousal.Neutral, Emotion.Notr, Emotion.Mutlu, Confidence.Orta,
        Agreement: true, Incongruent: false, Pattern: IncongruencePattern.Yok,
        Congruence: 1.0, TextValence: 0.0, AudioValence: 0.0);

    private static ChunkResult Chunk(double startSec, double endSec, DetectedLanguage lang) =>
        new(TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec), Fused, "x", lang);

    [Fact]
    public void Dominant_NoChunks_IsOther() =>
        Assert.Equal(DetectedLanguage.Other, SessionLanguage.Dominant(Array.Empty<ChunkResult>()));

    [Fact]
    public void Dominant_SingleLanguage_ReturnsIt() =>
        Assert.Equal(DetectedLanguage.Turkish,
            SessionLanguage.Dominant(new[] { Chunk(0, 10, DetectedLanguage.Turkish) }));

    [Fact]
    public void Dominant_WeighsByDurationNotChunkCount()
    {
        // İki kısa İngilizce chunk (toplam 6 sn) vs bir uzun Türkçe chunk (30 sn).
        // Sayıya göre İngilizce kazanırdı; süreye göre Türkçe kazanmalı.
        var chunks = new[]
        {
            Chunk(0, 30, DetectedLanguage.Turkish),
            Chunk(30, 33, DetectedLanguage.English),
            Chunk(33, 36, DetectedLanguage.English),
        };
        Assert.Equal(DetectedLanguage.Turkish, SessionLanguage.Dominant(chunks));
    }

    [Fact]
    public void Dominant_KnownLanguageBeatsOther_EvenWhenOtherHasMoreTime()
    {
        // Uzun sessiz/anlaşılmaz bölümler, gerçekten tanınmış konuşmayı bastırmamalı.
        var chunks = new[]
        {
            Chunk(0, 100, DetectedLanguage.Other),
            Chunk(100, 105, DetectedLanguage.Turkish),
        };
        Assert.Equal(DetectedLanguage.Turkish, SessionLanguage.Dominant(chunks));
    }

    [Fact]
    public void Dominant_AllOther_FallsBackToOther()
    {
        var chunks = new[] { Chunk(0, 5, DetectedLanguage.Other), Chunk(5, 10, DetectedLanguage.Other) };
        Assert.Equal(DetectedLanguage.Other, SessionLanguage.Dominant(chunks));
    }

    [Fact]
    public void Dominant_ZeroDurationChunksCountAsOne()
    {
        // Dejenere (sıfır süreli) chunk'lar toplamı sıfıra düşürüp sonucu belirsizleştirmemeli.
        var chunks = new[]
        {
            Chunk(0, 0, DetectedLanguage.English),
            Chunk(0, 0, DetectedLanguage.English),
            Chunk(0, 0, DetectedLanguage.Turkish),
        };
        Assert.Equal(DetectedLanguage.English, SessionLanguage.Dominant(chunks));
    }

    [Fact]
    public void Dominant_TieIsDeterministic()
    {
        var a = new[] { Chunk(0, 5, DetectedLanguage.Turkish), Chunk(5, 10, DetectedLanguage.English) };
        var b = new[] { Chunk(0, 5, DetectedLanguage.English), Chunk(5, 10, DetectedLanguage.Turkish) };
        // Eşit süre → enum sırası belirler; girdi sırası sonucu değiştirmemeli.
        Assert.Equal(SessionLanguage.Dominant(a), SessionLanguage.Dominant(b));
    }

    [Fact]
    public void ChunkResult_LanguageDefaultsToOther()
    {
        // Dili taşımayan eski/yalın çağrı yerleri uydurma bir dil iddia etmemeli.
        var c = new ChunkResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), Fused, "merhaba");
        Assert.Equal(DetectedLanguage.Other, c.Language);
    }
}
