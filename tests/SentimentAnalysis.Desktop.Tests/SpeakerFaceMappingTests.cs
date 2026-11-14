using System;
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

/// <summary>Raporda konuşmacı kartından yüz seçimi: kişiye özel üç kaynaklı duygu ve kişinin adı.</summary>
public class SpeakerFaceMappingTests
{
    private sealed class FakeAggregator : IMeetingAggregator
    {
        public MeetingSummary Aggregate(MeetingSession s) => new(
            Emotion.Notr, new Dictionary<Emotion, double>(), 0, 0,
            Array.Empty<ChunkResult>(), ValenceArousal.Neutral, 1.0, "özet");
    }

    private static readonly SpeakerId A = new(0), B = new(1);

    private static ChunkResult Chunk(double a, double b) =>
        new(TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b),
            new FusedResult(new ValenceArousal(0, 0.5), Emotion.Notr, Emotion.Notr, Confidence.Orta,
                true, false, IncongruencePattern.Yok, 1.0,
                TextVa: new ValenceArousal(0, 0.5), ProsodyVa: new ValenceArousal(0, 0.5),
                TextConfidence: 0.8, ProsodyConfidence: 0.8),
            "merhaba");

    private static DecisionWindow Window(double start, params FaceWindowSummary[] faces) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(start + 10),
            new Indicators(new double[6], new int?[6], 0.8),
            new Recommendation(LiveAction.DevamEt, 0.6, Array.Empty<Ranked<LiveAction>>(), Array.Empty<Criterion>(), null,
                Confidence.Orta, "neden")) { Faces = faces };

    private static FaceWindowSummary Face(int index, double valenceShift) =>
        new(index, 1, 0, valenceShift, 0, 0, 1);

    /// <summary>İki konuşmacı (A ilk 10 sn, B sonraki 10 sn); yüz 0 çok olumlu, yüz 1 çok olumsuz.</summary>
    private static ReportViewModel Build(IReadOnlyDictionary<int, string>? names = null)
    {
        var vm = new ReportViewModel(new FakeAggregator());
        var windows = new[] { Window(0, Face(0, 0.9), Face(1, -0.9)), Window(10, Face(0, 0.9), Face(1, -0.9)) };
        vm.LoadDecisions(new DecisionSession(MeetingProfile.Mulakat, new double[6], new double[6], windows));
        var chunks = new[] { Chunk(0, 10), Chunk(10, 20) };
        vm.SetFaceContext(chunks, names ?? new Dictionary<int, string>());
        var speakers = new[]
        {
            new SpeakerSegment(A, TimeSpan.Zero, TimeSpan.FromSeconds(10)),
            new SpeakerSegment(B, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)),
        };
        var transcript = new[]
        {
            new TranscriptSegment("merhaba nasılsın", TimeSpan.Zero, TimeSpan.FromSeconds(10)),
            new TranscriptSegment("iyiyim sağ ol", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)),
        };
        vm.ApplySpeakers(transcript, speakers, 0.5);
        vm.ApplySpeakerEmotions(chunks, speakers, 0.5, Array.Empty<SpeakerProsodySummary>());
        return vm;
    }

    private static FaceOptionVm Option(SpeakerEmotionVm card, int faceIndex) =>
        card.FaceOptions.Single(o => o.FaceIndex == faceIndex);

    [Fact]
    public void YuzSecenekleri_EslestirmeYokArdindanGorulenYuzler_AdlarıylaGelir()
    {
        var vm = Build(new Dictionary<int, string> { [0] = "betül" });

        var options = vm.SpeakerEmotions[0].FaceOptions;

        Assert.Equal(new[] { "Eşleştirme yok", "betül", "Yüz 2" }, options.Select(o => o.Text));
        Assert.Null(options[0].FaceIndex);
    }

    [Fact]
    public void BaslangicMevcutDavranis_EslesmeYok_ZatenEtiketVeNot()
    {
        var vm = Build();

        var card = vm.SpeakerEmotions[0];

        Assert.Null(card.SelectedFace!.FaceIndex);
        Assert.Equal("Konuşmacı A", card.Label);
        Assert.False(card.HasFaceNote);
    }

    [Fact]
    public void YuzSecilince_KisiyeOzelUcKaynakliDuyguHesaplanir()
    {
        var vm = Build();
        var before = vm.SpeakerEmotions[0].CombinedAnalysis.Valence;

        vm.SpeakerEmotions[0].SelectedFace = Option(vm.SpeakerEmotions[0], 0);

        var after = vm.SpeakerEmotions.Single(s => s.SpeakerIndex == 0);
        Assert.True(after.CombinedAnalysis.Valence > before + 0.2);
        Assert.True(after.HasFaceNote);
        Assert.Contains("1/1", after.FaceNote);
    }

    [Fact]
    public void IkiKonusmaci_HerBiriKendiYuzuyle_ZitYonde()
    {
        var vm = Build();
        vm.SpeakerEmotions[0].SelectedFace = Option(vm.SpeakerEmotions[0], 0);
        vm.SpeakerEmotions[1].SelectedFace = Option(vm.SpeakerEmotions[1], 1);

        var a = vm.SpeakerEmotions.Single(s => s.SpeakerIndex == 0);
        var b = vm.SpeakerEmotions.Single(s => s.SpeakerIndex == 1);

        Assert.True(a.CombinedAnalysis.Valence > 0.2);
        Assert.True(b.CombinedAnalysis.Valence < -0.2);
        Assert.Equal(Option(a, 0).FaceIndex, a.SelectedFace!.FaceIndex);   // seçim yeniden kurulumda korunur
        Assert.Equal(1, b.SelectedFace!.FaceIndex);
    }

    [Fact]
    public void YuzSecilince_KisiAdiKartta_SatirlardaVePayda_GorunurAd()
    {
        var vm = Build(new Dictionary<int, string> { [0] = "betül" });

        vm.SpeakerEmotions[0].SelectedFace = Option(vm.SpeakerEmotions[0], 0);

        Assert.Equal("betül", vm.SpeakerEmotions.Single(s => s.SpeakerIndex == 0).Label);
        Assert.Equal("betül", vm.SpeakerLines.First(l => l.SpeakerIndex == 0).Label);
        Assert.Equal("betül", vm.TalkBalance.First(t => t.SpeakerIndex == 0).Label);
        Assert.Equal("Konuşmacı B", vm.SpeakerEmotions.Single(s => s.SpeakerIndex == 1).Label);
    }

    [Fact]
    public void YuzeAdVerilmemisse_KonusmaciEtiketiKorunur()
    {
        var vm = Build();

        vm.SpeakerEmotions[0].SelectedFace = Option(vm.SpeakerEmotions[0], 0);

        Assert.Equal("Konuşmacı A", vm.SpeakerEmotions.Single(s => s.SpeakerIndex == 0).Label);
    }

    [Fact]
    public void EslestirmeKaldirilinca_EskiDuyguyaDonulur()
    {
        var vm = Build();
        var before = vm.SpeakerEmotions[0].CombinedAnalysis.Valence;
        vm.SpeakerEmotions[0].SelectedFace = Option(vm.SpeakerEmotions[0], 0);

        var card = vm.SpeakerEmotions.Single(s => s.SpeakerIndex == 0);
        card.SelectedFace = card.FaceOptions[0];

        var again = vm.SpeakerEmotions.Single(s => s.SpeakerIndex == 0);
        Assert.Equal(before, again.CombinedAnalysis.Valence, 3);
        Assert.False(again.HasFaceNote);
    }

    [Fact]
    public void Yonlendirme_YuzGorulduAmaEslestirmeYoksa_Gorunur()
    {
        var vm = Build();

        Assert.Contains("Bu kişinin yüzü", vm.FaceMappingHint);
    }

    [Fact]
    public void Yonlendirme_EslestirmeYapilincaKaybolur()
    {
        var vm = Build();

        vm.SpeakerEmotions[0].SelectedFace = Option(vm.SpeakerEmotions[0], 0);

        Assert.Equal(string.Empty, vm.FaceMappingHint);
    }

    [Fact]
    public void Yonlendirme_YuzHicGorulmediyseYok()
    {
        var vm = new ReportViewModel(new FakeAggregator());
        var chunks = new[] { Chunk(0, 10) };
        var speakers = new[] { new SpeakerSegment(A, TimeSpan.Zero, TimeSpan.FromSeconds(10)) };
        vm.ApplySpeakers(new[] { new TranscriptSegment("merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(10)) }, speakers, 0.5);
        vm.ApplySpeakerEmotions(chunks, speakers, 0.5, Array.Empty<SpeakerProsodySummary>());

        Assert.Equal(string.Empty, vm.FaceMappingHint);
    }

    [Fact]
    public void YuzNotu_YuzunIfadesiniVeEtkisiniSoyler()
    {
        var vm = Build();

        vm.SpeakerEmotions[0].SelectedFace = Option(vm.SpeakerEmotions[0], 0);

        var note = vm.SpeakerEmotions.Single(s => s.SpeakerIndex == 0).FaceNote;
        Assert.Contains("1/1", note);
        Assert.Contains("etkisi", note);
        Assert.Contains("gülümseme", note);
    }

    [Fact]
    public void YuzNotu_KucukEtkiyiAcikcaBelirtir()
    {
        // Yüz neredeyse nötr (+0,03): kullanıcı "hiçbir şey olmadı" sanmasın, etkinin küçük olduğunu görsün.
        var vm = new ReportViewModel(new FakeAggregator());
        var windows = new[] { Window(0, new FaceWindowSummary(0, 1, 0, 0.03, 0, 0.1, 1)) };
        vm.LoadDecisions(new DecisionSession(MeetingProfile.Mulakat, new double[6], new double[6], windows));
        var chunks = new[] { Chunk(0, 10) };
        vm.SetFaceContext(chunks, new Dictionary<int, string>());
        var speakers = new[] { new SpeakerSegment(A, TimeSpan.Zero, TimeSpan.FromSeconds(10)) };
        vm.ApplySpeakers(new[] { new TranscriptSegment("merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(10)) }, speakers, 0.5);
        vm.ApplySpeakerEmotions(chunks, speakers, 0.5, Array.Empty<SpeakerProsodySummary>());

        vm.SpeakerEmotions[0].SelectedFace = Option(vm.SpeakerEmotions[0], 0);

        Assert.Contains("çok küçük", vm.SpeakerEmotions[0].FaceNote);
    }

    /// <summary>B'nin yalnızca kısa bölgeleri var: hiçbir parçayı kazanamaz, dolayısıyla duygu kartı çıkmaz.</summary>
    private static ReportViewModel BuildBWithoutCard(IReadOnlyDictionary<int, string> names)
    {
        var vm = new ReportViewModel(new FakeAggregator());
        var windows = new[] { Window(0, Face(0, 0.9), Face(1, -0.9)) };
        vm.LoadDecisions(new DecisionSession(MeetingProfile.Mulakat, new double[6], new double[6], windows));
        var chunks = new[] { Chunk(0, 10) };
        vm.SetFaceContext(chunks, names);
        var speakers = new[]
        {
            new SpeakerSegment(A, TimeSpan.Zero, TimeSpan.FromSeconds(8)),
            new SpeakerSegment(B, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(10)),
        };
        var transcript = new[]
        {
            new TranscriptSegment("merhaba nasılsın", TimeSpan.Zero, TimeSpan.FromSeconds(8)),
            new TranscriptSegment("iyiyim", TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(10)),
        };
        vm.ApplySpeakers(transcript, speakers, 0.5);
        vm.ApplySpeakerEmotions(chunks, speakers, 0.5, Array.Empty<SpeakerProsodySummary>());
        return vm;
    }

    [Fact]
    public void EslemeSatirlari_DuyguKartiOlmayanKonusmaciIcinDeVar()
    {
        var vm = BuildBWithoutCard(new Dictionary<int, string>());

        Assert.Single(vm.SpeakerEmotions);                       // B'nin kartı yok
        Assert.Equal(new[] { 0, 1 }, vm.FaceMappings.Select(r => r.SpeakerIndex));
        Assert.Equal(new[] { "Konuşmacı A", "Konuşmacı B" }, vm.FaceMappings.Select(r => r.Label));
    }

    [Fact]
    public void KartiOlmayanKonusmaciyaYuzSecilir_AdSatirdaVePayda_GorunurAd()
    {
        var vm = BuildBWithoutCard(new Dictionary<int, string> { [0] = "betül", [1] = "yeşim" });

        var rowB = vm.FaceMappings.Single(r => r.SpeakerIndex == 1);
        rowB.SelectedFace = rowB.FaceOptions.Single(o => o.FaceIndex == 1);

        Assert.Equal("yeşim", vm.SpeakerLines.Single(l => l.SpeakerIndex == 1).Label);
        Assert.Equal("yeşim", vm.TalkBalance.Single(t => t.SpeakerIndex == 1).Label);
        Assert.Equal("yeşim", vm.FaceMappings.Single(r => r.SpeakerIndex == 1).Label);
        Assert.Equal("Konuşmacı A", vm.SpeakerLines.Single(l => l.SpeakerIndex == 0).Label);
    }

    [Fact]
    public void EslemeSatiri_SecimKorunur_VeKartlaTutarli()
    {
        var vm = BuildBWithoutCard(new Dictionary<int, string> { [0] = "betül" });

        var rowA = vm.FaceMappings.Single(r => r.SpeakerIndex == 0);
        rowA.SelectedFace = rowA.FaceOptions.Single(o => o.FaceIndex == 0);

        Assert.Equal(0, vm.FaceMappings.Single(r => r.SpeakerIndex == 0).SelectedFace!.FaceIndex);
        Assert.Equal(0, vm.SpeakerEmotions.Single().SelectedFace!.FaceIndex);
        Assert.True(vm.SpeakerEmotions.Single().HasFaceNote);
    }

    [Fact]
    public void EslemeSatiri_YuzHicGorulmediyseYok()
    {
        var vm = new ReportViewModel(new FakeAggregator());
        var speakers = new[] { new SpeakerSegment(A, TimeSpan.Zero, TimeSpan.FromSeconds(10)) };
        vm.ApplySpeakers(new[] { new TranscriptSegment("merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(10)) }, speakers, 0.5);

        Assert.Empty(vm.FaceMappings);
        Assert.False(vm.HasFaceMappings);
    }

    [Fact]
    public void Yonlendirme_KartOlmasaBileSatirlerdenTureyer()
    {
        var vm = BuildBWithoutCard(new Dictionary<int, string>());

        Assert.Contains("Bu kişinin yüzü", vm.FaceMappingHint);
    }
}
