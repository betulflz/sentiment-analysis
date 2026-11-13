using System;
using System.Linq;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

public class DiarizationRoleLabelerTests
{
    private static readonly SpeakerId A = new(0), B = new(1), C = new(2);
    private static SpeakerSegment Seg(SpeakerId s, double a, double b) => new(s, TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b));
    private static AudioChunk Chunk(double a, double b) =>
        new(new float[(int)((b - a) * 16000)], 16000, TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b));

    [Fact]
    public void HostYoneten_DigerleriKarsiTaraf()
    {
        var t = new DiarizationRoleLabeler(new[] { Seg(A, 0, 4), Seg(B, 4, 9) }, A).Label(Chunk(0, 10));
        Assert.True(t.Enrolled);
        Assert.False(t.EnrollmentDoubtful);
        Assert.Equal(new[] { SpeakerRole.Yoneten, SpeakerRole.KarsiTaraf }, t.Spans.Select(s => s.Role));
        Assert.All(t.Spans, s => Assert.Equal(1.0, s.Similarity));
    }

    [Fact]
    public void Unknown_Atlanir()
    {
        var t = new DiarizationRoleLabeler(new[] { Seg(A, 0, 4), Seg(SpeakerId.Unknown, 4, 6), Seg(B, 6, 9) }, A).Label(Chunk(0, 10));
        Assert.Equal(2, t.Spans.Count);
        Assert.DoesNotContain(t.Spans, s => s.Start == TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void ParcaSinirindaKirpilir()
    {
        var t = new DiarizationRoleLabeler(new[] { Seg(B, 8, 14) }, A).Label(Chunk(10, 20));
        var s = Assert.Single(t.Spans);
        Assert.Equal(TimeSpan.FromSeconds(10), s.Start);
        Assert.Equal(TimeSpan.FromSeconds(14), s.End);
    }

    [Fact]
    public void KesisimYok_BosAmaEnrolled()
    {
        var t = new DiarizationRoleLabeler(new[] { Seg(B, 30, 40) }, A).Label(Chunk(0, 10));
        Assert.Empty(t.Spans);
        Assert.True(t.Enrolled);
    }

    [Fact]
    public void UcKisi_HostDisindakilerKarsiTaraf()
    {
        var t = new DiarizationRoleLabeler(new[] { Seg(A, 0, 2), Seg(B, 2, 5), Seg(C, 5, 8) }, B).Label(Chunk(0, 10));
        Assert.Equal(new[] { SpeakerRole.KarsiTaraf, SpeakerRole.Yoneten, SpeakerRole.KarsiTaraf }, t.Spans.Select(s => s.Role));
    }

    [Fact]
    public void AyrimSinirindanSonra_RolsuzKarisikYol()
    {
        // Uzun dosya: ayrım ilk 20 sn'yi kapsadı (sınır); sonrası rolsüz → karışık yol + kısmi not.
        var l = new DiarizationRoleLabeler(new[] { Seg(A, 0, 10), Seg(B, 10, 20) }, A, TimeSpan.FromSeconds(20));
        Assert.True(l.Label(Chunk(10, 20)).Enrolled);
        var past = l.Label(Chunk(20, 30));
        Assert.False(past.Enrolled);
        Assert.Empty(past.Spans);
    }
}
