using System;
using System.IO;
using System.Linq;
using System.Text;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Infrastructure.Storage;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Storage.Tests;

public class EncryptedAudioStoreTests
{
    private static (EncryptedAudioStore store, string dir) NewStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cfg = AppConfig.Default with { Storage = AppConfig.Default.Storage with { StorageDirectory = dir } };
        return (new EncryptedAudioStore(cfg, new FileKeyProvider(cfg)), dir);
    }

    [Fact]
    public void WriteThenRead_RoundTrips()
    {
        var (store, _) = NewStore();
        var data = Encoding.UTF8.GetBytes("ham ses örneği");
        var path = store.Write(Guid.NewGuid(), data);
        Assert.True(File.Exists(path));
        Assert.Equal(data, store.Read(path));
    }

    [Fact]
    public void WrittenFile_IsNotPlaintext()
    {
        var (store, _) = NewStore();
        var data = Encoding.UTF8.GetBytes("GIZLI_ANAHTAR_KELIME");
        var path = store.Write(Guid.NewGuid(), data);
        var onDisk = File.ReadAllBytes(path);
        // Düz metin diskte görünmemeli.
        Assert.DoesNotContain(Encoding.UTF8.GetString(onDisk), "GIZLI_ANAHTAR_KELIME");
        Assert.NotEqual(data, onDisk);
    }

    [Fact]
    public void Read_WithDifferentKey_Fails()
    {
        // Aynı dosya, farklı anahtar dizini → çözülemez.
        var (store, _) = NewStore();
        var path = store.Write(Guid.NewGuid(), Encoding.UTF8.GetBytes("veri"));

        var otherDir = Path.Combine(Path.GetTempPath(), "sa-audio2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(otherDir);
        var otherCfg = AppConfig.Default with { Storage = AppConfig.Default.Storage with { StorageDirectory = otherDir } };
        var otherStore = new EncryptedAudioStore(otherCfg, new FileKeyProvider(otherCfg));

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => otherStore.Read(path));
    }

    [Fact]
    public void Delete_RemovesFile_AndIsIdempotent()
    {
        var (store, _) = NewStore();
        var path = store.Write(Guid.NewGuid(), new byte[] { 1, 2, 3 });
        store.Delete(path);
        Assert.False(File.Exists(path));
        store.Delete(path); // ikinci sil → sessiz
    }
}
