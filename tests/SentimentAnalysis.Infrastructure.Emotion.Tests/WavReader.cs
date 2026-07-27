using System;
using System.IO;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

/// <summary>Minimal 16-bit PCM mono WAV okuyucu (yalnızca test için).</summary>
internal static class WavReader
{
    public static float[] ReadMono16(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.ReadBytes(12); // "RIFF"<size>"WAVE"
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var id = new string(reader.ReadChars(4));
            int size = reader.ReadInt32();
            if (id == "data")
            {
                int count = size / 2;
                var samples = new float[count];
                for (int i = 0; i < count; i++) samples[i] = reader.ReadInt16() / 32768f;
                return samples;
            }
            reader.BaseStream.Seek(size, SeekOrigin.Current);
        }
        throw new InvalidDataException("WAV 'data' chunk bulunamadı.");
    }
}
