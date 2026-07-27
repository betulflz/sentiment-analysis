using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Pipeline;

/// <summary>Bir chunk'ın ses + transkript kalite sinyallerini üretir (füzyonun kalite-ağırlıklandırması
/// için). SNR ve konuşma-oranı basit çerçeve-enerji sezgiselleridir (kalibrasyon → Alt-Proje 09).
/// Saf, deterministik. (04a'nın 06'ya bıraktığı iş.)</summary>
public static class QualitySignalsBuilder
{
    private const int FrameSize = 320;   // 20 ms @16k
    private const double Eps = 1e-9;
    private const double SpeechFactor = 0.1; // tepe-enerjinin %10'u üstü = konuşma çerçevesi

    public static QualitySignals Build(AudioChunk chunk, Transcript transcript)
    {
        var (snr, speechRatio) = AnalyzeAudio(chunk.Samples);
        int words = CountWords(transcript.Text);
        return new QualitySignals(
            AudioSnr: snr,
            SpeechRatio: speechRatio,
            Duration: chunk.End - chunk.Start,
            TranscriptWordCount: words,
            TranscriptAvgLogProb: transcript.AvgLogProb);
    }

    private static (double snr, double speechRatio) AnalyzeAudio(float[] samples)
    {
        if (samples.Length == 0) return (0.0, 0.0);

        // Çerçeve-başı RMS.
        int frameCount = Math.Max(1, samples.Length / FrameSize);
        var rms = new double[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            int start = f * FrameSize;
            int end = Math.Min(start + FrameSize, samples.Length);
            double sq = 0;
            for (int i = start; i < end; i++) sq += (double)samples[i] * samples[i];
            rms[f] = Math.Sqrt(sq / Math.Max(1, end - start));
        }

        double peak = 0;
        for (int f = 0; f < frameCount; f++) if (rms[f] > peak) peak = rms[f];
        if (peak < Eps) return (0.0, 0.0); // tümü sessiz

        // Konuşma oranı: tepe-enerjinin SpeechFactor'ü üstündeki çerçeveler.
        double threshold = peak * SpeechFactor;
        int speechFrames = 0;
        for (int f = 0; f < frameCount; f++) if (rms[f] > threshold) speechFrames++;
        double speechRatio = (double)speechFrames / frameCount;

        // SNR(dB): genel sinyal RMS'i vs gürültü tabanı (en düşük %10 çerçeve RMS ortalaması).
        var sorted = (double[])rms.Clone();
        Array.Sort(sorted);
        int noiseCount = Math.Max(1, frameCount / 10);
        double noise = 0;
        for (int i = 0; i < noiseCount; i++) noise += sorted[i];
        noise = noise / noiseCount + Eps;

        double signal = 0;
        for (int f = 0; f < frameCount; f++) signal += rms[f];
        signal /= frameCount;

        double snr = 20.0 * Math.Log10((signal + Eps) / noise);
        if (snr < 0) snr = 0; // negatif SNR'ı 0'a kırp (güvenilirlik tabanı)
        return (snr, speechRatio);
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
