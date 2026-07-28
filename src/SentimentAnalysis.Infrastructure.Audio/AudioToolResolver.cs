using System;
using System.IO;

namespace SentimentAnalysis.Infrastructure.Audio;

public sealed class AudioToolResolver : IAudioToolResolver
{
    private readonly IRuntimePlatform _platform;

    public AudioToolResolver(IRuntimePlatform platform)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    }

    public ResolvedAudioTool Resolve()
    {
        var envPath = Environment.GetEnvironmentVariable("SENTIMENT_FFMPEG_PATH");
        if (!string.IsNullOrEmpty(envPath))
        {
            if (!File.Exists(envPath))
            {
                throw new FileNotFoundException($"SENTIMENT_FFMPEG_PATH ortam değişkeninde belirtilen dosya bulunamadı: {envPath}");
            }
            return new ResolvedAudioTool(envPath, AudioConversionToolKind.Ffmpeg);
        }

        if (_platform.IsWindows)
        {
            // Windows
            var winBundled = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "win-x64", "ffmpeg.exe");
            if (File.Exists(winBundled))
            {
                return new ResolvedAudioTool(winBundled, AudioConversionToolKind.Ffmpeg);
            }

            if (ExistsOnPath("ffmpeg.exe", out var pathFfmpegExe))
            {
                return new ResolvedAudioTool(pathFfmpegExe, AudioConversionToolKind.Ffmpeg);
            }

            if (ExistsOnPath("ffmpeg", out var pathFfmpeg))
            {
                return new ResolvedAudioTool(pathFfmpeg, AudioConversionToolKind.Ffmpeg);
            }

            throw new InvalidOperationException("FFmpeg bulunamadı. Ses dosyasını analiz edebilmek için Windows uyumlu FFmpeg'in kurulu olması veya uygulamanın tools klasöründe ffmpeg.exe bulunması gerekir.");
        }
        else if (_platform.IsLinux)
        {
            // Linux
            var linuxBundled = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "linux-x64", "ffmpeg");
            if (File.Exists(linuxBundled))
            {
                return new ResolvedAudioTool(linuxBundled, AudioConversionToolKind.Ffmpeg);
            }

            if (ExistsOnPath("ffmpeg", out var pathFfmpeg))
            {
                return new ResolvedAudioTool(pathFfmpeg, AudioConversionToolKind.Ffmpeg);
            }

            const string linuxDefault = "/usr/bin/ffmpeg";
            if (File.Exists(linuxDefault))
            {
                return new ResolvedAudioTool(linuxDefault, AudioConversionToolKind.Ffmpeg);
            }

            throw new InvalidOperationException("FFmpeg bulunamadı. Ses dosyasını analiz edebilmek için FFmpeg'i paket yöneticinizle kurun veya uygulamanın tools klasörüne Linux uyumlu ffmpeg dosyasını ekleyin.");
        }
        else if (_platform.IsMacOS)
        {
            // macOS
            const string afconvertPath = "/usr/bin/afconvert";
            if (File.Exists(afconvertPath))
            {
                return new ResolvedAudioTool(afconvertPath, AudioConversionToolKind.Afconvert);
            }

            var osxBundled = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "osx-x64", "ffmpeg");
            if (File.Exists(osxBundled))
            {
                return new ResolvedAudioTool(osxBundled, AudioConversionToolKind.Ffmpeg);
            }

            if (ExistsOnPath("ffmpeg", out var pathFfmpeg))
            {
                return new ResolvedAudioTool(pathFfmpeg, AudioConversionToolKind.Ffmpeg);
            }

            var osxDefaults = new[] { "/usr/local/bin/ffmpeg", "/opt/homebrew/bin/ffmpeg", "/usr/bin/ffmpeg" };
            foreach (var cp in osxDefaults)
            {
                if (File.Exists(cp))
                {
                    return new ResolvedAudioTool(cp, AudioConversionToolKind.Ffmpeg);
                }
            }

            throw new InvalidOperationException("Ses dönüştürme aracı bulunamadı. macOS afconvert aracına erişilemiyor ve FFmpeg de bulunamadı.");
        }
        else
        {
            throw new PlatformNotSupportedException("Bu işletim sistemi için ses dönüştürme desteği bulunmuyor.");
        }
    }

    private static bool ExistsOnPath(string fileName, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return false;

        var paths = pathEnv.Split(Path.PathSeparator);
        foreach (var path in paths)
        {
            try
            {
                var fullPath = Path.Combine(path, fileName);
                if (File.Exists(fullPath))
                {
                    resolvedPath = fullPath;
                    return true;
                }
            }
            catch
            {
                // Ignore path access errors
            }
        }
        return false;
    }
}
