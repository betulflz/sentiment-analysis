namespace SentimentAnalysis.Infrastructure.Diarization;

/// <summary>Diarization modellerinin yerel yollarını çözer; dosya yoksa aksiyon-alınabilir
/// <see cref="System.IO.FileNotFoundException"/> atar (sessizce boş sonuç dönmek yasak —
/// kullanıcı "konuşmacı bulunamadı" ile "model kurulu değil"i ayırt edebilmeli).</summary>
public interface IDiarizationModelProvider
{
    /// <summary>pyannote segmentation ONNX yolu.</summary>
    string SegmentationModelPath { get; }

    /// <summary>Konuşmacı embedding çıkarıcı ONNX yolu.</summary>
    string EmbeddingModelPath { get; }
}
