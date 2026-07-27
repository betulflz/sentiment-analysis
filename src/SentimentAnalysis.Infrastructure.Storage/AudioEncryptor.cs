using System.Security.Cryptography;

namespace SentimentAnalysis.Infrastructure.Storage;

/// <summary>Ham ses bytes'ını AES-256-GCM ile şifreler/çözer. Blob düzeni: <c>[nonce(12)][tag(16)][ciphertext]</c>.
/// Anahtarsız/kurcalanmış veri <see cref="AuthenticationTagMismatchException"/> atar. Saf (anahtar parametreli).</summary>
public static class AudioEncryptor
{
    private const int NonceSize = 12; // AesGcm.NonceByteSizes.MaxSize
    private const int TagSize = 16;   // AesGcm.TagByteSizes.MaxSize

    public static byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[plaintext.Length];
        using (var aes = new AesGcm(key, TagSize)) // tag-size ZORUNLU (SYSLIB0053)
            aes.Encrypt(nonce, plaintext, cipher, tag);

        var blob = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, blob, NonceSize + TagSize, cipher.Length);
        return blob;
    }

    public static byte[] Decrypt(byte[] blob, byte[] key)
    {
        if (blob.Length < NonceSize + TagSize)
            throw new ArgumentException("Şifreli blob çok kısa (nonce+tag yok).", nameof(blob));

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipher = new byte[blob.Length - NonceSize - TagSize];
        Buffer.BlockCopy(blob, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(blob, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(blob, NonceSize + TagSize, cipher, 0, cipher.Length);

        var plaintext = new byte[cipher.Length];
        using (var aes = new AesGcm(key, TagSize))
            aes.Decrypt(nonce, cipher, tag, plaintext); // yanlış anahtar/kurcalama → AuthenticationTagMismatchException
        return plaintext;
    }
}
