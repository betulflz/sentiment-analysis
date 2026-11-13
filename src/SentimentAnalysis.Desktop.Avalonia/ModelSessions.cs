using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Application.Runtime;
using SentimentAnalysis.Application.Vision;
using SentimentAnalysis.Desktop;
using SentimentAnalysis.Infrastructure.Diarization;
using SentimentAnalysis.Infrastructure.Emotion;
using SentimentAnalysis.Infrastructure.Stt;
using SentimentAnalysis.Infrastructure.Vad;

namespace SentimentAnalysis.Desktop.Avalonia;

/// <summary>Job providers own models only; shared settings and storage belong to the app.</summary>
internal static class ModelSessions
{
    internal static OwnedModelSession<IAnalysisRunner> CreateAnalysis(IServiceProvider app)
    {
        var services = new ServiceCollection();
        services.AddApplicationCore(app.GetRequiredService<AppConfig>())
            .AddFusion().AddStt().AddProsodyEmotion().AddTextEmotion().AddSileroVad();
        // Instance registrations are externally owned: disposing this provider must not close SQLite.
        services.AddSingleton(app.GetRequiredService<ISessionStore>());
        services.AddSingleton(app.GetRequiredService<IDiagnosticLog>());
        services.AddPipeline();
        var models = services.BuildServiceProvider();
        try
        {
            var runner = new PipelineAnalysisRunner(
                models.GetRequiredService<EmotionPipeline>(),
                app.GetRequiredService<Func<IAudioCaptureSource?>>(),
                app.GetRequiredService<AppConfig>(),
                app.GetRequiredService<ICaptureModeState>(),
                app.GetService<ISessionAudioSink>(),
                app.GetService<IDiarizationState>(),
                app.GetRequiredService<Func<IVideoCaptureSource?>>(),
                app.GetRequiredService<Func<VisionAnalyzer>>(),
                app.GetRequiredService<SpeakerRoleTracker>());
            return new OwnedModelSession<IAnalysisRunner>(runner, new AnalysisOwner(runner, models));
        }
        catch { models.Dispose(); throw; }
    }

    internal static OwnedModelSession<ISpeakerAnalysis> CreateSpeaker(IServiceProvider app)
    {
        var services = new ServiceCollection();
        services.AddApplicationCore(app.GetRequiredService<AppConfig>());
        services.AddSingleton(app.GetRequiredService<IDiarizationState>());
        services.AddSingleton(app.GetRequiredService<IDiarizationStore>());
        services.AddSingleton(app.GetRequiredService<ISpeakerDataReader>());
        services.AddSherpaDiarization().AddProsodyEmotion();
        services.AddSingleton<DiarizationJob>();
        services.AddSingleton<SpeakerProsodyAnalyzer>();
        services.AddSingleton<ISpeakerAnalysis, SpeakerAnalysisService>();
        var models = services.BuildServiceProvider();
        try { return new OwnedModelSession<ISpeakerAnalysis>(models.GetRequiredService<ISpeakerAnalysis>(), models); }
        catch { models.Dispose(); throw; }
    }

    private sealed class AnalysisOwner(PipelineAnalysisRunner runner, ServiceProvider models) : IDisposable
    {
        public void Dispose()
        {
            try { runner.Dispose(); }
            finally { models.Dispose(); }
        }
    }
}
