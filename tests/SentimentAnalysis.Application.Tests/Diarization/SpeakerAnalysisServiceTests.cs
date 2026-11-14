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
        public int SeenExpected = -1;
        public Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(float[] mono16k, int expectedSpeakers, CancellationToken ct)
        {
            SeenExpected = expectedSpeakers;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeStore(float[]? audio, IReadOnlyList<SpeakerSegment> saved) : IDiarizationStore
    {
        public IReadOnlyList<SpeakerSegment> Saved { get; private set; } = saved;

        /// <summary>Kaç kez çağrıldığı (düzeltme turu 1 / bulgu 3): SqliteSessionStore'da her
        /// çağrı yeniden şifre çözüp yeni bir tahsis yapar; servis sesi İKİ KEZ okumamalıdır.</summary>
        public int GetSessionAudioCalls { get; private set; }

        public Task<float[]?> GetSessionAudioAsync(Guid sessionId, CancellationToken ct)
        {
            GetSessionAudioCalls++;
            return Task.FromResult(audio);
        }

        public Task SaveSpeakerSegmentsAsync(Guid sessionId, IReadOnlyList<SpeakerSegment> segments, CancellationToken ct)
        {
            Saved = segments;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeReader(IReadOnlyList<SpeakerSegment> speakers, IReadOnlyList<TranscriptSegment>? transcript = null) : ISpeakerDataReader
    {
        public Task<IReadOnlyList<TranscriptSegment>> GetTranscriptSegmentsAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(transcript ?? Array.Empty<TranscriptSegment>());
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

    // --- Karşı tarafa odak: dosya modunda ön-hesaplı bölgeler ---
    private sealed class ThrowingDiarizer : IDiarizer
    {
        public Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(float[] mono16k, int expectedSpeakers, CancellationToken ct) =>
            throw new InvalidOperationException("ön-hesaplı yolda diarizer çağrılmamalı");
    }

    [Fact]
    public async Task Precomputed_DiarizerCagrilmaz_SesOkunmaz_BolgelerKaydedilir_ProzodiVerilenSesten()
    {
        var speakers = new[]
        {
            new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(4)),
            new SpeakerSegment(new SpeakerId(1), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)),
        };
        var store = new FakeStore(null, Array.Empty<SpeakerSegment>());
        var job = new DiarizationJob(new ThrowingDiarizer(), store, AppConfig.Default);
        var service = new SpeakerAnalysisService(job, new FakeReader(speakers), new FakeState { Enabled = false },
            new SpeakerProsodyAnalyzer(new FakeProsody(), AppConfig.Default));

        var result = await service.RunPrecomputedAsync(Guid.NewGuid(), speakers, new float[8 * 16000], CancellationToken.None);

        Assert.Equal(DiarizationOutcome.Completed, result.Outcome);
        Assert.Equal(0, store.GetSessionAudioCalls);
        Assert.Equal(speakers, store.Saved);
        Assert.Equal(2, result.SpeakerProsody.Count);
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
        var service = new SpeakerAnalysisService(job, new FakeReader(speakers), new FakeState(), analyzer);

        var result = await service.RunAsync(Guid.NewGuid(), 0, CancellationToken.None);

        Assert.Equal(DiarizationOutcome.Completed, result.Outcome);
        Assert.Equal(2, result.SpeakerProsody.Count);
        Assert.Equal(0.6, result.SpeakerProsody[0].Va.Valence, precision: 3);
    }

    [Fact]
    public async Task RunAsync_TekrarSatirlarRaporda_Temizlenir()
    {
        static TimeSpan Sec(double s) => TimeSpan.FromSeconds(s);
        var speakers = new[] { new SpeakerSegment(new SpeakerId(0), Sec(0), Sec(60)) };
        var transcript = new[]
        {
            new TranscriptSegment("Hangi karta?", Sec(55.56), Sec(56.78)),
            new TranscriptSegment("Hangi karta?", Sec(56), Sec(56.78)),
        };
        var job = new DiarizationJob(new FakeDiarizer(speakers), new FakeStore(new float[60 * 16000], speakers), AppConfig.Default);
        var service = new SpeakerAnalysisService(job, new FakeReader(speakers, transcript), new FakeState(),
            new SpeakerProsodyAnalyzer(new FakeProsody(), AppConfig.Default));

        var result = await service.RunAsync(Guid.NewGuid(), 0, CancellationToken.None);

        Assert.Single(result.Transcript);
    }

    [Fact]
    public async Task RunAsync_BeklenenKisiSayisiAyiriciyaKadarIner()
    {
        var speakers = new[] { new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(4)) };
        var diarizer = new FakeDiarizer(speakers);
        var job = new DiarizationJob(diarizer, new FakeStore(new float[4 * 16000], speakers), AppConfig.Default);
        var service = new SpeakerAnalysisService(job, new FakeReader(speakers), new FakeState(),
            new SpeakerProsodyAnalyzer(new FakeProsody(), AppConfig.Default));

        await service.RunAsync(Guid.NewGuid(), 3, CancellationToken.None);

        Assert.Equal(3, diarizer.SeenExpected);
    }

    [Fact]
    public async Task RunAsync_Tamamlaninca_OturumSesiTekSeferOkunur()
    {
        // Bulgu 3 (düzeltme turu 1): SqliteSessionStore her çağrıda dosyayı yeniden şifre çözüp
        // yeni bir float[] ayırır; RunWithAudioAsync üzerinden TEK okuma yapılmalı — hem diarizer
        // hem prozodi analizörü aynı örneği paylaşmalı.
        var speakers = new[]
        {
            new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(4)),
            new SpeakerSegment(new SpeakerId(1), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)),
        };
        var audio = new float[8 * 16000];
        var store = new FakeStore(audio, speakers);
        var job = new DiarizationJob(new FakeDiarizer(speakers), store, AppConfig.Default);
        var analyzer = new SpeakerProsodyAnalyzer(new FakeProsody(), AppConfig.Default);
        var service = new SpeakerAnalysisService(job, new FakeReader(speakers), new FakeState(), analyzer);

        await service.RunAsync(Guid.NewGuid(), 0, CancellationToken.None);

        Assert.Equal(1, store.GetSessionAudioCalls);
    }

    [Fact]
    public async Task RunAsync_SesYoksa_NoAudioDoner_ProzodiListesiBosKalir()
    {
        // Bulgu 2 (düzeltme turu 1): önceki adı ("...ProzodiListesiBosKalir") SpeakerAnalysisService
        // içinde "Completed ama ses null" diye ayrı bir savunma dalı varmış gibi okunuyordu; ama
        // DiarizationJob zaten NoAudio'da erken çıkıyor, o dal HİÇ ÇALIŞTIRILMIYORDU. Bulgu 3
        // düzeltmesiyle (RunWithAudioAsync) bu dal artık YAPISAL OLARAK İMKANSIZ: Completed
        // dönüldüğünde Audio her zaman doludur (bkz. DiarizationJob dokümantasyonu), bu yüzden
        // SpeakerAnalysisService'teki ayrı null/boş kontrolü tamamen kaldırıldı. Bu test artık
        // yalnızca gerçekten var olan yolu doğruluyor: ses hiç yoksa NoAudio döner ve
        // SpeakerProsody boş kalır — ayrı bir dal iddia etmiyor.
        var store = new FakeStore(audio: null, saved: Array.Empty<SpeakerSegment>());
        var job = new DiarizationJob(new FakeDiarizer(Array.Empty<SpeakerSegment>()), store, AppConfig.Default);
        var analyzer = new SpeakerProsodyAnalyzer(new FakeProsody(), AppConfig.Default);
        var service = new SpeakerAnalysisService(job, new FakeReader(Array.Empty<SpeakerSegment>()), new FakeState(), analyzer);

        var result = await service.RunAsync(Guid.NewGuid(), 0, CancellationToken.None);

        Assert.Equal(DiarizationOutcome.NoAudio, result.Outcome);
        Assert.Empty(result.SpeakerProsody);
    }
}
