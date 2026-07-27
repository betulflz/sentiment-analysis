using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Infrastructure.Stt;

/// <summary>STT motoru DI kaydı. Motor ve model sağlayıcı singleton (model açılışta bir kez
/// yüklenip sıcak tutulur). `AddApplicationCore()` ile birlikte kullanılır (AppConfig'e bağımlı).</summary>
public static class SttServiceCollectionExtensions
{
    public static IServiceCollection AddStt(this IServiceCollection services)
    {
        services.AddSingleton<ISttModelProvider, FileSttModelProvider>();
        services.AddSingleton<ISpeechToText, WhisperSpeechToText>();
        return services;
    }
}
