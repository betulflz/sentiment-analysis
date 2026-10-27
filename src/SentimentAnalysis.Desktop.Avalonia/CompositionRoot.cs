using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Application.Vision;
using SentimentAnalysis.Desktop;
using SentimentAnalysis.Infrastructure.Audio;
using SentimentAnalysis.Infrastructure.Diarization;
using SentimentAnalysis.Infrastructure.Emotion;
using SentimentAnalysis.Infrastructure.Storage;
using SentimentAnalysis.Infrastructure.Stt;
using SentimentAnalysis.Infrastructure.Vad;
using SentimentAnalysis.Infrastructure.Vision;

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
        // Teşhis kanalı: üretimde SESSİZ. Paketlenmiş uygulamada (macOS .app, Windows GUI)
        // bağlı bir konsol olmadığından konsola yazmak maliyet ödeyip fayda sağlamaz; ayrıca
        // ses geri çağrımı gibi gerçek zamanlı yollarda I/O ses kopmasına yol açar.
        // Bir sorunu izlemek gerektiğinde SENTIMENT_DIAG=1 ile açılır ve çıktı Trace'e gider
        // (hata ayıklayıcı, DebugView veya eklenecek bir dinleyici) — her platformda çalışır.
        services.AddSingleton<IDiagnosticLog>(_ =>
            Environment.GetEnvironmentVariable("SENTIMENT_DIAG") == "1"
                ? new TraceDiagnosticLog()
                : NullDiagnosticLog.Instance);

        services.AddSingleton<Func<IAudioCaptureSource?>>(sp =>
        {
            var selected = sp.GetRequiredService<ISelectedAudio>();
            var mode = sp.GetRequiredService<ICaptureModeState>();
            var audioSourceFactory = sp.GetRequiredService<IAudioSourceFactory>();
            var log = sp.GetRequiredService<IDiagnosticLog>();
            return () => mode.Mode switch
            {
                CaptureMode.Mikrofon or CaptureMode.Kamera => new MicrophoneCaptureSource(log),
                _ when selected.Path is null => null,
                _ when IsVideo(selected.Path) => new FfmpegRealtimeAudioSource(selected.Path),
                _ => audioSourceFactory.Create(selected.Path),
            };
        });

        // Görüntü kanalı (11b): kamera modunda kamera, dosya modunda video uzantılı dosya. Ses dosyası ve
        // mikrofon modunda görüntü YOK (davranış değişmez). Modeller ilk görüntülü koşuda yüklenir.
        services.AddSingleton<Func<IVideoCaptureSource?>>(sp =>
        {
            var selected = sp.GetRequiredService<ISelectedAudio>();
            var mode = sp.GetRequiredService<ICaptureModeState>();
            var cfg = sp.GetRequiredService<AppConfig>().Vision;
            return () => mode.Mode switch
            {
                CaptureMode.Kamera => FfmpegVideoCaptureSource.ForCamera(cfg),
                CaptureMode.Dosya when selected.Path is not null && IsVideo(selected.Path) =>
                    FfmpegVideoCaptureSource.ForFile(selected.Path, cfg),
                _ => null,
            };
        });
        services.AddSingleton<Func<VisionAnalyzer>>(sp =>
        {
            var cfg = sp.GetRequiredService<AppConfig>().Vision;
            return () =>
            {
                var detector = new YuNetFaceDetector(cfg);
                try
                {
                    return new VisionAnalyzer(detector, new FerPlusExpressionModel(cfg), new VisionTimeline(cfg), cfg);
                }
                catch
                {
                    detector.Dispose();
                    throw;
                }
            };
        });
        services.AddSingleton<IAnalysisRunner>(sp => new PipelineAnalysisRunner(
            sp.GetRequiredService<EmotionPipeline>(),
            sp.GetRequiredService<Func<IAudioCaptureSource?>>(),
            sp.GetRequiredService<AppConfig>(),
            sp.GetRequiredService<ICaptureModeState>(),
            sp.GetService<ISessionAudioSink>(),
            sp.GetService<IDiarizationState>(),
            sp.GetRequiredService<Func<IVideoCaptureSource?>>(),
            sp.GetRequiredService<Func<VisionAnalyzer>>()));

        // Konuşmacı ayrımı (10b): motor + arka plan geçişi + UI'nin dokunduğu servis.
        // Model YÜKLENMEZ; ilk gerçek çağrıya kadar maliyeti sıfırdır (özellik kapalıysa hiç).
        services.AddSingleton<IDiarizationState, DiarizationState>();
        services.AddSherpaDiarization();
        services.AddSingleton<DiarizationJob>();
        services.AddSingleton<SpeakerProsodyAnalyzer>();
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
            action => global::Avalonia.Threading.Dispatcher.UIThread.Post(action),
            sp.GetRequiredService<IDecisionStore>()));

        return services.BuildServiceProvider();
    }

    private static bool IsVideo(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mov" or ".m4v";
}
