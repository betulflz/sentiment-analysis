using System;
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class ReportViewModelTests
{
    private sealed class FakeAggregator : IMeetingAggregator
    {
        public MeetingSummary Aggregate(MeetingSession s) => new(
            Emotion.Mutlu,
            new Dictionary<Emotion, double> { [Emotion.Mutlu] = 0.6, [Emotion.Uzgun] = 0.4 },
            ValenceTrend: 0.25, EmotionalVolatility: 0.1,
            PeakMoments: s.Chunks.Take(1).ToList(),
            AverageVa: new ValenceArousal(0.2, 0.5), AverageCongruence: 0.8,
            TurkishSummary: "Toplantı olumlu geçti.") { AnalyzedDuration = TimeSpan.FromSeconds(100) };
    }

    private static MeetingSession Session() => new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, DetectedLanguage.Turkish,
        new[] { new ChunkResult(TimeSpan.Zero, TimeSpan.FromSeconds(1),
            new FusedResult(new ValenceArousal(0.5,0.6), Emotion.Mutlu, Emotion.Notr, Confidence.Yuksek,
                true, false, IncongruencePattern.Yok, 0.9,
                TextVa: new ValenceArousal(0.5, 0.6), ProsodyVa: new ValenceArousal(0.5, 0.6),
                TextConfidence: 0.5, ProsodyConfidence: 0.5), "merhaba") });

    [Fact]
    public void Load_ProducesLocalizedReport()
    {
        var vm = new ReportViewModel(new FakeAggregator());
        Assert.False(vm.HasReport);

        vm.Load(Session());

        Assert.True(vm.HasReport);
        Assert.Equal("Mutlu", vm.DominantEmotion);
        Assert.Equal("Toplantı olumlu geçti.", vm.TurkishSummary);
        Assert.Equal(0.25, vm.ValenceTrend, precision: 5);
        Assert.Equal(2, vm.TimeShare.Count);
        Assert.Contains(vm.TimeShare, s => s.Emotion == "Mutlu" && Math.Abs(s.Share - 0.6) < 1e-9);
        Assert.Single(vm.PeakMoments);
        Assert.Equal("merhaba", vm.PeakMoments[0].Transcript);
    }

    [Fact]
    public void DuyguHaritasi_BuyuktenKucugeSureVeOzet()
    {
        var vm = new ReportViewModel(new FakeAggregator());
        vm.Load(Session());

        Assert.Equal(new[] { "Mutlu", "Üzgün" }, vm.TimeShare.Select(t => t.Emotion));
        Assert.Equal(Emotion.Mutlu, vm.TimeShare[0].Key);
        Assert.Equal("%60 · yaklaşık 1 dk 0 sn", vm.TimeShare[0].Text);
        Assert.Equal("%40 · yaklaşık 40 sn", vm.TimeShare[1].Text);
        Assert.Equal("Konuşmanın en büyük kısmı (%60) mutlu bir havada geçti.", vm.EmotionMapSummary);
    }

    [Fact]
    public void DuyguHaritasi_SureYoksaYalnizYuzde()
    {
        var vm = new ReportViewModel(new FakeAggregator());
        vm.LoadSummary(new FakeAggregator().Aggregate(Session()) with { AnalyzedDuration = TimeSpan.Zero });
        Assert.Equal("%60", vm.TimeShare[0].Text);
    }

    // --- 10b: konuşmacı etiketli transkript ---

    private static TranscriptSegment T(string text, double s, double e) =>
        new(text, TimeSpan.FromSeconds(s), TimeSpan.FromSeconds(e));

    private static SpeakerSegment S(int spk, double s, double e) =>
        new(new SpeakerId(spk), TimeSpan.FromSeconds(s), TimeSpan.FromSeconds(e));

    [Fact]
    public void ApplySpeakers_ArdisikAyniKonusmaci_TekSatirdaBirlesir()
    {
        var vm = new ReportViewModel(new FakeAggregator());

        vm.ApplySpeakers(
            [T("Merhaba", 0, 2), T("nasılsın", 2, 4), T("iyiyim", 4, 6)],
            [S(0, 0, 4), S(1, 4, 6)],
            minOverlapRatio: 0.5);

        Assert.Equal(2, vm.SpeakerLines.Count);
        Assert.Equal("Konuşmacı A", vm.SpeakerLines[0].Label);
        Assert.Equal("Merhaba nasılsın", vm.SpeakerLines[0].Text);
        Assert.Equal("Konuşmacı B", vm.SpeakerLines[1].Label);
        Assert.Equal("iyiyim", vm.SpeakerLines[1].Text);
        // Sohbet balonu: harf rozeti, konuşmacı rengi; A solda, B sağda.
        Assert.Equal("A", vm.SpeakerLines[0].Initial);
        Assert.Equal(0, vm.SpeakerLines[0].SpeakerIndex);
        Assert.False(vm.SpeakerLines[0].IsRight);
        Assert.Equal("B", vm.SpeakerLines[1].Initial);
        Assert.True(vm.SpeakerLines[1].IsRight);
        Assert.True(vm.HasSpeakers);
    }

    [Fact]
    public void ApplySpeakers_KonusmaciBolgesiYok_BilinmeyenEtiketi()
    {
        var vm = new ReportViewModel(new FakeAggregator());

        vm.ApplySpeakers([T("mırıltı", 0, 2)], [], minOverlapRatio: 0.5);

        Assert.Single(vm.SpeakerLines);
        Assert.Equal("Bilinmeyen", vm.SpeakerLines[0].Label);
        Assert.Equal("?", vm.SpeakerLines[0].Initial);
    }

    [Fact]
    public void SetDiarizationOutcome_Basarisiz_DurustcuBildirir_SatirUretmez()
    {
        var vm = new ReportViewModel(new FakeAggregator());

        vm.SetDiarizationOutcome(DiarizationOutcome.Failed);

        Assert.Contains("başarısız", vm.DiarizationStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(vm.SpeakerLines);          // uydurma etiket gösterme
        Assert.False(vm.HasSpeakers);
    }

    [Fact]
    public void SetDiarizationOutcome_SesYok_NedeniniSoyler()
    {
        var vm = new ReportViewModel(new FakeAggregator());

        vm.SetDiarizationOutcome(DiarizationOutcome.NoAudio);

        Assert.False(string.IsNullOrWhiteSpace(vm.DiarizationStatus));
        Assert.Contains("ses", vm.DiarizationStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(vm.SpeakerLines);
    }

    [Fact]
    public void SetDiarizationOutcome_Tamamlandi_AmaKonusmaciYok_AyirtEdilemediDer()
    {
        // "Çalıştı ama ayırt edemedi" ile "hiç çalışmadı" farklı şeylerdir (bkz. DiarizationJob).
        var vm = new ReportViewModel(new FakeAggregator());

        vm.ApplySpeakers([T("tek kişi", 0, 5)], [], minOverlapRatio: 0.5);
        vm.SetDiarizationOutcome(DiarizationOutcome.Completed);

        Assert.NotEqual(vm.DiarizationStatus, string.Empty);
        Assert.DoesNotContain("başarısız", vm.DiarizationStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiarizationStatus_Baslangicta_Bos_SeritGorunmez()
    {
        // Özellik kapalıyken kullanıcıya hiçbir şey gösterilmemeli (davranış değişmez).
        var vm = new ReportViewModel(new FakeAggregator());

        Assert.Equal(string.Empty, vm.DiarizationStatus);
        Assert.False(vm.HasSpeakers);
    }

    [Fact]
    public void SetDiarizationOutcome_Calisiyor_HesaplaniyorDer()
    {
        var vm = new ReportViewModel(new FakeAggregator());

        vm.SetDiarizationRunning();

        Assert.Contains("hesaplanıyor", vm.DiarizationStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplySpeakers_BelirsizAtama_SatiraYansir()
    {
        // Eşik-altı örtüşme: cümle en iyi adaya atanır ama UI'ın uyarabilmesi için
        // belirsizlik satıra kadar taşınmalıdır.
        var vm = new ReportViewModel(new FakeAggregator());
        var transcript = new[] { new TranscriptSegment("merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(10)) };
        var speakers = new[] { new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(3)) };

        vm.ApplySpeakers(transcript, speakers, minOverlapRatio: 0.5);

        Assert.Single(vm.SpeakerLines);
        Assert.Equal("Konuşmacı A", vm.SpeakerLines[0].Label);
        Assert.True(vm.SpeakerLines[0].Uncertain);
    }

    // --- 11e: karar bölümleri ---

    private static DecisionWindow DW(double start, double k1, LiveAction shown) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(start + 10),
            new Indicators(new[] { k1, 0, 0, 0, 0, 0 }, new int?[6], 0.8),
            new Recommendation(shown, 0.6, Array.Empty<Ranked<LiveAction>>(), Array.Empty<Criterion>(), null,
                shown == LiveAction.DevamEt ? Confidence.Orta : Confidence.Dusuk, $"neden {start}"));

    private static DecisionSession DecisionData(params DecisionWindow[] windows) =>
        new(MeetingProfile.Satis, Application.Decision.DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis),
            Application.Decision.DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis), windows);

    private static DecisionSession ThreeWindows() => DecisionData(
        DW(0, 0.1, LiveAction.DevamEt), DW(10, 0.8, LiveAction.EmpatiKur), DW(20, 0.1, LiveAction.DevamEt));

    [Fact]
    public void LoadDecisions_ZamanCizelgesi6SatirPencereSayisiKadarDeger()
    {
        var vm = new ReportViewModel(new FakeAggregator());
        vm.LoadDecisions(ThreeWindows());

        Assert.True(vm.HasDecisionData);
        Assert.True(vm.FollowUp.HasDecision);
        Assert.Equal(6, vm.IndicatorTimeline.Count);
        Assert.Equal("Gerilim", vm.IndicatorTimeline[0].Name);
        Assert.Equal(new[] { 0.1, 0.8, 0.1 }, vm.IndicatorTimeline[0].Values);
        Assert.Equal("00:00–00:30", vm.TimelineRange);
        Assert.Equal("00:00", vm.TimelineStart);
        Assert.Equal("00:30", vm.TimelineEnd);
    }

    [Fact]
    public void LoadDecisions_OneriDegisimleriVeKritikAnlar()
    {
        var vm = new ReportViewModel(new FakeAggregator());
        vm.LoadDecisions(ThreeWindows());

        Assert.Equal(3, vm.Recommendations.Count);
        var r = vm.Recommendations[1];
        Assert.Equal("00:10", r.Time);
        Assert.Equal("Empati kur, sakinleştir", r.Action);
        Assert.Equal("neden 10", r.Reason);
        Assert.True(r.IsLowConfidence);

        var m = Assert.Single(vm.CriticalMoments);
        Assert.Equal("00:10–00:20", m.TimeRange);
        Assert.Equal("Gerilim %64 — Görüşme geneli · gösterilen öneri: Empati kur, sakinleştir", m.Text);
    }

    [Fact]
    public void LoadDecisions_PencereYok_HasDecisionDataFalse()
    {
        var vm = new ReportViewModel(new FakeAggregator());
        vm.LoadDecisions(ThreeWindows());
        vm.LoadDecisions(DecisionData());

        Assert.False(vm.HasDecisionData);
        Assert.False(vm.FollowUp.HasDecision);
        Assert.Empty(vm.CriticalMoments);
    }

    private static SpeakerSegment Seg(int speaker, double start, double end) =>
        new(new SpeakerId(speaker), TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end));

    [Fact]
    public void ApplySpeakers_KonusmaDengesiDoldurulur()
    {
        var vm = new ReportViewModel(new FakeAggregator());
        vm.ApplySpeakers(Array.Empty<TranscriptSegment>(), new[] { Seg(0, 0, 60), Seg(1, 60, 90) }, 0.5);

        Assert.Equal(2, vm.TalkBalance.Count);
        Assert.Equal("Konuşmacı A", vm.TalkBalance[0].Label);
        Assert.Equal("1 dk 0 sn · %67", vm.TalkBalance[0].Text);
        Assert.Equal(2 / 3.0, vm.TalkBalance[0].Share, 9);
        Assert.Equal("30 sn · %33", vm.TalkBalance[1].Text);
        Assert.Equal(0, vm.TalkBalance[0].SpeakerIndex);
        Assert.Equal(1, vm.TalkBalance[1].SpeakerIndex);
    }

    [Fact]
    public void SetDiarizationOutcome_Basarisiz_KonusmaDengesiTemizlenir()
    {
        var vm = new ReportViewModel(new FakeAggregator());
        vm.ApplySpeakers(Array.Empty<TranscriptSegment>(), new[] { Seg(0, 0, 60) }, 0.5);
        vm.SetDiarizationOutcome(DiarizationOutcome.Failed);
        Assert.Empty(vm.TalkBalance);
    }

    // --- 11e final inceleme ---

    [Fact]
    public void LoadDecisions_HasCriticalMoments_ListeyleUyumlu()
    {
        var vm = new ReportViewModel(new FakeAggregator());
        vm.LoadDecisions(ThreeWindows());
        Assert.True(vm.HasCriticalMoments);

        vm.LoadDecisions(DecisionData(DW(0, 0.1, LiveAction.DevamEt)));
        Assert.Empty(vm.CriticalMoments);
        Assert.False(vm.HasCriticalMoments);
    }

    [Fact]
    public void LoadSummary_OncekiGorusmeninKonusmaciVerisiniTemizler()
    {
        // Konuşmacı ayrımı sonraki koşuda kapalıysa geçiş hiç çalışmaz — eski görüşmenin satırları kalmamalı.
        var vm = new ReportViewModel(new FakeAggregator());
        vm.ApplySpeakers(Array.Empty<TranscriptSegment>(), new[] { Seg(0, 0, 60), Seg(1, 60, 90) }, 0.5);
        vm.SetDiarizationOutcome(DiarizationOutcome.Completed);

        vm.LoadSummary(new MeetingSummary(Emotion.Notr, new Dictionary<Emotion, double>(), 0, 0,
            Array.Empty<ChunkResult>(), ValenceArousal.Neutral, 1.0, "özet"));

        Assert.Empty(vm.TalkBalance);
        Assert.Empty(vm.SpeakerLines);
        Assert.Empty(vm.SpeakerEmotions);
        Assert.False(vm.HasSpeakers);
        Assert.Equal(string.Empty, vm.DiarizationStatus);
    }
}
