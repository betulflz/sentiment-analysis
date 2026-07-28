using System;
using System.IO;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Infrastructure.Audio;

namespace SentimentAnalysis.Desktop.Avalonia;

/// <summary>Seçili ses dosyasından <see cref="IAudioCaptureSource"/> üretir. WAV doğrudan; mp3/m4a/aac
/// geçici 16 kHz mono WAV'a çevrilir.</summary>
public sealed class AfconvertAudioSourceFactory : IAudioSourceFactory
{
    private readonly CrossPlatformAudioConverter _converter;

    public AfconvertAudioSourceFactory(CrossPlatformAudioConverter converter)
    {
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
    }

    public IAudioCaptureSource Create(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".wav") return new FileCaptureSource(path);
        if (ext is ".mp3" or ".m4a" or ".aac")
        {
            return new ConvertingCaptureSource(path, _converter);
        }
        throw new NotSupportedException($"Desteklenmeyen ses formatı: {ext}. Desteklenen: wav, mp3, m4a, aac.");
    }
}
