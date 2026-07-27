using System;
using System.IO;
using System.Linq;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Infrastructure.Storage;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Storage.Tests;

public class FileKeyProviderTests
{
    private static AppConfig CfgWithDir(string dir) =>
        AppConfig.Default with { Storage = AppConfig.Default.Storage with { StorageDirectory = dir } };

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void GetKey_GeneratesThirtyTwoByteKey_WhenAbsent()
    {
        var dir = NewTempDir();
        var key = new FileKeyProvider(CfgWithDir(dir)).GetKey();
        Assert.Equal(32, key.Length);
        Assert.True(File.Exists(Path.Combine(dir, "storage.key")));
        Assert.False(key.All(b => b == 0), "anahtar sıfır olmamalı");
    }

    [Fact]
    public void GetKey_IsStableAcrossCalls()
    {
        var dir = NewTempDir();
        var cfg = CfgWithDir(dir);
        var k1 = new FileKeyProvider(cfg).GetKey();
        var k2 = new FileKeyProvider(cfg).GetKey(); // aynı dosyadan okunur
        Assert.Equal(k1, k2);
    }

    [Fact]
    public void DefaultStorageDirectory_UnderApplicationData()
    {
        var def = FileKeyProvider.DefaultStorageDirectory();
        Assert.Contains("SentimentAnalysis", def);
    }
}
