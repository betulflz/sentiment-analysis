using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Desktop;

/// <summary>Seçili dosyadan ses kaynağı üretir (WAV doğrudan; diğer formatlar app-katmanında
/// platforma uygun bir dönüştürücüyle — macOS'ta afconvert, Windows/Linux'ta ffmpeg). App implemente eder.</summary>
public interface IAudioSourceFactory
{
    IAudioCaptureSource Create(string path);
}
