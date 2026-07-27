namespace SentimentAnalysis.Infrastructure.Storage;

/// <summary>Simetrik şifreleme anahtarı (AES-256, 32 byte) sağlar. Bu tur dosya-anahtar
/// (<see cref="FileKeyProvider"/>); OS keychain/DPAPI implementasyonu → 07b (aynı arayüz).</summary>
public interface IKeyProvider
{
    byte[] GetKey();
}
