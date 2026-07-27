using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Infrastructure.Vad;

/// <summary>Modeli <c>{ModelDirectory}/{fileName}</c> yolundan çözer; ModelDirectory null ise
/// uygulama veri klasörü altındaki varsayılana düşer.</summary>
public sealed class FileVadModelProvider : IVadModelProvider
{
    private readonly VadConfig _cfg;

    public FileVadModelProvider(AppConfig config) => _cfg = config.Vad;

    public string ResolveModelPath(string fileName)
    {
        var dir = _cfg.ModelDirectory ?? DefaultModelDirectory();
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Silero VAD modeli bulunamadı: {path}. Modeli bu konuma koyun " +
                "(github.com/snakers4/silero-vad → src/silero_vad/data/silero_vad.onnx).", path);
        return path;
    }

    public static string DefaultModelDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SentimentAnalysis", "models");
}
