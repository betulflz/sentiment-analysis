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
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton(AppConfig.Default);
        services.AddSherpaDiarization();
        return services.BuildServiceProvider();
    }

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
}
