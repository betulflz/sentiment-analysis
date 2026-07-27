using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class TempWavCaptureSourceTests
{
    private static string WriteTempWav(int sampleRate, int channels, int frames)
    {
        var path = Path.Combine(Path.GetTempPath(), "sa-tmp-" + Guid.NewGuid().ToString("N") + ".wav");
        using var w = new BinaryWriter(File.Create(path));
        int dataLen = frames * channels * 2;
        w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataLen); w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)channels);
        w.Write(sampleRate); w.Write(sampleRate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataLen);
        for (int i = 0; i < frames * channels; i++) w.Write((short)0);
        return path;
    }

    [Fact]
    public async Task CaptureAsync_DeletesTempFileAfterEnumeration()
    {
        var path = WriteTempWav(16000, 1, 32000); // 2 sn
        var src = new TempWavCaptureSource(path);
        var opts = new CaptureOptions(TimeSpan.FromSeconds(1), TimeSpan.Zero);

        var chunks = new List<Core.AudioChunk>();
        await foreach (var c in src.CaptureAsync(opts, CancellationToken.None)) chunks.Add(c);

        Assert.NotEmpty(chunks);
        Assert.False(File.Exists(path), "Geçici WAV akış bittikten sonra silinmeliydi.");
    }

    [Fact]
    public async Task CaptureAsync_DeletesTempFileEvenWhenCancelled()
    {
        var path = WriteTempWav(16000, 1, 160000); // 10 sn → birden çok chunk
        var src = new TempWavCaptureSource(path);
        var opts = new CaptureOptions(TimeSpan.FromSeconds(1), TimeSpan.Zero);
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in src.CaptureAsync(opts, cts.Token))
                cts.Cancel();   // ilk chunk'tan sonra iptal et
        });

        Assert.False(File.Exists(path), "İptal edilse de geçici WAV silinmeliydi.");
    }

    [Fact]
    public async Task CaptureAsync_DeletesTempFileWhenDecodingFails()
    {
        // Bozuk WAV: başlık geçerli değil → WavDecoder fırlatır. Dosya yine de temizlenmeli.
        var path = Path.Combine(Path.GetTempPath(), "sa-bad-" + Guid.NewGuid().ToString("N") + ".wav");
        await File.WriteAllBytesAsync(path, new byte[64]); // 44+ bayt ama RIFF değil
        var src = new TempWavCaptureSource(path);
        var opts = new CaptureOptions(TimeSpan.FromSeconds(1), TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var _ in src.CaptureAsync(opts, CancellationToken.None)) { }
        });

        Assert.False(File.Exists(path), "Çözme hatasında da geçici WAV silinmeliydi.");
    }
}
