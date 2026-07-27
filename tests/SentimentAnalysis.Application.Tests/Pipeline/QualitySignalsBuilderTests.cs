using System;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Pipeline;

public class QualitySignalsBuilderTests
{
    private static AudioChunk Chunk(float[] s, double startSec = 0, double endSec = 1) =>
        new(s, 16000, TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec));

    private static Transcript Tr(string text, double logProb = -0.3) =>
        new(text, DetectedLanguage.Turkish, logProb);

    [Fact]
    public void Build_CopiesDurationWordCountAndLogProb()
    {
        var q = QualitySignalsBuilder.Build(Chunk(new float[16000], 1, 3.5), Tr("bugün hava çok güzel", -0.42));
        Assert.Equal(TimeSpan.FromSeconds(2.5), q.Duration);
        Assert.Equal(4, q.TranscriptWordCount);
        Assert.Equal(-0.42, q.TranscriptAvgLogProb, precision: 5);
    }

    [Fact]
    public void Build_EmptyTranscript_ZeroWords()
    {
        var q = QualitySignalsBuilder.Build(Chunk(new float[1600]), Tr("   "));
        Assert.Equal(0, q.TranscriptWordCount);
    }

    [Fact]
    public void Build_Silence_LowSnrAndSpeechRatio()
    {
        var q = QualitySignalsBuilder.Build(Chunk(new float[16000]), Tr("x"));
        Assert.Equal(0.0, q.AudioSnr, precision: 5);
        Assert.Equal(0.0, q.SpeechRatio, precision: 5);
    }

    [Fact]
    public void Build_HalfLoud_PositiveSnr_MidSpeechRatio()
    {
        // İlk yarı yüksek (0.5 genlik kare-dalga), ikinci yarı sessiz.
        var s = new float[16000];
        for (int i = 0; i < 8000; i++) s[i] = (i % 2 == 0) ? 0.5f : -0.5f;
        var q = QualitySignalsBuilder.Build(Chunk(s), Tr("konuşma"));
        Assert.True(q.AudioSnr > 0, $"SNR>0 beklenir, {q.AudioSnr}");
        Assert.InRange(q.SpeechRatio, 0.3, 0.7); // ~yarısı konuşma
    }

    [Fact]
    public void Build_EmptySamples_SafeZeroSignals()
    {
        var q = QualitySignalsBuilder.Build(Chunk(Array.Empty<float>(), 0, 0), Tr("x"));
        Assert.Equal(0.0, q.AudioSnr, precision: 5);
        Assert.Equal(0.0, q.SpeechRatio, precision: 5);
        Assert.Equal(TimeSpan.Zero, q.Duration);
    }
}
