using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Application.Runtime;
using SentimentAnalysis.Desktop;
using SentimentAnalysis.Desktop.Avalonia;

namespace SentimentAnalysis.Desktop.Avalonia.Tests;

/// <summary>Exercise the production DI graph without loading models or starting a window/microphone.</summary>
public sealed class ModelCompositionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sentiment-model-lifetime-" + Guid.NewGuid());
    private AppConfig Config => AppConfig.Default with
    {
        Storage = AppConfig.Default.Storage with { StorageDirectory = _directory }
    };

    [Fact]
    public async Task App_root_does_not_own_native_models_and_can_shutdown_idle()
    {
        await using var app = (ServiceProvider)CompositionRoot.BuildServiceProvider(Config);
        Assert.Null(app.GetService<ISpeechToText>());
        Assert.Null(app.GetService<IProsodyEmotionModel>());
        Assert.Null(app.GetService<ITextEmotionModel>());
        Assert.Null(app.GetService<IVadGate>());
        Assert.Null(app.GetService<IDiarizer>());
        var runner = Assert.IsType<ResourceManagedAnalysisRunner>(app.GetRequiredService<IAnalysisRunner>());
        await ModelWorkShutdown.RunAsync(app.GetRequiredService<ModelWorkQueue>(), runner,
            () => Task.CompletedTask, () => app.DisposeAsync());
    }

    [Fact]
    public async Task Analysis_graph_is_fresh_and_disposal_keeps_shared_storage_usable()
    {
        await using var app = (ServiceProvider)CompositionRoot.BuildServiceProvider(Config);
        var first = ModelSessions.CreateAnalysis(app);
        var second = ModelSessions.CreateAnalysis(app);
        Assert.NotSame(first.Service, second.Service);
        await first.DisposeAsync();
        await second.DisposeAsync();
        Assert.Empty(await app.GetRequiredService<ISessionStore>().ListSessionsAsync(default));
    }

    [Fact]
    public async Task Speaker_graph_resolves_and_disposal_keeps_shared_storage_usable()
    {
        await using var app = (ServiceProvider)CompositionRoot.BuildServiceProvider(Config);
        await using (var session = ModelSessions.CreateSpeaker(app))
            Assert.IsType<SpeakerAnalysisService>(session.Service);
        Assert.Empty(await app.GetRequiredService<ISessionStore>().ListSessionsAsync(default));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
