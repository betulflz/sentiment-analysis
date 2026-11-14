using SentimentAnalysis.Desktop;
using SentimentAnalysis.Infrastructure.Audio;

namespace SentimentAnalysis.Desktop.Avalonia;

/// <summary>macOS <c>afplay</c> ile çalar. Örnek geçici klasöre PCM16 WAV olarak yazılır ve çalma bitince/iptalde
/// silinir (ses kalıcı olarak diske yazılmaz). Yeni çağrı öncekini iptal eder.</summary>
public sealed class AfplaySamplePlayer(IExternalProcessRunner runner, IRuntimePlatform platform, string? tempDirectory = null)
    : ISpeakerSamplePlayer
{
    private readonly object _gate = new();
    private CancellationTokenSource? _current;

    public bool IsSupported => platform.IsMacOS;

    public async Task PlayAsync(float[] mono16k, CancellationToken ct)
    {
        if (!IsSupported) return;
        var mine = CancellationTokenSource.CreateLinkedTokenSource(ct);
        CancellationTokenSource? previous;
        lock (_gate) { previous = _current; _current = mine; }
        previous?.Cancel();

        var path = Path.Combine(tempDirectory ?? Path.GetTempPath(), $"konusmaci-ornek-{Guid.NewGuid():N}.wav");
        try
        {
            await File.WriteAllBytesAsync(path, Wav(mono16k), mine.Token).ConfigureAwait(false);
            await runner.RunAsync("/usr/bin/afplay", new[] { path }, mine.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* yeni Dinle ya da panel kapandı */ }
        catch (Exception ex)
        {
            // Dinle bir async komut: istisna UI iş parçacığında uygulamayı çökertirdi. Örnek dinletilemez, seçim sürer.
            System.Diagnostics.Debug.WriteLine($"Konuşmacı örneği çalınamadı: {ex.Message}");
        }
        finally
        {
            try { File.Delete(path); } catch (Exception) { }
            lock (_gate) { if (ReferenceEquals(_current, mine)) _current = null; }
            mine.Dispose();
        }
    }

    internal static byte[] Wav(float[] s)
    {
        using var ms = new MemoryStream(44 + s.Length * 2);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + s.Length * 2); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(s.Length * 2);
        foreach (var v in s) w.Write((short)Math.Clamp(v * 32767f, short.MinValue, short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
