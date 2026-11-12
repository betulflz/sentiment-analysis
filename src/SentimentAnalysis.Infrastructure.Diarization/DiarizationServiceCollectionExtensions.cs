using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SentimentAnalysis.Application.Abstractions;

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
        services.AddSingleton<IDiarizer, SherpaDiarizer>();
        services.AddSingleton<ISpeakerEmbedder, SherpaSpeakerEmbedder>();
        return services;
    }
}
