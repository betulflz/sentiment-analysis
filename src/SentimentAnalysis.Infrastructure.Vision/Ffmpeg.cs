using System.Text.RegularExpressions;

namespace SentimentAnalysis.Infrastructure.Vision;

/// <summary>Görüntü/video kaynağının kullanıcıya gösterilecek Türkçe hatası.</summary>
public sealed class VideoSourceException : Exception
{
    public VideoSourceException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>ffmpeg'in mutlak yolunu bulur. 11a: <c>.app</c> Finder'dan açılınca PATH'te Homebrew
/// görünmeyebilir — bu yüzden bilinen konumlar PATH'ten ÖNCE denenir.</summary>
public static class FfmpegLocator
{
    private static readonly string[] Candidates = { "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/usr/bin/ffmpeg" };

    public static string Find()
    {
        var env = Environment.GetEnvironmentVariable("SENTIMENT_FFMPEG_PATH");
        if (!string.IsNullOrEmpty(env))
            return File.Exists(env) ? env
                : throw new VideoSourceException($"SENTIMENT_FFMPEG_PATH ile belirtilen ffmpeg bulunamadı: {env}");

        foreach (var c in Candidates)
            if (File.Exists(c)) return c;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var p = Path.Combine(dir, "ffmpeg");
            if (File.Exists(p)) return p;
        }
        throw new VideoSourceException("ffmpeg bulunamadı. Kurmak için: brew install ffmpeg");
    }
}

/// <summary><c>ffmpeg -f avfoundation -list_devices true -i ""</c> çıktısını çözer.</summary>
public static class AvFoundationDevices
{
    private static readonly Regex Device = new(@"\[(\d+)\]\s+(.+)$");

    /// <summary>Yalnızca "video devices" bölümünde, adı <paramref name="nameContains"/> içeren ilk cihazın indeksi.</summary>
    public static int? FindVideoIndex(string listDevicesStderr, string nameContains)
    {
        bool inVideo = false;
        foreach (var raw in listDevicesStderr.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Contains("video devices", StringComparison.OrdinalIgnoreCase)) { inVideo = true; continue; }
            if (line.Contains("audio devices", StringComparison.OrdinalIgnoreCase)) { inVideo = false; continue; }
            if (!inVideo) continue;
            var m = Device.Match(line);
            if (m.Success && m.Groups[2].Value.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
                return int.Parse(m.Groups[1].Value);
        }
        return null;
    }
}

/// <summary>ffmpeg komut satırları (11a'da ölçülmüş biçimleriyle) ve çıkış kodlarının Türkçe açıklaması.
/// Argümanlar TOKEN listesidir (<c>ProcessStartInfo.ArgumentList</c>): dosya adındaki tırnak/boşluk ffmpeg'e
/// ek seçenek sokamaz.</summary>
public static class FfmpegArgs
{
    public const int FrameWidth = 640, FrameHeight = 360;

    public static string[] VideoFile(string path, int fps) =>
        ["-hide_banner", "-loglevel", "error", "-re", "-i", path,
         "-vf", $"fps={fps},scale={FrameWidth}:{FrameHeight}:force_original_aspect_ratio=decrease,pad={FrameWidth}:{FrameHeight}:(ow-iw)/2:(oh-ih)/2",
         "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"];

    public static string[] Camera(int index, int fps) =>
        ["-hide_banner", "-loglevel", "error", "-f", "avfoundation", "-pixel_format", "nv12", "-framerate", "30",
         "-video_size", "1280x720", "-i", $"{index}:none",
         "-vf", $"fps={fps},scale={FrameWidth}:{FrameHeight}", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"];

    public static string[] ListDevices => ["-hide_banner", "-f", "avfoundation", "-list_devices", "true", "-i", ""];

    public static string[] AudioFileRealtime(string path) =>
        ["-hide_banner", "-loglevel", "error", "-re", "-i", path, "-vn", "-ac", "1", "-ar", "16000", "-f", "s16le", "pipe:1"];

    public static string ExplainExit(int exitCode, string stderr, bool camera)
    {
        if (camera && stderr.Contains("Cannot use", StringComparison.OrdinalIgnoreCase))
            return "Kamera izni reddedildi. Sistem Ayarları › Gizlilik ve Güvenlik › Kamera'dan izin verin.";
        if (stderr.Contains("does not contain any stream", StringComparison.OrdinalIgnoreCase))
            return "Bu videoda ses yok — yalnızca görüntü analiz edilecek.";
        if (stderr.Contains("No such file", StringComparison.OrdinalIgnoreCase))
            return "Video dosyası bulunamadı.";
        var first = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        return $"ffmpeg hata verdi (kod {exitCode}): {first}";
    }
}
