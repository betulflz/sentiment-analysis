using System.Diagnostics;
using System.Globalization;
using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Infrastructure.Vision;

/// <summary>Ses/video dosyasının sesini ffmpeg ile BELLEKTE 16 kHz mono float32'ye çözer (stdout boru hattı; diske
/// ara dosya yazılmaz). Karşı tarafa odak (2026-11-13): dosya modunda analizden önce konuşmacı ayrımının girdisi.</summary>
public sealed class FfmpegAudioFileDecoder : IAudioFileDecoder
{
    public async Task<float[]> DecodeMono16kAsync(string path, TimeSpan maxDuration, CancellationToken ct)
    {
        // Güvenlik: yalnız var olan YEREL dosya; ffmpeg girdisi "file" protokolüyle sınırlı (oynatma listesi/concat
        // içindeki uzak adreslere ya da başka protokollere gidilmez). Yol "file:" önekiyle verilir ki "-" ile başlayan
        // ya da "concat:" gibi görünen ad seçenek/protokol sayılmasın.
        if (path.Contains("://", StringComparison.Ordinal) || !File.Exists(path))
            throw new InvalidOperationException($"Yalnız bu bilgisayardaki yerel bir dosya çözülebilir: {path}");
        var full = Path.GetFullPath(path);

        var psi = new ProcessStartInfo(FfmpegLocator.Find())
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in new[] { "-nostdin", "-hide_banner", "-loglevel", "error",
                     "-protocol_whitelist", "file", "-i", "file:" + full,
                     "-t", maxDuration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                     "-vn", "-ac", "1", "-ar", "16000", "-f", "f32le", "pipe:1" })
            psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg başlatılamadı.");
        try
        {
            var stderr = process.StandardError.ReadToEndAsync(ct);
            using var buffer = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Dosyanın sesi çözülemedi (ffmpeg {process.ExitCode}): {(await stderr.ConfigureAwait(false)).Trim()}");
            var bytes = buffer.GetBuffer();
            var samples = new float[buffer.Length / 4];
            Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 4);
            return samples;
        }
        finally
        {
            FfmpegVideoCaptureSource.Kill(process);
        }
    }
}
