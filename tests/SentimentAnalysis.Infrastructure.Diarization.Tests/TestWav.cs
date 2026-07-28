namespace SentimentAnalysis.Infrastructure.Diarization.Tests;

/// <summary>16-bit PCM mono WAV okuyucu (yalnızca test).
/// <para>NOT: sherpa-onnx'in <c>WaveReader</c>'ı NuGet paketinde YOKTUR — deponun dotnet
/// örneklerinde kendi yazdıkları bir yardımcıdır (10a spike'ında öğrenildi). Bu yüzden
/// testlerde kendi okuyucumuzu kullanıyoruz.</para></summary>
internal static class TestWav
{
    public static (float[] Samples, int SampleRate) Read(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        if (new string(br.ReadChars(4)) != "RIFF") throw new InvalidDataException("RIFF değil");
        br.ReadInt32();
        if (new string(br.ReadChars(4)) != "WAVE") throw new InvalidDataException("WAVE değil");

        int sampleRate = 0, channels = 0, bits = 0;
        while (fs.Position < fs.Length)
        {
            string id = new(br.ReadChars(4));
            int size = br.ReadInt32();

            if (id == "fmt ")
            {
                br.ReadInt16();                    // audio format
                channels = br.ReadInt16();
                sampleRate = br.ReadInt32();
                br.ReadInt32(); br.ReadInt16();    // byte rate, block align
                bits = br.ReadInt16();
                if (size > 16) br.ReadBytes(size - 16);
            }
            else if (id == "data")
            {
                if (bits != 16) throw new InvalidDataException($"16-bit bekleniyor, {bits} geldi");
                if (channels != 1) throw new InvalidDataException($"mono bekleniyor, {channels} kanal geldi");

                var bytes = br.ReadBytes(size);
                var samples = new float[size / 2];
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
                return (samples, sampleRate);
            }
            else br.ReadBytes(size);
        }

        throw new InvalidDataException("data chunk bulunamadı");
    }
}
