using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>16 kHz mono sinyali <see cref="CaptureOptions.ChunkLength"/> uzunluklu, örtüşmeli
/// (<see cref="CaptureOptions.Overlap"/>) <see cref="AudioChunk"/>'lara böler. Son chunk kısa
/// olabilir; sinyali tam kapsar. Saf, deterministik.</summary>
public static class AudioChunker
{
    private const int Rate = AudioPreprocessor.TargetRate; // 16000

    public static IReadOnlyList<AudioChunk> Chunk(float[] mono16k, CaptureOptions options)
    {
        int samplesPerChunk = (int)Math.Round(options.ChunkLength.TotalSeconds * Rate);
        int step = (int)Math.Round((options.ChunkLength - options.Overlap).TotalSeconds * Rate);
        if (samplesPerChunk <= 0) throw new ArgumentException("ChunkLength pozitif olmalı.", nameof(options));
        if (step <= 0) throw new ArgumentException("Overlap, ChunkLength'ten küçük olmalı.", nameof(options));

        var result = new List<AudioChunk>();
        if (mono16k.Length == 0) return result;

        int start = 0;
        while (true)
        {
            int end = Math.Min(start + samplesPerChunk, mono16k.Length);
            var slice = new float[end - start];
            Array.Copy(mono16k, start, slice, 0, end - start);
            result.Add(new AudioChunk(
                slice, Rate,
                TimeSpan.FromSeconds(start / (double)Rate),
                TimeSpan.FromSeconds(end / (double)Rate)));
            if (end >= mono16k.Length) break;
            start += step;
        }
        return result;
    }
}
