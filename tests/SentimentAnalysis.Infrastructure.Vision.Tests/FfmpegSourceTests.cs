using System.Diagnostics;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Infrastructure.Vision;

namespace SentimentAnalysis.Infrastructure.Vision.Tests;

public class FfmpegPureTests
{
    // 11a §3'te ölçülen gerçek çıktı.
    private const string ListDevices = """
        [AVFoundation indev @ 0x7d9020140] AVFoundation video devices:
        [AVFoundation indev @ 0x7d9020140] [0] FaceTime HD Kamera
        [AVFoundation indev @ 0x7d9020140] [1] Betül iPhone’u Kamerası
        [AVFoundation indev @ 0x7d9020140] [2] Betül iPhone’u Masa Görüntüsü Kamerası
        [AVFoundation indev @ 0x7d9020140] [3] Capture screen 0
        [AVFoundation indev @ 0x7d9020140] AVFoundation audio devices:
        [AVFoundation indev @ 0x7d9020140] [0] Microsoft Teams Audio
        [AVFoundation indev @ 0x7d9020140] [1] Betül iPhone’u Mikrofonu
        [AVFoundation indev @ 0x7d9020140] [2] MacBook Air Mikrofonu
        [in#0 @ 0x7d9020000] Error opening input: Input/output error
        """;

    [Fact]
    public void FindVideoIndex_ParsesSpikeOutput()
    {
        Assert.Equal(0, AvFoundationDevices.FindVideoIndex(ListDevices, "FaceTime"));
        Assert.Equal(1, AvFoundationDevices.FindVideoIndex(ListDevices, "iPhone"));
        Assert.Null(AvFoundationDevices.FindVideoIndex(ListDevices, "Mikrofon"));
        Assert.Null(AvFoundationDevices.FindVideoIndex("", "FaceTime"));
    }

    [Fact]
    public void Args_VideoFile_ContainsRealtimeAndFilter()
    {
        var a = string.Join(' ', FfmpegArgs.VideoFile("/v/a b.mp4", 5));
        Assert.Contains("-re -i /v/a b.mp4", a);
        Assert.Contains("fps=5,scale=640:360:force_original_aspect_ratio=decrease,pad=640:360", a);
        Assert.Contains("-pix_fmt rgb24 -f rawvideo pipe:1", a);
    }

    [Fact]
    public void Args_PathWithQuotes_StaysSingleToken()
    {
        // Güvenlik: dosya adı ffmpeg'e ek seçenek sokamamalı (ör. -y ile dosya yazdırma).
        const string evil = "/v/a\" -y -f mp4 \"/tmp/x.mov";
        foreach (var args in new[] { FfmpegArgs.VideoFile(evil, 5), FfmpegArgs.AudioFileRealtime(evil) })
        {
            int i = args.ToList().IndexOf("-i");
            Assert.Equal(evil, args[i + 1]);
            Assert.DoesNotContain("-y", args);
        }
    }

    [Fact]
    public void FirstFrameTimeout_CameraWaitsForPermissionPrompt()
    {
        var cfg = new VisionConfig();
        // 11a §3: izin istemi yanıtlanırken ffmpeg bloklanır; istem hızlı yanıtlansa bile ilk kare 7,5 sn.
        Assert.True(FfmpegVideoCaptureSource.FirstFrameTimeout(cfg, camera: true) >= TimeSpan.FromSeconds(60));
        Assert.Equal(TimeSpan.FromSeconds(10), FfmpegVideoCaptureSource.FirstFrameTimeout(cfg, camera: false));
    }

    [Fact]
    public void Args_Camera_UsesNv12AndIndex()
    {
        var a = string.Join(' ', FfmpegArgs.Camera(2, 5));
        Assert.Contains("-f avfoundation -pixel_format nv12", a);
        Assert.Contains("-i 2:none", a);
    }

    [Fact]
    public void Args_Audio_IsRealtimeMono16k()
    {
        var a = string.Join(' ', FfmpegArgs.AudioFileRealtime("/v/x.mp4"));
        Assert.Contains("-re -i /v/x.mp4 -vn -ac 1 -ar 16000 -f s16le pipe:1", a);
    }

    [Fact]
    public void ExplainExit_CameraDenied() =>
        Assert.Contains("Kamera izni reddedildi",
            FfmpegArgs.ExplainExit(251, "Failed to create AV capture input device: Cannot use FaceTime HD Kamera", camera: true));

    [Fact]
    public void ExplainExit_NoAudio() =>
        Assert.Contains("Bu videoda ses yok",
            FfmpegArgs.ExplainExit(234, "[out#0/s16le @ 0x1] Output file does not contain any stream", camera: false));

