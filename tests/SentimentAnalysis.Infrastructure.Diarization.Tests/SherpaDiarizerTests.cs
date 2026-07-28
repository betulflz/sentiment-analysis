using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Diarization;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Diarization.Tests;

public class SherpaDiarizerTests
{
    /// <summary>Model dosyalarına HİÇ dokunmayan sahte sağlayıcı: modelsiz ortamda çalışan
    /// testler gerçekten modelsiz kalmalı (yolları çözmeye çalışmak bile FileNotFoundException
    /// atardı).</summary>
    private sealed class FakeModels : IDiarizationModelProvider
    {
        public string SegmentationModelPath => "/yok/segmentation.onnx";
        public string EmbeddingModelPath => "/yok/embedding.onnx";
    }

    private static AppConfig Cfg() => AppConfig.Default;

    [Fact]
    public async Task DiarizeAsync_BosSes_ModelYuklemedenBosDondurur()
    {
        // Model yokken bile geçmeli: boş ses kısa devre yapar.
        using var sut = new SherpaDiarizer(Cfg(), new FakeModels());

        var segments = await sut.DiarizeAsync(Array.Empty<float>(), CancellationToken.None);

        Assert.Empty(segments);
    }

    [Fact]
    public async Task DiarizeAsync_IptalEdilmisToken_OperationCanceledFirlatir()
    {
        using var sut = new SherpaDiarizer(Cfg(), new FakeModels());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.DiarizeAsync(new float[16000], cts.Token));
    }

    [Fact]
    public async Task DiarizeAsync_ModelYok_FileNotFoundFirlatir_SURECI_OLDURMEZ()
    {
        // REGRESYON TESTİ: sherpa-onnx, model dosyası yoksa istisna fırlatmaz — hata basıp
        // SÜRECİ SONLANDIRIR. Bu test ilk yazıldığında test host'u çöktü ("Test ana işlemi
        // kilitlendi"). SherpaDiarizer artık dosya varlığını motora girmeden önce doğruluyor.
        // Bu test kaldırılır/gevşetilirse, eksik modeli olan bir kullanıcıda uygulama
        // sessizce kapanır ve hiçbir log kalmaz.
        // Ayrıca "model kurulu değil" ile "konuşmacı bulunamadı" karıştırılmamalı (master §12.2).
        using var sut = new SherpaDiarizer(Cfg(), new FakeModels());

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => sut.DiarizeAsync(new float[16000], CancellationToken.None));
    }

    [Fact]
    public async Task DiarizeAsync_DisposeSonrasi_ObjectDisposedFirlatir()
    {
        var sut = new SherpaDiarizer(Cfg(), new FakeModels());
        sut.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => sut.DiarizeAsync(new float[16000], CancellationToken.None));
    }

    [SkippableFact]
    public async Task DiarizeAsync_GercekModel_IkiKonusmaciyiAyirir()
    {
        Skip.If(Environment.GetEnvironmentVariable("DIAR_TEST") != "1",
            "Gerçek diarization modelleri gerekir; DIAR_TEST=1 ile çalıştırın.");
        var wav = Environment.GetEnvironmentVariable("DIAR_TEST_WAV") ?? string.Empty;
        Skip.If(!File.Exists(wav), "DIAR_TEST_WAV geçerli bir 16 kHz mono WAV yolu olmalı.");

        var (samples, rate) = TestWav.Read(wav);
        Skip.If(rate != 16000, $"16 kHz bekleniyor, {rate} geldi.");

        using var sut = new SherpaDiarizer(Cfg(), new FileDiarizationModelProvider(Cfg()));
        var segments = await sut.DiarizeAsync(samples, CancellationToken.None);

        Assert.NotEmpty(segments);
        Assert.All(segments, s => Assert.True(s.End > s.Start, "bölge uzunluğu pozitif olmalı"));

        // 10a ölçümü: iki konuşmacılı kayıtta eşik 0,8 ile tam 2 küme bulunur.
        var distinct = segments.Select(s => s.Speaker.Index).Distinct().ToList();
        Assert.Equal(2, distinct.Count);
        Assert.All(distinct, i => Assert.True(i >= 0, "gerçek kümelerde Bilinmeyen beklenmez"));
    }
}
