using System;
using System.Security.Cryptography;
using System.Text;
using SentimentAnalysis.Infrastructure.Storage;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Storage.Tests;

public class AudioEncryptorTests
{
    private static byte[] Key() => RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void EncryptThenDecrypt_RoundTrips()
    {
        var key = Key();
        var data = Encoding.UTF8.GetBytes("gizli ses verisi 12345");
        var blob = AudioEncryptor.Encrypt(data, key);

        Assert.NotEqual(data, blob);                    // şifreli ≠ düz
        Assert.True(blob.Length >= data.Length + 28);   // nonce(12)+tag(16)+cipher
        Assert.Equal(data, AudioEncryptor.Decrypt(blob, key));
    }

    [Fact]
    public void Decrypt_WrongKey_Throws()
    {
        var blob = AudioEncryptor.Encrypt(Encoding.UTF8.GetBytes("veri"), Key());
        Assert.Throws<AuthenticationTagMismatchException>(() => AudioEncryptor.Decrypt(blob, Key()));
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_Throws()
    {
        var key = Key();
        var blob = AudioEncryptor.Encrypt(Encoding.UTF8.GetBytes("veri veri veri"), key);
        blob[^1] ^= 0xFF; // son ciphertext byte'ını boz
        Assert.Throws<AuthenticationTagMismatchException>(() => AudioEncryptor.Decrypt(blob, key));
    }

    [Fact]
    public void Encrypt_Empty_RoundTrips()
    {
        var key = Key();
        var blob = AudioEncryptor.Encrypt(Array.Empty<byte>(), key);
        Assert.Empty(AudioEncryptor.Decrypt(blob, key));
    }

    [Fact]
    public void Encrypt_DifferentNoncesEachCall()
    {
        var key = Key();
        var d = Encoding.UTF8.GetBytes("aynı veri");
        Assert.NotEqual(AudioEncryptor.Encrypt(d, key), AudioEncryptor.Encrypt(d, key)); // rastgele nonce
    }
}
