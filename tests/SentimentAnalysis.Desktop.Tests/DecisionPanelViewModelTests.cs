using System;
using System.Linq;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Decision;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class DecisionPanelViewModelTests
{
    private static readonly DecisionConfig Cfg = new();

    private static ChunkResult Chunk(double t, double valence, double arousal, string text = "metin") =>
        new(TimeSpan.FromSeconds(t), TimeSpan.FromSeconds(t + 10),
            new FusedResult(new ValenceArousal(valence, arousal), Emotion.Notr, Emotion.Notr, Confidence.Orta,
                true, false, IncongruencePattern.Yok, 0.9,
                TextVa: new ValenceArousal(valence, arousal), ProsodyVa: new ValenceArousal(valence, arousal),
                TextConfidence: 0.7, ProsodyConfidence: 0.7),
            text);

    private static ChunkResult Tense(double t = 0) => Chunk(t, -0.8, 0.9);
    private static ChunkResult Calm(double t = 0) => Chunk(t, 0.5, 0.5);
    private static readonly FaceWindowSummary[] NoFaces = Array.Empty<FaceWindowSummary>();

    private static DecisionPanelViewModel Panel() => new(Cfg);

    [Fact]
    public void Initial_ShowsDashAndProfileWeights()
    {
        var p = Panel();
        Assert.Equal("—", p.ActionText);
        Assert.Equal(MeetingProfile.Satis, p.SelectedProfile);
        var expected = DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Satis);
        Assert.Equal(6, p.Weights.Count);
        for (int i = 0; i < 6; i++) Assert.Equal(expected[i], p.Weights[i].Value, 6);
    }

    [Fact]
    public void SelectProfile_ResetsSlidersToProfileWeights()
    {
        var p = Panel();
        p.Weights[0].Value = 0;
        p.SelectedProfile = MeetingProfile.Mulakat;
        var expected = DecisionKnowledgeBase.DefaultWeights(MeetingProfile.Mulakat);
        for (int i = 0; i < 6; i++) Assert.Equal(expected[i], p.Weights[i].Value, 6);
    }

    [Fact]
    public void OnWindow_TenseChunk_ShowsInterventionWithReason()
    {
        var p = Panel();
        p.Start();
        p.OnWindow(Tense(), NoFaces);
        Assert.NotEqual("Devam et", p.ActionText);
        Assert.NotEqual("—", p.ActionText);
        Assert.False(string.IsNullOrEmpty(p.ReasonText));
        Assert.StartsWith("Güven: ", p.ConfidenceText);
        Assert.True(p.Indicators[(int)Criterion.Gerilim].Value > 0.35);
        Assert.Single(p.History);
    }

    [Fact]
    public void OnWindow_CalmChunk_ShowsDevamEt()
    {
        var p = Panel();
        p.Start();
        p.OnWindow(Calm(), NoFaces);
        Assert.Equal("Devam et", p.ActionText);
        Assert.Equal(string.Empty, p.AlternativeText);
    }

    [Fact]
    public void OnWindow_WithFaces_FillsFaceBars()
    {
        var p = Panel();
        p.Start();
        p.OnWindow(Calm(), new[]
        {
            new FaceWindowSummary(0, 1, LookAwayRatio: 0.2, ValenceShift: 0, NegativeExpression: 0.1, SmileRatio: 0.3, Reliability: 1),
            new FaceWindowSummary(1, 1, LookAwayRatio: 0.6, ValenceShift: 0, NegativeExpression: 0.4, SmileRatio: 0, Reliability: 1),
        });
        Assert.Equal(new[] { "Yüz 1", "Yüz 2" }, p.Faces.Select(f => f.Label));
        Assert.Equal(0.6, p.Faces[1].LookAway, 6);
        Assert.Equal(0.4, p.Faces[1].Negative, 6);
        Assert.Equal(0.3, p.Faces[0].Smile, 6);
    }

    [Fact]
    public void WeightChange_ReranksImmediately()
    {
        var p = Panel();
        p.Start();
        p.OnWindow(Tense(), NoFaces);
        var before = p.ActionText;

        foreach (var w in p.Weights) w.Value = w.Criterion == Criterion.DusukEnerji ? 1 : 0;

        var expected = new LiveAdvisor(MeetingProfile.Satis, Cfg).Rerank(p.History[0].Indicators, p.CurrentWeights);
        Assert.Equal(DecisionText.Name(expected.Action), p.ActionText);
        Assert.NotEqual(before, p.ActionText);
    }

    [Fact]
    public void WeightChange_BeforeFirstWindow_NoRecommendation()
    {
        var p = Panel();
        p.Start();
        p.Weights[0].Value = 0;
        Assert.Equal("—", p.ActionText);
        Assert.Empty(p.History);
    }

    [Fact]
    public void AllWeightsZero_StillRecommends()
    {
        var p = Panel();
        p.Start();
        p.OnWindow(Tense(), NoFaces);
        foreach (var w in p.Weights) w.Value = 0;
        Assert.NotEqual("—", p.ActionText);
        Assert.DoesNotContain("NaN", p.ReasonText);
        Assert.All(p.Weights, w => Assert.Equal("%17", w.ShareText));
    }

    [Fact]
    public void Profile_LockedWhileCapturing()
    {
        var p = Panel();
        p.IsLocked = true;
        p.SelectedProfile = MeetingProfile.Toplanti;
        Assert.Equal(MeetingProfile.Satis, p.SelectedProfile);
        p.IsLocked = false;
        p.SelectedProfile = MeetingProfile.Toplanti;
        Assert.Equal(MeetingProfile.Toplanti, p.SelectedProfile);
    }

    [Fact]
    public void Start_ResetsHistoryAndAdvisor()
    {
        var p = Panel();
        p.Start();
        p.OnWindow(Chunk(0, 0.5, 0.5), NoFaces);
        p.OnWindow(Chunk(10, 0.0, 0.5), NoFaces);
        p.OnWindow(Chunk(20, -0.5, 0.5), NoFaces);

        p.Start();
        Assert.Empty(p.History);
        Assert.Equal("—", p.ActionText);
        Assert.Empty(p.Faces);

        p.OnWindow(Chunk(0, -0.9, 0.5), NoFaces);
        Assert.Equal(0.0, p.History[0].Indicators.Values[(int)Criterion.Kotulesme], 9); // eski valence serisi taşınmadı
    }

    [Fact]
    public void Ranking_EmptyBeforeFirstWindow()
    {
        Assert.Empty(Panel().Ranking);
        Assert.False(Panel().HasRanking);
    }

    [Fact]
    public void Ranking_ShownEvenWhenCardSaysContinue_AndFollowsSliders()
    {
        var p = Panel();
        p.Start();
        p.OnWindow(Calm(), NoFaces);
        Assert.Equal("Devam et", p.ActionText);
        Assert.Equal(3, p.Ranking.Count);
        Assert.True(p.HasRanking);
        Assert.StartsWith("1. ", p.Ranking[0].Label);
        Assert.True(p.Ranking[0].Closeness >= p.Ranking[1].Closeness);
        Assert.StartsWith("%", p.Ranking[0].PercentText);

        var before = p.Ranking.Select(r => (r.Label, r.Closeness)).ToArray();
        foreach (var w in p.Weights) w.Value = w.Criterion == Criterion.IlgiKaybi ? 1 : 0;
        var after = p.Ranking.Select(r => (r.Label, r.Closeness)).ToArray();
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Ranking_ClearedOnStart()
    {
        var p = Panel();
        p.Start();
        p.OnWindow(Tense(), NoFaces);
        p.Start();
        Assert.Empty(p.Ranking);
    }

    [Fact]
    public void Sliders_ZeroNeedCriterion_MarkedIdle()
    {
        // Gergin pencere: gerilim ihtiyacı var, ilgi kaybı (yüz yok) 0 → ilgi kaybı kaydırıcısı sıralamayı etkilemez.
        var p = Panel();
        p.Start();
        Assert.All(p.Weights, w => Assert.Equal(string.Empty, w.IdleNote));
        p.OnWindow(Tense(), NoFaces);
        Assert.NotEqual(string.Empty, p.Weights[(int)Criterion.IlgiKaybi].IdleNote);
        Assert.Equal(string.Empty, p.Weights[(int)Criterion.Gerilim].IdleNote);
        p.Start();
        Assert.All(p.Weights, w => Assert.Equal(string.Empty, w.IdleNote));
    }

    [Fact]
    public void AfterSession_SliderOrProfileChange_DoesNotRewriteHistory()
    {
        var p = Panel();
        p.Start();
        p.IsLocked = true;
        p.OnWindow(Tense(), NoFaces);
        p.IsLocked = false; // Durdur
        var recorded = p.History[^1].Recommendation;

        foreach (var w in p.Weights) w.Value = w.Criterion == Criterion.Kotulesme ? 1 : 0;
        p.SelectedProfile = MeetingProfile.Toplanti;

        Assert.Same(recorded, p.History[^1].Recommendation);
    }
}
