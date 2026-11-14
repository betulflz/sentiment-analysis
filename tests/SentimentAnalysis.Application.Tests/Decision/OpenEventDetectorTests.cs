using System;
using System.Linq;
using SentimentAnalysis.Application.Decision;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Decision;

/// <summary>İt.4: kanıta dayalı öneri — açık olaylar (cevapsız soru, belirsiz sonraki adım, uzun tek kişi), kaynak cümleli.</summary>
public class OpenEventDetectorTests
{
    private static readonly SpeakerId A = new(0), B = new(1);

    private static SpeakerLine L(SpeakerId who, double s, double e, string text) =>
        new(who, text, TimeSpan.FromSeconds(s), TimeSpan.FromSeconds(e), false);

    [Fact]
    public void Soru_KarsiTarafCevaplamadan_KonuDegisirse_CevapsizSoru()
    {
        var ev = OpenEventDetector.Detect(new[]
        {
            L(A, 0, 4, "Bütçeyi ne zaman onaylarsınız?"),
            L(A, 5, 9, "Neyse, ürün tarafına geçelim, yarın size teklif gönderirim."),
        });
        var q = Assert.Single(ev, e => e.Kind == OpenEventKind.CevapsizSoru);
        Assert.Equal("Bütçeyi ne zaman onaylarsınız?", q.Source);
        Assert.Equal(TimeSpan.Zero, q.At);
        Assert.Equal(A, q.Speaker);
        Assert.Contains("Bütçeyi ne zaman onaylarsınız?", q.Suggestion);
    }

    [Fact]
    public void Soru_KarsiTarafHemenCevaplarsa_OlayYok()
    {
        var ev = OpenEventDetector.Detect(new[]
        {
            L(A, 0, 4, "Bütçeyi ne zaman onaylarsınız?"),
            L(B, 5, 9, "Haftaya cuma onaylarız, size e-posta atarım."),
        });
        Assert.DoesNotContain(ev, e => e.Kind == OpenEventKind.CevapsizSoru);
    }

    [Fact]
    public void SoruIsaretsizSoruEki_DeSoruSayilir()
    {
        // Whisper soru işaretini sık atlar: "mı/mi/mu/mü" eki yeter.
        var ev = OpenEventDetector.Detect(new[]
        {
            L(A, 0, 3, "Fiyat sizin için uygun mu"),
            L(A, 4, 8, "Bir de teslimat var, yarın ararım."),
        });
        Assert.Contains(ev, e => e.Kind == OpenEventKind.CevapsizSoru);
    }

    [Fact]
    public void SonSoru_CevapGelmedenGorusmeBiterse_CevapsizSoru()
    {
        var ev = OpenEventDetector.Detect(new[]
        {
            L(B, 0, 5, "Teklifi yarın göndereceğim."),
            L(A, 6, 9, "Peki başka sorunuz var mı?"),
        });
        Assert.Contains(ev, e => e.Kind == OpenEventKind.CevapsizSoru);
    }

    [Fact]
    public void SonBolumdeSonrakiAdimYoksa_BelirsizSonrakiAdim()
    {
        var ev = OpenEventDetector.Detect(new[]
        {
            L(A, 0, 10, "Merhaba, ürünü anlatayım."),
            L(B, 11, 20, "Güzel görünüyor."),
            L(A, 21, 30, "Teşekkürler, iyi günler."),
        });
        var n = Assert.Single(ev, e => e.Kind == OpenEventKind.BelirsizSonrakiAdim);
        Assert.Equal("Teşekkürler, iyi günler.", n.Source);
    }

    [Fact]
    public void SonBolumdeTarihliSoz_VarsaOlayYok()
    {
        var ev = OpenEventDetector.Detect(new[]
        {
            L(A, 0, 10, "Merhaba, ürünü anlatayım."),
            L(B, 11, 20, "Güzel, haftaya salı tekrar görüşelim."),
            L(A, 21, 30, "Teşekkürler, iyi günler."),
        });
        Assert.DoesNotContain(ev, e => e.Kind == OpenEventKind.BelirsizSonrakiAdim);
    }

    [Fact]
    public void TekKisiUzunSureKesintisizKonusursa_UzunTekKisi()
    {
        var lines = Enumerable.Range(0, 12).Select(i => L(A, i * 10, i * 10 + 9.5, $"Cümle {i}.")).ToList();
        lines.Add(L(B, 125, 128, "Anladım."));
        var ev = OpenEventDetector.Detect(lines);
        var m = Assert.Single(ev, e => e.Kind == OpenEventKind.UzunTekKisi);
        Assert.Equal(A, m.Speaker);
        Assert.Equal("Cümle 0.", m.Source);
        Assert.Equal(TimeSpan.Zero, m.At);
    }

    [Fact]
    public void KarsiTarafAraraSozAlirsa_UzunTekKisiYok()
    {
        var lines = Enumerable.Range(0, 12)
            .Select(i => L(i % 4 == 3 ? B : A, i * 10, i * 10 + 9.5, $"Cümle {i}.")).ToList();
        Assert.DoesNotContain(OpenEventDetector.Detect(lines), e => e.Kind == OpenEventKind.UzunTekKisi);
    }

    [Fact]
    public void BosGirdi_OlayYok()
    {
        Assert.Empty(OpenEventDetector.Detect(Array.Empty<SpeakerLine>()));
    }

    [Fact]
    public void Olaylar_ZamanSirasinda()
    {
        var ev = OpenEventDetector.Detect(new[]
        {
            L(A, 0, 4, "Bütçe uygun mu?"),
            L(A, 5, 9, "Neyse."),
            L(A, 10, 14, "Fiyatı konuştunuz mu?"),
            L(A, 15, 19, "Teşekkürler."),
        });
        Assert.Equal(ev.OrderBy(e => e.At).Select(e => e.At), ev.Select(e => e.At));
    }
}
