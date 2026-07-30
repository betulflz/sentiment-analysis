using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace SentimentAnalysis.Infrastructure.Diarization.Tests;

/// <summary>Task 2 ölçüm koşucusu: <c>ClusteringThreshold</c>'un doğru değerini TAHMİNLE değil
/// GERÇEK KAYITLA ÖLÇEREK bulmak için yazıldı. Normal test koşusunda (CI dahil) çalışmaz —
/// gerçek model ve gerçek kayıt dosyası ister; <c>DIAR_SWEEP_WAV</c> ortam değişkeni yoksa atlanır.
/// <para>Kullanım: <c>DIAR_SWEEP_WAV=/tam/yol/toplanti.wav dotnet test --filter ThresholdSweepTests</c></para></summary>
public class ThresholdSweepTests
{
    private readonly ITestOutputHelper _output;

    public ThresholdSweepTests(ITestOutputHelper output) => _output = output;

    private static readonly double[] Thresholds = { 0.3, 0.4, 0.5, 0.6 };

    [SkippableFact]
    public async Task EsikTaramasi_GercekKayitla_KumeSayisiVeSureyiRaporlar()
    {
        var wav = Environment.GetEnvironmentVariable("DIAR_SWEEP_WAV") ?? string.Empty;
        Skip.If(string.IsNullOrWhiteSpace(wav), "DIAR_SWEEP_WAV ayarlı değil; ölçüm koşusu atlandı.");
        Skip.If(!File.Exists(wav), $"DIAR_SWEEP_WAV geçerli bir dosya değil: {wav}");

        var (samples, rate) = TestWav.Read(wav);
        Skip.If(rate != 16000, $"16 kHz mono WAV bekleniyor, {rate} Hz geldi.");

        var baseCfg = AppConfig.Default;
        var models = new FileDiarizationModelProvider(baseCfg);

        _output.WriteLine($"Ölçüm kaydı: {wav}");
        _output.WriteLine($"Toplam örnek: {samples.Length} ({samples.Length / 16000.0:F1} sn)");
        _output.WriteLine("eşik\tküme sayısı\ttoplam konuşma süresi (sn)");

        foreach (var threshold in Thresholds)
        {
            var cfg = baseCfg with { Diarization = baseCfg.Diarization with { ClusteringThreshold = threshold } };
            using var sut = new SherpaDiarizer(cfg, models);

            var segments = await sut.DiarizeAsync(samples, CancellationToken.None);
            var distinctSpeakers = segments.Select(s => s.Speaker.Index).Distinct().Count();
            var totalSpeechSeconds = segments.Sum(s => (s.End - s.Start).TotalSeconds);

            _output.WriteLine($"{threshold:F1}\t{distinctSpeakers}\t{totalSpeechSeconds:F1}");
        }

        // Bu test bir ASSERTION değil, bir ÖLÇÜM ARACIDIR: amacı belirli bir eşiği doğrulamak
        // değil, dört eşik için gerçek sonucu görünür kılmaktır. Sonuç
        // scratchpad/diar-threshold-2026-07-30.md dosyasına elle taşınır.
    }
}
