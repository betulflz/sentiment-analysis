using System;
using System.IO;
using System.Threading.Tasks;
using SentimentAnalysis.Infrastructure.Vision;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Vision.Tests;

public class FfmpegAudioFileDecoderTests
{
    private static bool FfmpegPresent() { try { FfmpegLocator.Find(); return true; } catch { return false; } }

    private static string Wav(double seconds, int rate = 44100)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dec-{Guid.NewGuid():N}.wav");
        int n = (int)(seconds * rate);
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(n * 2);
        for (int i = 0; i < n; i++) w.Write((short)(8000 * Math.Sin(2 * Math.PI * 440 * i / rate)));
        return path;
    }

    [SkippableFact]
    public async Task Coz_16kMono_SureDogru()
    {
        Skip.IfNot(FfmpegPresent(), "ffmpeg yok");
        var p = Wav(2.0);
        try
        {
            var s = await new FfmpegAudioFileDecoder().DecodeMono16kAsync(p, TimeSpan.FromMinutes(1), default);
            Assert.InRange(s.Length, 31_500, 32_500);
            Assert.Contains(s, v => Math.Abs(v) > 0.1f);
        }
        finally { File.Delete(p); }
    }

    [SkippableFact]
    public async Task UzunlukSiniri_Uygulanir()
    {
        Skip.IfNot(FfmpegPresent(), "ffmpeg yok");
        var p = Wav(3.0);
        try
        {
            var s = await new FfmpegAudioFileDecoder().DecodeMono16kAsync(p, TimeSpan.FromSeconds(1), default);
            Assert.InRange(s.Length, 15_500, 16_500);
        }
        finally { File.Delete(p); }
    }

    [SkippableFact]
    public async Task OlmayanDosya_AnlasilirHata()
    {
        Skip.IfNot(FfmpegPresent(), "ffmpeg yok");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FfmpegAudioFileDecoder().DecodeMono16kAsync("/yok/boyle-bir-dosya.wav", TimeSpan.FromMinutes(1), default));
        Assert.Contains("çözülemedi", ex.Message);
    }
}
