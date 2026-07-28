using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Audio;

public sealed class ConvertingCaptureSource : IAudioCaptureSource
{
    private readonly string _inputPath;
    private readonly CrossPlatformAudioConverter _converter;
    private int _isConverted;

    public ConvertingCaptureSource(string inputPath, CrossPlatformAudioConverter converter)
    {
        _inputPath = inputPath ?? throw new ArgumentNullException(nameof(inputPath));
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
    }

    public async IAsyncEnumerable<AudioChunk> CaptureAsync(
        CaptureOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        string? tempWavPath = null;
        try
        {
            if (Interlocked.CompareExchange(ref _isConverted, 1, 0) == 0)
            {
                tempWavPath = Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N") + ".wav");
                await _converter.ConvertAsync(_inputPath, tempWavPath, ct).ConfigureAwait(false);
            }
            else
            {
                throw new InvalidOperationException("Bu kaynak zaten dönüştürüldü.");
            }

            var fileSource = new FileCaptureSource(tempWavPath);
            await foreach (var chunk in fileSource.CaptureAsync(options, ct).WithCancellation(ct).ConfigureAwait(false))
            {
                yield return chunk;
            }
        }
        finally
        {
            if (tempWavPath != null)
            {
                try
                {
                    if (File.Exists(tempWavPath))
                    {
                        File.Delete(tempWavPath);
                    }
                }
                catch
                {
                    // Ignore cleanup exceptions
                }
            }
        }
    }
}
