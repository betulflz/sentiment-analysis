using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Application.Runtime;
using SentimentAnalysis.Application.Vision;
using SentimentAnalysis.Desktop;
using SentimentAnalysis.Infrastructure.Audio;
using SentimentAnalysis.Infrastructure.Storage;
using SentimentAnalysis.Infrastructure.Vision;

namespace SentimentAnalysis.Desktop.Avalonia;

/// <summary>Uygulama DI kompozisyon kökü: tüm Application/Infrastructure dilimleri + Desktop ViewModel'leri
/// tek bir <see cref="IServiceProvider"/>'da birleştirir. Avalonia'sız (Desktop kütüphanesi) — burada
/// yalnızca somut implementasyonlar bağlanır.</summary>
public static class CompositionRoot
{
    public static IServiceProvider BuildServiceProvider(AppConfig? config = null)
    {
        var services = new ServiceCollection();

        services
            .AddApplicationCore(config)
            .AddFusion()
            .AddSqliteStorage();
        services.AddSingleton<ModelWorkQueue>();

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
        // Native model graphs belong to one job, not to the application root.
        services.AddSingleton<ResourceManagedAnalysisRunner>(sp => new ResourceManagedAnalysisRunner(
            sp.GetRequiredService<ModelWorkQueue>(), () => ModelSessions.CreateAnalysis(sp)));
        services.AddSingleton<IAnalysisRunner>(sp => sp.GetRequiredService<ResourceManagedAnalysisRunner>());
        services.AddSingleton<IDiarizationState, DiarizationState>();
        services.AddSingleton<ISpeakerAnalysis>(sp => new ResourceManagedSpeakerAnalysis(
            sp.GetRequiredService<IDiarizationState>(), sp.GetRequiredService<ModelWorkQueue>(),
            () => ModelSessions.CreateSpeaker(sp)));
        // 12: canlı "Yöneten / Karşı taraf" — tanıtma örneği uygulama ömrü boyunca bellekte (diske yazılmaz).
        // Tanıtma analizden bağımsız olduğundan ses imzası (TitaNet) ve VAD modelleri RoleModels'te tembel yüklenir,
        // kapanışta kapatılır; kök sağlayıcıda IVadGate/ISpeakerEmbedder olarak kayıtlı DEĞİLDİR.
        services.AddSingleton<RoleModels>(sp => new RoleModels(sp.GetRequiredService<AppConfig>()));
        services.AddSingleton<SpeakerRoleTracker>(sp =>
        {
            var (embedder, vad) = sp.GetRequiredService<RoleModels>().Create();
            return new SpeakerRoleTracker(embedder, vad, sp.GetRequiredService<AppConfig>());
        });
        services.AddSingleton<IVoiceEnrollment, MicrophoneVoiceEnrollment>();
        // Karşı tarafa odak (2026-11-13): dosya modunda analizden önce konuşmacı ayrımı. Ses bellekte çözülür;
        // ayrım modeli ortak kuyrukta kendi iş oturumunda yüklenip kapatılır (kökte model yok).
        services.AddSingleton<IAudioFileDecoder, FfmpegAudioFileDecoder>();
        services.AddSingleton<ISpeakerPreparation>(sp => new ResourceManagedSpeakerPreparation(
            sp.GetRequiredService<IAudioFileDecoder>(), sp.GetRequiredService<ModelWorkQueue>(),
            () => ModelSessions.CreatePreparation(sp), sp.GetRequiredService<AppConfig>()));

        services.AddSingleton<LiveViewModel>(sp => new LiveViewModel(
            sp.GetRequiredService<IAnalysisRunner>(),
            sp.GetRequiredService<IAudioFilePicker>(),
            sp.GetRequiredService<ISelectedAudio>(),
            sp.GetRequiredService<AppConfig>(),
            sp.GetRequiredService<ICaptureModeState>(),
            sp.GetRequiredService<IDiarizationState>(),
            sp.GetRequiredService<IVoiceEnrollment>()));
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
