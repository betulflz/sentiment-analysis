namespace SentimentAnalysis.Infrastructure.Vad;

/// <summary>16 kHz mono chunk'ı Silero VAD'ın beklediği ardışık 512-örnek pencerelere böler.
/// Son pencere 512'den kısaysa sıfır-pad'lenir. Saf.</summary>
public static class VadWindower
{
    public const int FrameSize = 512; // 16 kHz'de 32 ms (spike ile doğrulandı)

    public static IReadOnlyList<float[]> ToFrames(float[] mono16k)
    {
        var frames = new List<float[]>();
        if (mono16k.Length == 0) return frames;

        for (int start = 0; start < mono16k.Length; start += FrameSize)
        {
            var frame = new float[FrameSize]; // sıfırlarla → tail otomatik pad
            int count = Math.Min(FrameSize, mono16k.Length - start);
            Array.Copy(mono16k, start, frame, 0, count);
            frames.Add(frame);
        }
        return frames;
    }
}
