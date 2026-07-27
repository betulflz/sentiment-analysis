using System.Buffers.Binary;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>Çözülmüş ham ses: interleaved float örnekler + kaynak rate/kanal (downmix/resample Task 2'de).</summary>
public readonly record struct DecodedAudio(float[] Interleaved, int SampleRate, int Channels);

/// <summary>Minimal, bağımlılıksız WAV (RIFF/WAVE) çözücü. Destekler: PCM 16-bit, PCM 32-bit int,
/// IEEE float 32-bit; mono/çok-kanal. Örnekleri <c>[-1,1]</c> float'a normalize eder (interleaved).
/// Saf.</summary>
public static class WavDecoder
{
    public static DecodedAudio Decode(byte[] wav)
    {
        if (wav.Length < 44) throw new InvalidDataException("WAV çok kısa.");
        var span = wav.AsSpan();
        if (span[0] != 'R' || span[1] != 'I' || span[2] != 'F' || span[3] != 'F' ||
            span[8] != 'W' || span[9] != 'A' || span[10] != 'V' || span[11] != 'E')
            throw new InvalidDataException("Geçersiz WAV: RIFF/WAVE başlığı yok.");

        short audioFormat = 0, channels = 0, bitsPerSample = 0;
        int sampleRate = 0;
        int pos = 12;
        ReadOnlySpan<byte> data = default;
        bool haveFmt = false, haveData = false;

        while (pos + 8 <= wav.Length)
        {
            var id = span.Slice(pos, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(pos + 4, 4));
            int body = pos + 8;
            if (id[0] == 'f' && id[1] == 'm' && id[2] == 't' && id[3] == ' ')
            {
                audioFormat = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(body, 2));
                channels = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(body + 2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(body + 4, 4));
                bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(body + 14, 2));
                haveFmt = true;
            }
            else if (id[0] == 'd' && id[1] == 'a' && id[2] == 't' && id[3] == 'a')
            {
                int avail = Math.Min(size, wav.Length - body);
                data = span.Slice(body, avail);
                haveData = true;
            }
            pos = body + size + (size & 1); // chunk'lar çift hizalı
            if (haveFmt && haveData) break;
        }

        if (!haveFmt || !haveData) throw new InvalidDataException("WAV: fmt/data chunk eksik.");

        float[] samples = (audioFormat, bitsPerSample) switch
        {
            (1, 16) => DecodePcm16(data),
            (1, 32) => DecodePcm32(data),
            (3, 32) => DecodeFloat32(data),
            _ => throw new NotSupportedException(
                $"Desteklenmeyen WAV formatı: audioFormat={audioFormat}, bits={bitsPerSample}. " +
                "Desteklenen: PCM16, PCM32, float32."),
        };

        return new DecodedAudio(samples, sampleRate, channels);
    }

    private static float[] DecodePcm16(ReadOnlySpan<byte> d)
    {
        int n = d.Length / 2;
        var s = new float[n];
        for (int i = 0; i < n; i++)
            s[i] = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(i * 2, 2)) / 32768f;
        return s;
    }

    private static float[] DecodePcm32(ReadOnlySpan<byte> d)
    {
        int n = d.Length / 4;
        var s = new float[n];
        for (int i = 0; i < n; i++)
            s[i] = BinaryPrimitives.ReadInt32LittleEndian(d.Slice(i * 4, 4)) / 2147483648f;
        return s;
    }

    private static float[] DecodeFloat32(ReadOnlySpan<byte> d)
    {
        int n = d.Length / 4;
        var s = new float[n];
        for (int i = 0; i < n; i++)
            s[i] = BinaryPrimitives.ReadSingleLittleEndian(d.Slice(i * 4, 4));
        return s;
    }
}
