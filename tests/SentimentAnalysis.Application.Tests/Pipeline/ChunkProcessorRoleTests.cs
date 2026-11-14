using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Tests.Pipeline;

/// <summary>12: ChunkProcessor rol çizelgesini ekler ve karşı tarafın duygusunu yalnız onun sesi/sözünden hesaplar.</summary>
public class ChunkProcessorRoleTests
{
    private const int Sr = 16000;
    private sealed class Vad : IVadGate { public bool HasSpeech(AudioChunk c) => true; }
    private sealed class Stt(Transcript t) : ISpeechToText { public Task<Transcript> TranscribeAsync(AudioChunk c, CancellationToken ct) => Task.FromResult(t); }
    private sealed class Prosody : IProsodyEmotionModel
    {
        public readonly List<AudioChunk> Seen = new();
        public Task<ProsodyEmotion> AnalyzeAsync(AudioChunk c, CancellationToken ct)
        { lock (Seen) Seen.Add(c); return Task.FromResult(new ProsodyEmotion(new ValenceArousal(0, 0.5), 0.5)); }
    }
    private sealed class Text : ITextEmotionModel
    {
        public readonly List<Transcript> Seen = new();
        public Task<TextEmotion> AnalyzeAsync(Transcript t, CancellationToken ct)
        { lock (Seen) Seen.Add(t); return Task.FromResult(new TextEmotion(new ValenceArousal(0, 0.5), 0.7)); }
    }
    /// <summary>Her çağrıda yeni bir sonuç (ayırt edilebilsin diye valence = çağrı sırası).</summary>
    private sealed class Fusion : IEmotionFusion
    {
        public int Calls;
        public FusedResult Fuse(TextEmotion t, ProsodyEmotion p, QualitySignals q)
        {
            Calls++;
            return new FusedResult(new ValenceArousal(Calls / 10.0, 0.5), Emotion.Notr, Emotion.Notr, Confidence.Orta, true, false,
                IncongruencePattern.Yok, 1, new ValenceArousal(0, 0), new ValenceArousal(0, 0), 0.7, 0.7);
        }
    }
    private sealed class Roles(RoleTimeline t) : IRoleLabeler
    {
        public int Calls;
        public RoleTimeline Label(AudioChunk c) { Calls++; return t; }
        public void Reset() { }
    }

    private static RoleSpan Span(double a, double b, SpeakerRole r) => new(TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b), r, 0.5);

    // Parça 100–110 sn; segmentler mutlak zamana çevrilecek (STT göreli verir).
    private static readonly AudioChunk Chunk = new(new float[10 * Sr], Sr, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(110));
    private static readonly Transcript Tx = new("Hoş geldiniz nasılsınız İyiyim teşekkürler gerçekten", DetectedLanguage.Turkish, -0.2, new[]
    {
        new TranscriptSegment("Hoş geldiniz nasılsınız", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(4)),
        new TranscriptSegment("İyiyim teşekkürler gerçekten", TimeSpan.FromSeconds(4.5), TimeSpan.FromSeconds(10)),
    });

    private static (ChunkProcessor Sut, Prosody P, Text T, Fusion F) Make()
    {
        var p = new Prosody(); var t = new Text(); var f = new Fusion();
        return (new ChunkProcessor(new Vad(), new Stt(Tx), p, t, f), p, t, f);
    }

    [Fact]
    public async Task RolEtiketleyiciYok_DavranisEskisiGibi()
    {
        var (sut, p, _, _) = Make();

        var r = await sut.ProcessAsync(Chunk, null, CancellationToken.None);

        Assert.Null(r!.Roles);
        Assert.Null(r.Counterpart);
        Assert.Single(p.Seen);
    }

    [Fact]
    public async Task TanitmaYok_RolEklenmez()
    {
        var (sut, p, _, _) = Make();

        var r = await sut.ProcessAsync(Chunk, new Roles(RoleTimeline.NotEnrolled), CancellationToken.None);

        Assert.Null(r!.Roles);
        Assert.Null(r.Counterpart);
        Assert.Single(p.Seen);
    }

    [Fact]
    public async Task KarisikParca_KarsiTarafYalnizKendiSesiVeSozundenHesaplanir()
    {
        var (sut, p, t, _) = Make();
        var tl = new RoleTimeline(new[] { Span(100, 104.5, SpeakerRole.Yoneten), Span(104.5, 110, SpeakerRole.KarsiTaraf) }, true, false);

        var r = await sut.ProcessAsync(Chunk, new Roles(tl), CancellationToken.None);

        Assert.Same(tl, r!.Roles);
        Assert.NotNull(r.Counterpart);
        Assert.NotSame(r.Fused, r.Counterpart);
        Assert.Equal(2, p.Seen.Count);
        Assert.Equal(5.5 * Sr, p.Seen.Single(c => c.Samples.Length != 10 * Sr).Samples.Length);   // yalnız karşı tarafın 5,5 sn'si
        Assert.Contains(t.Seen, x => x.Text == "İyiyim teşekkürler gerçekten");                     // yönetenin satırı yok
    }

    [Fact]
    public async Task YalnizKarsiTarafKonustu_GenelSonucOnundur()
    {
        var (sut, p, _, f) = Make();
        var tl = new RoleTimeline(new[] { Span(100, 110, SpeakerRole.KarsiTaraf) }, true, false);

        var r = await sut.ProcessAsync(Chunk, new Roles(tl), CancellationToken.None);

        Assert.Same(r!.Fused, r.Counterpart);
        Assert.Single(p.Seen);
        Assert.Equal(1, f.Calls);
    }

    [Fact]
    public async Task KarsiTarafNeredeyseKonusmadi_AyriSonucYok()
    {
        var (sut, p, _, _) = Make();
        var tl = new RoleTimeline(new[] { Span(100, 109, SpeakerRole.Yoneten), Span(109, 110, SpeakerRole.KarsiTaraf) }, true, false);

        var r = await sut.ProcessAsync(Chunk, new Roles(tl), CancellationToken.None);

        Assert.NotNull(r!.Roles);
        Assert.Null(r.Counterpart);
        Assert.Single(p.Seen);
    }
}
