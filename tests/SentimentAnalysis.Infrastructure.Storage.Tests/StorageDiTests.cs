using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Infrastructure.Storage;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Storage.Tests;

public class StorageDiTests
{
    [Fact]
    public void AddSqliteStorage_ResolvesSessionStore()
    {
        var provider = new ServiceCollection()
            .AddApplicationCore()
            .AddSqliteStorage()
            .BuildServiceProvider();

        Assert.IsType<SqliteSessionStore>(provider.GetRequiredService<ISessionStore>());
        Assert.NotNull(provider.GetRequiredService<IKeyProvider>());
    }
}
