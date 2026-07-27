using System.Runtime.CompilerServices;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>
/// Geçici bir WAV dosyasını okuyan <see cref="IAudioCaptureSource"/>; akış bittiğinde (normal bitiş,
/// iptal veya hata fark etmeksizin) dosyayı <b>siler</b>. Dönüştürme sonucu üretilen geçici WAV'lar
/// aksi halde <c>%TEMP%</c>'te birikirdi — 30 dakikalık bir mp3 için ~55 MB.
/// </summary>
public sealed class TempWavCaptureSource : IAudioCaptureSource
{
    private readonly string _tempWavPath;
    private readonly FileCaptureSource _inner;

    public TempWavCaptureSource(string tempWavPath)
    {
        _tempWavPath = tempWavPath;
        _inner = new FileCaptureSource(tempWavPath);
    }

    public async IAsyncEnumerable<AudioChunk> CaptureAsync(
        CaptureOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        try
        {
            await foreach (var chunk in _inner.CaptureAsync(options, ct).ConfigureAwait(false))
                yield return chunk;
        }
        finally
        {
            TryDelete(_tempWavPath);
        }
    }

    /// <summary>Silme başarısızlığı analizi bozmamalı (dosya kilidi, izin) — yut ve devam et.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { /* dosya kilitli — OS temp temizliğine bırak */ }
        catch (UnauthorizedAccessException) { /* izin yok — aynı */ }
    }
}
