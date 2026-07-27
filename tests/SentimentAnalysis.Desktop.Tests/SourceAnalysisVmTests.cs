using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;

namespace SentimentAnalysis.Desktop.Tests;

public class SourceAnalysisVmTests
{
    private const double T = 0.35;

    private static ChunkResult Chunk(
        ValenceArousal textVa, ValenceArousal prosodyVa, ValenceArousal combined,
        double textReliability = 0.9, double audioReliability = 0.8,
        IncongruencePattern pattern = IncongruencePattern.Yok, bool incongruent = false)
    {
        var fused = new FusedResult(
            combined, EmotionWheel.Map(combined), Emotion.Notr, Confidence.Orta,
            Agreement: false, Incongruent: incongruent, Pattern: pattern, Congruence: 0.5,
            TextValence: textVa.Valence, AudioValence: prosodyVa.Valence,
            TextArousal: textVa.Arousal, AudioArousal: prosodyVa.Arousal,
            TextConfidence: 0.7, ProsodyConfidence: 0.6,
            TextReliability: textReliability, AudioReliability: audioReliability);
        return new ChunkResult(TimeSpan.Zero, TimeSpan.FromSeconds(10), fused, "test", DetectedLanguage.Turkish);
    }

    [Fact]
    public void Empty_HasPlaceholderAndNoShares()
    {
        var vm = SourceAnalysisVm.Empty("Metin Analizi");
        Assert.Equal("Metin Analizi", vm.Title);
        Assert.Equal("—", vm.Emotion);
        Assert.Empty(vm.Shares);
    }

    [Fact]
    public void Shares_AreEightAndSumToExactlyHundred()
    {
        var vm = new SourceAnalysisVm("X", new ValenceArousal(0.3, 0.6), T);
        Assert.Equal(8, vm.Shares.Count);
        Assert.Equal(100, vm.Shares.Sum(s => s.Percent));
    }

    [Fact]
    public void Shares_AreDescending()
    {
        var vm = new SourceAnalysisVm("X", new ValenceArousal(-0.5, 0.8), T);
        for (int i = 1; i < vm.Shares.Count; i++)
            Assert.True(vm.Shares[i - 1].Percent >= vm.Shares[i].Percent);
    }

    [Fact]
    public void DominantEmotion_MatchesWheelMapping()
    {
        var va = new ValenceArousal(0.8, 0.5);   // Mutlu merkezi
        var vm = new SourceAnalysisVm("X", va, T);
        Assert.Equal("Mutlu", vm.Emotion);
        Assert.Equal("Mutlu", vm.Shares[0].Emotion);
    }

    [Fact]
    public void FromChunk_FeedsEachColumnFromItsOwnSource_NotMixed()
    {
        // Metin sütunu metnin VA'sını, ses sütunu sesin VA'sını göstermeli — karışmamalı.
        var textVa = new ValenceArousal(0.8, 0.5);      // Mutlu
        var prosodyVa = new ValenceArousal(-0.8, 0.9);  // Kızgın
        var combined = new ValenceArousal(0.0, 0.5);    // Nötr

        var (text, audio, final) = SourceAnalysisVm.FromChunk(Chunk(textVa, prosodyVa, combined), T);

        Assert.Equal("Metin Analizi", text.Title);
        Assert.Equal(0.8, text.Valence, precision: 6);
        Assert.Equal(0.5, text.Arousal, precision: 6);

        Assert.Equal("Ses Analizi", audio.Title);
        Assert.Equal(-0.8, audio.Valence, precision: 6);
        Assert.Equal(0.9, audio.Arousal, precision: 6);

        Assert.Equal("Nihai Karar", final.Title);
        Assert.Equal(0.0, final.Valence, precision: 6);
    }

    [Fact]
    public void FromChunk_IncongruentChunk_ThreeColumnsShowDifferentEmotions()
    {
        // Maskeleme senaryosu: söz olumlu, ton olumsuz, nihai ortada.
        var (text, audio, final) = SourceAnalysisVm.FromChunk(
            Chunk(new ValenceArousal(0.8, 0.5), new ValenceArousal(-0.8, 0.9), new ValenceArousal(-0.2, 0.7),
                  pattern: IncongruencePattern.Maskeleme, incongruent: true), T);

        Assert.Equal("Mutlu", text.Emotion);
        Assert.Equal("Kızgın", audio.Emotion);
        Assert.NotEqual(text.Emotion, final.Emotion);
        Assert.NotEqual(audio.Emotion, final.Emotion);
    }

    [Fact]
    public void FromChunk_SilentChannel_IsMarked()
    {
        var (text, audio, final) = SourceAnalysisVm.FromChunk(
            Chunk(new ValenceArousal(0.5, 0.5), new ValenceArousal(0.3, 0.6), new ValenceArousal(0.3, 0.6),
                  textReliability: 0.0), T);

        Assert.True(text.Silent, "sıfır ağırlıklı metin kanalı işaretlenmeli");
        Assert.False(audio.Silent);
        Assert.False(final.Silent);
    }

    [Fact]
    public void FromChunk_CarriesReliabilityAndConfidence()
    {
        var (text, audio, _) = SourceAnalysisVm.FromChunk(
            Chunk(new ValenceArousal(0.5, 0.5), new ValenceArousal(0.3, 0.6), new ValenceArousal(0.4, 0.55),
                  textReliability: 0.91, audioReliability: 0.54), T);

        Assert.Equal(0.91, text.Reliability, precision: 6);
        Assert.Equal(0.54, audio.Reliability, precision: 6);
        Assert.Equal(0.7, text.ModelConfidence, precision: 6);
        Assert.Equal(0.6, audio.ModelConfidence, precision: 6);
    }
}
