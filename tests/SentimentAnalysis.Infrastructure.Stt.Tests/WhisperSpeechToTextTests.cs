using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Stt;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Stt.Tests;

public class WhisperSpeechToTextTests
{
    /// <summary>Model çözülmemeli (guard önce çalışmalı) diye kanıtlayan test double.</summary>
    private sealed class UnusedModelProvider : ISttModelProvider
    {
        public string ResolveModelPath(string modelName) =>
            throw new InvalidOperationException("guard'dan önce model çözülmemeliydi");
    }

    /// <summary>Entegrasyon testi için sabit yol döndüren sağlayıcı.</summary>
    private sealed class FixedModelProvider(string path) : ISttModelProvider
    {
        public string ResolveModelPath(string modelName) => path;
    }

    [Fact]
    public async Task TranscribeAsync_Throws_ForNon16kHz()
    {
        using var stt = new WhisperSpeechToText(AppConfig.Default, new UnusedModelProvider());
        var chunk = new AudioChunk(new float[8000], SampleRate: 8000, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<ArgumentException>(() => stt.TranscribeAsync(chunk, CancellationToken.None));
    }

    [SkippableFact]
    public async Task TranscribeAsync_RealModel_TranscribesTurkish()
    {
        var modelPath = Environment.GetEnvironmentVariable("WHISPER_MODEL_PATH");
        var wavPath = Environment.GetEnvironmentVariable("WHISPER_SAMPLE_WAV");
        Skip.If(string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath),
            "WHISPER_MODEL_PATH ayarlı değil; gerçek-model entegrasyon testi atlandı.");
        Skip.If(string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath),
            "WHISPER_SAMPLE_WAV ayarlı değil; örnek ses yok.");

        var samples = WavReader.ReadMono16(wavPath!);
        var cfg = AppConfig.Default with { Stt = AppConfig.Default.Stt with { Language = "tr" } };
        using var stt = new WhisperSpeechToText(cfg, new FixedModelProvider(modelPath!));
        var chunk = new AudioChunk(samples, 16000, TimeSpan.Zero, TimeSpan.FromSeconds(samples.Length / 16000.0));

        var t = await stt.TranscribeAsync(chunk, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(t.Text));
        Assert.Equal(DetectedLanguage.Turkish, t.Language);
        Assert.True(t.AvgLogProb < 0.0);
    }

    /// <summary>
    /// REGRESYON: Whisper.net, <c>WithProbabilities()</c> çağrılmadıkça
    /// <c>SegmentData.Probability</c>'yi 0 bırakır. O durumda TranscriptAssembler
    /// avg-logprob'u tabana (log 1e-6 ≈ −13.8) kırpar, QualityWeighting metin
    /// güvenilirliğini 0 hesaplar ve <b>füzyonda metin kanalı hiç oy kullanmaz</b> —
    /// sistem iki kanallı görünürken tek kanallı çalışır. Üstelik rAudio·rText = 0
    /// olduğu için güven düzeyi asla "Yüksek" olamaz.
    /// <para>Eski iddia (<c>AvgLogProb &lt; 0</c>) bu hatayı yakalayamıyordu: −13.8 de
    /// sıfırdan küçüktür. Anlamlı sınır, füzyonun kullandığı eşiktir.</para>
    /// </summary>
    [SkippableFact]
    public async Task TranscribeAsync_RealModel_ProducesUsableProbability_NotClampedFloor()
    {
        var modelPath = Environment.GetEnvironmentVariable("WHISPER_MODEL_PATH");
        var wavPath = Environment.GetEnvironmentVariable("WHISPER_SAMPLE_WAV");
        Skip.If(string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath),
            "WHISPER_MODEL_PATH ayarlı değil; gerçek-model entegrasyon testi atlandı.");
        Skip.If(string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath),
            "WHISPER_SAMPLE_WAV ayarlı değil; örnek ses yok.");

        var samples = WavReader.ReadMono16(wavPath!);
        var cfg = AppConfig.Default with { Stt = AppConfig.Default.Stt with { Language = "tr" } };
        using var stt = new WhisperSpeechToText(cfg, new FixedModelProvider(modelPath!));
        var chunk = new AudioChunk(samples, 16000, TimeSpan.Zero, TimeSpan.FromSeconds(samples.Length / 16000.0));

        var t = await stt.TranscribeAsync(chunk, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(t.Text));

        // Olasılık taban değerine kırpılmış olmamalı (kırpılma = probabilities kapalı).
        double floorLogProb = Math.Log(1e-6);
        Assert.True(t.AvgLogProb > floorLogProb + 1.0,
            $"avg-logprob tabana kırpılmış görünüyor ({t.AvgLogProb:F4}). " +
            "WhisperProcessorBuilder.WithProbabilities() çağrılmıyor olabilir.");

        // Ve füzyonun metin kanalına ağırlık verebilmesi için eşiğin üstünde olmalı.
        double logProbFloor = AppConfig.Default.Fusion.LogProbFloor;   // −1.0
        Assert.True(t.AvgLogProb > logProbFloor,
            $"avg-logprob füzyon eşiğinin ({logProbFloor}) altında ({t.AvgLogProb:F4}); " +
            "metin kanalı sıfır ağırlık alır ve karara katılmaz.");
    }
}
