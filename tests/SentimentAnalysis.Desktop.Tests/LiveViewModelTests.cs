using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class LiveViewModelTests
{
    private static ChunkResult Chunk(double t, Emotion e, double v, bool flagged = false) =>
        new(TimeSpan.FromSeconds(t), TimeSpan.FromSeconds(t + 1),
            new FusedResult(new ValenceArousal(v, 0.6), e, Emotion.Notr, Confidence.Orta,
                !flagged, flagged, flagged ? IncongruencePattern.Maskeleme : IncongruencePattern.Yok,
                flagged ? 0.2 : 0.9,
                TextVa: new ValenceArousal(v, 0.6), ProsodyVa: new ValenceArousal(v, 0.6),
                TextConfidence: 0.7, ProsodyConfidence: 0.7),
            $"metin-{t}");

    private sealed class FakeRunner : IAnalysisRunner
    {
        private readonly ChunkResult[] _chunks;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FakeRunner(params ChunkResult[] chunks) => _chunks = chunks;
        public async Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct)
        {
            Started.TrySetResult();
            await Task.Yield();
            foreach (var c in _chunks) { ct.ThrowIfCancellationRequested(); progress.Report(c); }
            return new MeetingSummary(Emotion.Mutlu, new System.Collections.Generic.Dictionary<Emotion,double>(),
                0, 0, Array.Empty<ChunkResult>(), new ValenceArousal(0.3, 0.5), 0.8, "özet");
        }
    }

    [Fact]
    public void Report_AddsRowAndUpdatesCurrent()
    {
        var vm = new LiveViewModel(new FakeRunner());
        vm.Report(Chunk(0, Emotion.Kizgin, -0.7, flagged: true));

        Assert.Single(vm.Timeline);
        Assert.Equal("Kızgın", vm.Timeline[0].Emotion);
        Assert.Equal("Kızgın", vm.CurrentEmotion);
        Assert.Equal(-0.7, vm.CurrentValence, precision: 5);
        Assert.True(vm.Timeline[0].Flagged);
        Assert.Contains("Maskeleme", vm.CongruenceBadge); // uyumsuzluk rozeti lokalize
    }

    [Fact]
    public void Report_FaceFusedChunk_ShowsFaceContribution()
    {
        var vm = new LiveViewModel(new FakeRunner());
        var speech = Chunk(0, Emotion.Uzgun, -0.4);
        vm.Report(speech);
        Assert.Equal(string.Empty, vm.FaceContributionText);

        vm.Report(speech with
        {
            Fused = speech.Fused with
            {
                Combined = new ValenceArousal(0.25, 0.6), PrimaryEmotion = Emotion.Mutlu,
                SpeechVa = speech.Fused.Combined, FaceValence = 0.9, FaceWeight = 1.0,
            },
        });
        Assert.Equal("Mutlu", vm.CurrentEmotion);
        Assert.Equal("Yüz dahil: konuşma −0,40 · yüz +0,90 → nihai +0,25", vm.FaceContributionText);
    }

    [Fact]
    public async Task Start_RunsRunner_FeedsTimeline_ThenStops()
    {
        var runner = new FakeRunner(Chunk(0, Emotion.Mutlu, 0.5), Chunk(1, Emotion.Sakin, 0.3));
        var vm = new LiveViewModel(runner);

        Assert.False(vm.IsCapturing);
        vm.StartCommand.Execute(null);
        await runner.Started.Task;            // koşu başladı
        // StartCommand async void olabilir → kısa bekleme yerine tamamlanmayı VM üzerinden bekle:
        await vm.WaitForIdleAsync();          // test yardımcısı: koşu görevini bekler

        Assert.Equal(2, vm.Timeline.Count);
        Assert.False(vm.IsCapturing);         // koşu bitince kapanır
    }

    [Fact]
    public async Task Stop_CancelsCapture()
    {
        var runner = new FakeRunner(Chunk(0, Emotion.Mutlu, 0.5));
        var vm = new LiveViewModel(runner);
        vm.StartCommand.Execute(null);
        await runner.Started.Task;            // koşu başladı
        vm.StopCommand.Execute(null);         // iptal
        // İptal sonrası IsCapturing false'a döner (WaitForIdle ile deterministik).
        await vm.WaitForIdleAsync();
        Assert.False(vm.IsCapturing); // çökme olmamalı
    }

    /// <summary>Gerçek mikrofon akışında "Durdur" iptal ile biter, ama PipelineAnalysisRunner bunu
    /// normal bitiş sayıp toplanan zaman çizelgesinden bir özet döner (bkz. görev 5 düzeltme turu —
    /// aksi halde Completed hiç tetiklenmez, Rapor/Geçmiş güncellenmez). Bu test, runner Stop sonrası
    /// (istisna fırlatmadan) bir özetle döndüğünde LiveViewModel'in Completed'i tetiklediğini
    /// doğruluyor — PipelineAnalysisRunner'ın kendisi Desktop.Avalonia'da olduğundan (test projesi yok)
    /// oradaki gerçek davranışı burada taklit ediyoruz.</summary>
    private sealed class StoppableRunner : IAnalysisRunner
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct)
        {
            Started.TrySetResult();
            var stopped = new TaskCompletionSource();
            using var reg = ct.Register(() => stopped.TrySetResult());
            await stopped.Task.ConfigureAwait(false); // Durdur'u bekle
            return new MeetingSummary(Emotion.Mutlu, new System.Collections.Generic.Dictionary<Emotion, double>(),
                0, 0, Array.Empty<ChunkResult>(), new ValenceArousal(0.3, 0.5), 0.8, "durdurulan-özet");
        }
    }

    private sealed class GatedRunner : IAnalysisRunner
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SkippedChunkCount { get; set; }
        public async Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct)
        {
            Started.TrySetResult();
            await _release.Task.ConfigureAwait(false);
            return new MeetingSummary(Emotion.Notr, new System.Collections.Generic.Dictionary<Emotion, double>(),
                0, 0, Array.Empty<ChunkResult>(), ValenceArousal.Neutral, 1.0, "özet");
        }
        public void Release() => _release.TrySetResult();
    }

    [Fact]
    public async Task Start_ClearsStaleCongruenceBadgeFromPreviousRun()
    {
        var runner = new GatedRunner();
        var vm = new LiveViewModel(runner);
        vm.Report(Chunk(0, Emotion.Kizgin, -0.7, flagged: true)); // önceki koşudan "Maskeleme" rozeti
        Assert.Contains("Maskeleme", vm.CongruenceBadge);

        vm.StartCommand.Execute(null);
        await runner.Started.Task;

        // İlk chunk henüz gelmeden rozet sıfırlanmış olmalı — sarkan eski rozet gösterilmemeli.
        Assert.Equal(string.Empty, vm.CongruenceBadge);

        runner.Release();
        await vm.WaitForIdleAsync();
    }

    /// <summary>"Durdur"dan sonra motor kanalda kalan chunk'ları drene edip özet üretene kadar
    /// birkaç saniye geçebilir (Whisper/ONNX) — bu süre boyunca ekran donmuş görünmesin diye
    /// "Dinleniyor" rozeti "Analiz tamamlanıyor…"ya döner, Completed ateşlenince eski hâline
    /// döner (görev 5 düzeltme turu 2).</summary>
    [Fact]
    public async Task Stop_ShowsFinishingText_ThenClearsWhenRunCompletes()
    {
        var runner = new GatedRunner();
        var vm = new LiveViewModel(runner);

        vm.StartCommand.Execute(null);
        await runner.Started.Task;
        Assert.False(vm.IsFinishing);
        Assert.Equal("Dinleniyor", vm.CaptureStatusText);

        vm.StopCommand.Execute(null);

        Assert.True(vm.IsFinishing);
        Assert.Equal("Analiz tamamlanıyor…", vm.CaptureStatusText);

        runner.Release(); // motor "drene" işini bitirdi (bu testte gerçek drene yok, taklit)
        await vm.WaitForIdleAsync();

        Assert.False(vm.IsFinishing);
        Assert.Equal("Dinleniyor", vm.CaptureStatusText);
    }

    /// <summary>08e düzeltme turu Fix 2: <see cref="IAnalysisRunner.SkippedChunkCount"/> sıfır
    /// olmayan bir değerle döndüğünde (kanal taşması/drenaj sınırı) LiveViewModel bunu görünür
    /// bir Türkçe nota çevirir; sıfırsa not tamamen boş kalır (şerit çöker, alarm göstermez).</summary>
    [Fact]
    public async Task Completed_WithSkippedChunks_ShowsNotice_ThenHiddenOnCleanRun()
    {
        var runner = new GatedRunner { SkippedChunkCount = 3 };
        var vm = new LiveViewModel(runner);

        Assert.Equal(string.Empty, vm.SkippedChunksNotice); // koşu öncesi: gizli

        vm.StartCommand.Execute(null);
        await runner.Started.Task;
        runner.Release();
        await vm.WaitForIdleAsync();

        Assert.Equal(3, vm.SkippedChunkCount);
        Assert.Contains("3 ses parçası", vm.SkippedChunksNotice);
        Assert.Contains("atlandı", vm.SkippedChunksNotice);

        // Bir sonraki koşu temiz (atlanan yok) ise not tekrar gizlenmeli — sarkan eski bilgi kalmamalı.
        var cleanRunner = new GatedRunner { SkippedChunkCount = 0 };
        var vm2 = new LiveViewModel(cleanRunner);
        vm2.StartCommand.Execute(null);
        await cleanRunner.Started.Task;
        cleanRunner.Release();
        await vm2.WaitForIdleAsync();

        Assert.Equal(0, vm2.SkippedChunkCount);
        Assert.Equal(string.Empty, vm2.SkippedChunksNotice);
    }

    [Fact]
    public async Task Stop_WhenRunnerEndsGracefully_RaisesCompleted()
    {
        var runner = new StoppableRunner();
        var vm = new LiveViewModel(runner);
        MeetingSummary? completed = null;
        vm.Completed += s => completed = s;

        vm.StartCommand.Execute(null);
        await runner.Started.Task;
        vm.StopCommand.Execute(null);
        await vm.WaitForIdleAsync();

        Assert.NotNull(completed);
        Assert.Equal("durdurulan-özet", completed!.TurkishSummary);
    }

    /// <summary>Metin ve ses birbirinden AYRI VA taşıyan chunk — panellerin karışmadığını gösterir.</summary>
    private static ChunkResult SplitChunk(ValenceArousal text, ValenceArousal prosody, ValenceArousal combined) =>
        new(TimeSpan.Zero, TimeSpan.FromSeconds(1),
            new FusedResult(combined, EmotionWheel.Map(combined), Emotion.Notr, Confidence.Dusuk,
                false, true, IncongruencePattern.Maskeleme, 0.1,
                TextVa: text, ProsodyVa: prosody, TextConfidence: 0.8, ProsodyConfidence: 0.6),
            "harika, çok memnunum");

    [Fact]
    public void Panels_BeforeAnyChunk_AreEmpty()
    {
        var vm = new LiveViewModel(new FakeRunner());

        Assert.False(vm.TextAnalysis.HasData);
        Assert.False(vm.AudioAnalysis.HasData);
        Assert.False(vm.FinalAnalysis.HasData);
        Assert.Equal("Metin Analizi", vm.TextAnalysis.Title);
        Assert.Equal("Ses Analizi", vm.AudioAnalysis.Title);
        Assert.Equal("Nihai Karar", vm.FinalAnalysis.Title);
    }

    [Fact]
    public void Report_FeedsEachPanelFromItsOwnSource()
    {
        var vm = new LiveViewModel(new FakeRunner());

        vm.Report(SplitChunk(
            text:     new ValenceArousal(0.80, 0.50),   // "Mutlu"
            prosody:  new ValenceArousal(-0.50, 0.80),  // "Gergin"
            combined: new ValenceArousal(0.00, 0.50))); // "Nötr"

        Assert.Equal("Mutlu", vm.TextAnalysis.Emotion);
        Assert.Equal("Gergin", vm.AudioAnalysis.Emotion);
        Assert.Equal("Nötr", vm.FinalAnalysis.Emotion);

        // Eksenler de kendi kaynağından gelir — karışmıyor.
        Assert.Equal(0.80, vm.TextAnalysis.Valence, precision: 6);
        Assert.Equal(-0.50, vm.AudioAnalysis.Valence, precision: 6);
    }

    [Fact]
    public void Report_NotrYaricapi_ConfigtenPanellereUlasir()
    {
        // Yarıçap config'te durup panellere ulaşmazsa kullanıcı hâlâ "Nötr" görür.
        var va = new ValenceArousal(0.30, 0.50);
        var chunk = SplitChunk(text: va, prosody: va, combined: va);

        var withRadius = new LiveViewModel(new FakeRunner(), config: WithNeutralRadius(0.2));
        var classic = new LiveViewModel(new FakeRunner(), config: WithNeutralRadius(null));
        withRadius.Report(chunk);
        classic.Report(chunk);

        Assert.Equal("Nötr", classic.FinalAnalysis.Emotion);
        Assert.Equal("Sakin", withRadius.FinalAnalysis.Emotion);
        Assert.Equal("Sakin", withRadius.TextAnalysis.Emotion);
        Assert.Equal("Sakin", withRadius.AudioAnalysis.Emotion);
    }

    private static AppConfig WithNeutralRadius(double? radius) =>
        AppConfig.Default with { Fusion = AppConfig.Default.Fusion with { NeutralRadius = radius } };

    [Fact]
    public void Report_PanelsExposeFullDistribution()
    {
        var vm = new LiveViewModel(new FakeRunner());

        vm.Report(Chunk(0, Emotion.Mutlu, 0.5));

        Assert.Equal(8, vm.FinalAnalysis.Shares.Count);
        Assert.Equal(100, vm.FinalAnalysis.Shares.Sum(s => s.Percent));
        Assert.True(vm.FinalAnalysis.HasData);
    }

    [Fact]
    public void Report_SetsLocalizedConfidence()
    {
        var vm = new LiveViewModel(new FakeRunner());

        vm.Report(Chunk(0, Emotion.Mutlu, 0.5));   // Confidence.Orta

        Assert.Equal("Orta", vm.CurrentConfidence);
    }

    [Fact]
    public void CanStart_FileMode_RequiresSelectedFile()
    {
        var selected = new SelectedAudio();
        var mode = new CaptureModeState { Mode = CaptureMode.Dosya };
        var vm = new LiveViewModel(new FakeRunner(), null, selected, null, mode);

        Assert.False(vm.StartCommand.CanExecute(null));

        // CanExecute(null) canlı durumu doğrudan okur, ayrı bir tetikleyiciye gerek yok.
        selected.Path = "/tmp/ses.wav";

        Assert.True(vm.StartCommand.CanExecute(null));
    }

    [Fact]
    public void CanStart_MicrophoneMode_DoesNotRequireFile()
    {
        var mode = new CaptureModeState();
        var vm = new LiveViewModel(new FakeRunner(), null, new SelectedAudio(), null, mode);

        vm.UseMicrophone = true;

        Assert.True(vm.StartCommand.CanExecute(null));
        Assert.Equal(CaptureMode.Mikrofon, mode.Mode);
    }

    [Fact]
    public void UseMicrophone_WritesThroughToSharedState()
    {
        var mode = new CaptureModeState();
        var vm = new LiveViewModel(new FakeRunner(), null, new SelectedAudio(), null, mode);

        vm.UseMicrophone = true;
        Assert.Equal(CaptureMode.Mikrofon, mode.Mode);

        vm.UseMicrophone = false;
        Assert.Equal(CaptureMode.Dosya, mode.Mode);
    }

    private sealed class ThrowingRunner : IAnalysisRunner
    {
        private readonly Exception _ex;
        public ThrowingRunner(Exception ex) => _ex = ex;
        public Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct)
            => throw _ex;
    }

    [Fact]
    public async Task Start_OnPermissionError_SurfacesFullMessageInCaptureError()
    {
        const string uzunMesaj = "Mikrofon izni reddedildi. Sistem Ayarları > Gizlilik ve Güvenlik > " +
            "Mikrofon bölümünden bu uygulamaya izin verin ve tekrar deneyin.";
        var runner = new ThrowingRunner(new InvalidOperationException(uzunMesaj));
        var vm = new LiveViewModel(runner, null, new SelectedAudio { Path = "/tmp/ses.wav" });

        vm.StartCommand.Execute(null);
        await vm.WaitForIdleAsync();

        Assert.Contains(uzunMesaj, vm.CaptureError);
        Assert.False(vm.IsCapturing);
    }

    // --- Kamera modu ve yüz notları (11b) ---

    [Fact]
    public void UseCamera_SetsModeKamera_AndClearsMicrophone()
    {
        var mode = new CaptureModeState();
        var vm = new LiveViewModel(new FakeRunner(), null, new SelectedAudio(), null, mode);

        vm.UseMicrophone = true;
        vm.UseCamera = true;
        Assert.Equal(CaptureMode.Kamera, mode.Mode);
        Assert.False(vm.UseMicrophone);
        Assert.False(vm.IsFileMode);
        Assert.True(vm.StartCommand.CanExecute(null)); // kamera modu dosya istemez

        vm.UseMicrophone = true;
        Assert.False(vm.UseCamera);
        Assert.Equal(CaptureMode.Mikrofon, mode.Mode);

        vm.UseMicrophone = false;
        Assert.Equal(CaptureMode.Dosya, mode.Mode);
        Assert.True(vm.IsFileMode);
    }

    private sealed class FaceRunner : IAnalysisRunner
    {
        public Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct) =>
            Task.FromResult(MeetingSummary.CreateEmpty(AnalysisOutcome.NoResult));
        public IReadOnlyList<FaceWindowSummary> FacesFor(ChunkResult r) =>
            new[] { new FaceWindowSummary(0, 1, 0.5, 0, 0, 0, 1) };
    }

    [Fact]
    public void LiveViewModel_Report_UsesRunnerFacesFor()
    {
        var vm = new LiveViewModel(new FaceRunner());
        vm.Report(Chunk(0, Emotion.Notr, 0));
        Assert.StartsWith("Yüz 1: dönük %50", vm.Timeline[0].FaceNote);
    }

    private sealed class WarningRunner : IAnalysisRunner
    {
        public Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct)
        {
            ((ICaptureWarnings)progress).Warn("Bu videoda ses yok.");
            return Task.FromResult(MeetingSummary.CreateEmpty(AnalysisOutcome.NoResult));
        }
    }

    [Fact]
    public async Task Warn_DuringRun_KeptWhenRunHasNoResult()
    {
        var vm = new LiveViewModel(new WarningRunner());
        vm.StartCommand.Execute(null);
        await vm.WaitForIdleAsync();

        Assert.StartsWith("Bu videoda ses yok.", vm.CaptureError);
        Assert.Contains("Analiz sonucu üretilemedi", vm.CaptureError);
    }

    // --- Karar paneli (11d) ---

    [Fact]
    public void Report_FeedsDecisionPanel()
    {
        var vm = new LiveViewModel(new FaceRunner());
        vm.Decision.Start();
        vm.Report(Chunk(0, Emotion.Notr, 0));
        Assert.Single(vm.Decision.History);
        Assert.Single(vm.Decision.Faces);
    }

    [Fact]
    public async Task Start_LocksProfileAndResetsPanel()
    {
        var runner = new StoppableRunner();
        var vm = new LiveViewModel(runner);
        vm.Report(Chunk(0, Emotion.Notr, 0));          // önceki oturumdan kalan pencere
        vm.StartCommand.Execute(null);
        await runner.Started.Task;
        Assert.True(vm.Decision.IsLocked);
        Assert.Empty(vm.Decision.History);
        vm.StopCommand.Execute(null);
        await vm.WaitForIdleAsync();
        Assert.False(vm.Decision.IsLocked);
    }

    [Fact]
    public async Task Start_OncekiYuzDahilSatiriniTemizler()
    {
        var vm = new LiveViewModel(new FakeRunner());
        var speech = Chunk(0, Emotion.Uzgun, -0.4);
        vm.Report(speech with { Fused = speech.Fused with { SpeechVa = speech.Fused.Combined, FaceValence = 0.9, FaceWeight = 1.0 } });
        Assert.NotEqual(string.Empty, vm.FaceContributionText);

        vm.StartCommand.Execute(null);
        Assert.Equal(string.Empty, vm.FaceContributionText);
        await vm.WaitForIdleAsync();
    }

    /// <summary>İptal edilene kadar hiç parça üretmez — sessiz video gibi.</summary>
    private sealed class SilentRunner : IAnalysisRunner
    {
        public async Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("ulaşılmaz");
        }
    }

    [Fact]
    public async Task SessizKosu_KonusmaBekleniyorGosterilir_IlkSatirdaKalkar()
    {
        var vm = new LiveViewModel(new SilentRunner());
        Assert.False(vm.WaitingForSpeech);

        vm.StartCommand.Execute(null);
        Assert.True(vm.WaitingForSpeech);

        vm.Report(Chunk(0, Emotion.Notr, 0));
        Assert.False(vm.WaitingForSpeech);

        vm.StopCommand.Execute(null);
        await vm.WaitForIdleAsync();
        Assert.False(vm.WaitingForSpeech);
    }

    [Fact]
    public async Task YeniKosu_OncekiSatirlarVarken_YineKonusmaBekler()
    {
        var vm = new LiveViewModel(new SilentRunner());
        vm.Report(Chunk(0, Emotion.Notr, 0)); // önceki koşudan kalan satır
        vm.StartCommand.Execute(null);
        Assert.True(vm.WaitingForSpeech);
        vm.StopCommand.Execute(null);
        await vm.WaitForIdleAsync();
    }
}
