using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Infrastructure.Storage;

/// <summary>SQLite depolama DI kaydı. `ISessionStore` düz AddSingleton ile → 06'nın TryAdd InMemory'sini
/// composition root sırasıyla ezer (bu uzantı çağrıldığında gerçek store kazanır). `AddApplicationCore()` ile.</summary>
public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddSqliteStorage(this IServiceCollection services)
    {
        services.AddSingleton<IKeyProvider, FileKeyProvider>();
        services.AddSingleton<EncryptedAudioStore>();
        services.AddSingleton<ISessionStore, SqliteSessionStore>();
        return services;
    }
}
