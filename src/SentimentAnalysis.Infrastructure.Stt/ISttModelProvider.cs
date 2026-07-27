namespace SentimentAnalysis.Infrastructure.Stt;

/// <summary>Model adını (ör. "large-v3-turbo") yerel GGML dosya yoluna çözer.
/// Bu sürümde yalnızca yerel dosya çözümü yapar (auto-download YAGNI, ertelendi).</summary>
public interface ISttModelProvider
{
    /// <summary>Dosya yolunu döndürür; dosya yoksa aksiyon-alınabilir mesajla
    /// <see cref="System.IO.FileNotFoundException"/> atar.</summary>
    string ResolveModelPath(string modelName);
}
