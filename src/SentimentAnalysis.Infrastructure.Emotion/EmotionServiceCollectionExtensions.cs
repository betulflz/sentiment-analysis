using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;   // TryAddSingleton
using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>Duygu modelleri DI kaydı. Singleton (model + tokenizer açılışta bir kez, sıcak).
/// `AddApplicationCore()` ile kullanılır. Prozodi (04a) ve metin (04b) bağımsız eklenebilir;
/// ortak <see cref="IEmotionModelProvider"/> TryAdd ile tek kez kaydedilir.</summary>
public static class EmotionServiceCollectionExtensions
{
    public static IServiceCollection AddProsodyEmotion(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmotionModelProvider, FileEmotionModelProvider>();
        services.AddSingleton<IProsodyEmotionModel, OnnxProsodyEmotionModel>();
        return services;
    }

    public static IServiceCollection AddTextEmotion(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmotionModelProvider, FileEmotionModelProvider>();
        services.AddSingleton<ITextEmotionModel, OnnxTextEmotionModel>();
        return services;
    }
}
