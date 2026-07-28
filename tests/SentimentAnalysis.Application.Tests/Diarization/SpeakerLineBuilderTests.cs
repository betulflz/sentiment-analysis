using System;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

public class SpeakerLineBuilderTests
{
    private static (TranscriptSegment, SpeakerId) A(string text, double s, double e, int spk) =>
        (new TranscriptSegment(text, TimeSpan.FromSeconds(s), TimeSpan.FromSeconds(e)),
         spk < 0 ? SpeakerId.Unknown : new SpeakerId(spk));

    [Fact]
    public void Build_ArdisikAyniKonusmaci_TekSatirdaBirlesir()
    {
        var lines = SpeakerLineBuilder.Build([
            A("Merhaba", 0, 2, 0), A("nasılsın", 2, 4, 0), A("iyiyim", 4, 6, 1)]);

        Assert.Equal(2, lines.Count);
        Assert.Equal("Merhaba nasılsın", lines[0].Text);
        Assert.Equal(new SpeakerId(0), lines[0].Speaker);
        Assert.Equal("iyiyim", lines[1].Text);
    }

    [Fact]
    public void Build_BirlesenSatirinZamanAraligi_IlkBaslangicSonBitis()
    {
        var lines = SpeakerLineBuilder.Build([A("a", 1, 2, 0), A("b", 2, 5, 0)]);

        Assert.Single(lines);
        Assert.Equal(TimeSpan.FromSeconds(1), lines[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(5), lines[0].End);
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
    public void Build_ArdisikBilinmeyenler_Birlesir()
    {
        var lines = SpeakerLineBuilder.Build([A("mır", 0, 1, -1), A("mırıl", 1, 2, -1)]);

        Assert.Single(lines);
        Assert.Equal(SpeakerId.Unknown, lines[0].Speaker);
        Assert.Equal("mır mırıl", lines[0].Text);
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
}
