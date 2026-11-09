using System.Collections.Generic;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class TurkishSummaryWriterTests
{
    private static MeetingSummary Metrics(
        Emotion dominant, double trend, double volatility, double congruence) =>
        new(dominant, new Dictionary<Emotion, double> { [dominant] = 1.0 },
            trend, volatility, new List<ChunkResult>(),
            new ValenceArousal(0.0, 0.5), congruence, TurkishSummary: string.Empty);

    [Fact]
    public void Write_MentionsDominantEmotionInTurkish()
    {
        var text = TurkishSummaryWriter.Write(Metrics(Emotion.Mutlu, 0.1, 0.05, 0.9));
        Assert.Contains("en çok mutlu bir hava", text);
    }

    [Fact]
    public void Write_SaysWarming_ForPositiveTrend()
    {
        var text = TurkishSummaryWriter.Write(Metrics(Emotion.Mutlu, 0.2, 0.05, 0.9));
        Assert.Contains("olumluya döndü", text);
    }

    [Fact]
    public void Write_SaysCooling_ForNegativeTrend()
    {
        var text = TurkishSummaryWriter.Write(Metrics(Emotion.Uzgun, -0.2, 0.05, 0.9));
        Assert.Contains("olumsuza döndü", text);
    }

    [Fact]
    public void Write_FlagsLowCongruence()
    {
        var text = TurkishSummaryWriter.Write(Metrics(Emotion.Gergin, 0.0, 0.05, 0.2));
        Assert.Contains("sık sık çelişti", text);
    }

    [Fact]
    public void Write_TeknikTerimKullanmaz()
    {
        // Konuyu bilmeyen kullanıcı için: "örtüşme", "oynaklık", "baskın" gibi terimler yok.
        var text = TurkishSummaryWriter.Write(Metrics(Emotion.Gergin, 0.0, 0.4, 0.5));
        Assert.Equal("Görüşmede en çok gergin bir hava vardı. Görüşme ilerledikçe hava pek değişmedi; "
                   + "duygular sık sık değişti. Söylenenlerle ses tonu kısmen uyumluydu.", text);
    }

    [Fact]
    public void Tags_CumleyleAyniIfadeler_RenkTonuyla()
    {
        var tags = TurkishSummaryWriter.Tags(Metrics(Emotion.Gergin, 0.2, 0.4, 0.5));
        Assert.Equal(new[]
        {
            ("Hava: olumluya döndü", SummaryTone.Good),
            ("Duygular: sık sık değişti", SummaryTone.Warn),
            ("Söz ve ses tonu: kısmen uyumluydu", SummaryTone.Warn),
        }, tags);
    }

    [Fact]
    public void Tags_OlumsuzVeCelisik_Kirmizi()
    {
        var tags = TurkishSummaryWriter.Tags(Metrics(Emotion.Uzgun, -0.2, 0.05, 0.2));
        Assert.Equal(SummaryTone.Bad, tags[0].Tone);
        Assert.Equal(("Duygular: genelde aynı kaldı", SummaryTone.Neutral), tags[1]);
        Assert.Equal(("Söz ve ses tonu: sık sık çelişti", SummaryTone.Bad), tags[2]);
    }

    [Fact]
    public void Write_IsNonEmpty()
    {
        var text = TurkishSummaryWriter.Write(Metrics(Emotion.Notr, 0.0, 0.0, 1.0));
        Assert.False(string.IsNullOrWhiteSpace(text));
    }
}
