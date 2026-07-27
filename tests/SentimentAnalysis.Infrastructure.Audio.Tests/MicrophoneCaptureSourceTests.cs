using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class MicrophoneCaptureSourceTests
{
    /// <summary>Gerçek mikrofon gerektirir: MIC_TEST=1 ile açılır. Kullanıcı testin
    /// çalıştığı ~6 saniye boyunca KONUŞMALIDIR.</summary>
    [SkippableFact]
    public async Task CaptureAsync_RealMicrophone_ProducesChunksWithAudibleSignal()
    {
        Skip.If(Environment.GetEnvironmentVariable("MIC_TEST") != "1",
            "Gerçek mikrofon testi: MIC_TEST=1 ile çalıştırın ve test sırasında konuşun.");
        Skip.IfNot(OperatingSystem.IsMacOS(), "Mikrofon yakalama şu an yalnızca macOS'ta.");

        using var source = new MicrophoneCaptureSource();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var options = new CaptureOptions(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(0.5));

        var collected = new List<AudioChunk>();
        try
        {
            await foreach (var chunk in source.CaptureAsync(options, cts.Token))
            {
                collected.Add(chunk);
                if (collected.Count >= 2) break;
            }
        }
        catch (OperationCanceledException) { /* süre doldu — topladıklarımızı değerlendiririz */ }

        Assert.NotEmpty(collected);
        Assert.All(collected, c => Assert.Equal(16000, c.SampleRate));
        Assert.All(collected, c => Assert.NotEmpty(c.Samples));

        // Zaman damgaları ilerlemeli (mutlak indeksten hesaplanıyor).
        for (int i = 1; i < collected.Count; i++)
            Assert.True(collected[i].Start > collected[i - 1].Start);

        // MUTLAK EŞİK KULLANMA (spike dersi): sessiz oda tabanı 0,0098-0,0168 ölçüldü,
        // konuşma 0,0805. Dijital sessizlikten AÇIKÇA ayrıldığını doğrula.
        double peak = collected.SelectMany(c => c.Samples).Max(Math.Abs);
        Assert.True(peak > 0.005,
            $"Mikrofondan anlamlı sinyal gelmedi (tepe {peak:F4}). İzin verilmiş mi? " +
            "Test sırasında konuştunuz mu?");
    }

    [Fact]
    public void Dispose_WithoutCapture_DoesNotThrow()
    {
        var source = new MicrophoneCaptureSource();

        source.Dispose();
        source.Dispose();   // idempotent
    }

    private static readonly CaptureOptions Options = new(TimeSpan.FromSeconds(1), TimeSpan.Zero);

    /// <summary>Yeniden giriş koruması: gerçek donanım/AudioQueue başlatmadan test etmek için
    /// private <c>_queue</c> alanına yansıma ile sahte (sıfır olmayan) bir işaretçi yerleştiriyoruz
    /// — bu, "bu örnekte zaten canlı bir kuyruk var" durumunu taklit eder. AudioQueueStart hiç
    /// çağrılmaz, yani gerçek mikrofon yakalaması BAŞLATILMAZ (görev talimatı gereği). Testin
    /// sonunda sahte işaretçiyi sıfırlıyoruz ki Dispose native API'yi geçersiz bir işaretçiyle
    /// çağırmasın.</summary>
    [Fact]
    public async Task CaptureAsync_WhileQueueAlreadyLive_ThrowsInvalidOperationException()
    {
        using var source = new MicrophoneCaptureSource();
        var queueField = typeof(MicrophoneCaptureSource)
            .GetField("_queue", BindingFlags.NonPublic | BindingFlags.Instance)!;
        queueField.SetValue(source, new IntPtr(1));

        try
        {
            var enumerator = source.CaptureAsync(Options, CancellationToken.None).GetAsyncEnumerator();
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await enumerator.MoveNextAsync());
        }
        finally
        {
            // Temizlik: gerçek olmayan işaretçiyi sıfırla ki Dispose() native
            // AudioQueueStop/Dispose'u geçersiz bir adresle çağırıp çökmesin.
            queueField.SetValue(source, IntPtr.Zero);
        }
    }

    [Fact]
    public async Task CaptureAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var source = new MicrophoneCaptureSource();
        source.Dispose();

        var enumerator = source.CaptureAsync(Options, CancellationToken.None).GetAsyncEnumerator();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await enumerator.MoveNextAsync());
    }
}
