using System;
using System.Linq;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

public class TranscriptCleanupTests
{
    private static TranscriptSegment T(string text, double s, double e) =>
        new(text, TimeSpan.FromSeconds(s), TimeSpan.FromSeconds(e));

    private static SpeakerSegment S(int speaker, double start, double end) =>
        new(new SpeakerId(speaker), TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end));

    // --- Örtüşen parçalardan gelen tekrar satırlar (2026-11-09 canlı oturumundan gerçek örnekler) ---

    [Fact]
    public void Tekrar_ZamaniCakisanAyniYazi_KisasiAtilir()
    {
        var kept = TranscriptDeduper.Dedupe(new[]
        {
            T("Merhaba Yeşim, ne olmak istersin?", 8, 10),
            T("Ne almak istersin?", 8, 10),          // sonraki parçanın aynı bölgeyi yeniden tanıması
            T("Kafanda bir marka.", 32.56, 34),
            T("kafanda bir marka araştırdın bir şey var mı", 32, 38),
            T("Hangi karta?", 55.56, 56.78),
            T("Hangi karta?", 56, 56.78),
            T("Bonus", 57.26, 57.7),
            T("Bones.", 57.22, 57.68),
            T("Fiyatı 5.000'den az olsun.", 16, 26),
            T("Fiyatı 5 bin anız olsun.", 24, 25.5),
        });

        Assert.Equal(new[]
        {
            "Merhaba Yeşim, ne olmak istersin?", "Fiyatı 5.000'den az olsun.",
            "kafanda bir marka araştırdın bir şey var mı", "Hangi karta?",
            "Bones.", "Bonus", // 8 harften kısa yazı varyantları kasten korunur: gerçek cevabı silmektense tekrarı bırak
        }, kept.Select(k => k.Text));
    }

    [Fact]
    public void Tekrar_ZamaniCakismayanAyniKelime_Korunur()
    {
        // İki ayrı "Tamam." — farklı anlarda söylenmiş, tekrar değil.
        var kept = TranscriptDeduper.Dedupe(new[] { T("Tamam.", 47.54, 47.9), T("Tamam.", 48.9, 50) });
        Assert.Equal(2, kept.Count);
    }

    [Fact]
    public void Tekrar_ZamaniCakisanFarkliCumleler_Korunur()
    {
        var kept = TranscriptDeduper.Dedupe(new[]
        {
            T("Tam olarak nasıl bir model bakıyorsun?", 16, 18),
            T("Fiyatı 5.000'den az olsun.", 16, 26),
        });
        Assert.Equal(2, kept.Count);
    }

    [Fact]
    public void Tekrar_UzunSatirlaCakisanKisaFarkliCumle_Korunur()
    {
        // Güvenlik incelemesi: harf düzeyinde eşleşme "Evet." harflerini her uzun cümlede bulurdu.
        // Kısa bir "Evet." / "Hayır." gerçek bir cevaptır, tekrar sayılmamalı.
        var kept = TranscriptDeduper.Dedupe(new[]
        {
            T("Evet, küçük, ince bir şey istiyorum ve fiyatı da çok yüksek olmasın.", 26, 34),
            T("Evet.", 27, 27.6),
            T("Hayır.", 30, 30.5),
        });
        Assert.Equal(3, kept.Count);
    }

    [Fact]
    public void Tekrar_UzunlukuBenzerAmaFarkliAnlamliCumleler_Korunur()
    {
        var kept = TranscriptDeduper.Dedupe(new[]
        {
            T("Kırmızı modeller çok fazla mevcut değil.", 24, 27.5),
            T("Tam olarak nasıl bir model bakıyorsun?", 24.5, 27),
        });
        Assert.Equal(2, kept.Count);
    }

    // --- Hayalet konuşmacılar ---

    [Fact]
    public void Hayalet_KucukPayliKonusmaci_BilinmeyeneDonerKalanlarYenidenNumaralanir()
    {
        // Gerçek 2 kişi: 0 (33,8 sn) ve 2 (36,3 sn); 1 = 0,3 sn'lik kırıntı, 3 = 5 sn (%6).
        var segs = GhostSpeakerFilter.Apply(new[]
        {
            S(0, 0, 33.8), S(1, 34, 34.3), S(2, 35, 71.3), S(3, 72, 77),
        });

        Assert.Equal(new[] { 0, -1, 1, -1 }, segs.Select(s => s.Speaker.Index));
    }

    [Fact]
    public void Hayalet_UzunKonusanAzPayliKisi_Korunur()
    {
        // %8'in altında ama 6 sn'den uzun: gerçek bir katılımcı olabilir.
        var segs = GhostSpeakerFilter.Apply(new[] { S(0, 0, 100), S(1, 100, 107) });
        Assert.Equal(new[] { 0, 1 }, segs.Select(s => s.Speaker.Index));
    }

    [Fact]
    public void Hayalet_SifirSureliBolgeKimlikKaydirmaz()
    {
        // Güvenlik incelemesi: süresiz (End <= Start) bölgeler paydan düşer ama hayalet kontrolünden
        // kaçıp numaralamayı kaydırmamalı; çıktı her yolda kanonik (ilk konuşan A) olmalı.
        var segs = GhostSpeakerFilter.Apply(new[] { S(3, 0, 0), S(5, 1, 60), S(7, 61, 120) });
        Assert.Equal(new[] { -1, 0, 1 }, segs.Select(s => s.Speaker.Index).Select(i => i < 0 ? -1 : i));
    }

    [Fact]
    public void Hayalet_TekKonusmaci_Korunur() =>
        Assert.Equal(0, Assert.Single(GhostSpeakerFilter.Apply(new[] { S(0, 0, 3) })).Speaker.Index);
}
