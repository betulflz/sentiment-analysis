using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Infrastructure.Diarization;

/// <summary>Modelleri uygulama veri klasöründeki <c>models/</c> altından çözer.
/// <para>Dosya adları 10a spike'ında ölçülen kurulumla birebir aynıdır: segmentation için
/// <b>fp32</b> (<c>model.onnx</c>) kullanılır, int8 değil.</para></summary>
public sealed class FileDiarizationModelProvider : IDiarizationModelProvider
{
    private const string SegmentationDir = "sherpa-onnx-pyannote-segmentation-3-0";
    private const string SegmentationFile = "model.onnx";
    private const string EmbeddingFile = "speaker-embedding.onnx";

    private readonly string _dir;

    public FileDiarizationModelProvider(AppConfig config)
        => _dir = config.Vad.ModelDirectory ?? DefaultModelDirectory();

    public string SegmentationModelPath => Resolve(
        Path.Combine(SegmentationDir, SegmentationFile),
        "sherpa-onnx pyannote segmentation modeli",
        "github.com/k2-fsa/sherpa-onnx → releases/speaker-segmentation-models/" +
        "sherpa-onnx-pyannote-segmentation-3-0.tar.bz2 (açıp models/ altına koyun)");

    public string EmbeddingModelPath => Resolve(
        EmbeddingFile,
        "konuşmacı embedding modeli",
        "github.com/k2-fsa/sherpa-onnx → releases/speaker-recongition-models/" +
        "wespeaker_en_voxceleb_resnet34_LM.onnx " +
        $"(indirip {EmbeddingFile} adıyla models/ altına koyun)");

    private string Resolve(string relative, string what, string howToGet)
    {
        var path = Path.Combine(_dir, relative);
        if (!File.Exists(path))
            throw new FileNotFoundException($"{what} bulunamadı: {path}. {howToGet}", path);
        return path;
    }

    public static string DefaultModelDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SentimentAnalysis", "models");
}
