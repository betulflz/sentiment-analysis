using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class ChunkRowVmTests
{
    private static ChunkResult MakeChunk(ValenceArousal combined) =>
        new(TimeSpan.Zero, TimeSpan.FromSeconds(1),
            new FusedResult(combined, Emotion.Notr, Emotion.Notr, Confidence.Orta,
                true, false, IncongruencePattern.Yok, 1,
                TextVa: ValenceArousal.Neutral, ProsodyVa: ValenceArousal.Neutral,
                TextConfidence: 0, ProsodyConfidence: 0),
            "merhaba");

    [Fact]
    public void AxisSummary_MatchesEmotionLocalizerForSameVa()
    {
        var va = new ValenceArousal(-0.34, 0.28);
        var row = new ChunkRowVm(MakeChunk(va));

        var expected = $"{EmotionLocalizer.ValenceText(va.Valence)} · {EmotionLocalizer.ArousalText(va.Arousal)}";
        Assert.Equal(expected, row.AxisSummary);
    }

    [Fact]
    public void AxisSummary_UsesPlainTurkishNotRawNumbers()
    {
        var row = new ChunkRowVm(MakeChunk(new ValenceArousal(0.62, 0.41)));

        Assert.Equal("Çok olumlu · Orta canlılıkta", row.AxisSummary);
    }
}
