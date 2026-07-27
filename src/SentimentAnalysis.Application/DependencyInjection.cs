using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Application;

/// <summary>Application katmanı DI kayıt noktası. Infrastructure implementasyonları
/// (Alt-projeler 02–07) kendi uzantı metotlarıyla eklenir.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationCore(
        this IServiceCollection services, AppConfig? config = null)
    {
        services.AddSingleton(config ?? AppConfig.Default);
        // Somut servisler (ISpeechToText, IEmotionFusion, ...) Infrastructure
        // katmanında ayrı AddXxx() uzantılarıyla kaydedilecek.
        return services;
    }
}
