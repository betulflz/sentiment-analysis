# Alt-Proje 08c: Kullanılabilir Dosya-Analiz Demosu — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`).

**Goal:** UI'dan bir ses dosyası (mp3/m4a/wav) seçip **gerçekten analiz ettirmek**: Dosya Seç → Başlat → transkript+duygu+uyumsuzluk canlı çizelgeye akar → bitince Rapor gösterilir + Geçmiş yenilenir. mp3/m4a macOS `afconvert` ile otomatik 16kHz WAV'a çevrilir.

**Architecture:** MVVM sınırı korunur. Yeni soyutlamalar `Desktop` kütüphanesinde (test edilebilir): `IAudioFilePicker`, `ISelectedAudio`, `IAudioSourceFactory`. `LiveViewModel` bunlarla dosya seçip Başlat'ı etkinleştirir; koşu bitince `MeetingSummary`'yi Rapor'a verir + Geçmiş'i yeniler. Avalonia-özel implementasyonlar (dosya seçici StorageProvider, afconvert Process) `Desktop.Avalonia`'da (fonksiyonel, görsel QA kullanıcıda).

**Tech Stack:** .NET 9 · CommunityToolkit.Mvvm 8.4.2 · Avalonia 11.3.1 · macOS `afconvert` (yerleşik).

