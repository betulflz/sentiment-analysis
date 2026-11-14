using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Decision;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Tests.Decision;

public class TalkMeterTests
{
    private static readonly FusedResult F = new(new ValenceArousal(0, 0.5), Emotion.Notr, Emotion.Notr, Confidence.Orta, true, false,
        IncongruencePattern.Yok, 1, new ValenceArousal(0, 0), new ValenceArousal(0, 0), 0.7, 0.7);

    private static TimeSpan S(double x) => TimeSpan.FromSeconds(x);

    /// <summary>[start, start+10) parçası; <paramref name="hostUntil"/>'e kadar yöneten, sonra karşı taraf konuşur.</summary>
    private static ChunkResult Chunk(double start, double hostUntil, string hostText = "", string otherText = "")
    {
        var spans = new List<RoleSpan>();
        if (hostUntil > start) spans.Add(new(S(start), S(hostUntil), SpeakerRole.Yoneten, 0.6));
        if (hostUntil < start + 10) spans.Add(new(S(Math.Max(start, hostUntil)), S(start + 10), SpeakerRole.KarsiTaraf, 0.2));
        var segs = new List<TranscriptSegment>();
        if (hostText.Length > 0) segs.Add(new(hostText, S(start), S(hostUntil)));
        if (otherText.Length > 0) segs.Add(new(otherText, S(hostUntil), S(start + 10)));
        return new ChunkResult(S(start), S(start + 10), F, hostText + " " + otherText, segs)
        { Roles = new RoleTimeline(spans, true, false) };
    }

    [Fact]
    public void RolsuzParca_Bilinmiyor()
    {
        var m = new TalkMeter(new DecisionConfig());

        var t = m.Add(new ChunkResult(S(0), S(10), F, "x"));

        Assert.Null(t.HostShare);
        Assert.Null(t.HostWordsPerSecond);
    }

    [Fact]
    public void AzKonusmada_PayBilinmiyor_YeterinceKonusulunca_Hesaplanir()
    {
        var m = new TalkMeter(new DecisionConfig());

        Assert.Null(m.Add(Chunk(0, 8)).HostShare);            // 10 sn < 15 sn (0–8 yöneten, 8–10 karşı taraf)
        var t = m.Add(Chunk(8, 18));                            // 8–18 yöneten; 8–10 zaten sayıldı → yalnız 10–18 yeni

        Assert.Equal(18, t.HostSeconds + t.CounterpartSeconds, 3);
        Assert.Equal(16.0 / 18, t.HostShare!.Value, 3);
    }

    [Fact]
    public void Ortusme_IkiKezSayilmaz()
    {
        var m = new TalkMeter(new DecisionConfig());
        m.Add(Chunk(0, 10));

        var t = m.Add(Chunk(8, 8));                             // 8–18 tamamen karşı taraf; 8–10 zaten sayıldı

        Assert.Equal(10, t.HostSeconds, 3);
        Assert.Equal(8, t.CounterpartSeconds, 3);
    }

    [Fact]
    public void Pencere_Kayar_EskiKonusmaDuser()
    {
        var m = new TalkMeter(new DecisionConfig(TalkWindowSeconds: 20));
        m.Add(Chunk(0, 10));                                     // yöneten
        m.Add(Chunk(10, 10));                                    // karşı taraf

        var t = m.Add(Chunk(20, 20));                            // karşı taraf; 0–10 penceren çıktı

        Assert.Equal(0, t.HostShare!.Value, 3);
    }

    [Fact]
    public void YonetenHizi_KendiSozuVeSuresinden()
    {
        var m = new TalkMeter(new DecisionConfig());

        var t = m.Add(Chunk(0, 4, hostText: "bir iki üç dört beş altı yedi sekiz dokuz on on bir on iki", otherText: "evet"));

        Assert.Equal(14.0 / 4, t.HostWordsPerSecond!.Value, 3);  // 14 kelime / 4 sn (karşı tarafın "evet"i sayılmaz)
    }

    [Fact]
    public void Reset_SifirlarVeSayaciBasaAlir()
    {
        var m = new TalkMeter(new DecisionConfig());
        m.Add(Chunk(0, 10)); m.Add(Chunk(10, 20));

        m.Reset();
        var t = m.Add(Chunk(0, 0));

        Assert.Equal(0, t.HostSeconds, 3);
        Assert.Equal(10, t.CounterpartSeconds, 3);
    }
}

public class RoleOfTests
{
    private static TimeSpan S(double x) => TimeSpan.FromSeconds(x);

    [Fact]
    public void Esitlik_Belirsiz_CogunlukKazanir()
    {
        var t = new RoleTimeline(new[]
        {
            new RoleSpan(S(0), S(2), SpeakerRole.Yoneten, 0.6),
            new RoleSpan(S(2), S(4), SpeakerRole.KarsiTaraf, 0.2),
        }, true, false);

        Assert.Equal(SpeakerRole.Belirsiz, t.RoleOf(S(1), S(3)));     // 1 sn – 1 sn
        Assert.Equal(SpeakerRole.KarsiTaraf, t.RoleOf(S(1), S(4)));
        Assert.Equal(SpeakerRole.Yoneten, t.RoleOf(S(0), S(3)));
        Assert.Equal(SpeakerRole.Belirsiz, t.RoleOf(S(5), S(6)));     // konuşma yok
    }
}
