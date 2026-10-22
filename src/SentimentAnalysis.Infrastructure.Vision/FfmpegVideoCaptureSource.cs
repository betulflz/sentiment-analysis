using System.Diagnostics;
using System.Runtime.CompilerServices;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Vision;

/// <summary>ffmpeg alt sürecinden 640×360 RGB24 kare akışı (video dosyası gerçek hızda ya da kamera).
/// <para><b>İptal:</b> akış normal biter, süreç öldürülür. <b>İlk kare zaman aşımı:</b> 11a ölçümünde
/// kamera izni istemi açıkken ffmpeg hiçbir şey göndermeden bekledi — süresiz bloklanmamak için ilk
/// kare <see cref="VisionConfig.FirstFrameTimeoutSeconds"/> içinde gelmezse anlamlı hata atılır.</para></summary>
public sealed class FfmpegVideoCaptureSource : IVideoCaptureSource
{
    private readonly string? _file;
    private readonly VisionConfig _cfg;

    private FfmpegVideoCaptureSource(string? file, VisionConfig cfg)
    {
        _file = file;
        _cfg = cfg;
    }

    public static FfmpegVideoCaptureSource ForFile(string path, VisionConfig cfg) => new(path, cfg);

    public static FfmpegVideoCaptureSource ForCamera(VisionConfig cfg) => new(null, cfg);

    private bool IsCamera => _file is null;

    /// <summary>İlk kare için bekleme sınırı. Kamerada izin istemi yanıtlanırken ffmpeg bloklanır (11a §3);
    /// kullanıcının istemi okumasına yetecek kadar beklenir — aksi halde "İzin Ver"e geç basan kullanıcı
    /// o oturumu yüzsüz geçirirdi.</summary>
    public static TimeSpan FirstFrameTimeout(VisionConfig cfg, bool camera) =>
        TimeSpan.FromSeconds(camera ? cfg.CameraFirstFrameTimeoutSeconds : cfg.FirstFrameTimeoutSeconds);

    public async IAsyncEnumerable<VideoFrame> CaptureAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var ffmpeg = FfmpegLocator.Find();
        string args;
        if (IsCamera)
        {
            var index = AvFoundationDevices.FindVideoIndex(await ListDevicesAsync(ffmpeg, ct).ConfigureAwait(false), _cfg.CameraName)
                ?? throw new VideoSourceException($"Kamera bulunamadı: \"{_cfg.CameraName}\".");
            args = FfmpegArgs.Camera(index, _cfg.CaptureFps);
        }
        else
        {
            args = FfmpegArgs.VideoFile(_file!, _cfg.CaptureFps);
        }

        using var process = Start(ffmpeg, args);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var stdout = process.StandardOutput.BaseStream;
        int frameBytes = FfmpegArgs.FrameWidth * FfmpegArgs.FrameHeight * 3;
        try
        {
            for (int index = 0; ; index++)
            {
                var buffer = new byte[frameBytes];
                ReadResult result;
                if (index == 0)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(FirstFrameTimeout(_cfg, IsCamera));
                    result = await ReadFrameAsync(stdout, buffer, timeout.Token).ConfigureAwait(false);
                    if (result == ReadResult.Cancelled && !ct.IsCancellationRequested)
                        throw new VideoSourceException(IsCamera
                            ? "Kamera açılamadı: izin istemi yanıt bekliyor olabilir ya da kamera başka bir uygulamada kullanılıyor."
                            : "Video okunamadı (zaman aşımı).");
                }
                else
                {
                    result = await ReadFrameAsync(stdout, buffer, ct).ConfigureAwait(false);
                }

                if (result == ReadResult.Cancelled) yield break;
                if (result == ReadResult.EndOfStream)
                {
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    if (process.ExitCode != 0)
                        throw new VideoSourceException(FfmpegArgs.ExplainExit(process.ExitCode, await stderr.ConfigureAwait(false), IsCamera));
                    yield break;
                }

                yield return new VideoFrame(buffer, FfmpegArgs.FrameWidth, FfmpegArgs.FrameHeight,
                    TimeSpan.FromSeconds(index / (double)_cfg.CaptureFps));
            }
        }
        finally
        {
            Kill(process);
        }
    }

    private enum ReadResult { Frame, EndOfStream, Cancelled }

    private static async Task<ReadResult> ReadFrameAsync(Stream s, byte[] buffer, CancellationToken ct)
    {
        int read = 0;
        try
        {
            while (read < buffer.Length)
            {
                int n = await s.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
                if (n == 0) return ReadResult.EndOfStream;
                read += n;
            }
            return ReadResult.Frame;
        }
        catch (OperationCanceledException)
        {
            return ReadResult.Cancelled;
        }
    }

    private static async Task<string> ListDevicesAsync(string ffmpeg, CancellationToken ct)
    {
        using var p = Start(ffmpeg, FfmpegArgs.ListDevices);
        var err = await p.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        return err;
    }

    internal static Process Start(string ffmpeg, string args)
    {
        try
        {
            return Process.Start(new ProcessStartInfo(ffmpeg, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
        }
        catch (Exception ex)
        {
            throw new VideoSourceException($"ffmpeg başlatılamadı: {ex.Message}", ex);
        }
    }

    internal static void Kill(Process p)
    {
        try
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // süreç zaten bitti
        }
    }
}
