namespace SentimentAnalysis.Infrastructure.Vad;

/// <summary>Silero VAD model dosya adını yerel yola çözer; yoksa aksiyon-alınabilir
/// <see cref="System.IO.FileNotFoundException"/> atar.</summary>
public interface IVadModelProvider
{
    string ResolveModelPath(string fileName);
}
