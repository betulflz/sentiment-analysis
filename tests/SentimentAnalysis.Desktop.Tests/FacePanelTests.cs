using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class FacePanelTests
{
    private sealed class FaceRunner : IAnalysisRunner
    {
        public VideoFrame? Frame = new(new byte[4 * 2 * 3], 4, 2, TimeSpan.Zero);
        public IReadOnlyList<(int Index, FaceBox Box)> Boxes = Array.Empty<(int, FaceBox)>();
        public IReadOnlyList<FaceWindowSummary> Summaries = Array.Empty<FaceWindowSummary>();
        public Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct) =>
            Task.FromResult(MeetingSummary.CreateEmpty(AnalysisOutcome.NoResult));
        public VideoFrame? LatestFrame => Frame;
        public IReadOnlyList<(int Index, FaceBox Box)> LatestFaces => Boxes;
        public IReadOnlyList<FaceWindowSummary> FacesFor(ChunkResult r) => Summaries;
    }

    private static (int, FaceBox)[] Faces(int n) =>
        Enumerable.Range(0, n).Select(i => (i, new FaceBox(10 * i, 20, 30, 40))).ToArray();

    private static ChunkResult Chunk() =>
        new(TimeSpan.Zero, TimeSpan.FromSeconds(10),
            new FusedResult(new ValenceArousal(0, 0.5), Emotion.Notr, Emotion.Notr, Confidence.Orta,
                true, false, IncongruencePattern.Yok, 0.9,
                TextVa: new ValenceArousal(0, 0.5), ProsodyVa: new ValenceArousal(0, 0.5),
                TextConfidence: 0.7, ProsodyConfidence: 0.7),
            "metin");

    private static LiveViewModel Vm(FaceRunner runner, MeetingProfile profile)
    {
        var vm = new LiveViewModel(runner);
        vm.Decision.SelectedProfile = profile;
        return vm;
    }

    [Fact]
    public void Satis_TekOrtakKare_PanelYok()
    {
        var runner = new FaceRunner { Boxes = Faces(2) };
        var vm = Vm(runner, MeetingProfile.Satis);

        vm.RefreshPreview();

        Assert.False(vm.ShowFacePanels);
        Assert.Empty(vm.FacePanels);
        Assert.True(vm.ShowSharedPreview);
    }

    [Theory]
    [InlineData(MeetingProfile.Mulakat)]
    [InlineData(MeetingProfile.Toplanti)]
    public void MulakatVeToplanti_YuzBasinaPanel_OrtakKareGizlenir(MeetingProfile profile)
    {
        var runner = new FaceRunner { Boxes = Faces(2) };
        var vm = Vm(runner, profile);

        vm.RefreshPreview();

        Assert.True(vm.ShowFacePanels);
        Assert.False(vm.ShowSharedPreview);
        Assert.Equal(new[] { "Yüz 1", "Yüz 2" }, vm.FacePanels.Select(p => p.Name));
        Assert.Equal(new FaceBoxVm(10, 20, 30, 40, "Yüz 2"), vm.FacePanels[1].Box);
    }

    [Fact]
    public void Mulakat_YuzYoksa_OrtakKareGorunurKalir()
    {
        var vm = Vm(new FaceRunner(), MeetingProfile.Mulakat);

        vm.RefreshPreview();

        Assert.False(vm.ShowFacePanels);
        Assert.True(vm.ShowSharedPreview);
    }

    [Fact]
    public void Ad_YuzKaybolupGelinceKorunur()
    {
        var runner = new FaceRunner { Boxes = Faces(2) };
        var vm = Vm(runner, MeetingProfile.Mulakat);
        vm.RefreshPreview();

        vm.FacePanels[1].Name = "Ayşe";
        runner.Boxes = Array.Empty<(int, FaceBox)>();
        vm.RefreshPreview();
        runner.Boxes = Faces(2);
        vm.RefreshPreview();

        Assert.Equal("Ayşe", vm.FacePanels[1].Name);
    }

    [Fact]
    public void Ad_BosBirakilirsaVarsayilanEtiketeDoner()
    {
        var runner = new FaceRunner { Boxes = Faces(1) };
        var vm = Vm(runner, MeetingProfile.Mulakat);
        vm.RefreshPreview();

        vm.FacePanels[0].Name = "  ";

        Assert.Equal("Yüz 1", vm.FacePanels[0].DisplayName);
    }

    [Fact]
    public void Kutu_5HzdeGuncellenir_PanelNesnesiAyniKalir()
    {
        var runner = new FaceRunner { Boxes = Faces(1) };
        var vm = Vm(runner, MeetingProfile.Mulakat);
        vm.RefreshPreview();
        var panel = vm.FacePanels[0];
        panel.Name = "Ali";

        runner.Boxes = new[] { (0, new FaceBox(99, 98, 31, 41)) };
        vm.RefreshPreview();

        Assert.Same(panel, Assert.Single(vm.FacePanels));
        Assert.Equal(99, panel.Box.X);
        Assert.Equal("Ali", panel.Name);
    }

    [Fact]
    public void Pencere_GostergeleriVeDuyguPanelleriDoldurur()
    {
        var runner = new FaceRunner
        {
            Boxes = Faces(2),
            Summaries = new[]
            {
                new FaceWindowSummary(0, 1, LookAwayRatio: 0.2, ValenceShift: 0.4, NegativeExpression: 0.1, SmileRatio: 0.5, Reliability: 1),
                new FaceWindowSummary(1, 1, LookAwayRatio: 0.6, ValenceShift: -0.4, NegativeExpression: 0.7, SmileRatio: 0, Reliability: 1),
            }
        };
        var vm = Vm(runner, MeetingProfile.Mulakat);
        vm.RefreshPreview();

        vm.Report(Chunk());

        Assert.Equal(0.2, vm.FacePanels[0].LookAway);
        Assert.Equal(0.5, vm.FacePanels[0].Smile);
        Assert.Equal(0.7, vm.FacePanels[1].Negative);
        Assert.Equal("Olumlu", vm.FacePanels[0].Emotion);
        Assert.Equal("Olumsuz", vm.FacePanels[1].Emotion);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0, "Sakin")]
    [InlineData(0.3, 0.0, 0.0, "Olumlu")]
    [InlineData(0.0, 0.0, 0.5, "Olumlu")]
    [InlineData(-0.3, 0.0, 0.0, "Olumsuz")]
    [InlineData(0.0, 0.6, 0.0, "Olumsuz")]
    public void DuyguMetni_PencereOzetindenTurer(double valenceShift, double negative, double smile, string expected)
    {
        var summary = new FaceWindowSummary(0, 1, 0, valenceShift, negative, smile, 1);
        Assert.Equal(expected, FacePanelVm.EmotionText(summary));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(9, 3)]
    public void Izgara_SutunSayisi(int faces, int columns)
    {
        var vm = Vm(new FaceRunner { Boxes = Faces(faces) }, MeetingProfile.Toplanti);
        vm.RefreshPreview();
        Assert.Equal(columns, vm.FacePanelColumns);
    }

    [Fact]
    public void YeniKosu_PanelAdlariSifirlanir()
    {
        var runner = new FaceRunner { Boxes = Faces(1) };
        var vm = Vm(runner, MeetingProfile.Mulakat);
        vm.RefreshPreview();
        vm.FacePanels[0].Name = "Ayşe";

        vm.ResetFacePanels();
        vm.RefreshPreview();

        Assert.Equal("Yüz 1", vm.FacePanels[0].Name);
    }
}
