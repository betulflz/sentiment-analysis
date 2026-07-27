using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Infrastructure.Storage;

/// <summary>AES-256 anahtarını <c>{StorageDirectory}/storage.key</c> dosyasında tutar; yoksa
/// kriptografik rastgele üretip yazar (Unix'te 0600). DÜZ METİN diskte — bu tur dev/geçici; OS
/// keychain/DPAPI ile değiştirilecek (07b). Thread-safe (dosya-var ise oku, atomik değil ama tek-yazar varsayımı).</summary>
public sealed class FileKeyProvider : IKeyProvider
{
    private const int KeySize = 32;
    private readonly string _keyPath;

    public FileKeyProvider(AppConfig config)
    {
        var dir = config.Storage.StorageDirectory ?? DefaultStorageDirectory();
        Directory.CreateDirectory(dir);
        _keyPath = Path.Combine(dir, "storage.key");
    }

    public byte[] GetKey()
    {
        if (File.Exists(_keyPath))
        {
            var existing = File.ReadAllBytes(_keyPath);
            if (existing.Length == KeySize) return existing;
        }
        var key = RandomNumberGenerator.GetBytes(KeySize);
        File.WriteAllBytes(_keyPath, key);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            File.SetUnixFileMode(_keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); // 0600
        return key;
    }

    public static string DefaultStorageDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SentimentAnalysis", "storage");
}
