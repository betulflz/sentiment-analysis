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

    /// <summary>Gerçek mikrofon gerektirir: MIC_TEST=1 ile açılır. "Durdur" (iptal) artık normal
    /// bir bitiştir — bu test, tam bir chunk oluşmadan (10 sn'lik chunk uzunluğunda ~2 sn'de)
    /// iptal edildiğinde CaptureAsync'in OperationCanceledException FIRLATMADIĞINI, bunun yerine
    /// tampondaki artığı (StreamingChunkBuffer.Flush) verip normal bittiğini doğrular (görev 5
    /// düzeltme turu 2 — kullanıcının "Durdur'a basınca rapor istiyorum" talebi). Otomatik
    /// derleme/CI'da donanım/izin olmadan çalıştırılamaz, bu yüzden MIC_TEST=1 ile kapılı;
    /// bu sınıfın diğer testleri (aşağıda) donanım gerektirmeyen sahte-işaretçi teknikleriyle
    /// aynı mantığı DOLAYLI olarak (CS1626 restructure'ın kendisinin derlendiğini, reentrancy
    /// korumasının bozulmadığını) doğruluyor — ama gerçek "tail iptalde verilir" davranışı
    /// yalnızca gerçek donanımla, burada kanıtlanabilir.</summary>
    [SkippableFact]
    public async Task CaptureAsync_RealMicrophone_CancelBeforeFullChunk_YieldsTailWithoutThrowing()
    {
        Skip.If(Environment.GetEnvironmentVariable("MIC_TEST") != "1",
            "Gerçek mikrofon testi: MIC_TEST=1 ile çalıştırın ve test sırasında konuşun.");
        Skip.IfNot(OperatingSystem.IsMacOS(), "Mikrofon yakalama şu an yalnızca macOS'ta.");

        using var source = new MicrophoneCaptureSource();
        using var cts = new CancellationTokenSource();
        // Chunk uzunluğu 10 sn, ama ~2 sn sonra iptal edeceğiz — hiçbir chunk TAM olarak
        // oluşamaz; yalnızca tamponun artığı (Flush) verilebilir.
        var options = new CaptureOptions(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));

        var collected = new List<AudioChunk>();
        _ = Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(2)); cts.Cancel(); });

        // YENİ SÖZLEŞME: fırlatmamalı — enumerasyon normal bitmeli.
        await foreach (var chunk in source.CaptureAsync(options, cts.Token))
            collected.Add(chunk);

        Assert.NotEmpty(collected); // en azından tampon artığı (tail) verilmiş olmalı
        Assert.All(collected, c => Assert.Equal(16000, c.SampleRate));
        // Artık, tam 10 sn'lik bir chunk'tan belirgin şekilde kısa olmalı (~2 sn'lik veri).
        Assert.True(collected[^1].Samples.Length < 16000 * 9,
            $"artık chunk tam chunk kadar uzun görünüyor ({collected[^1].Samples.Length} örnek) — Flush çalışmamış olabilir");
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
