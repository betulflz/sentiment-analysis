using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Infrastructure.Diarization;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Diarization.Tests;

/// <summary>Eksik bir DI kaydı yalnızca ÇALIŞMA ANINDA patlar ve kullanıcı bunu "özellik
/// çalışmıyor" olarak görür. Bu testler grafiğin gerçekten çözüldüğünü derleme değil koşu
/// zamanında kanıtlar.</summary>
public class DiarizationDiTests
{
    private static ServiceProvider Build(bool windowed = false)
    {
        var services = new ServiceCollection();
        var cfg = AppConfig.Default;
        services.AddSingleton(cfg with { Diarization = cfg.Diarization with { UseWindowedDiarizer = windowed } });
        services.AddSingleton<IVadGate>(new NoVad());
        services.AddSherpaDiarization();
        return services.BuildServiceProvider();
    }

    private sealed class NoVad : IVadGate { public bool HasSpeech(SentimentAnalysis.Core.AudioChunk c) => false; }

    [Fact]
    public void PencereliAyar_IDiarizerPencereli_ModelYuklemez()
    {
        // İterasyon 2: varsayılan pencereli ayrım (TitaNet + VAD); çözümleme native modele dokunmaz.
        using var sp = Build(windowed: true);
        Assert.IsType<SentimentAnalysis.Application.Diarization.WindowedDiarizer>(sp.GetRequiredService<IDiarizer>());
        Assert.Same(sp.GetRequiredService<IDiarizer>(), sp.GetRequiredService<IDiarizer>());
    }

    [Fact]
    public void Varsayilan_PencereliAyrim() => Assert.True(AppConfig.Default.Diarization.UseWindowedDiarizer);

    [Fact]
    public void AddSherpaDiarization_IDiarizerCozulur()
    {
        using var sp = Build();

        var diarizer = sp.GetRequiredService<IDiarizer>();

        Assert.IsType<SherpaDiarizer>(diarizer);
    }

    [Fact]
    public void AddSherpaDiarization_Singleton_ModelBirKezYuklenir()
    {
        // Model ~700 MB; istek başına yeniden yüklenmesi kabul edilemez (master §8).
        using var sp = Build();

        Assert.Same(sp.GetRequiredService<IDiarizer>(), sp.GetRequiredService<IDiarizer>());
    }

    [Fact]
    public void AddSherpaDiarization_CozumlemeModelYUKLEMEZ()
    {
        // Özellik kapalıyken maliyeti sıfır olmalı: DI çözümlemesi native modele dokunmamalı.
        // (Dokunsaydı, modeli olmayan kullanıcıda uygulama açılışta çökerdi — bkz. Task 6:
        // sherpa eksik modelde süreci sonlandırır.)
        using var sp = Build();

        var ex = Record.Exception(() => sp.GetRequiredService<IDiarizer>());

        Assert.Null(ex);
    }

    [Fact]
    public void AddSherpaDiarization_SesImzasiCikaricisiCozulur_ModelYuklemez()
    {
        using var sp = Build();

        var embedder = sp.GetRequiredService<ISpeakerEmbedder>();

        Assert.IsType<SherpaSpeakerEmbedder>(embedder);
        Assert.Same(embedder, sp.GetRequiredService<ISpeakerEmbedder>());
    }
}
