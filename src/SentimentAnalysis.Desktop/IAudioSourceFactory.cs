using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Desktop;

/// <summary>Seçili dosyadan ses kaynağı üretir (WAV doğrudan; mp3/m4a app-katmanında afconvert ile). App implemente eder.</summary>
public interface IAudioSourceFactory
{
    IAudioCaptureSource Create(string path);
}
