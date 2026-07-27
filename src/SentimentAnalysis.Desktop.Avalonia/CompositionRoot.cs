using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Desktop;
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
        services.AddSingleton<IAudioSourceFactory, AfconvertAudioSourceFactory>();
        services.AddSingleton<IAudioFilePicker>(_ => new AvaloniaAudioFilePicker(() => MainWindowAccessor.Current));
        services.AddSingleton<Func<IAudioCaptureSource?>>(sp =>
        {
            var selected = sp.GetRequiredService<ISelectedAudio>();
            var audioSourceFactory = sp.GetRequiredService<IAudioSourceFactory>();
            return () => selected.Path is null ? null : audioSourceFactory.Create(selected.Path);
        });
        services.AddSingleton<IAnalysisRunner, PipelineAnalysisRunner>();

        services.AddSingleton<LiveViewModel>(sp => new LiveViewModel(
            sp.GetRequiredService<IAnalysisRunner>(),
            sp.GetRequiredService<IAudioFilePicker>(),
            sp.GetRequiredService<ISelectedAudio>()));
        services.AddSingleton<ReportViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>(sp =>
        {
            var config = sp.GetRequiredService<AppConfig>();
            // Kalıcı ayar kaydı 08b'de; bu tur onSave no-op (yalnızca in-memory config nesnesini günceller).
            return new SettingsViewModel(config, updated => System.Diagnostics.Debug.WriteLine($"Ayarlar kaydedildi (bu tur no-op): {updated}"));
        });
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
