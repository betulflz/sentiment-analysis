using System.Diagnostics;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Infrastructure.Audio;

namespace SentimentAnalysis.Desktop.Avalonia;

/// <summary>Seçili ses dosyasından <see cref="IAudioCaptureSource"/> üretir. WAV doğrudan; mp3/m4a/aac
/// macOS yerleşik `afconvert` ile geçici 16 kHz mono WAV'a çevrilir (Windows decode 05b). Saf değil (Process/IO).</summary>
public sealed class AfconvertAudioSourceFactory : IAudioSourceFactory
{
    public IAudioCaptureSource Create(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".wav") return new FileCaptureSource(path);
        if (ext is ".mp3" or ".m4a" or ".aac")
        {
            var outWav = Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N") + ".wav");
            var psi = new ProcessStartInfo("/usr/bin/afconvert")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("WAVE");
            psi.ArgumentList.Add("-d"); psi.ArgumentList.Add("LEI16@16000");
            psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("1");
            psi.ArgumentList.Add(path); psi.ArgumentList.Add(outWav);
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0 || !File.Exists(outWav))
                throw new InvalidOperationException($"afconvert başarısız (çıkış {p.ExitCode}): {err}");
            return new FileCaptureSource(outWav);
        }
        throw new NotSupportedException($"Desteklenmeyen ses formatı: {ext}. Desteklenen: wav, mp3, m4a, aac.");
    }
}
