using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Infrastructure.Vad;

/// <summary>Silero VAD DI kaydı. Kapı + model sağlayıcı singleton (model açılışta bir kez, sıcak).
/// `AddApplicationCore()` ile kullanılır.</summary>
public static class VadServiceCollectionExtensions
{
    public static IServiceCollection AddSileroVad(this IServiceCollection services)
    {
        services.TryAddSingleton<IVadModelProvider, FileVadModelProvider>();
        services.AddSingleton<IVadGate, SileroVadGate>();
        return services;
    }
}
