using System.Buffers.Binary;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>Ham PCM16 (little-endian, işaretli) baytları −1..+1 aralığında float örneklere çevirir.
/// AudioQueue'nun verdiği format budur (spike ile ölçüldü). Saf, tahsissiz, deterministik.</summary>
public static class Pcm16Converter
{
    /// <summary>Böleni 32768'dir: −32768 tam olarak −1.0'a, +32767 ise 1.0'ın hemen altına düşer.
    /// Asimetri kasıtlıdır — PCM16'nın kendi asimetrisidir, kırpma yapılmaz.</summary>
    private const float Scale = 32768f;

    /// <returns>Yazılan örnek sayısı (<paramref name="pcm16le"/> uzunluğunun yarısı).</returns>
    public static int ToFloat(ReadOnlySpan<byte> pcm16le, Span<float> destination)
    {
        if (pcm16le.Length % 2 != 0)
            throw new ArgumentException(
                $"PCM16 bayt sayısı çift olmalı; gelen {pcm16le.Length}.", nameof(pcm16le));

        int count = pcm16le.Length / 2;
        if (destination.Length < count)
            throw new ArgumentException(
                $"Hedef tampon küçük: {count} örnek gerekiyor, {destination.Length} verildi.",
                nameof(destination));

        for (int i = 0; i < count; i++)
            destination[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm16le.Slice(i * 2, 2)) / Scale;

        return count;
    }
}
