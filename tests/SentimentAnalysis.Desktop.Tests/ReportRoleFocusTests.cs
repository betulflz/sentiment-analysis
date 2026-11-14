using System;
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

/// <summary>Karşı tarafa odak: rapor rol notu ve (dosya modunda) yönetenin kartında yalnız davranış.</summary>
public class ReportRoleFocusTests
{
    private sealed class FakeAggregator : IMeetingAggregator
    {
        public MeetingSummary Aggregate(MeetingSession s) => new(
            Emotion.Notr, new Dictionary<Emotion, double>(), 0, 0,
            Array.Empty<ChunkResult>(), ValenceArousal.Neutral, 1.0, "özet");
    }

    private static readonly SpeakerId A = new(0), B = new(1);
    private static readonly RoleTimeline Enrolled = new(Array.Empty<RoleSpan>(), true, false);

    private static ChunkResult Chunk(double a, double b, string text = "merhaba") =>
        new(TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b),
            new FusedResult(new ValenceArousal(0, 0.5), Emotion.Notr, Emotion.Notr, Confidence.Orta,
                true, false, IncongruencePattern.Yok, 1.0,
                TextVa: new ValenceArousal(0, 0.5), ProsodyVa: new ValenceArousal(0, 0.5),
                TextConfidence: 0.8, ProsodyConfidence: 0.8),
            text);

    [Fact]
    public void RolNotu_Karisik_Kismi_Tam_Bos()
    {
        var vm = new ReportViewModel(new FakeAggregator());
        vm.SetRoleContext(new[] { Chunk(0, 10), Chunk(10, 20) });
        Assert.Equal("Yöneten ayrılmadı — sonuçlar iki tarafın karışık sesinden.", vm.RoleNotice);

        vm.SetRoleContext(new[] { Chunk(0, 10) with { Roles = Enrolled }, Chunk(10, 20) });
        Assert.Equal("Yöneten yalnız kısmen ayrıldı — bazı bölümler karışık sesten.", vm.RoleNotice);

        vm.SetRoleContext(new[] { Chunk(0, 10) with { Roles = Enrolled }, Chunk(10, 20) with { Roles = Enrolled }, Chunk(20, 30, "") });
        Assert.Equal(string.Empty, vm.RoleNotice);
        Assert.False(vm.HasRoleNotice);

        vm.SetRoleContext(Array.Empty<ChunkResult>());
        Assert.Equal(string.Empty, vm.RoleNotice);
    }

    [Fact]
    public void YonetenKarti_DuygusuzDavranisli_DigerKartNormal()
    {
        var vm = new ReportViewModel(new FakeAggregator());
        var chunks = new[] { Chunk(0, 30), Chunk(30, 40) };
        var speakers = new[]
        {
            new SpeakerSegment(A, TimeSpan.Zero, TimeSpan.FromSeconds(30)),
            new SpeakerSegment(B, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(40)),
        };
        var transcript = new[]
        {
            new TranscriptSegment(string.Join(" ", Enumerable.Repeat("kelime", 60)), TimeSpan.Zero, TimeSpan.FromSeconds(30)),
            new TranscriptSegment("iyiyim sağ ol", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(40)),
        };
        vm.SetHost(A);
        vm.ApplySpeakers(transcript, speakers, 0.5);
        vm.ApplySpeakerEmotions(chunks, speakers, 0.5, Array.Empty<SpeakerProsodySummary>());

        var host = vm.SpeakerEmotions.Single(s => s.SpeakerIndex == 0);
        var other = vm.SpeakerEmotions.Single(s => s.SpeakerIndex == 1);
        Assert.True(host.IsHost);
        Assert.False(host.ShowEmotion);
        Assert.Equal("Görüşmeyi yöneten (siz) · konuşma payı %75 · dakikada 120 kelime", host.HostBehaviorText);
        Assert.False(other.IsHost);
        Assert.True(other.ShowEmotion);
    }

    [Fact]
    public void HostYoksa_TumKartlarNormal()
    {
        var vm = new ReportViewModel(new FakeAggregator());
        var chunks = new[] { Chunk(0, 10), Chunk(10, 20) };
        var speakers = new[] { new SpeakerSegment(A, TimeSpan.Zero, TimeSpan.FromSeconds(10)), new SpeakerSegment(B, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)) };
        vm.ApplySpeakers(new[] { new TranscriptSegment("a", TimeSpan.Zero, TimeSpan.FromSeconds(10)) }, speakers, 0.5);
        vm.ApplySpeakerEmotions(chunks, speakers, 0.5, Array.Empty<SpeakerProsodySummary>());
        Assert.All(vm.SpeakerEmotions, s => Assert.True(s.ShowEmotion));
    }
}
