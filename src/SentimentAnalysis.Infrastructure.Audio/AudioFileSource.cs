using System.Diagnostics;
using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>
/// Bir ses/video dosyasından <see cref="IAudioCaptureSource"/> üretir. WAV doğrudan okunur;
/// diğer formatlar geçici bir 16 kHz mono WAV'a dönüştürülür.
/// <para>Hangi aracın çağrılacağına saf <see cref="TranscodePlan"/> karar verir (macOS → afconvert,
/// Windows/Linux → ffmpeg). Burada yalnızca <c>Process</c> ve dosya sistemi işi yapılır —
/// motor sınırı kuralı.</para>
/// <para>Hem Avalonia kabuğu hem CLI bunu kullanır; dönüştürme mantığı tek yerde yaşar.</para>
/// </summary>
public static class AudioFileSource
{
    /// <summary>Dönüştürmenin tamamlanması için üst sınır — asılı kalan bir sürece karşı koruma.</summary>
    private static readonly TimeSpan TranscodeTimeout = TimeSpan.FromMinutes(10);

    public static IAudioCaptureSource Create(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Ses dosyası bulunamadı: {path}", path);

        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (TranscodePlan.IsNative(ext))
            return new FileCaptureSource(path);

        if (!TranscodePlan.IsTranscodable(ext))
            throw new NotSupportedException(TranscodePlan.UnsupportedMessage(ext));

        var outWav = Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N") + ".wav");
        var plan = TranscodePlan.For(path, outWav, OperatingSystem.IsMacOS());

        try
        {
            RunTranscode(plan, outWav);
        }
        catch
        {
            TryDelete(outWav);   // yarım kalan çıktıyı bırakma
            throw;
        }

        return new TempWavCaptureSource(outWav);
    }

    private static void RunTranscode(TranscodeCommand plan, string outWav)
    {
        var toolPath = ResolveTool(plan);

        var psi = new ProcessStartInfo(toolPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in plan.Arguments) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"{plan.ToolDisplayName} başlatılamadı.");

        // stderr'i process bitmeden oku: ffmpeg ayrıntılı yazdığında boru dolar ve
        // WaitForExit() sonsuza kadar bloklanır (klasik deadlock).
        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();

        if (!process.WaitForExit((int)TranscodeTimeout.TotalMilliseconds))
        {
            TryKill(process);
            throw new TimeoutException(
                $"{plan.ToolDisplayName} {TranscodeTimeout.TotalMinutes:0} dakikada bitmedi; iptal edildi.");
        }

        var stderr = stderrTask.GetAwaiter().GetResult();
        _ = stdoutTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{plan.ToolDisplayName} başarısız (çıkış kodu {process.ExitCode}): {stderr.Trim()}");

        if (!File.Exists(outWav) || new FileInfo(outWav).Length <= 44)
            throw new InvalidOperationException(
                $"{plan.ToolDisplayName} çalıştı ama kullanılabilir bir WAV üretmedi. " +
                $"Dosyada ses akışı olmayabilir. {plan.ToolDisplayName} çıktısı: {stderr.Trim()}");
    }

    /// <summary>Aday araçları sırayla dener: mutlak yol varsa dosya olarak, aksi halde PATH üzerinden.</summary>
    private static string ResolveTool(TranscodeCommand plan)
    {
        foreach (var candidate in plan.ToolCandidates)
        {
            if (Path.IsPathRooted(candidate))
            {
                if (File.Exists(candidate)) return candidate;
                continue;
            }
            var onPath = FindOnPath(candidate);
            if (onPath is not null) return onPath;
        }

        throw new InvalidOperationException(
            $"Ses dönüştürme için {plan.ToolDisplayName} gerekli ama bulunamadı. {plan.InstallHint} " +
            "Alternatif: dosyayı kendiniz 16 kHz mono WAV'a çevirip öyle seçin.");
    }

    private static string? FindOnPath(string exeName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) return null;

        // Windows'ta PATH girdileri tırnaklı olabilir; ayrıca uzantısız ada .exe eklenmeli.
        var names = OperatingSystem.IsWindows() && !exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? new[] { exeName + ".exe", exeName }
            : new[] { exeName };

        foreach (var rawDir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = rawDir.Trim().Trim('"');
            if (dir.Length == 0) continue;
            foreach (var name in names)
            {
                string full;
                try { full = Path.Combine(dir, name); }
                catch (ArgumentException) { continue; }   // PATH'te geçersiz karakter
                if (File.Exists(full)) return full;
            }
        }
        return null;
    }

    private static void TryKill(Process p)
    {
        try { p.Kill(entireProcessTree: true); } catch { /* zaten bitmiş olabilir */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* temp temizliğine bırak */ }
    }
}
