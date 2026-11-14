using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

/// <summary>Konuşmacı duygusu, chunk ortalaması yerine konuşmacının KENDİ sesinden ölçülür.
/// Eski yöntemde 10–30 sn'lik chunk'lar birden fazla kişiyi içerdiğinde hiçbirine %50 örtüşme
/// yetişmiyor ve chunk ölçümden tamamen düşüyordu — yani en çok karşılıklı konuşulan anlar
/// sistematik olarak eleniyordu.</summary>
public class SpeakerProsodyAnalyzerTests
{
    private static SpeakerSegment Seg(int speaker, double startSec, double endSec) =>
        new(new SpeakerId(speaker), TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec));

    /// <summary>Verilen valence'ı aynen döndürür; hangi seslerin modele gittiğini kaydeder.</summary>
    private sealed class FakeProsody : IProsodyEmotionModel
    {
        private readonly Func<AudioChunk, double> _valence;
        public List<int> SampleCounts { get; } = new();
        public FakeProsody(Func<AudioChunk, double>? valence = null) => _valence = valence ?? (_ => 0.5);
        public Task<ProsodyEmotion> AnalyzeAsync(AudioChunk chunk, CancellationToken ct)
        {
            SampleCounts.Add(chunk.Samples.Length);
            return Task.FromResult(new ProsodyEmotion(new ValenceArousal(_valence(chunk), 0.5), 0.8));
        }
    }

    private static float[] Audio(double seconds) => new float[(int)(seconds * 16000)];

    [Fact]
    public void RegionSelector_KisaBolgeleriEler()
    {
        var regions = new[] { Seg(0, 0, 0.5), Seg(0, 1, 3) };

        var selected = SpeakerRegionSelector.Select(regions, minSeconds: 1.0, budgetSeconds: 60);

        Assert.Single(selected);
        Assert.Equal(TimeSpan.FromSeconds(1), selected[0].Start);
    }

    [Fact]
    public void RegionSelector_UzunBolgeyiOnceAlir()
    {
        var regions = new[] { Seg(0, 0, 2), Seg(0, 10, 20) };

        var selected = SpeakerRegionSelector.Select(regions, minSeconds: 1.0, budgetSeconds: 60);

        Assert.Equal(TimeSpan.FromSeconds(10), selected[0].Start);   // 10 sn'lik önce
    }

    [Fact]
    public void RegionSelector_ButceyiAsmaz()
    {
        var regions = new[] { Seg(0, 0, 30), Seg(0, 40, 70), Seg(0, 80, 110) };

        var selected = SpeakerRegionSelector.Select(regions, minSeconds: 1.0, budgetSeconds: 60);

        Assert.Equal(2, selected.Count);
        Assert.Equal(60, selected.Sum(s => (s.End - s.Start).TotalSeconds));
    }

    [Fact]
    public void RegionSelector_ButceyiAsanTekBolge_KirpilirAtilmaz()
    {
        // Final inceleme / bulgu 1: eskiden "sığmayan bölgeyi atla" kuralı, tek bölgesi bütçeden
        // uzun olan konuşmacıyı TAMAMEN ölçümsüz bırakıyordu — en çok konuşan kişi en çok sese
        // sahipken hiç prozodi üretilmiyordu. Artık kalan bütçe kadar önek alınır.
        var regions = new[] { Seg(0, 0, 90) };

        var selected = SpeakerRegionSelector.Select(regions, minSeconds: 1.0, budgetSeconds: 60);

        Assert.Single(selected);
        Assert.Equal(TimeSpan.Zero, selected[0].Start);
        Assert.Equal(60, (selected[0].End - selected[0].Start).TotalSeconds, precision: 6);
    }

    [Fact]
    public void RegionSelector_KalanButceKadarOnekAlirVeDurur()
    {
        // 50 sn alındıktan sonra 20 sn'lik bölgeden yalnızca 10 sn'lik önek alınır; seçim durur,
        // arkadan gelen kısa bölgeler bütçeyi doldurmaya çalışmaz (kural: kırpma sonrası DUR).
        var regions = new[] { Seg(0, 0, 50), Seg(0, 60, 80), Seg(0, 100, 105) };

        var selected = SpeakerRegionSelector.Select(regions, minSeconds: 1.0, budgetSeconds: 60);

        Assert.Equal(2, selected.Count);
        Assert.Equal(50, (selected[0].End - selected[0].Start).TotalSeconds, precision: 6);
        Assert.Equal(TimeSpan.FromSeconds(60), selected[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(70), selected[1].End);   // 10 sn'lik önek
        Assert.Equal(60, selected.Sum(s => (s.End - s.Start).TotalSeconds), precision: 6);
    }

    [Fact]
    public void RegionSelector_KalanButceMinSecondsAltindaysa_OnekAlinmaz()
    {
        // Kalan bütçe 0,5 sn: bu kadar kısa bir önekten güvenilir prozodi çıkmaz → alınmaz.
        var regions = new[] { Seg(0, 0, 59.5), Seg(0, 60, 80) };

        var selected = SpeakerRegionSelector.Select(regions, minSeconds: 1.0, budgetSeconds: 60);

        Assert.Single(selected);
        Assert.Equal(59.5, (selected[0].End - selected[0].Start).TotalSeconds, precision: 6);
    }

    [Fact]
    public void RegionSelector_Deterministiktir()
    {
        var regions = new[] { Seg(0, 5, 10), Seg(0, 0, 5), Seg(0, 20, 25) };

        var a = SpeakerRegionSelector.Select(regions, 1.0, 60).Select(s => s.Start);
        var b = SpeakerRegionSelector.Select(regions, 1.0, 60).Select(s => s.Start);

        Assert.Equal(a, b);
    }

    [Fact]
    public void RegionSelector_KirpmaliDurumdaDaDeterministiktir()
    {
        // Kırpma devreye girdiğinde de sonuç koşudan koşuya aynı olmalı (saf karar mantığı).
        var regions = new[] { Seg(0, 0, 40), Seg(0, 50, 90), Seg(0, 100, 130) };

        var a = SpeakerRegionSelector.Select(regions, 1.0, 60).Select(s => (s.Start, s.End)).ToList();
        var b = SpeakerRegionSelector.Select(regions, 1.0, 60).Select(s => (s.Start, s.End)).ToList();

        Assert.Equal(a, b);
        Assert.Equal(60, a.Sum(s => (s.End - s.Start).TotalSeconds), precision: 6);
    }

    [Fact]
    public async Task AnalyzeAsync_HerKonusmaciIcinKendiSesiniKullanir()
    {
        // Konuşmacı 0: 0–4 sn, konuşmacı 1: 4–8 sn. Sahte model, sesin uzunluğuna göre
        // farklı valence döndürmez; burada önemli olan HER konuşmacı için ayrı sonuç üretilmesi.
        var speakers = new[] { Seg(0, 0, 4), Seg(1, 4, 8) };
        var analyzer = new SpeakerProsodyAnalyzer(new FakeProsody(), AppConfig.Default);

        var result = await analyzer.AnalyzeAsync(Audio(8), speakers, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(new SpeakerId(0), result[0].Speaker);
        Assert.Equal(new SpeakerId(1), result[1].Speaker);
        Assert.Equal(4.0, result[0].AnalyzedSeconds, precision: 3);
    }

    [Fact]
    public async Task AnalyzeAsync_SureAgirlikliOrtalamaAlir()
    {
        // 9 sn'lik bölge valence +1, 1 sn'lik bölge valence −1 → ağırlıklı ortalama +0,8.
        var speakers = new[] { Seg(0, 0, 9), Seg(0, 10, 11) };
        var model = new FakeProsody(chunk => chunk.Samples.Length > 5 * 16000 ? 1.0 : -1.0);
        var analyzer = new SpeakerProsodyAnalyzer(model, AppConfig.Default);

        var result = await analyzer.AnalyzeAsync(Audio(12), speakers, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(0.8, result[0].Va.Valence, precision: 3);
        Assert.Equal(2, result[0].RegionCount);
    }

    [Fact]
    public async Task AnalyzeAsync_YeterliSesiOlmayanKonusmaci_SonucUretmez()
    {
        // 0,5 sn'lik tek bölge: prozodi için yetersiz. Uydurma sonuç ÜRETİLMEZ.
        var speakers = new[] { Seg(0, 0, 0.5), Seg(1, 1, 5) };
        var analyzer = new SpeakerProsodyAnalyzer(new FakeProsody(), AppConfig.Default);

        var result = await analyzer.AnalyzeAsync(Audio(6), speakers, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(new SpeakerId(1), result[0].Speaker);
    }

    [Fact]
    public async Task AnalyzeAsync_Bilinmeyen_AnalizEdilmez()
    {
        var speakers = new[]
        {
            new SpeakerSegment(SpeakerId.Unknown, TimeSpan.Zero, TimeSpan.FromSeconds(4)),
            Seg(0, 4, 8),
        };
        var analyzer = new SpeakerProsodyAnalyzer(new FakeProsody(), AppConfig.Default);

        var result = await analyzer.AnalyzeAsync(Audio(8), speakers, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(new SpeakerId(0), result[0].Speaker);
    }

    [Fact]
    public async Task AnalyzeAsync_SesSinirlariniAsanBolge_Kirpilir()
    {
        // Bölge 10 sn'ye kadar diyor ama elimizde 5 sn ses var: taşma çökertmemeli.
        var speakers = new[] { Seg(0, 0, 10) };
        var model = new FakeProsody();
        var analyzer = new SpeakerProsodyAnalyzer(model, AppConfig.Default);

        var result = await analyzer.AnalyzeAsync(Audio(5), speakers, CancellationToken.None);

        Assert.Single(result);
        Assert.All(model.SampleCounts, n => Assert.True(n <= 5 * 16000));
    }
}
