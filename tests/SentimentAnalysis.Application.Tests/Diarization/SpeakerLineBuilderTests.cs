using System;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

public class SpeakerLineBuilderTests
{
    private static AlignedSegment A(string text, double s, double e, int spk) =>
        new(new TranscriptSegment(text, TimeSpan.FromSeconds(s), TimeSpan.FromSeconds(e)),
            spk < 0 ? SpeakerId.Unknown : new SpeakerId(spk), Uncertain: false);

    private static AlignedSegment Aligned(int speaker, string text, double startSec, double endSec, bool uncertain) =>
        new(new TranscriptSegment(text, TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec)),
            new SpeakerId(speaker), uncertain);

    [Fact]
    public void Build_ArdisikAyniKonusmaci_HerCumleAyriSatir()
    {
        // 2026-11-09: birleştirme, birkaç dakikalık konuşmayı tek dev balona çeviriyordu; her cümle
        // kendi satırında ve kendi saatiyle kalır.
        var lines = SpeakerLineBuilder.Build([
            A("Merhaba", 0, 2, 0), A("nasılsın", 2, 4, 0), A("iyiyim", 4, 6, 1)]);

        Assert.Equal(new[] { "Merhaba", "nasılsın", "iyiyim" }, lines.Select(l => l.Text));
        Assert.Equal(new SpeakerId(0), lines[1].Speaker);
        Assert.Equal(TimeSpan.FromSeconds(2), lines[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(4), lines[1].End);
    }

    [Fact]
    public void Build_KonusmaciDegisipGeriDonerse_UcSatirOlur()
    {
        var lines = SpeakerLineBuilder.Build([
            A("a", 0, 1, 0), A("b", 1, 2, 1), A("c", 2, 3, 0)]);

        Assert.Equal(3, lines.Count);      // A→B→A birleşmez
        Assert.Equal(new SpeakerId(0), lines[2].Speaker);
    }

    [Fact]
    public void Build_BosGirdi_BosDondurur()
    {
        Assert.Empty(SpeakerLineBuilder.Build([]));
    }

    [Fact]
    public void Build_TekSegment_TekSatir()
    {
        var lines = SpeakerLineBuilder.Build([A("tek", 0, 1, 0)]);

        Assert.Single(lines);
        Assert.Equal("tek", lines[0].Text);
    }

    [Fact]
    public void Build_HicMetinKaybolmaz()
    {
        var lines = SpeakerLineBuilder.Build([
            A("bir", 0, 1, 0), A("iki", 1, 2, 1), A("üç", 2, 3, 1), A("dört", 3, 4, 0)]);

        var all = string.Join(" ", lines.Select(l => l.Text));
        Assert.Equal("bir iki üç dört", all);
    }

    [Fact]
    public void AyniKonusmaci_FarkliKesinlik_Birlesmez()
    {
        // Emin bir cümleyle belirsiz bir cümle aynı satıra karışırsa, satırın HANGİ kısmına
        // güvenileceği kaybolur — kullanıcı yanlış alıntı yapar.
        var aligned = new[]
        {
            Aligned(0, "kesin cümle", 0, 1, uncertain: false),
            Aligned(0, "belirsiz cümle", 1, 2, uncertain: true),
        };

        var lines = SpeakerLineBuilder.Build(aligned);

        Assert.Equal(2, lines.Count);
        Assert.False(lines[0].Uncertain);
        Assert.True(lines[1].Uncertain);
    }
}
