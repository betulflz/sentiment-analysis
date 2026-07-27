using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Application.Pipeline;

/// <summary>Pipeline orkestrasyon DI kaydı. `AddApplicationCore()` + model/füzyon uzantılarıyla
/// (AddStt/AddProsodyEmotion/AddTextEmotion/AddSileroVad/AddFusion) birlikte kullanılır.
/// `ISessionStore` bu turda bellek-içi (gerçek store 07'de `TryAdd`'i ezmeden değiştirilebilir).</summary>
public static class PipelineServiceCollectionExtensions
{
    public static IServiceCollection AddPipeline(this IServiceCollection services)
    {
        services.TryAddSingleton<ISessionStore, InMemorySessionStore>();
        services.AddSingleton<IChunkProcessor, ChunkProcessor>();
        services.AddSingleton<EmotionPipeline>();
        return services;
    }
}
