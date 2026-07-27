namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>Ham çözülmüş sesi model/VAD/STT girdisine standardize eder: çok-kanal → mono (kanal
/// ortalaması), ardından hedef 16 kHz'e lineer interpolasyonla resample. Saf.</summary>
public static class AudioPreprocessor
{
    public const int TargetRate = 16000;

    public static float[] ToMono16k(DecodedAudio audio) =>
        ToMono16k(audio.Interleaved, audio.SampleRate, audio.Channels);

    public static float[] ToMono16k(float[] interleaved, int sampleRate, int channels)
    {
        if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (interleaved.Length == 0) return Array.Empty<float>();

        var mono = channels == 1 ? interleaved : Downmix(interleaved, channels);
        return sampleRate == TargetRate ? mono : Resample(mono, sampleRate, TargetRate);
    }

    private static float[] Downmix(float[] interleaved, int channels)
    {
        int frames = interleaved.Length / channels;
        var mono = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            int baseIdx = f * channels;
            for (int c = 0; c < channels; c++) sum += interleaved[baseIdx + c];
            mono[f] = (float)(sum / channels);
        }
        return mono;
    }

    /// <summary>Lineer interpolasyon resample. İlk örnek korunur; çıktı uzunluğu
    /// <c>round(inLen * dst/src)</c>. Kısa/tek-örnek girdide güvenli.</summary>
    private static float[] Resample(float[] input, int srcRate, int dstRate)
    {
        int inLen = input.Length;
        if (inLen == 1) return new[] { input[0] };

        int outLen = Math.Max(1, (int)Math.Round((long)inLen * dstRate / (double)srcRate));
        var output = new float[outLen];
        double ratio = (double)(inLen - 1) / Math.Max(1, outLen - 1);
        for (int j = 0; j < outLen; j++)
        {
            double srcPos = j * ratio;
            int i0 = (int)Math.Floor(srcPos);
            int i1 = Math.Min(i0 + 1, inLen - 1);
            double frac = srcPos - i0;
            output[j] = (float)(input[i0] * (1 - frac) + input[i1] * frac);
        }
        return output;
    }
}
