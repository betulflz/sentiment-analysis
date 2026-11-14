using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Diarization;
using SentimentAnalysis.Infrastructure.Vad;

namespace SentimentAnalysis.Desktop.Avalonia;

/// <summary>"Sesimi tanıt" ve canlı rol ayrımı (12) için ses imzası + VAD modelleri. Tanıtma analizden bağımsız
/// olduğundan analiz işine bağlanamaz; bunun yerine modeller ilk kullanımda özel bir alt sağlayıcıda yüklenir ve
/// uygulama kapanırken kapatılır. Kök sağlayıcıya <see cref="IVadGate"/>/<see cref="ISpeakerEmbedder"/> olarak
/// KAYDEDİLMEZ (kök yerel model sahibi olmaz kuralı).</summary>
internal sealed class RoleModels(AppConfig config) : IDisposable
{
    private readonly Lazy<ServiceProvider> _models = new(() =>
    {
        var services = new ServiceCollection();
        services.AddApplicationCore(config);
        services.AddSherpaDiarization().AddSileroVad();
        return services.BuildServiceProvider();
    });

    public (ISpeakerEmbedder, IVadGate) Create() =>
        (new LazyEmbedder(() => _models.Value.GetRequiredService<ISpeakerEmbedder>()),
         new LazyVad(() => _models.Value.GetRequiredService<IVadGate>()));

    public void Dispose()
    {
        if (_models.IsValueCreated) _models.Value.Dispose();
    }

    private sealed class LazyEmbedder(Func<ISpeakerEmbedder> factory) : ISpeakerEmbedder
    {
        public float[] Embed(float[] mono16k) => factory().Embed(mono16k);
    }

    private sealed class LazyVad(Func<IVadGate> factory) : IVadGate
    {
        public bool HasSpeech(AudioChunk chunk) => factory().HasSpeech(chunk);
    }
}
