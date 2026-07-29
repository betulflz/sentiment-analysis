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

    // --- Teşhis notu ---
    // "Neden Nötr?" sorusunun cevabı çoğu zaman girdi kalitesindedir. Kullanıcı bunu
    // görmeden ayar yapamaz; biz de log okumadan teşhis koyamayız.

    [Fact]
    public void KaliteNotu_HerIkiKanalinGuvenilirliginiYuzdeOlarakGosterir()
    {
        var row = new ChunkRowVm(MakeChunkWithReliability(audio: 0.62, text: 0.9));

        Assert.Equal("Ses girdisi %62 · Metin girdisi %90", row.QualityNote);
    }

    [Fact]
    public void KaliteNotu_TamKaliteliGirdideDeGosterilir()
    {
        // Sessizce gizlemek "sorun yok" ile "ölçüm yok"u ayırt edilemez kılar.
        var row = new ChunkRowVm(MakeChunkWithReliability(audio: 1.0, text: 1.0));

        Assert.Equal("Ses girdisi %100 · Metin girdisi %100", row.QualityNote);
    }

    [Fact]
    public void KaliteZayif_DusukGuvenilirlikteIsaretlenir()
    {
        // UI'ın "bu satıra fazla güvenme" uyarısını tek noktadan sorabilmesi için.
        Assert.True(new ChunkRowVm(MakeChunkWithReliability(audio: 0.1, text: 0.9)).WeakInput);
        Assert.False(new ChunkRowVm(MakeChunkWithReliability(audio: 0.8, text: 0.9)).WeakInput);
    }

    private static ChunkResult MakeChunkWithReliability(double audio, double text) =>
        new(TimeSpan.Zero, TimeSpan.FromSeconds(1),
            new FusedResult(ValenceArousal.Neutral, Emotion.Notr, Emotion.Notr, Confidence.Orta,
                true, false, IncongruencePattern.Yok, 1,
                TextVa: ValenceArousal.Neutral, ProsodyVa: ValenceArousal.Neutral,
                TextConfidence: 0, ProsodyConfidence: 0,
                AudioReliability: audio, TextReliability: text),
            "merhaba");
}
