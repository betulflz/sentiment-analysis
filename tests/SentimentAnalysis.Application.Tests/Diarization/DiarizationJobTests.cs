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

public class DiarizationJobTests
{
    private sealed class FakeStore(float[]? audio) : IDiarizationStore
    {
        public readonly List<SpeakerSegment> Saved = new();
        public int SaveCalls;

        public Task<float[]?> GetSessionAudioAsync(Guid id, CancellationToken ct)
            => Task.FromResult(audio);

        public Task SaveSpeakerSegmentsAsync(Guid id, IReadOnlyList<SpeakerSegment> segs, CancellationToken ct)
        {
            SaveCalls++;
            Saved.AddRange(segs);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDiarizer(IReadOnlyList<SpeakerSegment> result) : IDiarizer
    {
        public bool WasCalled;
        public Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(float[] a, CancellationToken ct)
        {
            WasCalled = true;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingDiarizer(Exception ex) : IDiarizer
    {
        public Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(float[] a, CancellationToken ct)
            => throw ex;
    }

    private static SpeakerSegment S(int spk, double a, double b) =>
        new(new SpeakerId(spk), TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b));

    [Fact]
    public async Task RunAsync_SesVarsa_KonusmaciSegmentleriniSaklar()
    {
        var store = new FakeStore(new float[16000 * 10]);
        var diarizer = new FakeDiarizer([S(0, 0, 5), S(1, 5, 10)]);

        var outcome = await new DiarizationJob(diarizer, store, AppConfig.Default)
            .RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DiarizationOutcome.Completed, outcome);
        Assert.Equal(2, store.Saved.Count);
    }

    [Fact]
    public async Task RunAsync_SesYoksa_NoAudioDoner_DiarizerCagrilmaz()
    {
        var store = new FakeStore(null);
        var diarizer = new FakeDiarizer([]);

        var outcome = await new DiarizationJob(diarizer, store, AppConfig.Default)
            .RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DiarizationOutcome.NoAudio, outcome);
        Assert.False(diarizer.WasCalled);       // pahalı işi boşuna başlatma
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task RunAsync_BosSes_NoAudioDoner()
    {
        var store = new FakeStore(Array.Empty<float>());
        var diarizer = new FakeDiarizer([]);

        var outcome = await new DiarizationJob(diarizer, store, AppConfig.Default)
            .RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DiarizationOutcome.NoAudio, outcome);
        Assert.False(diarizer.WasCalled);
    }

    [Fact]
    public async Task RunAsync_DiarizerPatlarsa_FailedDoner_HataYukariAkmaz()
    {
        // Arka plan işi uygulamayı çökertmemeli; kullanıcıya "başarısız" denir.
        var store = new FakeStore(new float[16000]);
        var diarizer = new ThrowingDiarizer(new InvalidOperationException("native patladı"));

        var outcome = await new DiarizationJob(diarizer, store, AppConfig.Default)
            .RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DiarizationOutcome.Failed, outcome);
        Assert.Equal(0, store.SaveCalls);       // yarım/boş sonuç YAZILMAZ
    }

    [Fact]
    public async Task RunAsync_ModelYok_FailedDoner()
    {
        var store = new FakeStore(new float[16000]);
        var diarizer = new ThrowingDiarizer(new System.IO.FileNotFoundException("model yok"));

        var outcome = await new DiarizationJob(diarizer, store, AppConfig.Default)
            .RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DiarizationOutcome.Failed, outcome);
    }

    [Fact]
    public async Task RunAsync_Iptal_OperationCanceledFirlatir_YarimSonucYazmaz()
    {
        // İptal bir HATA değil, koşunun durumudur — Failed'a çevrilip yutulmamalı.
        var store = new FakeStore(new float[16000]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DiarizationJob(new FakeDiarizer([]), store, AppConfig.Default)
                .RunAsync(Guid.NewGuid(), cts.Token));

        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task RunAsync_DiarizerIptalFirlatirsa_FailedYapmaz_IptaliYukariGecirir()
    {
        var store = new FakeStore(new float[16000]);
        var diarizer = new ThrowingDiarizer(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DiarizationJob(diarizer, store, AppConfig.Default)
                .RunAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task RunAsync_SaklamadanOnce_KonusmaSirasinaGoreNumaralandirir()
    {
        // Motor keyfi kimlikler döndürür; SAKLANAN veri kanonik olmalı — aksi halde raporu
        // okuyan her yüzey ayrı ayrı düzeltmek zorunda kalır ve biri unutulur.
        var store = new FakeStore(new float[16000]);
        var diarizer = new FakeDiarizer([S(5, 0, 2), S(3, 2, 4)]);

        var outcome = await new DiarizationJob(diarizer, store, AppConfig.Default)
            .RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DiarizationOutcome.Completed, outcome);
        Assert.Equal(new[] { 0, 1 }, store.Saved.Select(s => s.Speaker.Index));
    }

    [Fact]
    public async Task RunAsync_KonusmaciBulunamadi_CompletedDoner_BosListeSaklar()
    {
        // Sessiz/tek kişilik kayıtta diarization boş dönebilir; bu bir HATA değildir.
        var store = new FakeStore(new float[16000]);
        var diarizer = new FakeDiarizer([]);

        var outcome = await new DiarizationJob(diarizer, store, AppConfig.Default)
            .RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DiarizationOutcome.Completed, outcome);
        Assert.Equal(1, store.SaveCalls);       // "sonuç yok" da bir sonuçtur, yazılır
        Assert.Empty(store.Saved);
    }
}
