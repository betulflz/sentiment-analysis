using System;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

/// <summary>12: "Sesimi tanıt" düğmesi ve durum metni.</summary>
public class VoiceEnrollmentTests
{
    private sealed class FakeRunner : IAnalysisRunner
    {
        public Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct) =>
            Task.FromResult(new MeetingSummary(Emotion.Notr, new System.Collections.Generic.Dictionary<Emotion, double>(),
                0, 0, Array.Empty<ChunkResult>(), new ValenceArousal(0, 0), 0, ""));
    }

    private sealed class FakeEnrollment : IVoiceEnrollment
    {
        public EnrollmentResult Result = new(true, false, 6, "Sesiniz tanındı.");
        public TaskCompletionSource<EnrollmentResult>? Gate;
        public bool IsEnrolled { get; private set; }
        public bool Available = true;
        public bool IsAvailable => Available;
        public double RecordSeconds => 8;
        public void Clear() => IsEnrolled = false;
        public async Task<EnrollmentResult> RecordAsync(CancellationToken ct)
        {
            var r = Gate is null ? Result : await Gate.Task;
            if (r.Success) IsEnrolled = true;
            return r;
        }
    }

    private static LiveViewModel Vm(FakeEnrollment? e) => new(new FakeRunner(), enrollment: e) { UseMicrophone = true };

    private const string Hint = "Başlamadan önce 'Sesimi tanıt' ile sesinizi tanıtın — öneriler yalnız karşı tarafa göre hesaplanır.";

    [Fact]
    public void Mikrofon_TanitmaYok_BaslatKapali()
    {
        var vm = Vm(new FakeEnrollment());
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.Equal(Hint, vm.StartHint);
    }

    [Fact]
    public async Task Mikrofon_TanitmaVar_BaslatAcik()
    {
        var vm = Vm(new FakeEnrollment());
        await vm.EnrollCommand.ExecuteAsync(null);
        Assert.True(vm.StartCommand.CanExecute(null));
        Assert.Equal(string.Empty, vm.StartHint);
    }

    [Fact]
    public void Kamera_TanitmaYok_BaslatKapali()
    {
        var vm = new LiveViewModel(new FakeRunner(), enrollment: new FakeEnrollment()) { UseCamera = true };
        Assert.False(vm.StartCommand.CanExecute(null));
    }

    [Fact]
    public void Dosya_TanitmaGerekmez()
    {
        var vm = new LiveViewModel(new FakeRunner(), enrollment: new FakeEnrollment());
        Assert.Equal(string.Empty, vm.StartHint);
        vm.UseMicrophone = true;
        Assert.Equal(Hint, vm.StartHint);
        vm.UseMicrophone = false;
        Assert.Equal(string.Empty, vm.StartHint);
    }

    [Fact]
    public void TanitmaImkansiz_BaslatAcik()
    {
        var vm = Vm(new FakeEnrollment { Available = false });
        Assert.True(vm.StartCommand.CanExecute(null));
        Assert.Equal(string.Empty, vm.StartHint);
    }

    [Fact]
    public async Task TanitmaSilinince_BaslatKapanir()
    {
        var vm = Vm(new FakeEnrollment());
        await vm.EnrollCommand.ExecuteAsync(null);
        vm.ClearEnrollmentCommand.Execute(null);
        Assert.False(vm.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task SupheliTanitma_BaslatAcik()
    {
        var vm = Vm(new FakeEnrollment { Result = new(true, true, 6, "Kayıtta birden fazla ses var gibi.") });
        await vm.EnrollCommand.ExecuteAsync(null);
        Assert.True(vm.StartCommand.CanExecute(null));
    }

    [Fact]
    public void ModDegisince_CanExecuteChanged()
    {
        var vm = new LiveViewModel(new FakeRunner(), enrollment: new FakeEnrollment());
        int raised = 0;
        vm.StartCommand.CanExecuteChanged += (_, _) => raised++;
        vm.UseMicrophone = true;
        Assert.True(raised > 0);
    }

    [Fact]
    public void TanitmaServisiYoksa_DugmeGorunmez()
    {
        var vm = Vm(null);

        Assert.False(vm.ShowEnrollment);
        Assert.Equal(string.Empty, vm.EnrollmentStatus);
    }

    [Fact]
    public void DosyaModunda_DugmeGorunmez_CanliModdaGorunur()
    {
        var vm = new LiveViewModel(new FakeRunner(), enrollment: new FakeEnrollment());
        Assert.False(vm.ShowEnrollment);   // varsayılan: dosya modu

        vm.UseCamera = true;

        Assert.True(vm.ShowEnrollment);
        Assert.Contains("karşı tarafın sesine göre", vm.EnrollmentStatus);
    }

    [Fact]
    public async Task BasariliTanitma_DurumuVeDugmeyiGunceller()
    {
        var vm = Vm(new FakeEnrollment());
        Assert.Equal("Sesimi tanıt", vm.EnrollButtonText);

        await vm.EnrollCommand.ExecuteAsync(null);

        Assert.Contains("Sesiniz tanındı", vm.EnrollmentStatus);
        Assert.False(vm.EnrollmentWarning);
        Assert.Equal("Sesimi yeniden tanıt", vm.EnrollButtonText);
    }

    [Fact]
    public async Task SupheliTanitma_UyariGosterir()
    {
        var e = new FakeEnrollment { Result = new(true, true, 6, "Kayıtta birden fazla ses var gibi.") };
        var vm = Vm(e);

        await vm.EnrollCommand.ExecuteAsync(null);

        Assert.True(vm.EnrollmentWarning);
        Assert.Contains("birden fazla ses", vm.EnrollmentStatus);
    }

    [Fact]
    public async Task KayitSirasinda_BaslatVeTanitKapali()
    {
        var e = new FakeEnrollment { Gate = new() };
        var vm = Vm(e);

        var run = vm.EnrollCommand.ExecuteAsync(null);

        Assert.True(vm.IsEnrolling);
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.Equal("Kaydediliyor…", vm.EnrollButtonText);
        e.Gate.SetResult(new(true, false, 6, "ok"));
        await run;
        Assert.True(vm.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task TanitmayiSil_DugmeVeDurumGuncellenir()
    {
        var vm = Vm(new FakeEnrollment());
        Assert.False(vm.CanClearEnrollment);
        await vm.EnrollCommand.ExecuteAsync(null);
        Assert.True(vm.CanClearEnrollment);

        vm.ClearEnrollmentCommand.Execute(null);

        Assert.False(vm.CanClearEnrollment);
        Assert.Equal("Sesimi tanıt", vm.EnrollButtonText);
        Assert.Contains("silindi", vm.EnrollmentStatus);
    }

    [Fact]
    public async Task TanitmaIkinciGorusmedeKullanilirsa_Uyarir_IlkindeUyarmaz()
    {
        var vm = Vm(new FakeEnrollment());
        await vm.EnrollCommand.ExecuteAsync(null);

        vm.StartCommand.Execute(null);
        await vm.WaitForIdleAsync();
        Assert.False(vm.EnrollmentWarning);

        vm.StartCommand.Execute(null);
        await vm.WaitForIdleAsync();
        Assert.True(vm.EnrollmentWarning);
        Assert.Contains("Önceki görüşmenin ses tanıtması", vm.EnrollmentStatus);
    }

    [Fact]
    public async Task YenidenTanitma_UyariyiKaldirir()
    {
        var vm = Vm(new FakeEnrollment());
        await vm.EnrollCommand.ExecuteAsync(null);
        vm.StartCommand.Execute(null); await vm.WaitForIdleAsync();

        await vm.EnrollCommand.ExecuteAsync(null);
        vm.StartCommand.Execute(null); await vm.WaitForIdleAsync();

        Assert.False(vm.EnrollmentWarning);
    }
}
