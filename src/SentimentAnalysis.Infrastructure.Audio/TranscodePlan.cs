namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>Bir ses dosyasını 16 kHz mono WAV'a çevirmek için gereken harici araç çağrısı.
/// <paramref name="ToolCandidates"/> sırayla denenir (ilk bulunan kullanılır).</summary>
public sealed record TranscodeCommand(
    IReadOnlyList<string> ToolCandidates,
    IReadOnlyList<string> Arguments,
    string ToolDisplayName,
    string InstallHint);

/// <summary>
/// Hangi uzantının hangi araçla, hangi argümanlarla 16 kHz mono WAV'a çevrileceğine karar verir.
/// <b>Saf ve deterministik</b> — dosya sistemine veya <c>Process</c>'e dokunmaz, bu yüzden
/// platform-başına davranış birim testlenebilir (motor sınırı kuralı: IO
/// <see cref="TranscodingAudioSourceFactory"/> tarafında).
/// </summary>
public static class TranscodePlan
{
    /// <summary>WAV dışında kabul edilen kaynak formatlar.</summary>
    public static IReadOnlyCollection<string> TranscodableExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wma", ".mp4", ".mkv", ".webm" };

    /// <summary>Doğrudan çözülebilen (dönüştürme gerektirmeyen) formatlar.</summary>
    public static IReadOnlyCollection<string> NativeExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".wav" };

    /// <summary>Uzantı doğrudan <see cref="WavDecoder"/> ile çözülebilir mi?</summary>
    public static bool IsNative(string extension) => NativeExtensions.Contains(extension);

    /// <summary>Uzantı (WAV olmamak kaydıyla) desteklenen bir dönüştürme kaynağı mı?</summary>
    public static bool IsTranscodable(string extension) => TranscodableExtensions.Contains(extension);

    /// <summary>
    /// <paramref name="inputPath"/> → <paramref name="outputWavPath"/> (16 kHz mono PCM16) için komut üretir.
    /// macOS'ta sistemle gelen <c>afconvert</c>, diğer platformlarda <c>ffmpeg</c> tercih edilir;
    /// macOS'ta da ffmpeg yedek olarak listelenir (afconvert bazı konteynerleri açamaz).
    /// </summary>
    public static TranscodeCommand For(string inputPath, string outputWavPath, bool isMacOs)
    {
        if (isMacOs)
        {
            return new TranscodeCommand(
                ToolCandidates: new[] { "/usr/bin/afconvert", "afconvert" },
                Arguments: new[]
                {
                    "-f", "WAVE",
                    "-d", "LEI16@16000",
                    "-c", "1",
                    inputPath, outputWavPath,
                },
                ToolDisplayName: "afconvert",
                InstallHint: "afconvert macOS ile birlikte gelir; bulunamadıysa ffmpeg kurun: brew install ffmpeg");
        }

        return new TranscodeCommand(
            ToolCandidates: new[] { "ffmpeg", "ffmpeg.exe" },
            Arguments: new[]
            {
                "-hide_banner", "-loglevel", "error",
                "-nostdin",
                "-y",                       // varolan geçici dosyanın üzerine yaz
                "-i", inputPath,
                "-vn",                      // video akışını yok say (mp4/mkv girdileri)
                "-ac", "1",                 // mono
                "-ar", "16000",             // 16 kHz
                "-acodec", "pcm_s16le",     // WavDecoder'ın desteklediği format
                "-f", "wav",
                outputWavPath,
            },
            ToolDisplayName: "ffmpeg",
            InstallHint: OperatingSystem.IsWindows()
                ? "ffmpeg bulunamadı. Kurulum: winget install Gyan.FFmpeg (ardından terminali yeniden açın)."
                : "ffmpeg bulunamadı. Kurulum: apt install ffmpeg / dnf install ffmpeg.");
    }

    /// <summary>Desteklenmeyen uzantı için kullanıcıya gösterilecek mesaj.</summary>
    public static string UnsupportedMessage(string extension)
    {
        var all = NativeExtensions.Concat(TranscodableExtensions).Select(e => e.TrimStart('.'));
        return $"Desteklenmeyen ses formatı: '{extension}'. Desteklenenler: {string.Join(", ", all)}.";
    }
}
