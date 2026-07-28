using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Storage;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Storage.Tests;

public class SqliteSessionStoreTests
{
    private static (SqliteSessionStore store, EncryptedAudioStore audio, string dir) NewStore(Func<DateTimeOffset>? clock = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cfg = AppConfig.Default with { Storage = AppConfig.Default.Storage with { StorageDirectory = dir } };
        var audio = new EncryptedAudioStore(cfg, new FileKeyProvider(cfg));
        return (new SqliteSessionStore(cfg, audio, clock), audio, dir);
    }

    private static ChunkResult Result(double start, string transcript) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(start + 1),
            new FusedResult(new ValenceArousal(0.3, 0.6), Emotion.Mutlu, Emotion.Sakin,
                Confidence.Yuksek, true, false, IncongruencePattern.Yok, 0.9,
                TextVa: new ValenceArousal(0.3, 0.6), ProsodyVa: new ValenceArousal(0.2, 0.6),
                TextConfidence: 0.8, ProsodyConfidence: 0.7),
            transcript);

    // --- 10b: konuşmacı ayrımı için zaman damgalı transkript + konuşmacı bölgeleri ---

    private static ChunkResult ResultWithSegments(double start, string transcript,
        params TranscriptSegment[] segments) =>
        Result(start, transcript) with { Segments = segments };

    [Fact]
    public async Task SaveChunk_TranskriptSegmentleriniSaklar_VeSirayaGoreOkur()
    {
        var (store, _, _) = NewStore();
        var id = Guid.NewGuid();

        await store.SaveChunkAsync(id, ResultWithSegments(30, "Merhaba dünya",
            new TranscriptSegment("Merhaba", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(32)),
            new TranscriptSegment("dünya", TimeSpan.FromSeconds(32), TimeSpan.FromSeconds(34))),
            CancellationToken.None);
        await store.SaveChunkAsync(id, ResultWithSegments(40, "iyiyim",
            new TranscriptSegment("iyiyim", TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(42))),
            CancellationToken.None);

        var segs = await store.GetTranscriptSegmentsAsync(id, CancellationToken.None);

        Assert.Equal(3, segs.Count);
        Assert.Equal("Merhaba", segs[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(30), segs[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(34), segs[1].End);
        Assert.Equal("iyiyim", segs[2].Text);          // chunk'lar arası sıra korunur
    }

    [Fact]
    public async Task GetTranscriptSegments_SegmentsizOturum_BosDondurur()
    {
        var (store, _, _) = NewStore();
        var id = Guid.NewGuid();
        await store.SaveChunkAsync(id, Result(0, "metin"), CancellationToken.None);

        Assert.Empty(await store.GetTranscriptSegmentsAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task GetTranscriptSegments_BilinmeyenOturum_BosDondurur()
    {
        var (store, _, _) = NewStore();
        Assert.Empty(await store.GetTranscriptSegmentsAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task SaveSpeakerSegments_RoundTrip()
    {
        var (store, _, _) = NewStore();
        var id = Guid.NewGuid();

        await store.SaveSpeakerSegmentsAsync(id,
        [
            new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(15)),
            new SpeakerSegment(new SpeakerId(1), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)),
        ], CancellationToken.None);

        var segs = await store.GetSpeakerSegmentsAsync(id, CancellationToken.None);

        Assert.Equal(2, segs.Count);
        Assert.Equal(new SpeakerId(0), segs[0].Speaker);
        Assert.Equal(TimeSpan.FromSeconds(15), segs[0].End);
        Assert.Equal(new SpeakerId(1), segs[1].Speaker);
    }

    [Fact]
    public async Task SaveSpeakerSegments_TekrarCalistirma_CiftKayitYapmaz()
    {
        // Arka plan geçişi yeniden çalıştırılabilir olmalı (kullanıcı raporu tekrar açtı vb.).
        var (store, _, _) = NewStore();
        var id = Guid.NewGuid();
        SpeakerSegment[] first = [new(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(10))];
        SpeakerSegment[] second =
        [
            new(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(5)),
            new(new SpeakerId(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)),
        ];

        await store.SaveSpeakerSegmentsAsync(id, first, CancellationToken.None);
        await store.SaveSpeakerSegmentsAsync(id, second, CancellationToken.None);

        var segs = await store.GetSpeakerSegmentsAsync(id, CancellationToken.None);
        Assert.Equal(2, segs.Count);                    // eskisi silindi, yenisi yazıldı
        Assert.Equal(new SpeakerId(1), segs[1].Speaker);
    }

    [Fact]
    public async Task SaveSpeakerSegments_BilinmeyenKonusmaci_EksiBirOlarakKorunur()
    {
        var (store, _, _) = NewStore();
        var id = Guid.NewGuid();

        await store.SaveSpeakerSegmentsAsync(id,
            [new SpeakerSegment(SpeakerId.Unknown, TimeSpan.Zero, TimeSpan.FromSeconds(3))],
            CancellationToken.None);

        var segs = await store.GetSpeakerSegmentsAsync(id, CancellationToken.None);
        Assert.Equal(SpeakerId.Unknown, segs[0].Speaker);
    }

    [Fact]
    public async Task GetSession_ChunklaraSegmentDagitmaz_OturumDuzeyindeOkunur()
    {
        // Segmentler OTURUM düzeyinde saklanır, chunk'a değil: örtüşmeli pencerelerde bir
        // segment iki chunk'a birden düşerdi ve "hangi chunk'ın?" sorusunun doğru cevabı yok.
        // Konuşmacı hizalaması zaten oturum düzeyinde çalışır (GetTranscriptSegmentsAsync).
        var (store, _, _) = NewStore();
        var id = Guid.NewGuid();
        await store.SaveChunkAsync(id, ResultWithSegments(0, "merhaba",
            new TranscriptSegment("merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(2))),
            CancellationToken.None);

        var session = await store.GetSessionAsync(id, CancellationToken.None);
        Assert.Empty(session!.Chunks[0].Segments);      // chunk'a geri yazılmaz

        var segs = await store.GetTranscriptSegmentsAsync(id, CancellationToken.None);
        Assert.Single(segs);                            // ama kaybolmaz
        Assert.Equal("merhaba", segs[0].Text);
    }

    [Fact]
    public async Task SaveChunk_GetSession_RoundTripsMetricsAndTranscript()
    {
        var (store, _, _) = NewStore();
        var id = Guid.NewGuid();
        await store.SaveChunkAsync(id, Result(0, "merhaba"), CancellationToken.None);
        await store.SaveChunkAsync(id, Result(1, "dünya"), CancellationToken.None);

        var session = await store.GetSessionAsync(id, CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal(id, session!.Id);
        Assert.Equal(2, session.Chunks.Count);
        Assert.Equal("merhaba", session.Chunks[0].Transcript);
        Assert.Equal(TimeSpan.FromSeconds(1), session.Chunks[0].TimeEnd);
        Assert.Equal(Emotion.Mutlu, session.Chunks[0].Fused.PrimaryEmotion);
        Assert.Equal(Confidence.Yuksek, session.Chunks[0].Fused.Confidence);
        Assert.Equal(0.3, session.Chunks[0].Fused.Combined.Valence, precision: 5);
        Assert.Equal("dünya", session.Chunks[1].Transcript);
    }

    [Fact]
    public async Task GetSession_Unknown_ReturnsNull()
    {
        var (store, _, _) = NewStore();
        Assert.Null(await store.GetSessionAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ListSessions_ReturnsAll()
    {
        var (store, _, _) = NewStore();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        await store.SaveChunkAsync(a, Result(0, "a"), CancellationToken.None);
        await store.SaveChunkAsync(b, Result(0, "b"), CancellationToken.None);
        var list = await store.ListSessionsAsync(CancellationToken.None);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, s => s.Id == a);
        Assert.Contains(list, s => s.Id == b);
    }

    [Fact]
    public async Task Persistence_SurvivesNewStoreInstance()
    {
        var (store, audio, dir) = NewStore();
        var id = Guid.NewGuid();
        await store.SaveChunkAsync(id, Result(0, "kalıcı"), CancellationToken.None);

        var cfg = AppConfig.Default with { Storage = AppConfig.Default.Storage with { StorageDirectory = dir } };
        var store2 = new SqliteSessionStore(cfg, audio);   // aynı db dosyası
        var session = await store2.GetSessionAsync(id, CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal("kalıcı", session!.Chunks[0].Transcript);
    }

    [Fact]
    public async Task PurgeExpiredAudio_DeletesOldAudio_KeepsMetrics()
    {
        // t0'da kayıt; saat 8 gün ileri → retention (7g) dolar.
        var now = DateTimeOffset.UnixEpoch;
        var (store, _, _) = NewStore(() => now);
        var id = Guid.NewGuid();
        await store.SaveChunkAsync(id, Result(0, "metrik"), CancellationToken.None);
        var audioPath = await store.SaveAudioAsync(id, new byte[] { 1, 2, 3, 4 }, CancellationToken.None);
        Assert.True(File.Exists(audioPath));

        now = DateTimeOffset.UnixEpoch.AddDays(8); // saat ilerledi
        await store.PurgeExpiredAudioAsync(CancellationToken.None);

        Assert.False(File.Exists(audioPath), "süresi dolan ses silinmeli");
        var session = await store.GetSessionAsync(id, CancellationToken.None);
        Assert.NotNull(session);                       // metrik korunur
        Assert.Single(session!.Chunks);
        Assert.Equal("metrik", session.Chunks[0].Transcript);
    }

    [Fact]
    public async Task PurgeExpiredAudio_KeepsFreshAudio()
    {
        var now = DateTimeOffset.UnixEpoch;
        var (store, _, _) = NewStore(() => now);
        var id = Guid.NewGuid();
        var audioPath = await store.SaveAudioAsync(id, new byte[] { 9 }, CancellationToken.None);

        now = DateTimeOffset.UnixEpoch.AddDays(3); // retention dolmadı
        await store.PurgeExpiredAudioAsync(CancellationToken.None);
        Assert.True(File.Exists(audioPath), "taze ses korunmalı");
    }
}
