using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Application.Fusion;

/// <summary>Füzyon motoru DI kaydı. Saf/durumsuz olduğundan singleton.
/// `AddApplicationCore()` ile birlikte kullanılır (AppConfig'e bağımlı).</summary>
public static class FusionServiceCollectionExtensions
{
    public static IServiceCollection AddFusion(this IServiceCollection services)
    {
        services.AddSingleton<IEmotionFusion, EmotionFusion>();
        services.AddSingleton<IMeetingAggregator, MeetingAggregator>();
        return services;
    }
}
