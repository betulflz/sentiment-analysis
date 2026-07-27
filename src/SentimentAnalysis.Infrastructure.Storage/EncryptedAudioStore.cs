using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Infrastructure.Storage;

/// <summary>Ham sesi şifreleyip <c>{StorageDirectory}/audio/{sessionId}.enc</c> altında saklar; okur/siler.
/// Anahtar <see cref="IKeyProvider"/>'dan. Diskte yalnızca AES-GCM şifreli veri bulunur.</summary>
public sealed class EncryptedAudioStore
{
    private readonly IKeyProvider _keys;
    private readonly string _audioDir;

    public EncryptedAudioStore(AppConfig config, IKeyProvider keys)
    {
        _keys = keys;
        var dir = config.Storage.StorageDirectory ?? FileKeyProvider.DefaultStorageDirectory();
        _audioDir = Path.Combine(dir, "audio");
        Directory.CreateDirectory(_audioDir);
    }

    public string Write(Guid sessionId, byte[] rawAudio)
    {
        var path = Path.Combine(_audioDir, sessionId.ToString("N") + ".enc");
        var blob = AudioEncryptor.Encrypt(rawAudio, _keys.GetKey());
        File.WriteAllBytes(path, blob);
        return path;
    }

    public byte[] Read(string path) => AudioEncryptor.Decrypt(File.ReadAllBytes(path), _keys.GetKey());

    public void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
