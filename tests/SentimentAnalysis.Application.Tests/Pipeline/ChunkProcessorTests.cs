using System;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Pipeline;

public class ChunkProcessorTests
{
    private sealed class FakeVad(bool speech) : IVadGate { public bool HasSpeech(AudioChunk c) => speech; }
    private sealed class FakeStt(Transcript t) : ISpeechToText
    { public int Calls; public Task<Transcript> TranscribeAsync(AudioChunk c, CancellationToken ct) { Interlocked.Increment(ref Calls); return Task.FromResult(t); } }
    private sealed class FakeProsody(ProsodyEmotion p) : IProsodyEmotionModel
    { public int Calls; public Task<ProsodyEmotion> AnalyzeAsync(AudioChunk c, CancellationToken ct) { Interlocked.Increment(ref Calls); return Task.FromResult(p); } }
    private sealed class FakeText(TextEmotion e) : ITextEmotionModel
    { public Transcript? Seen; public Task<TextEmotion> AnalyzeAsync(Transcript t, CancellationToken ct) { Seen = t; return Task.FromResult(e); } }
    private sealed class FakeFusion(FusedResult r) : IEmotionFusion
    { public QualitySignals? Q; public FusedResult Fuse(TextEmotion t, ProsodyEmotion p, QualitySignals q) { Q = q; return r; } }

    private static AudioChunk Chunk() => new(new float[16000], 16000, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
    private static readonly Transcript Tx = new("merhaba dünya", DetectedLanguage.Turkish, -0.2);
    private static readonly ProsodyEmotion Pr = new(new ValenceArousal(0.1, 0.6), 0.5);
    private static readonly TextEmotion Te = new(new ValenceArousal(0.3, 0.4), 0.7);
    private static readonly FusedResult Fr = new(new ValenceArousal(0.2, 0.5), Emotion.Notr, Emotion.Notr,
        Confidence.Orta, true, false, IncongruencePattern.Yok, 1.0,
        TextVa: new ValenceArousal(0.3, 0.5), ProsodyVa: new ValenceArousal(0.1, 0.5),
        TextConfidence: 0.7, ProsodyConfidence: 0.7);

    [Fact]
    public async Task ProcessAsync_Silence_ReturnsNull_NoModelCalls()
    {
        var stt = new FakeStt(Tx); var pros = new FakeProsody(Pr);
        var sut = new ChunkProcessor(new FakeVad(false), stt, pros, new FakeText(Te), new FakeFusion(Fr));
        var r = await sut.ProcessAsync(Chunk(), CancellationToken.None);
        Assert.Null(r);
        Assert.Equal(0, stt.Calls);
        Assert.Equal(0, pros.Calls);
    }

    [Fact]
    public async Task ProcessAsync_Speech_RunsSttAndProsody_BuildsChunkResult()
    {
        var stt = new FakeStt(Tx); var pros = new FakeProsody(Pr);
        var text = new FakeText(Te); var fusion = new FakeFusion(Fr);
        var sut = new ChunkProcessor(new FakeVad(true), stt, pros, text, fusion);

        var r = await sut.ProcessAsync(Chunk(), CancellationToken.None);

        Assert.NotNull(r);
        Assert.Equal(TimeSpan.FromSeconds(2), r!.TimeStart);
        Assert.Equal(TimeSpan.FromSeconds(4), r.TimeEnd);
        Assert.Equal("merhaba dünya", r.Transcript);
        Assert.Same(Fr, r.Fused);
        Assert.Equal(1, stt.Calls);
        Assert.Equal(1, pros.Calls);
        Assert.Same(Tx, text.Seen);                 // metin-duygu STT transcript'ini aldı
        Assert.Equal(2, fusion.Q!.TranscriptWordCount); // quality doğru kuruldu
        Assert.Equal(TimeSpan.FromSeconds(2), fusion.Q.Duration);
    }

    // --- 10b: konuşmacı hizalaması mutlak zaman ister ---

    [Fact]
    public async Task ProcessAsync_SegmentZamanlariniMutlakaCevirir()
    {
        // Chunk 30. saniyede başlıyor; STT segmentleri CHUNK'A GÖRELİ (0 ve 2 sn).
        var chunk = new AudioChunk(new float[16000], 16000,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(40));
        var tx = new Transcript("Merhaba dünya", DetectedLanguage.Turkish, -0.2,
            new[]
            {
                new TranscriptSegment("Merhaba", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2)),
                new TranscriptSegment("dünya",   TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)),
            });
        var sut = new ChunkProcessor(new FakeVad(true), new FakeStt(tx), new FakeProsody(Pr),
            new FakeText(Te), new FakeFusion(Fr));

        var r = await sut.ProcessAsync(chunk, CancellationToken.None);

        Assert.NotNull(r);
        Assert.Equal(2, r!.Segments.Count);
        Assert.Equal(TimeSpan.FromSeconds(30), r.Segments[0].Start);   // 30 + 0
        Assert.Equal(TimeSpan.FromSeconds(32), r.Segments[0].End);     // 30 + 2
        Assert.Equal(TimeSpan.FromSeconds(32), r.Segments[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(34), r.Segments[1].End);
        Assert.Equal("Merhaba", r.Segments[0].Text);                   // metin değişmez
        Assert.Equal("Merhaba dünya", r.Transcript);
    }

    [Fact]
    public async Task ProcessAsync_SegmentsizTranscript_BosSegmentListesi()
    {
        // 10b öncesi davranış: segment üretmeyen STT'lerde kırılma olmamalı.
        var sut = new ChunkProcessor(new FakeVad(true), new FakeStt(Tx), new FakeProsody(Pr),
            new FakeText(Te), new FakeFusion(Fr));

        var r = await sut.ProcessAsync(Chunk(), CancellationToken.None);

        Assert.NotNull(r);
        Assert.Empty(r!.Segments);
    }

    [Fact]
    public async Task ProcessAsync_ChunkSifirdanBasliyorsa_ZamanlarDegismez()
    {
        var chunk = new AudioChunk(new float[16000], 16000, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        var tx = new Transcript("x", DetectedLanguage.Turkish, -0.2,
            new[] { new TranscriptSegment("x", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)) });
        var sut = new ChunkProcessor(new FakeVad(true), new FakeStt(tx), new FakeProsody(Pr),
            new FakeText(Te), new FakeFusion(Fr));

        var r = await sut.ProcessAsync(chunk, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(1), r!.Segments[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(3), r.Segments[0].End);
    }
}
