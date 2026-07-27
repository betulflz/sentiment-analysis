using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class FileCaptureSourceTests
{
    private static string WriteTempWav(int sampleRate, int channels, int frames)
    {
        var path = Path.Combine(Path.GetTempPath(), "sa-cap-" + Guid.NewGuid().ToString("N") + ".wav");
        using var w = new BinaryWriter(File.Create(path));
        int dataLen = frames * channels * 2; // PCM16
        w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataLen); w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)channels);
        w.Write(sampleRate); w.Write(sampleRate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataLen);
        for (int i = 0; i < frames * channels; i++) w.Write((short)0);
        return path;
    }

    [Fact]
    public async Task CaptureAsync_WavFile_YieldsChunks()
    {
        var path = WriteTempWav(16000, 1, 48000); // 3 sn mono 16k
        try
        {
            var src = new FileCaptureSource(path);
            var opts = new CaptureOptions(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
            var chunks = new System.Collections.Generic.List<Core.AudioChunk>();
            await foreach (var c in src.CaptureAsync(opts, CancellationToken.None)) chunks.Add(c);

            Assert.Equal(2, chunks.Count);
            Assert.All(chunks, c => Assert.Equal(16000, c.SampleRate));
            Assert.False(src.IsCapturing); // akış bitince kapanır
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CaptureAsync_ResamplesStereo44kToMono16k()
    {
        var path = WriteTempWav(44100, 2, 44100); // 1 sn stereo 44.1k
        try
        {
            var src = new FileCaptureSource(path);
            var opts = new CaptureOptions(TimeSpan.FromSeconds(2), TimeSpan.Zero);
            var chunks = new System.Collections.Generic.List<Core.AudioChunk>();
            await foreach (var c in src.CaptureAsync(opts, CancellationToken.None)) chunks.Add(c);

            Assert.Single(chunks); // 1 sn < 2 sn chunk → tek chunk
            Assert.Equal(16000, chunks[0].SampleRate);
            Assert.InRange(chunks[0].Samples.Length, 15900, 16100); // ~16000 örnek (1 sn @16k)
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CaptureAsync_Mp3_ThrowsNotSupported()
    {
        var path = Path.Combine(Path.GetTempPath(), "x-" + Guid.NewGuid().ToString("N") + ".mp3");
        await File.WriteAllBytesAsync(path, new byte[] { 0, 1, 2, 3 });
        try
        {
            var src = new FileCaptureSource(path);
            var opts = new CaptureOptions(TimeSpan.FromSeconds(2), TimeSpan.Zero);
            await Assert.ThrowsAsync<NotSupportedException>(async () =>
            {
                await foreach (var _ in src.CaptureAsync(opts, CancellationToken.None)) { }
            });
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CaptureAsync_MissingFile_ThrowsFileNotFound()
    {
        var src = new FileCaptureSource("/nonexistent/nope.wav");
        var opts = new CaptureOptions(TimeSpan.FromSeconds(2), TimeSpan.Zero);
        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
        {
            await foreach (var _ in src.CaptureAsync(opts, CancellationToken.None)) { }
        });
    }
}
