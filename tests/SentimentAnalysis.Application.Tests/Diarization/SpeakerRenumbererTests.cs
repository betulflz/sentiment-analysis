using System;
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

/// <summary>sherpa küme kimliklerini KEYFİ verir — ilk konuşana 0 verdiği garanti değildir.
/// Yeniden numaralandırma olmadan ilk konuşan kişi "Konuşmacı F" olarak görünebilir ve
/// kullanıcı bunu rastgele isim atama sanır.</summary>
public class SpeakerRenumbererTests
{
    private static SpeakerSegment Seg(int index, double startSec, double endSec) =>
        new(new SpeakerId(index), TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec));

    [Fact]
    public void IlkKonusan_SifirOlur()
    {
        var input = new[] { Seg(5, 0, 2), Seg(3, 2, 4) };

        var result = SpeakerRenumberer.ByFirstAppearance(input);

        Assert.Equal(0, result[0].Speaker.Index);
        Assert.Equal("Konuşmacı A", result[0].Speaker.DefaultLabel);
    }

    [Fact]
    public void Numaralar_BosluksuzArtar()
    {
        var input = new[] { Seg(3, 0, 1), Seg(7, 1, 2), Seg(1, 2, 3) };

        var result = SpeakerRenumberer.ByFirstAppearance(input);

        Assert.Equal(new[] { 0, 1, 2 }, result.Select(s => s.Speaker.Index));
    }

    [Fact]
    public void AyniKonusmaci_AyniNumarayiAlir()
    {
        var input = new[] { Seg(9, 0, 1), Seg(4, 1, 2), Seg(9, 2, 3) };

        var result = SpeakerRenumberer.ByFirstAppearance(input);

        Assert.Equal(0, result[0].Speaker.Index);
        Assert.Equal(1, result[1].Speaker.Index);
        Assert.Equal(0, result[2].Speaker.Index);
    }

    [Fact]
    public void GirdiSirasiKarisiksa_IlkKONUSMA_ANINA_GoreNumaralanir()
    {
        // Liste zaman sırasında değil: 5 numaralı küme 10. saniyede, 2 numaralı 1. saniyede.
        var input = new[] { Seg(5, 10, 12), Seg(2, 1, 3) };

        var result = SpeakerRenumberer.ByFirstAppearance(input);

        Assert.Equal(1, result[0].Speaker.Index);  // 5 → ikinci konuşan
        Assert.Equal(0, result[1].Speaker.Index);  // 2 → ilk konuşan
    }

    [Fact]
    public void CikisSirasi_GirdiSirasiniKorur()
    {
        var input = new[] { Seg(5, 10, 12), Seg(2, 1, 3) };

        var result = SpeakerRenumberer.ByFirstAppearance(input);

        Assert.Equal(TimeSpan.FromSeconds(10), result[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(1), result[1].Start);
    }

    [Fact]
    public void Bilinmeyen_DegismedenGecer()
    {
        var input = new[] { new SpeakerSegment(SpeakerId.Unknown, TimeSpan.Zero, TimeSpan.FromSeconds(1)), Seg(4, 1, 2) };

        var result = SpeakerRenumberer.ByFirstAppearance(input);

        Assert.Equal(SpeakerId.Unknown, result[0].Speaker);
        Assert.Equal(0, result[1].Speaker.Index);  // Bilinmeyen numara TÜKETMEZ
    }

    [Fact]
    public void BosListe_BosDoner()
    {
        Assert.Empty(SpeakerRenumberer.ByFirstAppearance(Array.Empty<SpeakerSegment>()));
    }

    [Fact]
    public void Deterministiktir()
    {
        var input = new[] { Seg(5, 0, 2), Seg(3, 2, 4), Seg(5, 4, 6) };

        var a = SpeakerRenumberer.ByFirstAppearance(input).Select(s => s.Speaker.Index);
        var b = SpeakerRenumberer.ByFirstAppearance(input).Select(s => s.Speaker.Index);

        Assert.Equal(a, b);
    }
}
