using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>Modeli <c>{ModelDirectory}/{fileName}</c> yolundan çözer; ModelDirectory null ise
/// uygulama veri klasörü altındaki varsayılana düşer.</summary>
public sealed class FileEmotionModelProvider : IEmotionModelProvider
{
    private readonly EmotionConfig _cfg;

    public FileEmotionModelProvider(AppConfig config) => _cfg = config.Emotion;

    public string ResolveModelPath(string fileName)
    {
        var dir = _cfg.ModelDirectory ?? DefaultModelDirectory();
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Duygu modeli bulunamadı: {path}. ONNX modelini bu konuma koyun " +
                "(audeering wav2vec2 boyutsal model: Zenodo 6221127 → model.onnx).", path);
        return path;
    }

    public static string DefaultModelDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SentimentAnalysis", "models");
}