## Global Constraints
- `net9.0`, Nullable+ImplicitUsings. **git YOK** → her task `dotnet build && dotnet test`.
- **0 uyarı** şart (CS1998 için test fake'lerinde `await Task.Yield()`).
- MVVM: ViewModel ML/IO'ya doğrudan bağlanmaz; yalnızca soyutlamalar. ViewModel'ler `CommunityToolkit.Mvvm` → headless TDD.
- **Görsel QA kullanıcıda** (Task 2 Avalonia/axaml render bu ortamda doğrulanamaz — dürüstlük).
- Mevcut tipler değişmez; mevcut `LiveViewModel`/`ReportViewModel`/`MainViewModel` **additive** genişletilir (mevcut testler kırılmaz).
- **mp3/m4a decode macOS-özel (`afconvert`)** — dokümante; Windows decode sonraya.

### Mevcut tipler/imzalar (tüketilir)
```csharp
// Desktop (mevcut)
public sealed partial class LiveViewModel : ObservableObject, IProgress<ChunkResult> { LiveViewModel(IAnalysisRunner runner); ObservableCollection<ChunkRowVm> Timeline; ... IsCapturing; StartCommand; StopCommand; void Report(ChunkResult); Task WaitForIdleAsync(); }
public interface IAnalysisRunner { Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct); }
public sealed partial class ReportViewModel : ObservableObject { ReportViewModel(IMeetingAggregator); void Load(MeetingSession); bool HasReport; string DominantEmotion; string TurkishSummary; double ValenceTrend; double Volatility; double AverageCongruence; IReadOnlyList<EmotionShareVm> TimeShare; IReadOnlyList<ChunkRowVm> PeakMoments; }
public sealed partial class MainViewModel : ObservableObject { MainViewModel(LiveViewModel, ReportViewModel, HistoryViewModel, SettingsViewModel); object CurrentView; ShowLiveCommand/ShowReportCommand/ShowHistoryCommand/ShowSettingsCommand; }
public sealed partial class HistoryViewModel : ObservableObject { HistoryViewModel(ISessionStore); IAsyncRelayCommand RefreshCommand; ObservableCollection<SessionRowVm> Sessions; }
// Core: MeetingSummary(DominantEmotion, TimeShareByEmotion, ValenceTrend, EmotionalVolatility, PeakMoments, AverageVa, AverageCongruence, TurkishSummary)
// Application: IAudioCaptureSource; FileCaptureSource(string path) : IAudioCaptureSource (WAV; 16k mono'ya normalize eder)
```

---

### Task 1: Dosya-seçim soyutlamaları + ViewModel wiring (Desktop, TDD)

**Files:**
- Create: `src/SentimentAnalysis.Desktop/IAudioFilePicker.cs`, `ISelectedAudio.cs`, `SelectedAudio.cs`, `IAudioSourceFactory.cs`
- Modify: `src/SentimentAnalysis.Desktop/LiveViewModel.cs`, `ReportViewModel.cs`, `MainViewModel.cs`
- Create: `tests/SentimentAnalysis.Desktop.Tests/FileAnalysisWiringTests.cs`

**Interfaces:**
- `interface IAudioFilePicker { Task<string?> PickAudioFileAsync(); }`
- `interface ISelectedAudio { string? Path { get; set; } }`; `sealed class SelectedAudio : ISelectedAudio { public string? Path { get; set; } }`
- `interface IAudioSourceFactory { IAudioCaptureSource Create(string path); }`
- `LiveViewModel` YENİ: ctor `(IAnalysisRunner runner, IAudioFilePicker picker, ISelectedAudio selected)`; `[ObservableProperty] string _selectedFile = "(dosya seçilmedi)";`; `SelectFileCommand` (async: picker → set path + SelectedFile + Start CanExecute bildir); `Start` **CanExecute** = `!IsCapturing && selected.Path != null`; koşu bitince `LastSummary` set + `event Action<MeetingSummary>? Completed`.
- `ReportViewModel` YENİ: `void LoadSummary(MeetingSummary s)` (yeniden-agregasyon yapmadan `MeetingSummary`'yi lokalize gösterir).
- `MainViewModel` YENİ: ctor'da `Live.Completed += s => { Report.LoadSummary(s); History.RefreshCommand.Execute(null); CurrentView = Report; }` bağla.

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Desktop.Tests/FileAnalysisWiringTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class FileAnalysisWiringTests
{
    private sealed class FakePicker(string? path) : IAudioFilePicker
    { public Task<string?> PickAudioFileAsync() => Task.FromResult(path); }

    private sealed class FakeRunner : IAnalysisRunner
    {
        public readonly ChunkResult[] Chunks;
        public FakeRunner(params ChunkResult[] c) => Chunks = c;
        public async Task<MeetingSummary> RunAsync(IProgress<ChunkResult> p, CancellationToken ct)
        {
            await Task.Yield();
            foreach (var c in Chunks) p.Report(c);
            return new MeetingSummary(Emotion.Mutlu, new Dictionary<Emotion,double>{[Emotion.Mutlu]=1.0},
                0.3, 0.1, Array.Empty<ChunkResult>(), new ValenceArousal(0.4,0.5), 0.9, "olumlu geçti");
        }
    }

    private static ChunkResult Chunk(double t) =>
        new(TimeSpan.FromSeconds(t), TimeSpan.FromSeconds(t+1),
            new FusedResult(new ValenceArousal(0.5,0.6), Emotion.Mutlu, Emotion.Notr, Confidence.Orta,
                true, false, IncongruencePattern.Yok, 0.9, 0.5, 0.5), $"m{t}");

    [Fact]
    public void Start_DisabledUntilFileSelected()
    {
        var sel = new SelectedAudio();
        var vm = new LiveViewModel(new FakeRunner(), new FakePicker(null), sel);
        Assert.False(vm.StartCommand.CanExecute(null)); // dosya yok → başlatılamaz
    }

    [Fact]
    public async Task SelectFile_SetsPathAndEnablesStart()
    {
        var sel = new SelectedAudio();
        var vm = new LiveViewModel(new FakeRunner(), new FakePicker("/tmp/ses.mp3"), sel);
        await vm.SelectFileCommand.ExecuteAsync(null);
        Assert.Equal("/tmp/ses.mp3", sel.Path);
        Assert.Equal("ses.mp3", vm.SelectedFile);
        Assert.True(vm.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task Start_AfterSelect_RunsAndRaisesCompleted()
    {
        var sel = new SelectedAudio();
        var runner = new FakeRunner(Chunk(0), Chunk(1));
        var vm = new LiveViewModel(runner, new FakePicker("/tmp/ses.mp3"), sel);
        await vm.SelectFileCommand.ExecuteAsync(null);

        MeetingSummary? done = null;
        vm.Completed += s => done = s;
        vm.StartCommand.Execute(null);
        await vm.WaitForIdleAsync();

        Assert.Equal(2, vm.Timeline.Count);
        Assert.NotNull(done);
        Assert.Equal("olumlu geçti", done!.TurkishSummary);
        Assert.False(vm.IsCapturing);
    }

    [Fact]
    public void ReportViewModel_LoadSummary_ShowsLocalizedFields()
    {
        var vm = new ReportViewModel(new NoAggregator());
        var s = new MeetingSummary(Emotion.Kizgin, new Dictionary<Emotion,double>{[Emotion.Kizgin]=0.7,[Emotion.Notr]=0.3},
            -0.5, 0.2, Array.Empty<ChunkResult>(), new ValenceArousal(-0.4,0.7), 0.6, "gergin");
        vm.LoadSummary(s);
        Assert.True(vm.HasReport);
        Assert.Equal("Kızgın", vm.DominantEmotion);
        Assert.Equal("gergin", vm.TurkishSummary);
        Assert.Equal(2, vm.TimeShare.Count);
    }

    private sealed class NoAggregator : IMeetingAggregator
    { public MeetingSummary Aggregate(MeetingSession s) => throw new NotImplementedException(); }

    private sealed class FakeStore : ISessionStore
    { public Task SaveChunkAsync(Guid i, ChunkResult r, CancellationToken c)=>Task.CompletedTask;
      public Task<MeetingSession?> GetSessionAsync(Guid i, CancellationToken c)=>Task.FromResult<MeetingSession?>(null);
      public Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken c)=>Task.FromResult<IReadOnlyList<MeetingSession>>(Array.Empty<MeetingSession>());
      public Task PurgeExpiredAudioAsync(CancellationToken c)=>Task.CompletedTask; }

    [Fact]
    public async Task MainViewModel_AfterRun_ShowsReportAndRefreshesHistory()
    {
        var sel = new SelectedAudio();
        var live = new LiveViewModel(new FakeRunner(Chunk(0)), new FakePicker("/tmp/a.wav"), sel);
        var report = new ReportViewModel(new NoAggregator());
        var history = new HistoryViewModel(new FakeStore());
        var settings = new SettingsViewModel(SentimentAnalysis.Application.Configuration.AppConfig.Default, _ => { });
        var main = new MainViewModel(live, report, history, settings);

        await live.SelectFileCommand.ExecuteAsync(null);
        live.StartCommand.Execute(null);
        await live.WaitForIdleAsync();

        Assert.True(report.HasReport);              // koşu sonrası rapor doldu
        Assert.IsType<ReportViewModel>(main.CurrentView); // rapora geçildi
    }
}
```

- [ ] **Step 2: Başarısız olduğunu doğrula** — FAIL (yeni tipler/üyeler yok). (Not: mevcut `LiveViewModel(IAnalysisRunner)` tek-parametreli ctor'a bağlı ESKİ testler var — ctor imzası değişince onlar kırılır. **Çözüm:** yeni parametreleri opsiyonel yap: `LiveViewModel(IAnalysisRunner runner, IAudioFilePicker? picker = null, ISelectedAudio? selected = null)` → eski testler (yalnız runner) derlenmeye devam eder; picker/selected null ise SelectFile no-op, Start eski davranışta [selected null → CanExecute her zaman true değil; bkz. aşağıda].)

- [ ] **Step 3: Soyutlamaları yaz**

`IAudioFilePicker.cs`, `ISelectedAudio.cs` + `SelectedAudio.cs`, `IAudioSourceFactory.cs`:

```csharp
namespace SentimentAnalysis.Desktop;
public interface IAudioFilePicker { Task<string?> PickAudioFileAsync(); }
```
```csharp
namespace SentimentAnalysis.Desktop;
public interface ISelectedAudio { string? Path { get; set; } }
public sealed class SelectedAudio : ISelectedAudio { public string? Path { get; set; } }
```
```csharp
using SentimentAnalysis.Application.Abstractions;
namespace SentimentAnalysis.Desktop;
/// <summary>Seçili dosyadan ses kaynağı üretir (WAV doğrudan; mp3/m4a app-katmanında afconvert ile). App implemente eder.</summary>
public interface IAudioSourceFactory { IAudioCaptureSource Create(string path); }
```

- [ ] **Step 4: `LiveViewModel`'i genişlet (additive, opsiyonel parametreli)**

`LiveViewModel.cs` — ctor'a opsiyonel `picker`/`selected` ekle; `SelectedFile` observable; `SelectFileCommand`; `Start` için `CanExecute` = `!IsCapturing && (_selected?.Path != null)`; `IsCapturing` değişince + SelectFile sonrası `StartCommand.NotifyCanExecuteChanged()`; koşu sonu `Completed?.Invoke(summary)`. Örnek:

```csharp
private readonly IAudioFilePicker? _picker;
private readonly ISelectedAudio? _selected;

public event Action<MeetingSummary>? Completed;
[ObservableProperty] private string _selectedFile = "(dosya seçilmedi)";

public LiveViewModel(IAnalysisRunner runner, IAudioFilePicker? picker = null, ISelectedAudio? selected = null)
{ _runner = runner; _picker = picker; _selected = selected; }

[RelayCommand]
private async Task SelectFileAsync()
{
    if (_picker is null) return;
    var path = await _picker.PickAudioFileAsync().ConfigureAwait(true);
    if (string.IsNullOrEmpty(path)) return;
    if (_selected is not null) _selected.Path = path;
    SelectedFile = System.IO.Path.GetFileName(path);
    StartCommand.NotifyCanExecuteChanged();
}

[RelayCommand(CanExecute = nameof(CanStart))]
private void Start()
{
    if (IsCapturing) return;
    IsCapturing = true;
    _cts = new CancellationTokenSource();
    _run = RunAsync(_cts.Token);
}
private bool CanStart() => !IsCapturing && _selected?.Path != null;

private async Task RunAsync(CancellationToken ct)
{
    try
    {
        var summary = await _runner.RunAsync(this, ct).ConfigureAwait(true);
        Completed?.Invoke(summary);
    }
    catch (OperationCanceledException) { }
    catch (Exception ex) { CongruenceBadge = "Hata: " + ex.Message; } // sessiz yutma yok (08 deferred fix)
    finally { IsCapturing = false; StartCommand.NotifyCanExecuteChanged(); }
}
```
> Not: `IsCapturing` set edildiğinde de `StartCommand.NotifyCanExecuteChanged()` çağrılmalı (RelayCommand CanExecute güncellensin). `[ObservableProperty]` ile üretilen `OnIsCapturingChanged` partial method'a ekle VEYA Start/RunAsync içinde elle çağır (yukarıda finally + Start'ta var). Eski tek-parametreli testler: `_selected` null → `CanStart` false; o testler Start'ı doğrudan `Execute` çağırıyorsa CanExecute'e bakmadan çalışır (RelayCommand.Execute CanExecute false olsa da gövdeyi çağırır mı? CommunityToolkit RelayCommand.Execute CanExecute'i kontrol ETMEZ, doğrudan çağırır) → eski testler geçmeye devam eder. **Doğrula:** eski `LiveViewModelTests` yeşil kalmalı.

- [ ] **Step 5: `ReportViewModel.LoadSummary` + `MainViewModel` wiring**

`ReportViewModel.cs`'e ekle (mevcut `Load(MeetingSession)`'ı KORU):
```csharp
public void LoadSummary(MeetingSummary s)
{
    DominantEmotion = EmotionLocalizer.Emotion(s.DominantEmotion);
    TurkishSummary = s.TurkishSummary;
    ValenceTrend = s.ValenceTrend; Volatility = s.EmotionalVolatility; AverageCongruence = s.AverageCongruence;
    TimeShare = s.TimeShareByEmotion.Select(kv => new EmotionShareVm(kv.Key, kv.Value)).ToList();
    PeakMoments = s.PeakMoments.Select(c => new ChunkRowVm(c)).ToList();
    HasReport = true;
}
```
`MainViewModel.cs` ctor'una ekle (mevcut atamaları KORU):
```csharp
Live.Completed += s =>
{
    Report.LoadSummary(s);
    History.RefreshCommand.Execute(null);
    CurrentView = Report;
};
```

- [ ] **Step 6: Testin geçtiğini + eski testlerin kırılmadığını doğrula** — Run: `dotnet build && dotnet test`. Expected: 0 uyarı; yeni FileAnalysisWiring testleri + eski tüm testler (194) PASS.

---

### Task 2: Avalonia dosya-seçici + afconvert kaynak + shell wiring (Desktop.Avalonia, fonksiyonel + görsel QA)

> Görsel/platform task — unit-testli değil. Doğrulama = `dotnet build` (0 uyarı/hata) + kullanıcı görsel QA.

**Files:**
- Create: `src/SentimentAnalysis.Desktop.Avalonia/AvaloniaAudioFilePicker.cs`, `AfconvertAudioSourceFactory.cs`
- Modify: `src/SentimentAnalysis.Desktop.Avalonia/CompositionRoot.cs`, `PipelineAnalysisRunner.cs`, `Views/LiveView.axaml`, `Views/SettingsView.axaml` (+ .cs gerekiyorsa)

**Interfaces:**
- `AvaloniaAudioFilePicker : IAudioFilePicker` — `TopLevel.StorageProvider.OpenFilePickerAsync` ile mp3/m4a/aac/wav filtreli tek dosya; seçilen `IStorageFile.TryGetLocalPath()` döner. (TopLevel'a erişim: ana pencere referansı DI'da tutulur veya `App` üzerinden.)
- `AfconvertAudioSourceFactory : IAudioSourceFactory` — `.wav` → `new FileCaptureSource(path)`; `.mp3/.m4a/.aac` → temp `{tmp}/{guid}.wav`'a `afconvert -f WAVE -d LEI16@16000 -c 1 <in> <out>` (Process, sıfır çıkış-kodu kontrolü; hata → net exception) → `new FileCaptureSource(tempWav)`. Desteklenmeyen uzantı → `NotSupportedException`.

- [ ] **Step 1: `AfconvertAudioSourceFactory`'ı yaz**

`src/SentimentAnalysis.Desktop.Avalonia/AfconvertAudioSourceFactory.cs`:
```csharp
using System.Diagnostics;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Desktop;
using SentimentAnalysis.Infrastructure.Audio;

namespace SentimentAnalysis.Desktop.Avalonia;

/// <summary>Seçili ses dosyasından <see cref="IAudioCaptureSource"/> üretir. WAV doğrudan; mp3/m4a/aac
/// macOS yerleşik `afconvert` ile geçici 16 kHz mono WAV'a çevrilir (Windows decode 05b). Saf değil (Process/IO).</summary>
public sealed class AfconvertAudioSourceFactory : IAudioSourceFactory
{
    public IAudioCaptureSource Create(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".wav") return new FileCaptureSource(path);
        if (ext is ".mp3" or ".m4a" or ".aac")
        {
            var outWav = Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N") + ".wav");
            var psi = new ProcessStartInfo("/usr/bin/afconvert")
            {
                RedirectStandardError = true, UseShellExecute = false,
            };
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("WAVE");
            psi.ArgumentList.Add("-d"); psi.ArgumentList.Add("LEI16@16000");
            psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("1");
            psi.ArgumentList.Add(path); psi.ArgumentList.Add(outWav);
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0 || !File.Exists(outWav))
                throw new InvalidOperationException($"afconvert başarısız (çıkış {p.ExitCode}): {err}");
            return new FileCaptureSource(outWav);
        }
        throw new NotSupportedException($"Desteklenmeyen ses formatı: {ext}. Desteklenen: wav, mp3, m4a, aac.");
    }
}
```

- [ ] **Step 2: `AvaloniaAudioFilePicker`'ı yaz**

`TopLevel` erişimi için ana pencere referansını DI'da tut. `AvaloniaAudioFilePicker(Func<TopLevel?> topLevelAccessor)`; `PickAudioFileAsync`: `var tl = topLevelAccessor(); if (tl is null) return null; var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, Title = "Ses dosyası seç", FileTypeFilter = new[] { new FilePickerFileType("Ses") { Patterns = new[]{"*.mp3","*.m4a","*.aac","*.wav"} } } }); return files.Count > 0 ? files[0].TryGetLocalPath() : null;`

- [ ] **Step 3: `CompositionRoot` + `PipelineAnalysisRunner` + `LiveViewModel` wiring**

- `CompositionRoot`: `AddSingleton<ISelectedAudio, SelectedAudio>()`, `AddSingleton<IAudioSourceFactory, AfconvertAudioSourceFactory>()`, `AddSingleton<IAudioFilePicker, AvaloniaAudioFilePicker>()` (TopLevel accessor MainWindow'dan), `LiveViewModel`'i `(IAnalysisRunner, IAudioFilePicker, ISelectedAudio)` ile çöz.
- `PipelineAnalysisRunner`: `sourceFactory` artık `() => { var sel = selected.Path; return sel is null ? null : audioSourceFactory.Create(sel); }` (DI'dan `ISelectedAudio` + `IAudioSourceFactory` al). Böylece Başlat seçili dosyayı analiz eder.
- `MainWindow` `TopLevel` accessor'ı: pencere oluşunca `AvaloniaAudioFilePicker`'a referans ver (veya `App.MainWindow` statik/servis).

- [ ] **Step 4: `LiveView.axaml` — "Dosya Seç" butonu + seçili dosya etiketi**

Mevcut Başlat/Durdur'un yanına `<Button Content="Dosya Seç" Command="{Binding SelectFileCommand}"/>` + `<TextBlock Text="{Binding SelectedFile}"/>`. Başlat `Command="{Binding StartCommand}"` zaten CanExecute ile pasif/aktif olur. Sade, hizalı düzen (frontend-design: net boşluk/tipografi).

- [ ] **Step 5: `SettingsView` "Kaydet" gerçek uygulansın (opsiyonel, minimal)** — `SettingsViewModel` `onSave` callback'i composition root'ta çalışan config'i güncelleyecek şekilde bağla (disk kalıcılığı 08b/sonra; bu tur runtime'a uygula veya no-op bırak + not).

- [ ] **Step 6: Derlenirlik + kullanıcı görsel QA** — Run: `dotnet build` → 0 uyarı/0 hata; `dotnet test` → 194 hâlâ yeşil. Kullanıcı `dotnet run` ile: Dosya Seç → mp3 seç → Başlat → çizelge dolar → Rapor açılır. **Rapora "görsel QA + gerçek dosyayla uçtan-uca kullanıcıda" yaz.**

---

## Self-Review
- Spec: dosya seç (mp3/wav) → analiz → canlı çizelge → rapor + geçmiş. Task 1 (wiring, TDD) + Task 2 (Avalonia+afconvert, fonksiyonel). ✔
- MVVM korunur: ViewModel yalnız soyutlamalar; afconvert/StorageProvider app-katmanında. ✔
- Additive: opsiyonel ctor parametreleri → eski 194 test kırılmaz (Step 4 notu). ✔
- Dürüstlük: Task 2 görsel QA kullanıcıda; mp3 macOS-özel (afconvert) dokümante. Hata artık yutulmuyor (CongruenceBadge'e yazılır). ✔
- Placeholder yok; kod blokları tam. Risk: `IStorageFile.TryGetLocalPath()` sandbox'ta null olabilir (macOS'ta normalde yol döner); afconvert Process çıkış-kodu kontrol edilir.

---

## Post-Review (uygulama sonrası)
- Task 1 (TDD): dosya-seçim wiring + eski testler korundu (opsiyonel ctor; CommunityToolkit Execute() CanExecute'i kontrol etmez). BONUS: 06 flaky iptal testi `ThrowsAsync`→`ThrowsAnyAsync` (TaskCanceledException alt-sınıf).
- Task 2 (Avalonia, görsel QA): `AfconvertAudioSourceFactory` (mp3/m4a/aac→afconvert 16k mono wav→FileCaptureSource; wav doğrudan), `AvaloniaAudioFilePicker` (StorageProvider + `MainWindowAccessor.Current` TopLevel), CompositionRoot sourceFactory = seçili dosya, LiveView "Dosya Seç" butonu. Tüm çözüm 0 uyarı/0 hata.
- **Nihai: 199 test geçiyor (modeller kurulu, env set), 0 atlanan, 0 uyarı.** mp3 macOS-özel (afconvert). Görsel + gerçek-dosya uçtan-uca QA kullanıcıda.
