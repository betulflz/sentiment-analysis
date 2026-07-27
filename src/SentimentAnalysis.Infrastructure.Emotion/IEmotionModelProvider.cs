namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>ONNX model dosya adını yerel yola çözer; yoksa aksiyon-alınabilir
/// <see cref="System.IO.FileNotFoundException"/> atar. (04a prozodi + 04b metin ortak.)</summary>
public interface IEmotionModelProvider
{
    string ResolveModelPath(string fileName);
}
