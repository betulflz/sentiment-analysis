using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SentimentAnalysis.Infrastructure.Audio;

public sealed class CrossPlatformAudioConverter
{
    private readonly IAudioToolResolver _resolver;
    private readonly IExternalProcessRunner _processRunner;

    public CrossPlatformAudioConverter(IAudioToolResolver resolver, IExternalProcessRunner processRunner)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task ConvertAsync(string inputPath, string outputPath, CancellationToken ct)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"Girdi ses dosyası bulunamadı: {inputPath}", inputPath);
        }

        var resolvedTool = _resolver.Resolve();

        var arguments = new List<string>();
        if (resolvedTool.Kind == AudioConversionToolKind.Ffmpeg)
        {
            arguments.Add("-y");
            arguments.Add("-hide_banner");
            arguments.Add("-loglevel");
            arguments.Add("error");
            arguments.Add("-i");
            arguments.Add(inputPath);
            arguments.Add("-vn");
            arguments.Add("-ac");
            arguments.Add("1");
            arguments.Add("-ar");
            arguments.Add("16000");
            arguments.Add("-c:a");
            arguments.Add("pcm_s16le");
            arguments.Add(outputPath);
        }
        else
        {
            // macOS afconvert
            arguments.Add("-f"); arguments.Add("WAVE");
            arguments.Add("-d"); arguments.Add("LEI16@16000");
            arguments.Add("-c"); arguments.Add("1");
            arguments.Add(inputPath); arguments.Add(outputPath);
        }

        ProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(resolvedTool.ExecutablePath, arguments, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            CleanupFile(outputPath);
            throw;
        }
        catch (Exception ex)
        {
            CleanupFile(outputPath);
            throw new InvalidOperationException($"Ses dönüştürme aracı çalıştırılırken hata oluştu: {ex.Message}", ex);
        }

        if (result.ExitCode != 0)
        {
            CleanupFile(outputPath);
            var errorDetails = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new InvalidOperationException($"Ses dönüştürme işlemi başarısız oldu (Hata Kodu: {result.ExitCode}). Detay: {errorDetails}");
        }

        // Verify and validate WAV file structure
        try
        {
            WavValidator.ValidateWav(outputPath);
        }
        catch (Exception ex)
        {
            CleanupFile(outputPath);
            throw new InvalidOperationException($"Oluşturulan dosya doğrulanamadı: {ex.Message}", ex);
        }
    }

    private static void CleanupFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup exceptions
        }
    }
}
