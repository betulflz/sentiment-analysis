using System;
using System.IO;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Infrastructure.Diarization;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Diarization.Tests;

/// <summary>Yanlış embedding modeli SESSİZCE kabul edilmemeli. 2026-07-29'da model
/// 3dspeaker'dan wespeaker'a geçti ve kümeleme eşiği ona göre ayarlandı; dosya adı aynı
/// kaldığı için eski model hiçbir hata üretmeden yüklendi ve iki kişilik kayıtta altı
/// konuşmacı bulundu. Boyut kontrolü kriptografik garanti değildir — amacı kaza eseri
/// yanlış dosyayı yakalamaktır.</summary>
public class FileDiarizationModelProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "diar-model-" + Guid.NewGuid());

    public FileDiarizationModelProviderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private AppConfig ConfigForTempDir() =>
        AppConfig.Default with { Vad = AppConfig.Default.Vad with { ModelDirectory = _dir } };

    private void WriteEmbedding(long bytes)
    {
        var path = Path.Combine(_dir, "speaker-embedding.onnx");
        using var fs = File.Create(path);
        fs.SetLength(bytes);
    }

    [Fact]
    public void EmbeddingModelPath_BeklenenBoyutta_KabulEder()
    {
        WriteEmbedding(26_000_000);   // wespeaker_en_voxceleb_resnet34_LM ~26 MB
        var provider = new FileDiarizationModelProvider(ConfigForTempDir());

        Assert.EndsWith("speaker-embedding.onnx", provider.EmbeddingModelPath);
    }

    [Fact]
    public void EmbeddingModelPath_EskiBuyukModel_AnlasilirHataVerir()
    {
        WriteEmbedding(39_593_761);   // eski 3dspeaker modelinin gerçek boyutu
        var provider = new FileDiarizationModelProvider(ConfigForTempDir());

        var ex = Assert.Throws<InvalidDataException>(() => _ = provider.EmbeddingModelPath);
        Assert.Contains("wespeaker_en_voxceleb_resnet34_LM", ex.Message);
        Assert.Contains("39593761", ex.Message.Replace(".", "").Replace(",", ""));
    }

    [Fact]
    public void EmbeddingModelPath_CokKucukDosya_AnlasilirHataVerir()
    {
        WriteEmbedding(1_000);
        var provider = new FileDiarizationModelProvider(ConfigForTempDir());

        Assert.Throws<InvalidDataException>(() => _ = provider.EmbeddingModelPath);
    }

    [Fact]
    public void EmbeddingModelPath_DosyaYok_FileNotFoundVerir()
    {
        var provider = new FileDiarizationModelProvider(ConfigForTempDir());

        Assert.Throws<FileNotFoundException>(() => _ = provider.EmbeddingModelPath);
    }
}
