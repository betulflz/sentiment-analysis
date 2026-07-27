using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>
/// Sürekli akan örnekleri biriktirip chunk sınırına ulaşınca örtüşmeli
/// <see cref="AudioChunk"/> üretir. <see cref="AudioChunker"/> dizi tabanlıdır (tüm ses elde
/// olduğunda çalışır); bu sınıf canlı akış içindir.
/// </summary>
/// <remarks>
/// Zaman damgaları akışın başından itibaren MUTLAK örnek indeksinden hesaplanır, böylece
/// zaman çizelgesi kayması olmaz. Saf ve deterministik — iş parçacığı güvenliği YOKTUR,
/// çağıran tek bir üreticiden beslemelidir.
/// </remarks>
public sealed class StreamingChunkBuffer
{
    private readonly int _sampleRate;
    private readonly int _samplesPerChunk;
    private readonly int _step;
    private readonly List<float> _pending = new();

    /// <summary>_pending[0]'ın akış başından itibaren mutlak örnek indeksi.</summary>
    private long _absoluteStart;

    /// <summary>Şimdiye kadar üretilmiş chunk'ların kapsadığı en son mutlak indeks.</summary>
    private long _coveredUntil;

    public StreamingChunkBuffer(int sampleRate, TimeSpan chunkLength, TimeSpan overlap)
    {
        _sampleRate = sampleRate;
        _samplesPerChunk = (int)Math.Round(chunkLength.TotalSeconds * sampleRate);
        _step = (int)Math.Round((chunkLength - overlap).TotalSeconds * sampleRate);

        if (_samplesPerChunk <= 0)
            throw new ArgumentException("ChunkLength pozitif olmalı.", nameof(chunkLength));
        if (_step <= 0)
            throw new ArgumentException("Overlap, ChunkLength'ten küçük olmalı.", nameof(overlap));
    }

    /// <summary>Gelen örnekleri ekler ve tamamlanan chunk'ları döndürür (yoksa boş liste).</summary>
    public IReadOnlyList<AudioChunk> Append(ReadOnlySpan<float> samples)
    {
        foreach (var s in samples) _pending.Add(s);

        var produced = new List<AudioChunk>();
        while (_pending.Count >= _samplesPerChunk)
        {
            produced.Add(Take(_samplesPerChunk));
            _pending.RemoveRange(0, _step);
            _absoluteStart += _step;
        }
        return produced;
    }

    /// <summary>Akış bittiğinde henüz hiçbir chunk'ın kapsamadığı artık varsa onu döndürür.</summary>
    public AudioChunk? Flush()
    {
        if (_pending.Count == 0) return null;
        if (_absoluteStart + _pending.Count <= _coveredUntil) return null;
        return Take(_pending.Count);
    }

    private AudioChunk Take(int count)
    {
        var slice = new float[count];
        _pending.CopyTo(0, slice, 0, count);

        long end = _absoluteStart + count;
        _coveredUntil = Math.Max(_coveredUntil, end);

        return new AudioChunk(
            slice, _sampleRate,
            TimeSpan.FromSeconds(_absoluteStart / (double)_sampleRate),
            TimeSpan.FromSeconds(end / (double)_sampleRate));
    }
}
