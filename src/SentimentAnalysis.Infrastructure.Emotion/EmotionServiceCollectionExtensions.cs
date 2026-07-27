using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;   // TryAddSingleton
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>Duygu modelleri DI kaydı. Singleton (model + tokenizer açılışta bir kez, sıcak).
/// `AddApplicationCore()` ile kullanılır. Prozodi (04a) ve metin (04b) bağımsız eklenebilir;
/// ortak <see cref="IEmotionModelProvider"/> TryAdd ile tek kez kaydedilir.</summary>
public static class EmotionServiceCollectionExtensions
{
    /// <summary>
    /// Prozodi kanalını kaydeder. Türkçe'ye özel SER modeli mevcutsa (veya
    /// <see cref="EmotionConfig.UseTurkishSer"/> ile zorlanmışsa) genel audeering modeliyle
    /// birlikte <see cref="CompositeProsodyEmotionModel"/> arkasında harmanlanır; aksi halde
    /// yalnızca genel model kullanılır.
    /// </summary>
    public static IServiceCollection AddProsodyEmotion(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmotionModelProvider, FileEmotionModelProvider>();

        services.AddSingleton<IProsodyEmotionModel>(sp =>
        {
            var config = sp.GetRequiredService<AppConfig>();
            var models = sp.GetRequiredService<IEmotionModelProvider>();
            var general = new OnnxProsodyEmotionModel(config, models);

            if (!ShouldUseTurkishSer(config, models))
                return general;

            var turkish = new OnnxTurkishSerModel(config, models);
            return new CompositeProsodyEmotionModel(general, turkish, config);
        });

        return services;
    }

    public static IServiceCollection AddTextEmotion(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmotionModelProvider, FileEmotionModelProvider>();
        services.AddSingleton<ITextEmotionModel, OnnxTextEmotionModel>();
        return services;
    }

    /// <summary>
    /// <c>UseTurkishSer</c> açıkça verilmişse ona uyulur (true iken dosya yoksa model
    /// yüklenirken anlaşılır bir <see cref="FileNotFoundException"/> alınır — sessizce
    /// devre dışı kalmaz). Verilmemişse dosyanın varlığına bakılır: modeli indirmemiş
    /// kullanıcı için sistem yine çalışır, indirmiş olan otomatik kazanır.
    /// </summary>
    private static bool ShouldUseTurkishSer(AppConfig config, IEmotionModelProvider models)
    {
        if (config.Emotion.UseTurkishSer is { } explicitChoice) return explicitChoice;

        try
        {
            models.ResolveModelPath(config.Emotion.TurkishSerModelFile);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;   // model indirilmemiş → yalnızca genel prozodi modeli
        }
    }
}
