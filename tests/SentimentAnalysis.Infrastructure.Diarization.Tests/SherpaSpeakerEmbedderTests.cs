using System;
using System.IO;
using System.Linq;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Infrastructure.Diarization;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Diarization.Tests;

public class SherpaSpeakerEmbedderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "role-model-" + Guid.NewGuid());

    public SherpaSpeakerEmbedderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private FileDiarizationModelProvider Provider() =>
        new(AppConfig.Default with { Vad = AppConfig.Default.Vad with { ModelDirectory = _dir } });

    private void WriteModel(long bytes)
    {
        using var fs = File.Create(Path.Combine(_dir, FileDiarizationModelProvider.RoleEmbeddingFile));
        fs.SetLength(bytes);
    }

    [Fact]
    public void ModelYok_FileNotFoundFirlatir_SURECI_OLDURMEZ()
    {
        // sherpa eksik modelde süreci sonlandırır; doğrulama motora girmeden önce yapılmalı.
        using var sut = new SherpaSpeakerEmbedder(Provider());

        Assert.Throws<FileNotFoundException>(() => sut.Embed(new float[24000]));
    }

    [Fact]
    public void YanlisBoyuttaModel_Reddedilir()
    {
        WriteModel(26_000_000);   // resnet34 boyutu: eşik bu modele göre ölçülmedi

        Assert.Throws<InvalidDataException>(() => Provider().RoleEmbeddingModelPath);
    }

    [Fact]
    public void BeklenenBoyuttaModel_YoluDondurur()
    {
        WriteModel(40_300_000);

        Assert.EndsWith(FileDiarizationModelProvider.RoleEmbeddingFile, Provider().RoleEmbeddingModelPath);
    }

    [Fact]
    public void BosSes_ModeleGirmedenReddedilir()
    {
        using var sut = new SherpaSpeakerEmbedder(Provider());

        Assert.Throws<ArgumentException>(() => sut.Embed(Array.Empty<float>()));
    }

    [SkippableFact]
    public void GercekModel_AyniSesAyniImza_FarkliSesFarkliImza()
    {
        Skip.If(Environment.GetEnvironmentVariable("ROLE_TEST") != "1",
            "ROLE_TEST=1 ve models/ altında TitaNet-small gerekir.");
        var wav = Environment.GetEnvironmentVariable("DIAR_TEST_WAV") ?? string.Empty;
        Skip.If(!File.Exists(wav), "DIAR_TEST_WAV geçerli bir 16 kHz mono WAV yolu olmalı.");
        var (samples, rate) = TestWav.Read(wav);
        Skip.If(rate != 16000 || samples.Length < 16000 * 6, "16 kHz, en az 6 sn bekleniyor.");

        using var sut = new SherpaSpeakerEmbedder(new FileDiarizationModelProvider(AppConfig.Default));
        var a = sut.Embed(samples[..24000]);
        var b = sut.Embed(samples[..24000]);

        Assert.Equal(192, a.Length);                     // TitaNet-small boyutu
        Assert.True(Cos(a, b) > 0.999);                   // deterministik
        Assert.True(a.All(float.IsFinite));
    }

    private static double Cos(float[] a, float[] b)
    {
        double d = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { d += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return d / Math.Sqrt(na * nb);
    }
}
