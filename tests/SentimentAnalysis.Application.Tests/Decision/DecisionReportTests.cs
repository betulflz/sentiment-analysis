using System;
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Decision;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Decision;

public class DecisionReportTests
{
    private static readonly DecisionConfig Cfg = new();

    /// <summary>10 sn'lik pencere; yalnızca K1 (gerilim) dolu, diğerleri 0.</summary>
    private static DecisionWindow W(double startSec, double k1, double quality = 1, LiveAction shown = LiveAction.DevamEt,
        int? face = null) =>
        new(TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(startSec + 10),
            new Indicators(new[] { k1, 0, 0, 0, 0, 0 }, new int?[] { face, null, null, null, null, null }, quality),
            new Recommendation(shown, 0.5, Array.Empty<Ranked<LiveAction>>(), Array.Empty<Criterion>(), face,
                Confidence.Orta, $"neden {startSec}"));

    [Fact]
    public void CriticalMoments_ArdisikEsikUstuPencereler_TekBolumOlur()
    {
        var windows = new[]
        {
            W(0, 0.1), W(10, 0.5, shown: LiveAction.SoruSor), W(20, 0.7, shown: LiveAction.EmpatiKur, face: 1),
            W(30, 0.4, shown: LiveAction.EmpatiKur), W(40, 0.1),
        };
        var m = Assert.Single(DecisionReport.CriticalMoments(windows, Cfg));
        Assert.Equal(TimeSpan.FromSeconds(10), m.Start);
        Assert.Equal(TimeSpan.FromSeconds(40), m.End);
        Assert.Equal(Criterion.Gerilim, m.Criterion);
        Assert.Equal(0.7, m.Peak, 9);
        Assert.Equal(1, m.Face);
        Assert.Equal(LiveAction.EmpatiKur, m.Shown);
    }

    [Fact]
    public void CriticalMoments_IhtiyacKaliteyleCarpilir() =>
        Assert.Empty(DecisionReport.CriticalMoments(new[] { W(0, 0.5, quality: 0.5) }, Cfg));

    [Fact]
    public void CriticalMoments_EnFazlaBes_TepeyeGoreSecilir_ZamanSirasiylaDoner()
    {
        // Tepeler zaman sırasında karışık: 0,4 0,9 0,5 1,0 0,6 0,8 0,7 — aralarında sakin pencere.
        double[] peaks = { 0.4, 0.9, 0.5, 1.0, 0.6, 0.8, 0.7 };
        var windows = new List<DecisionWindow>();
        for (int i = 0; i < peaks.Length; i++)
        {
            windows.Add(W(i * 20, peaks[i]));
            windows.Add(W(i * 20 + 10, 0));
        }
        var moments = DecisionReport.CriticalMoments(windows, Cfg);
        Assert.Equal(5, moments.Count);
        Assert.DoesNotContain(moments, m => m.Peak < 0.55);
        Assert.Equal(moments.OrderBy(m => m.Start).Select(m => m.Start), moments.Select(m => m.Start));
    }

    [Fact]
    public void CriticalMoments_YuzsuzPencere_FaceNull() =>
        Assert.Null(Assert.Single(DecisionReport.CriticalMoments(new[] { W(0, 0.8) }, Cfg)).Face);

    [Fact]
    public void CriticalMoments_BosGecmis_BosListe() =>
        Assert.Empty(DecisionReport.CriticalMoments(Array.Empty<DecisionWindow>(), Cfg));

    [Fact]
    public void Changes_IlkPencereVeDegisimler()
    {
        var windows = new[]
        {
            W(0, 0, shown: LiveAction.DevamEt), W(10, 0, shown: LiveAction.DevamEt),
            W(20, 0, shown: LiveAction.EmpatiKur), W(30, 0, shown: LiveAction.EmpatiKur),
            W(40, 0, shown: LiveAction.DevamEt),
        };
        var changes = DecisionReport.Changes(windows);
        Assert.Equal(new[] { 0.0, 20, 40 }, changes.Select(c => c.At.TotalSeconds));
        Assert.Equal(new[] { LiveAction.DevamEt, LiveAction.EmpatiKur, LiveAction.DevamEt }, changes.Select(c => c.Action));
        Assert.Equal("neden 20", changes[1].Reason);
        Assert.Equal(Confidence.Orta, changes[1].Confidence);
    }

    [Fact]
    public void Changes_BosGecmis_BosListe() => Assert.Empty(DecisionReport.Changes(Array.Empty<DecisionWindow>()));
}
