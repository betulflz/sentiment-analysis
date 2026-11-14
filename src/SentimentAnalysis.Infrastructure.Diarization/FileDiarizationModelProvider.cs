using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Infrastructure.Diarization;

/// <summary>Modelleri uygulama veri klasöründeki <c>models/</c> altından çözer.
/// <para>Dosya adları 10a spike'ında ölçülen kurulumla birebir aynıdır: segmentation için
/// <b>fp32</b> (<c>model.onnx</c>) kullanılır, int8 değil.</para></summary>
public sealed class FileDiarizationModelProvider : IDiarizationModelProvider, ISpeakerRoleModelProvider
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

    // wespeaker_en_voxceleb_resnet34_LM.onnx ~26 MB'dır. Aralık geniş tutulur: amaç sürüm
    // farkını reddetmek değil, YANLIŞ MODELİ yakalamaktır (eski 3dspeaker dosyası 39,5 MB).
    private const long EmbeddingMinBytes = 15_000_000;
    private const long EmbeddingMaxBytes = 35_000_000;

    public string EmbeddingModelPath
    {
        get
        {
            var path = Resolve(
                EmbeddingFile,
                "konuşmacı embedding modeli",
                "github.com/k2-fsa/sherpa-onnx → releases/speaker-recongition-models/" +
                "wespeaker_en_voxceleb_resnet34_LM.onnx " +
                $"(indirip {EmbeddingFile} adıyla models/ altına koyun)");

            long size = new FileInfo(path).Length;
            if (size < EmbeddingMinBytes || size > EmbeddingMaxBytes)
                throw new InvalidDataException(
                    $"Konuşmacı embedding modeli beklenen boyutta değil ({size} bayt; beklenen " +
                    $"{EmbeddingMinBytes}-{EmbeddingMaxBytes} bayt). Kümeleme eşiği " +
                    "wespeaker_en_voxceleb_resnet34_LM modeline göre ölçüldü; başka bir model " +
                    "sessizce yanlış konuşmacı sayısı üretir. Doğru dosyayı indirip " +
                    $"{EmbeddingFile} adıyla değiştirin: {path}");

            return path;
        }
    }

    // 12a: nemo_en_titanet_small.onnx ~40,3 MB. Tanıtma eşiği (0,41) bu modele göre ölçüldü; başka bir model
    // (ör. 26 MB resnet34) sessizce yanlış "Yöneten / Karşı taraf" etiketi üretir.
    public const string RoleEmbeddingFile = "speaker-embedding-titanet-small.onnx";
    private const long RoleMinBytes = 35_000_000;
    private const long RoleMaxBytes = 45_000_000;

    public string RoleEmbeddingModelPath
    {
        get
        {
            var path = Resolve(
                RoleEmbeddingFile,
                "konuşmacı rolü (TitaNet-small) modeli",
                "github.com/k2-fsa/sherpa-onnx → releases/speaker-recongition-models/nemo_en_titanet_small.onnx " +
                $"(indirip {RoleEmbeddingFile} adıyla models/ altına koyun)");

            long size = new FileInfo(path).Length;
            if (size < RoleMinBytes || size > RoleMaxBytes)
                throw new InvalidDataException(
                    $"Konuşmacı rolü modeli beklenen boyutta değil ({size} bayt; beklenen {RoleMinBytes}-{RoleMaxBytes}). " +
                    $"Tanıtma eşiği nemo_en_titanet_small modeline göre ölçüldü: {path}");
            return path;
        }
    }

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
