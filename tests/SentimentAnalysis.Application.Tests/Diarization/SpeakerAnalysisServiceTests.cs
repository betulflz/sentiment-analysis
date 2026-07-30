using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

public class SpeakerAnalysisServiceTests
{
    private sealed class FakeState : IDiarizationState { public bool Enabled { get; set; } = true; }

    private sealed class FakeDiarizer(IReadOnlyList<SpeakerSegment> result) : IDiarizer
    {
        public Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(float[] mono16k, CancellationToken ct) =>
            Task.FromResult(result);
    }

    private sealed class FakeStore(float[]? audio, IReadOnlyList<SpeakerSegment> saved) : IDiarizationStore
    {
        public IReadOnlyList<SpeakerSegment> Saved { get; private set; } = saved;
        public Task<float[]?> GetSessionAudioAsync(Guid sessionId, CancellationToken ct) => Task.FromResult(audio);
        public Task SaveSpeakerSegmentsAsync(Guid sessionId, IReadOnlyList<SpeakerSegment> segments, CancellationToken ct)
        {
            Saved = segments;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeReader(IReadOnlyList<SpeakerSegment> speakers) : ISpeakerDataReader
    {
        public Task<IReadOnlyList<TranscriptSegment>> GetTranscriptSegmentsAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TranscriptSegment>>(Array.Empty<TranscriptSegment>());
        public Task<IReadOnlyList<SpeakerSegment>> GetSpeakerSegmentsAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(speakers);
        public Task<IReadOnlyList<ChunkResult>> GetChunkResultsAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ChunkResult>>(Array.Empty<ChunkResult>());
    }

    private sealed class FakeProsody : IProsodyEmotionModel
    {
        public Task<ProsodyEmotion> AnalyzeAsync(AudioChunk chunk, CancellationToken ct) =>
            Task.FromResult(new ProsodyEmotion(new ValenceArousal(0.6, 0.7), 0.9));
    }

    [Fact]
    public async Task RunAsync_KonusmaciProzodisiniDeUretir()
    {
        var speakers = new[]
        {
            new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(4)),
            new SpeakerSegment(new SpeakerId(1), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)),
        };
        var audio = new float[8 * 16000];
        var store = new FakeStore(audio, speakers);
        var job = new DiarizationJob(new FakeDiarizer(speakers), store, AppConfig.Default);
        var analyzer = new SpeakerProsodyAnalyzer(new FakeProsody(), AppConfig.Default);
        var service = new SpeakerAnalysisService(job, new FakeReader(speakers), new FakeState(), analyzer, store);

        var result = await service.RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DiarizationOutcome.Completed, result.Outcome);
        Assert.Equal(2, result.SpeakerProsody.Count);
        Assert.Equal(0.6, result.SpeakerProsody[0].Va.Valence, precision: 3);
    }

    [Fact]
    public async Task RunAsync_SesYoksa_ProzodiListesiBosKalir()
    {
        var store = new FakeStore(audio: null, saved: Array.Empty<SpeakerSegment>());
        var job = new DiarizationJob(new FakeDiarizer(Array.Empty<SpeakerSegment>()), store, AppConfig.Default);
        var analyzer = new SpeakerProsodyAnalyzer(new FakeProsody(), AppConfig.Default);
        var service = new SpeakerAnalysisService(job, new FakeReader(Array.Empty<SpeakerSegment>()), new FakeState(), analyzer, store);

        var result = await service.RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DiarizationOutcome.NoAudio, result.Outcome);
        Assert.Empty(result.SpeakerProsody);
    }
}
