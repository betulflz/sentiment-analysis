using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;

namespace SentimentAnalysis.Infrastructure.Diarization;

/// <summary>sherpa-onnx diarization DI kaydı. Model singleton (bir kez yüklenir, sıcak tutulur).
/// <para>Kayıt, <c>DiarizationConfig.Enabled</c>'dan BAĞIMSIZ yapılır: bayrak "modeli yükle mi"
/// değil "özelliği kullan mı" sorusudur ve çalışma anında değişebilir. <see cref="SherpaDiarizer"/>
/// modeli zaten ilk gerçek çağrıya kadar yüklemez, dolayısıyla kapalıyken maliyeti sıfırdır.</para></summary>
public static class DiarizationServiceCollectionExtensions
{
    public static IServiceCollection AddSherpaDiarization(this IServiceCollection services)
    {
        services.TryAddSingleton<FileDiarizationModelProvider>();
        services.TryAddSingleton<IDiarizationModelProvider>(sp => sp.GetRequiredService<FileDiarizationModelProvider>());
        services.TryAddSingleton<ISpeakerRoleModelProvider>(sp => sp.GetRequiredService<FileDiarizationModelProvider>());
        services.AddSingleton<SherpaDiarizer>();
        // İterasyon 2 (2026-11-13): varsayılan pencereli ayrım (TitaNet imzası + VAD; çağıranın IVadGate kaydetmesi gerekir).
        services.AddSingleton<IDiarizer>(sp =>
        {
            var cfg = sp.GetRequiredService<AppConfig>();
            return cfg.Diarization.UseWindowedDiarizer
                ? new WindowedDiarizer(sp.GetRequiredService<ISpeakerEmbedder>(), sp.GetRequiredService<IVadGate>(), cfg)
                : sp.GetRequiredService<SherpaDiarizer>();
        });
        services.AddSingleton<ISpeakerEmbedder, SherpaSpeakerEmbedder>();
        return services;
    }
}
