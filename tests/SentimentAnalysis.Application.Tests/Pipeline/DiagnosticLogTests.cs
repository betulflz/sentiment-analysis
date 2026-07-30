using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Pipeline;

/// <summary>
/// Teşhis günlüğü sözleşmesi. <b>Neden bir arayüz:</b> üretim kodunda <c>Console.WriteLine</c>
/// iki ayrı sorun üretiyordu — (1) paketlenmiş uygulamada (macOS .app, Windows GUI) konsol
/// olmadığı için çıktı hiçbir yere gitmiyordu, yani maliyeti vardı faydası yoktu; (2) günlük
/// satırının kendisi (metin biçimleme + I/O) ses callback'i gibi gerçek zamanlı yollarda
/// çalışıyordu. Arayüz, üretimde SESSİZ (no-op) olan ve gerektiğinde açılabilen bir kanal verir.
/// </summary>
public class DiagnosticLogTests
{
    private sealed class SpyLog : IDiagnosticLog
    {
        public List<string> Messages { get; } = new();
        public bool IsEnabled { get; init; } = true;
        public void Write(string message) => Messages.Add(message);
    }

    private sealed class ExplodingLog : IDiagnosticLog
    {
        public bool IsEnabled => true;
        public void Write(string message) => throw new InvalidOperationException("günlük patladı");
    }

    [Fact]
    public void NullLog_Kapalidir_VeYazmakGuvenlidir()
    {
        var log = NullDiagnosticLog.Instance;

        Assert.False(log.IsEnabled);
        log.Write("bu hiçbir yere gitmemeli"); // fırlatmamalı
    }

    [Fact]
    public void Varsayilan_UretimdeSessizdir()
    {
        // Hattı günlük vermeden kurabilmek, "üretimde sessiz" varsayılanının kanıtıdır.
        var pipeline = new EmotionPipeline(
            new NoOpProcessor(), new NoOpStore(), new StubAggregator(), AppConfig.Default);

        Assert.NotNull(pipeline); // kurulum günlük gerektirmiyor
    }

    [Fact]
    public async Task Hat_GunlukAcikkenChunkIslemeyiRaporlar()
    {
        var log = new SpyLog();
        var pipeline = new EmotionPipeline(
            new NoOpProcessor(), new NoOpStore(), new StubAggregator(), AppConfig.Default, log);

        await pipeline.RunAsync(
            new SingleChunkSource(), new CaptureOptions(TimeSpan.FromSeconds(1), TimeSpan.Zero),
            Guid.NewGuid(), DateTimeOffset.UnixEpoch, DetectedLanguage.Turkish, null,
            CancellationToken.None);

        Assert.NotEmpty(log.Messages);
    }

    [Fact]
    public async Task Hat_GunlukKapaliyken_HicYazmaz()
    {
        // Kapalıyken yalnızca "yazmamak" yetmez; pahalı ölçümlerin (RMS/tepe: chunk başına
        // 160 bin örnek üzerinde LINQ) hiç HESAPLANMAMASI gerekir. Bu testin doğrudan
        // gözlemleyebildiği şey Write çağrısının hiç yapılmamasıdır.
        var log = new SpyLog { IsEnabled = false };
        var pipeline = new EmotionPipeline(
            new NoOpProcessor(), new NoOpStore(), new StubAggregator(), AppConfig.Default, log);

        await pipeline.RunAsync(
            new SingleChunkSource(), new CaptureOptions(TimeSpan.FromSeconds(1), TimeSpan.Zero),
            Guid.NewGuid(), DateTimeOffset.UnixEpoch, DetectedLanguage.Turkish, null,
            CancellationToken.None);

        Assert.Empty(log.Messages);
    }

    [Fact]
    public async Task Hat_GunlukPatlarsa_AnalizCokmez()
    {
        // Teşhis aracı, teşhis ettiği şeyi bozamaz.
        var pipeline = new EmotionPipeline(
            new NoOpProcessor(), new NoOpStore(), new StubAggregator(), AppConfig.Default,
            new ExplodingLog());

        var summary = await pipeline.RunAsync(
            new SingleChunkSource(), new CaptureOptions(TimeSpan.FromSeconds(1), TimeSpan.Zero),
            Guid.NewGuid(), DateTimeOffset.UnixEpoch, DetectedLanguage.Turkish, null,
            CancellationToken.None);

        Assert.NotNull(summary);
    }

    // ---- minimal sahteler ----
    private sealed class SingleChunkSource : IAudioCaptureSource
    {
        public async IAsyncEnumerable<AudioChunk> CaptureAsync(
            CaptureOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return new AudioChunk(new float[16000], 16000, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }
    }

    private sealed class NoOpProcessor : IChunkProcessor
    {
        public Task<ChunkResult?> ProcessAsync(AudioChunk chunk, CancellationToken ct) =>
            Task.FromResult<ChunkResult?>(null); // VAD sessiz gibi davran
        public Task WarmUpAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoOpStore : ISessionStore
    {
        public Task SaveChunkAsync(Guid sessionId, ChunkResult result, CancellationToken ct) => Task.CompletedTask;
        public Task<MeetingSession?> GetSessionAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<MeetingSession?>(null);
        public Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MeetingSession>>(Array.Empty<MeetingSession>());
        public Task PurgeExpiredAudioAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubAggregator : IMeetingAggregator
    {
        public MeetingSummary Aggregate(MeetingSession session) => new(
            Emotion.Notr, new Dictionary<Emotion, double>(), 0, 0,
            new List<ChunkResult>(), ValenceArousal.Neutral, 1.0, "özet");
    }
}
