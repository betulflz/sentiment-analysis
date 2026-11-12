using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

/// <summary>12: satırda kimin konuştuğu ve karşı tarafın duygusu.</summary>
public class ChunkRowRoleTests
{
    private static FusedResult F(Emotion e) => new(new ValenceArousal(0, 0.5), e, Emotion.Notr, Confidence.Orta,
        true, false, IncongruencePattern.Yok, 1, ValenceArousal.Neutral, ValenceArousal.Neutral, 0, 0);

    private static ChunkResult Chunk(double hostSeconds, FusedResult? counterpart)
    {
        var spans = new List<RoleSpan>();
        if (hostSeconds > 0) spans.Add(new(TimeSpan.Zero, TimeSpan.FromSeconds(hostSeconds), SpeakerRole.Yoneten, 0.6));
        if (hostSeconds < 10) spans.Add(new(TimeSpan.FromSeconds(hostSeconds), TimeSpan.FromSeconds(10), SpeakerRole.KarsiTaraf, 0.2));
        return new ChunkResult(TimeSpan.Zero, TimeSpan.FromSeconds(10), F(Emotion.Kizgin), "x")
        { Roles = new RoleTimeline(spans, true, false), Counterpart = counterpart };
    }

    [Fact]
    public void RolYok_EtiketYok_DuyguGenel()
    {
        var row = new ChunkRowVm(new ChunkResult(TimeSpan.Zero, TimeSpan.FromSeconds(10), F(Emotion.Kizgin), "x"));

        Assert.False(row.HasWho);
        Assert.Equal("Kızgın", row.Emotion);
    }

    [Theory]
    [InlineData(9.0, "Siz")]
    [InlineData(1.0, "Karşı taraf")]
    [InlineData(5.0, "İkiniz")]
    public void KimKonustu(double host, string expected) =>
        Assert.Equal(expected, new ChunkRowVm(Chunk(host, F(Emotion.Sakin))).Who);

    [Fact]
    public void KarsiTarafKonustuysa_DuyguOnun()
    {
        var row = new ChunkRowVm(Chunk(5, F(Emotion.Uzgun)));

        Assert.Equal("Üzgün", row.Emotion);   // genel (yönetenin baskın) "Kızgın" değil
    }
}

public class DecisionPanelTalkShareTests
{
    private static ChunkResult Chunk(double start, double hostUntil)
    {
        var f = new FusedResult(new ValenceArousal(0, 0.5), Emotion.Notr, Emotion.Notr, Confidence.Orta,
            true, false, IncongruencePattern.Yok, 1, ValenceArousal.Neutral, ValenceArousal.Neutral, 0.7, 0.7);
        var spans = new List<RoleSpan>
        {
            new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(hostUntil), SpeakerRole.Yoneten, 0.6),
            new(TimeSpan.FromSeconds(hostUntil), TimeSpan.FromSeconds(start + 10), SpeakerRole.KarsiTaraf, 0.2),
        };
        return new ChunkResult(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(start + 10), f, "x")
        { Roles = new RoleTimeline(spans, true, false), Counterpart = f };
    }

    [Fact]
    public void KonusmaPayi_YeterinceKonusulunca_Gorunur_YeniGorusmedeSifirlanir()
    {
        var vm = new DecisionPanelViewModel(new SentimentAnalysis.Application.Configuration.DecisionConfig());
        vm.OnWindow(Chunk(0, 7), Array.Empty<FaceWindowSummary>());
        Assert.False(vm.HasTalkShare);                     // 10 sn < 15 sn

        vm.OnWindow(Chunk(10, 17), Array.Empty<FaceWindowSummary>());

        Assert.Equal("Siz %70 · Karşı taraf %30", vm.TalkShareText);
        Assert.Equal(0.7, vm.HostShare, 3);
        vm.Start();
        Assert.False(vm.HasTalkShare);
    }
}
