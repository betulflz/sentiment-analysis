using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Desktop;
using SentimentAnalysis.Infrastructure.Audio;
using SentimentAnalysis.Infrastructure.Diarization;
using SentimentAnalysis.Infrastructure.Emotion;
using SentimentAnalysis.Infrastructure.Storage;
using SentimentAnalysis.Infrastructure.Stt;
using SentimentAnalysis.Infrastructure.Vad;

namespace SentimentAnalysis.Desktop.Avalonia;

/// <summary>Uygulama DI kompozisyon kökü: tüm Application/Infrastructure dilimleri + Desktop ViewModel'leri
/// tek bir <see cref="IServiceProvider"/>'da birleştirir. Avalonia'sız (Desktop kütüphanesi) — burada
/// yalnızca somut implementasyonlar bağlanır.</summary>
public static class CompositionRoot
{
    public static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services
            .AddApplicationCore()
            .AddFusion()
            .AddStt()
            .AddProsodyEmotion()
            .AddTextEmotion()
            .AddSileroVad()
            .AddPipeline()
            .AddSqliteStorage(); // AddPipeline'ın InMemorySessionStore TryAdd'ini ezer (kalıcı SQLite kazanır)

        // Dosya seçimi (08c): kullanıcı bir ses dosyası seçer, ISelectedAudio bunu Live/PipelineAnalysisRunner
        // arasında paylaşır. Kaynak fabrikası seçili dosyayı gerçek IAudioCaptureSource'a çevirir (afconvert).
        services.AddSingleton<ISelectedAudio, SelectedAudio>();
        services.AddSingleton<IRuntimePlatform, RuntimePlatform>();
        services.AddSingleton<IAudioToolResolver, AudioToolResolver>();
        services.AddSingleton<IExternalProcessRunner, ExternalProcessRunner>();
        services.AddSingleton<CrossPlatformAudioConverter>();
        services.AddSingleton<IAudioSourceFactory, AfconvertAudioSourceFactory>();
        services.AddSingleton<IAudioFilePicker>(_ => new AvaloniaAudioFilePicker(() => MainWindowAccessor.Current));
        services.AddSingleton<ICaptureModeState, CaptureModeState>();
        // NOT: LiveRecordingSession / WavRecorder BİLEREK bağlanmıyor. Bunlar "önce WAV'a kaydet,
        // Durdur'dan sonra dosyayı analiz et" yaklaşımına aittir; bu proje master §D2 gereği
        // chunked near-real-time akış kullanır (konuşurken analiz). Sınıflar depoda kalıyor
        // (CrossPlatformAudioConverter onları kullanıyor ve testlidirler), ama canlı analiz
        // yolu MicrophoneCaptureSource → EmotionPipeline akışıdır.
        services.AddSingleton<Func<IAudioCaptureSource?>>(sp =>
        {
            var selected = sp.GetRequiredService<ISelectedAudio>();
            var mode = sp.GetRequiredService<ICaptureModeState>();
            var audioSourceFactory = sp.GetRequiredService<IAudioSourceFactory>();
            return () => mode.Mode == CaptureMode.Mikrofon
                ? new MicrophoneCaptureSource()
                : selected.Path is null ? null : audioSourceFactory.Create(selected.Path);
        });
        services.AddSingleton<IAnalysisRunner, PipelineAnalysisRunner>();

        // Konuşmacı ayrımı (10b): motor + arka plan geçişi + UI'nin dokunduğu servis.
        // Model YÜKLENMEZ; ilk gerçek çağrıya kadar maliyeti sıfırdır (özellik kapalıysa hiç).
        services.AddSingleton<IDiarizationState, DiarizationState>();
        services.AddSherpaDiarization();
        services.AddSingleton<DiarizationJob>();
        services.AddSingleton<ISpeakerAnalysis, SpeakerAnalysisService>();

        services.AddSingleton<LiveViewModel>(sp => new LiveViewModel(
            sp.GetRequiredService<IAnalysisRunner>(),
            sp.GetRequiredService<IAudioFilePicker>(),
            sp.GetRequiredService<ISelectedAudio>(),
            sp.GetRequiredService<AppConfig>(),
            sp.GetRequiredService<ICaptureModeState>(),
            sp.GetRequiredService<IDiarizationState>()));
        services.AddSingleton<ReportViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>(sp =>
        {
            var config = sp.GetRequiredService<AppConfig>();
            // Kalıcı ayar kaydı 08b'de; bu tur onSave no-op (yalnızca in-memory config nesnesini günceller).
            return new SettingsViewModel(config,
                updated => System.Diagnostics.Debug.WriteLine($"Ayarlar kaydedildi (bu tur no-op): {updated}"),
                sp.GetRequiredService<IDiarizationState>());
        });
        // MainViewModel'e UI-thread dağıtıcısı EXPLICIT geçilir: Desktop projesi Avalonia'sız
        // (headless test edilebilir) kalmalı, ama arka plan konuşmacı geçişi bittiğinde rapor
        // güncellemesi UI thread'inde olmalı (08e Fix 4'ün aynı dersi).
        services.AddSingleton<MainViewModel>(sp => new MainViewModel(
            sp.GetRequiredService<LiveViewModel>(),
            sp.GetRequiredService<ReportViewModel>(),
            sp.GetRequiredService<HistoryViewModel>(),
            sp.GetRequiredService<SettingsViewModel>(),
            sp.GetRequiredService<ISpeakerAnalysis>(),
            sp.GetRequiredService<AppConfig>(),
            action => global::Avalonia.Threading.Dispatcher.UIThread.Post(action)));

        return services.BuildServiceProvider();
    }
}
