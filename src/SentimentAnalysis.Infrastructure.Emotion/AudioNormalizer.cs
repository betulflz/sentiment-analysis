namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>wav2vec2 girdisi için ham sesi zero-mean / unit-variance standardize eder
/// (audeering ön-işlemesiyle aynı). Sabit/sessiz sinyalde sıfır vektör döner. Saf.</summary>
public static class AudioNormalizer
{
    private const double Eps = 1e-7;

    public static float[] Standardize(float[] samples)
    {
        if (samples.Length == 0) return samples;

        double sum = 0;
        for (int i = 0; i < samples.Length; i++) sum += samples[i];
        double mean = sum / samples.Length;

        double sq = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double d = samples[i] - mean;
            sq += d * d;
        }
        double variance = sq / samples.Length;
        if (variance < Eps) return new float[samples.Length]; // sabit/sessiz → nötr

        double invStd = 1.0 / Math.Sqrt(variance + Eps);
        var result = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
            result[i] = (float)((samples[i] - mean) * invStd);
        return result;
    }
}
