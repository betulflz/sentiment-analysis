using System.Runtime.CompilerServices;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Audio;

namespace SentimentAnalysis.Infrastructure.Vision;

/// <summary>Video dosyasının sesini GERÇEK HIZDA (<c>-re</c>) 16 kHz mono akış olarak verir — görüntüyle
/// aynı saatte ilerlesin diye (spec 11 §3.7). Zaman damgaları akış başından itibaren mutlaktır
/// (<see cref="StreamingChunkBuffer"/>). İptalde tampondaki artık chunk verilir ve akış normal biter
/// (mikrofon kaynağıyla aynı davranış). Ses yolu olmayan videoda "Bu videoda ses yok" hatası atılır.</summary>
public sealed class FfmpegRealtimeAudioSource : IAudioCaptureSource
{
    private const int SampleRate = 16000;
    private readonly string _path;

    public FfmpegRealtimeAudioSource(string path) => _path = path;

    public async IAsyncEnumerable<AudioChunk> CaptureAsync(CaptureOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        var ffmpeg = FfmpegLocator.Find();
        using var process = FfmpegVideoCaptureSource.Start(ffmpeg, FfmpegArgs.AudioFileRealtime(_path));
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var stdout = process.StandardOutput.BaseStream;
        var buffer = new StreamingChunkBuffer(SampleRate, options.ChunkLength, options.Overlap);
        var bytes = new byte[3200 + 1]; // 0,1 sn + tek bayt artık için yer
        var samples = new float[bytes.Length / 2];
        int carry = 0;
        bool cancelled = false;
        try
        {
            while (true)
            {
                int n;
                try
                {
                    n = await stdout.ReadAsync(bytes.AsMemory(carry, bytes.Length - 1 - carry + 1 - 1), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    break;
                }
                if (n == 0) break;

                int total = carry + n;
                int even = total & ~1;
                int count = Pcm16Converter.ToFloat(bytes.AsSpan(0, even), samples);
                foreach (var chunk in buffer.Append(samples.AsSpan(0, count)))
                    yield return chunk;
                carry = total - even;
                if (carry == 1) bytes[0] = bytes[even];
            }

            if (buffer.Flush() is { } rest) yield return rest;

            if (!cancelled)
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                if (process.ExitCode != 0)
                    throw new VideoSourceException(FfmpegArgs.ExplainExit(process.ExitCode, await stderr.ConfigureAwait(false), camera: false));
            }
        }
        finally
        {
            FfmpegVideoCaptureSource.Kill(process);
        }
    }
}
