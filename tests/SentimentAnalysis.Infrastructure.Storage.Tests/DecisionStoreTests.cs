using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Storage;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Storage.Tests;

/// <summary>11e: oturumun karar verisi — pencere göstergeleri, gösterilen öneriler, profil, ağırlıklar, takip kararı.</summary>
public class DecisionStoreTests
{
    private static SqliteSessionStore NewStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cfg = AppConfig.Default with { Storage = AppConfig.Default.Storage with { StorageDirectory = dir } };
        return new SqliteSessionStore(cfg, new EncryptedAudioStore(cfg, new FileKeyProvider(cfg)));
    }

    private static readonly double[] ProfileW = { 0.3, 0.1, 0.25, 0.15, 0.1, 0.1 };
    private static readonly double[] FinalW = { 0.5, 0, 0.2, 0.1, 0.1, 0.1 };

    private static DecisionWindow WithFaces() =>
        new DecisionWindow(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(10),
            new Indicators(new[] { 0.7, 0.2, 0.5, 0.1, 0.3, 0 }, new int?[] { 1, 0, null, null, null, null }, 0.82),
            new Recommendation(LiveAction.EmpatiKur, 0.64,
                new[] { new Ranked<LiveAction>(LiveAction.EmpatiKur, 0.64, new[] { Criterion.Gerilim, Criterion.OlumsuzDuygu }) },
                new[] { Criterion.Gerilim, Criterion.OlumsuzDuygu }, 1, Confidence.Yuksek, "Yüz 2: gerilim %70."))
        {
            Faces = new[]
            {
                new FaceWindowSummary(0, 0.9, 0.2, -0.1, 0.05, 0.3, 0.8),
                new FaceWindowSummary(1, 0.6, 0.0, -0.4, 0.35, 0.0, 0.5),
            },
        };

    private static DecisionWindow Faceless(double start = 10) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(start + 10),
            new Indicators(new[] { 0.1, 0, 0.1, 0, 0.2, 0 }, new int?[6], 0.5),
            new Recommendation(LiveAction.DevamEt, 1, Array.Empty<Ranked<LiveAction>>(), Array.Empty<Criterion>(),
                null, Confidence.Orta, "Belirgin bir sorun yok."));

    private static DecisionSessionRecord Record(params DecisionWindow[] windows) =>
        new(new DecisionSession(MeetingProfile.Mulakat, ProfileW, FinalW, windows),
            FollowUpAction.EkGorusme, "Yüz 2: gerilim %70.");

    [Fact]
    public async Task Save_Get_RoundTrip()
    {
        var store = NewStore();
        var id = Guid.NewGuid();
        var saved = Record(WithFaces(), Faceless());
        await store.SaveDecisionSessionAsync(id, saved, CancellationToken.None);

        var got = await store.GetDecisionSessionAsync(id, CancellationToken.None);

        Assert.NotNull(got);
        Assert.Equal(FollowUpAction.EkGorusme, got!.FollowUp);
        Assert.Equal(saved.FollowUpReason, got.FollowUpReason);
        var s = got.Session;
        Assert.Equal(MeetingProfile.Mulakat, s.Profile);
        Assert.Equal(ProfileW, s.ProfileWeights.ToArray(), new ToleranceComparer());
        Assert.Equal(FinalW, s.FinalWeights.ToArray(), new ToleranceComparer());
        Assert.Equal(2, s.Windows.Count);

        for (int i = 0; i < 2; i++)
        {
            var (e, a) = (saved.Session.Windows[i], s.Windows[i]);
            Assert.Equal(e.Start, a.Start);
            Assert.Equal(e.End, a.End);
            Assert.Equal(e.Indicators.Values.ToArray(), a.Indicators.Values.ToArray(), new ToleranceComparer());
            Assert.Equal(e.Indicators.SourceFace.ToArray(), a.Indicators.SourceFace.ToArray());
            Assert.Equal(e.Indicators.DataQuality, a.Indicators.DataQuality, 9);
            Assert.Equal(e.Recommendation.Action, a.Recommendation.Action);
            Assert.Equal(e.Recommendation.Closeness, a.Recommendation.Closeness, 9);
            Assert.Equal(e.Recommendation.Confidence, a.Recommendation.Confidence);
            Assert.Equal(e.Recommendation.Face, a.Recommendation.Face);
            Assert.Equal(e.Recommendation.TopCriteria, a.Recommendation.TopCriteria);
            Assert.Equal(e.Recommendation.Reason, a.Recommendation.Reason);
            Assert.Equal(e.Faces, a.Faces);
            // Sıralama saklanmaz: rapor kaydırıcılarla yeniden hesaplar, gösterilen öneri yeterlidir.
            Assert.Empty(a.Recommendation.Ranking);
        }
    }

    [Fact]
    public async Task Save_IkiKez_SatirlarCogalmaz()
    {
        var store = NewStore();
        var id = Guid.NewGuid();
        await store.SaveDecisionSessionAsync(id, Record(WithFaces(), Faceless()), CancellationToken.None);
        await store.SaveDecisionSessionAsync(id, Record(Faceless(0)) with { FollowUp = FollowUpAction.DegerlendirmeyeGec },
            CancellationToken.None);

        var got = await store.GetDecisionSessionAsync(id, CancellationToken.None);

        Assert.Equal(FollowUpAction.DegerlendirmeyeGec, got!.FollowUp);
        var w = Assert.Single(got.Session.Windows);
        Assert.Empty(w.Faces);
        Assert.Equal(6, got.Session.FinalWeights.Count);
    }

    [Fact]
    public async Task Get_KayitYok_Null() =>
        Assert.Null(await NewStore().GetDecisionSessionAsync(Guid.NewGuid(), CancellationToken.None));

    [Fact]
    public async Task Save_BaskaOturumaDokunmaz()
    {
        var store = NewStore();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        await store.SaveDecisionSessionAsync(a, Record(WithFaces(), Faceless()), CancellationToken.None);
        await store.SaveDecisionSessionAsync(b, Record(Faceless()), CancellationToken.None);
        await store.SaveDecisionSessionAsync(b, Record(Faceless(), Faceless(20)), CancellationToken.None);

        var got = await store.GetDecisionSessionAsync(a, CancellationToken.None);

        Assert.Equal(2, got!.Session.Windows.Count);
        Assert.Equal(2, got.Session.Windows[0].Faces.Count);
    }

    private sealed class ToleranceComparer : System.Collections.Generic.IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < 1e-9;
        public int GetHashCode(double obj) => 0;
    }
}
