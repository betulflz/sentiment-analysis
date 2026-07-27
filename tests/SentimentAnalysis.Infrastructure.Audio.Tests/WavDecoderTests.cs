using System;
using System.IO;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class WavDecoderTests
{
    // Minimal WAV yazıcı (test yardımcısı): PCM16/PCM32/float32, verilen kanal/rate.
    private static byte[] WriteWav(short audioFormat, short bitsPerSample, int sampleRate, int channels, byte[] data)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        short blockAlign = (short)(channels * (bitsPerSample / 8));
        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(36 + data.Length);
        w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(16);
        w.Write(audioFormat);
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write(bitsPerSample);
        w.Write(new[] { 'd', 'a', 't', 'a' });
        w.Write(data.Length);
        w.Write(data);
        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void Decode_Pcm16Mono_ReturnsNormalizedFloats()
    {
        // örnekler: 0, 32767 (~+1), -32768 (-1)
        using var dms = new MemoryStream();
        using (var dw = new BinaryWriter(dms))
        {
            dw.Write((short)0);
            dw.Write((short)32767);
            dw.Write((short)-32768);
        }
        var wav = WriteWav(1, 16, 16000, 1, dms.ToArray());

        var d = WavDecoder.Decode(wav);

        Assert.Equal(16000, d.SampleRate);
        Assert.Equal(1, d.Channels);
        Assert.Equal(3, d.Interleaved.Length);
        Assert.Equal(0f, d.Interleaved[0], precision: 4);
        Assert.True(d.Interleaved[1] > 0.99f, $"~+1 beklenir, {d.Interleaved[1]}");
        Assert.Equal(-1f, d.Interleaved[2], precision: 4);
    }

    [Fact]
    public void Decode_Pcm16Stereo_KeepsInterleavedAndChannels()
    {
        using var dms = new MemoryStream();
        using (var dw = new BinaryWriter(dms))
        {
            dw.Write((short)32767); dw.Write((short)-32768); // frame 0: L,R
            dw.Write((short)0);     dw.Write((short)0);      // frame 1
        }
        var wav = WriteWav(1, 16, 44100, 2, dms.ToArray());

        var d = WavDecoder.Decode(wav);

        Assert.Equal(44100, d.SampleRate);
        Assert.Equal(2, d.Channels);
        Assert.Equal(4, d.Interleaved.Length); // 2 frame * 2 kanal
        Assert.True(d.Interleaved[0] > 0.99f);
        Assert.Equal(-1f, d.Interleaved[1], precision: 4);
    }

    [Fact]
    public void Decode_Float32_ReturnsSamplesDirectly()
    {
        using var dms = new MemoryStream();
        using (var dw = new BinaryWriter(dms))
        {
            dw.Write(0.5f);
            dw.Write(-0.25f);
        }
        var wav = WriteWav(3, 32, 16000, 1, dms.ToArray()); // format 3 = IEEE float

        var d = WavDecoder.Decode(wav);

        Assert.Equal(2, d.Interleaved.Length);
        Assert.Equal(0.5f, d.Interleaved[0], precision: 5);
        Assert.Equal(-0.25f, d.Interleaved[1], precision: 5);
    }

    [Fact]
    public void Decode_Pcm32Mono_ReturnsNormalizedFloats()
    {
        using var dms = new MemoryStream();
        using (var dw = new BinaryWriter(dms))
        {
            dw.Write(0);              // 0 → 0.0
            dw.Write(int.MaxValue);   // ~+1
            dw.Write(int.MinValue);   // -1 exactly
        }
        var wav = WriteWav(1, 32, 16000, 1, dms.ToArray());

        var d = WavDecoder.Decode(wav);

        Assert.Equal(3, d.Interleaved.Length);
        Assert.Equal(0f, d.Interleaved[0], precision: 4);
        Assert.True(d.Interleaved[1] > 0.99f, $"~+1 beklenir, {d.Interleaved[1]}");
        Assert.Equal(-1f, d.Interleaved[2], precision: 4);
    }

    [Fact]
    public void Decode_UnsupportedBitDepth_Throws()
    {
        var wav = WriteWav(1, 24, 16000, 1, new byte[6]); // 24-bit desteklenmiyor
        Assert.Throws<NotSupportedException>(() => WavDecoder.Decode(wav));
    }

    [Fact]
    public void Decode_NotRiff_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WavDecoder.Decode(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }
}
