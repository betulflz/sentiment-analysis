using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Infrastructure.Stt;

/// <summary>Modeli <c>{ModelDirectory}/ggml-{model}-{quantization}.bin</c> yolundan çözer.
/// ModelDirectory null ise uygulama veri klasörü altındaki varsayılana düşer.</summary>
public sealed class FileSttModelProvider : ISttModelProvider
{
    private readonly SttConfig _cfg;

    public FileSttModelProvider(AppConfig config) => _cfg = config.Stt;

    public string ResolveModelPath(string modelName)
    {
        var dir = _cfg.ModelDirectory ?? DefaultModelDirectory();
        var fileName = $"ggml-{modelName}-{_cfg.Quantization}.bin";
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Whisper modeli bulunamadı: {path}. Modeli indirip bu konuma koyun " +
                $"(huggingface.co/ggerganov/whisper.cpp → {fileName}).", path);
        return path;
    }

    /// <summary>Varsayılan model klasörü: <c>%AppData%/SentimentAnalysis/models</c>
    /// (cross-platform ApplicationData).</summary>
    public static string DefaultModelDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SentimentAnalysis", "models");
}