    [Fact]
    public void ExplainExit_GoruntuSureci_StreamYok_GoruntuYokDer()
    {
        // Yalnız-ses .mov: görüntü ffmpeg'i "stream yok" ile biter — "ses yok" demek yanlış olur (11b minor).
        var m = FfmpegArgs.ExplainExit(234, "[out#0/rawvideo @ 0x1] Output file does not contain any stream", camera: false, video: true);
        Assert.Contains("görüntü yok", m);
        Assert.DoesNotContain("ses yok", m);
    }

    [Fact]
    public void ExplainExit_Missing() =>
        Assert.Contains("bulunamadı",
            FfmpegArgs.ExplainExit(254, "Error opening input: No such file or directory", camera: false));

    [Fact]
    public void ExplainExit_Other_ShowsCodeAndFirstLine()
    {
        var m = FfmpegArgs.ExplainExit(1, "ilk satır\nikinci", camera: false);
        Assert.Contains("kod 1", m);
        Assert.Contains("ilk satır", m);
        Assert.DoesNotContain("ikinci", m);
    }

    [Fact]
    public void FfmpegLocator_EnvPathMissing_Throws()
    {
        var old = Environment.GetEnvironmentVariable("SENTIMENT_FFMPEG_PATH");
        try
        {
            Environment.SetEnvironmentVariable("SENTIMENT_FFMPEG_PATH", "/yok/ffmpeg");
            var ex = Assert.Throws<VideoSourceException>(FfmpegLocator.Find);
            Assert.Contains("/yok/ffmpeg", ex.Message);
        }
        finally { Environment.SetEnvironmentVariable("SENTIMENT_FFMPEG_PATH", old); }
    }
}

/// <summary>Gerçek ffmpeg + video ile. SENTIMENT_TEST_VIDEO yoksa atlanır. Aynı videoyu açan sınıflar
/// tek koleksiyonda sırayla koşar — <see cref="Cancel_KillsProcess"/>'in pgrep'i diğer testin ffmpeg'ini görmesin.</summary>
[Collection(RealVideoCollection.Name)]
public class FfmpegIntegrationTests
{
    private static string? Video => Environment.GetEnvironmentVariable("SENTIMENT_TEST_VIDEO");

    [SkippableFact]
    public async Task VideoFile_RealVideo_FramesAtFps()
    {
        Skip.If(string.IsNullOrEmpty(Video) || !File.Exists(Video), "SENTIMENT_TEST_VIDEO yok");
        var source = FfmpegVideoCaptureSource.ForFile(Video!, new VisionConfig());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        int frames = 0;
        await foreach (var f in source.CaptureAsync(cts.Token))
        {
            Assert.Equal(640 * 360 * 3, f.Rgb24.Length);
            Assert.Equal(TimeSpan.FromSeconds(frames / 5.0), f.Time);
            frames++;
        }
        Assert.InRange(frames, 9, 11);
    }

    [SkippableFact]
    public async Task Audio_RealVideo_ProducesChunks()
    {
        Skip.If(string.IsNullOrEmpty(Video) || !File.Exists(Video), "SENTIMENT_TEST_VIDEO yok");
        IAudioCaptureSource source = new FfmpegRealtimeAudioSource(Video!);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var options = new CaptureOptions(TimeSpan.FromSeconds(2), TimeSpan.Zero);
        await foreach (var chunk in source.CaptureAsync(options, cts.Token))
        {
            Assert.Equal(16000, chunk.SampleRate);
            Assert.Equal(32000, chunk.Samples.Length);
            Assert.Equal(TimeSpan.Zero, chunk.Start);
            break;
        }
    }

    [SkippableFact]
    public async Task Cancel_KillsProcess()
    {
        Skip.If(string.IsNullOrEmpty(Video) || !File.Exists(Video), "SENTIMENT_TEST_VIDEO yok");
        var source = FfmpegVideoCaptureSource.ForFile(Video!, new VisionConfig());
        using var cts = new CancellationTokenSource();
        await foreach (var _ in source.CaptureAsync(cts.Token))
        {
            cts.Cancel();
            break;
        }
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(2) && FfmpegRunningFor(Video!)) await Task.Delay(100);
        Assert.False(FfmpegRunningFor(Video!), "iptal sonrası ffmpeg süreci hâlâ çalışıyor");
    }

    // Desen ffmpeg İKİLİSİNİN komut satırıyla başlamalı: aksi halde testi çalıştıran kabuk (komut metninde
    // "ffmpeg" ve video yolu geçebilir) eşleşir ve test sahte-kırmızı verir.
    private static bool FfmpegRunningFor(string path)
    {
        var p = Process.Start(new ProcessStartInfo("pgrep", $"-f \"^[^ ]*/ffmpeg .*{Path.GetFileName(path)}\"") { RedirectStandardOutput = true })!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output.Trim().Length > 0;
    }
}

[CollectionDefinition(Name)]
public sealed class RealVideoCollection
{
    public const string Name = "Gerçek video (sıralı)";
}
