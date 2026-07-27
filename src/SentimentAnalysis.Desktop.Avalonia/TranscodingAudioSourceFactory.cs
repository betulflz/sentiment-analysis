using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Infrastructure.Audio;

namespace SentimentAnalysis.Desktop.Avalonia;

/// <summary>
/// <see cref="IAudioSourceFactory"/>'nin ince adaptörü — asıl iş
/// <see cref="AudioFileSource"/>'ta (Infrastructure.Audio) yaşar, böylece CLI ve eval araçları
/// da aynı dönüştürme yolunu kullanır.
/// <para>Önceki sürüm (<c>AfconvertAudioSourceFactory</c>) doğrudan <c>/usr/bin/afconvert</c>
/// çağırıyordu; Windows ve Linux'ta mp3/m4a bu yüzden çalışmıyordu.</para>
/// </summary>
public sealed class TranscodingAudioSourceFactory : IAudioSourceFactory
{
    public IAudioCaptureSource Create(string path) => AudioFileSource.Create(path);
}
