using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class RecordingCaptureSourceTests
{
    private sealed class FakeSource(int count, float value = 0.5f) : IAudioCaptureSource
    {
        public async IAsyncEnumerable<AudioChunk> CaptureAsync(
            CaptureOptions o, [EnumeratorCancellation] CancellationToken ct)
        {
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var samples = new float[1600];
                Array.Fill(samples, value);
                yield return new AudioChunk(samples, 16000,
                    TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(i + 1));
                await Task.Yield();
            }
        }
    }

    private static readonly CaptureOptions Opts = new(TimeSpan.FromSeconds(1), TimeSpan.Zero);

    private static async Task<int> DrainAsync(IAudioCaptureSource src, CancellationToken ct = default)
    {
        int n = 0;
        await foreach (var _ in src.CaptureAsync(Opts, ct)) n++;
        return n;
    }

    [Fact]
    public async Task Chunklari_DegistirmedenGecirir()
    {
        var src = new RecordingCaptureSource(new FakeSource(5), maxSamples: 1_000_000);
        Assert.Equal(5, await DrainAsync(src));
    }

    [Fact]
    public async Task GetPcm16_BiriktirilenOrnekleriDondurur()
    {
        var src = new RecordingCaptureSource(new FakeSource(3), maxSamples: 1_000_000);
        await DrainAsync(src);

        var pcm = src.GetPcm16();
        Assert.Equal(3 * 1600 * 2, pcm.Length);      // örnek başına 2 bayt
        Assert.False(src.LimitReached);
    }

    [Fact]
    public async Task UstSinirAsilirsa_BiriktirmeDurur_AmaChunkAkisiKesilmez()
    {
        // 3 chunk × 1600 örnek = 4800; sınır 3200 → yalnızca ilk 2 chunk sığar
        var src = new RecordingCaptureSource(new FakeSource(3), maxSamples: 3200);

        int passed = await DrainAsync(src);

        Assert.Equal(3, passed);                      // analiz akışı SÜRER — ses biriktirme durur
        Assert.True(src.LimitReached);
        Assert.Equal(3200 * 2, src.GetPcm16().Length);
    }

    [Fact]
    public async Task UstSinirTamOrtasindaKalirsa_KismenBiriktirir()
    {
        // sınır 2400 → ilk chunk (1600) tam, ikinciden yalnızca 800 örnek
        var src = new RecordingCaptureSource(new FakeSource(3), maxSamples: 2400);
        await DrainAsync(src);

        Assert.True(src.LimitReached);
        Assert.Equal(2400 * 2, src.GetPcm16().Length);
    }

    [Fact]
    public async Task HicChunkYoksa_BosPcmDondurur()
    {
        var src = new RecordingCaptureSource(new FakeSource(0), maxSamples: 1000);
        await DrainAsync(src);

        Assert.Empty(src.GetPcm16());
        Assert.False(src.LimitReached);
    }

    [Fact]
    public async Task Pcm16_Little_Endian_Ve_Olcekli()
    {
        // 0.5f → 0.5 * 32768 = 16384 = 0x4000 → little-endian: 0x00, 0x40
        var src = new RecordingCaptureSource(new FakeSource(1, value: 0.5f), maxSamples: 1_000_000);
        await DrainAsync(src);

        var pcm = src.GetPcm16();
        Assert.Equal(0x00, pcm[0]);
        Assert.Equal(0x40, pcm[1]);
    }

    [Fact]
    public async Task Pcm16_TasmayiKirpar_SarmalanmaOlmaz()
    {
        // 1.5f ölçeklenince short sınırını aşar; sarmalanırsa ses bozulur (negatife döner).
        var src = new RecordingCaptureSource(new FakeSource(1, value: 1.5f), maxSamples: 1_000_000);
        await DrainAsync(src);

        var pcm = src.GetPcm16();
        short first = BitConverter.ToInt16(pcm, 0);
        Assert.Equal(short.MaxValue, first);
    }

    [Fact]
    public async Task Iptal_IcKaynaginDavranisiniDegistirmez()
    {
        // İptal sözleşmesi (IAudioCaptureSource): dekoratör iptali yutmamalı/geciktirmemeli.
        var src = new RecordingCaptureSource(new FakeSource(100), maxSamples: 1_000_000);
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in src.CaptureAsync(Opts, cts.Token)) cts.Cancel();
        });
    }

    /// <summary>Gerçek akış gibi örtüşen parçalar: 10 sn uzunluk, 2 sn örtüşme (8 sn adım). Her örneğin
    /// değeri kendi mutlak saniyesidir — kayıttaki konum ile gerçek zaman karşılaştırılabilsin.</summary>
    private sealed class OverlappingSource(int count, int rate = 100) : IAudioCaptureSource
    {
        public async IAsyncEnumerable<AudioChunk> CaptureAsync(
            CaptureOptions o, [EnumeratorCancellation] CancellationToken ct)
        {
            for (int i = 0; i < count; i++)
            {
                double start = i * 8;
                var samples = new float[10 * rate];
                for (int k = 0; k < samples.Length; k++) samples[k] = (float)(start + (double)k / rate) / 100f;
                yield return new AudioChunk(samples, rate, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(start + 10));
                await Task.Yield();
            }
        }
    }

    [Fact]
    public async Task OrtusenParcalar_KayitGercekZamanlaHizaliKalir()
    {
        // 2026-11-08 canlı oturum: 3 parçalık 26 sn'lik görüşmenin kaydı 39,4 sn çıktı — örtüşme kayda iki kez
        // giriyor, konuşmacı bölgeleri her parçada 2 sn kayıyordu; transkript satırları yanlış kişiye gidiyordu.
        var src = new RecordingCaptureSource(new OverlappingSource(3), maxSamples: 1_000_000);
        await DrainAsync(src);

        var pcm = src.GetPcm16();
        Assert.Equal(26 * 100 * 2, pcm.Length); // 0–26 sn, örtüşmeler bir kez
        // 20. saniyedeki örnek gerçekten 20. saniyenin sesi olmalı.
        short at20 = (short)(pcm[20 * 100 * 2] | (pcm[20 * 100 * 2 + 1] << 8));
        Assert.Equal(0.20f, at20 / 32768f, 3);
    }
}
