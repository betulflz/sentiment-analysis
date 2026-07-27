using System.Runtime.CompilerServices;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>Bir ses DOSYASINI (bu turda WAV) 16 kHz mono <see cref="AudioChunk"/> akışına çeviren
/// yakalama kaynağı. Boru: WAV decode → mono/16k → örtüşmeli chunk. mp3/m4a bu turda ertelenmiş
/// (<see cref="NotSupportedException"/>). Canlı yakalama (ScreenCaptureKit/WASAPI) 05b'de aynı
/// arayüz ardında. Dosya başına örneklenir (DI singleton değil).</summary>
public sealed class FileCaptureSource : IAudioCaptureSource
{
    private readonly string _filePath;

    public FileCaptureSource(string filePath) => _filePath = filePath;

    /// <summary>Akış sürerken true (rıza göstergesi için minimal durum; zengin yüzey 06/08'de).</summary>
    public bool IsCapturing { get; private set; }

    public async IAsyncEnumerable<AudioChunk> CaptureAsync(
        CaptureOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        var ext = Path.GetExtension(_filePath).ToLowerInvariant();
        if (ext != ".wav")
            throw new NotSupportedException(
                $"Şimdilik yalnızca WAV destekleniyor; gelen '{ext}'. mp3/m4a decode sonraki turda " +
                "(aynı arayüz ardında). WAV'a dönüştürüp tekrar deneyin.");
        if (!File.Exists(_filePath))
            throw new FileNotFoundException($"Ses dosyası bulunamadı: {_filePath}", _filePath);

        IsCapturing = true;
        try
        {
            var bytes = await File.ReadAllBytesAsync(_filePath, ct).ConfigureAwait(false);
            var decoded = WavDecoder.Decode(bytes);
            var mono16k = AudioPreprocessor.ToMono16k(decoded);
            foreach (var chunk in AudioChunker.Chunk(mono16k, options))
            {
                ct.ThrowIfCancellationRequested();
                yield return chunk;
            }
        }
        finally
        {
            IsCapturing = false;
        }
    }
}
