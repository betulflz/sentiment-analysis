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

        // Tek örnek, iki arayüz (10b): ISessionAudioSink de aynı store'a çözülmelidir —
        // ayrı ayrı AddSingleton edilseydi İKİ ayrı SqliteSessionStore oluşur, ikisi de aynı
        // SQLite dosyasını kendi bağlantısıyla açardı (gereksiz ve kafa karıştırıcı).
        services.AddSingleton<SqliteSessionStore>();
        services.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<SqliteSessionStore>());
        services.AddSingleton<ISessionAudioSink>(sp => sp.GetRequiredService<SqliteSessionStore>());
        services.AddSingleton<IDiarizationStore>(sp => sp.GetRequiredService<SqliteSessionStore>());
        services.AddSingleton<ISpeakerDataReader>(sp => sp.GetRequiredService<SqliteSessionStore>());
        return services;
    }
}
