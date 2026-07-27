using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Audio;

namespace SentimentAnalysis.Cli;

/// <summary>
/// STT ve kalite sinyallerinin <b>ham</b> değerlerini yazar.
/// <para>Neden var: füzyon ağırlıkları Whisper'ın <c>avg-logprob</c>'una bağlı ve bu değerin
/// gerçek ölçeği modele/kütüphaneye göre değişir. Eşikler ölçülmeden ayarlanırsa bir kanal
/// sessizce sıfır ağırlık alır ve sistem "iki kanallı" görünürken tek kanallı çalışır.
/// Bu komut o değerleri görünür kılar (master §12.4: ölçmeden söz verme).</para>
/// </summary>
public static class SttDiagnostics
{
    public static async Task<int> RunAsync(string[] args, Func<AppConfig, ServiceProvider> buildProvider)
    {
        var opts = CliOptions.Parse(args);
        if (opts.AudioPath is null)
        {
            Console.Error.WriteLine("Bir ses dosyası yolu verilmeli. Örnek: sa stt kayit.wav");
            return 2;
        }

        var config = opts.ApplyTo(AppConfig.Default);
        using var provider = buildProvider(config);

        var stt = provider.GetRequiredService<ISpeechToText>();
        var vad = provider.GetRequiredService<IVadGate>();
        var source = AudioFileSource.Create(opts.AudioPath);
        var captureOptions = new CaptureOptions(
            TimeSpan.FromSeconds(opts.ChunkSeconds), TimeSpan.FromSeconds(opts.OverlapSeconds));

        Console.WriteLine($"Dosya: {Path.GetFullPath(opts.AudioPath)}");
        Console.WriteLine($"Kalite eşikleri: LogProbFloor={config.Fusion.LogProbFloor}  " +
                          $"MinReliableWords={config.Fusion.MinReliableWords}  " +
                          $"MinReliableSeconds={config.Fusion.MinReliableSeconds}  " +
                          $"SNR={config.Fusion.SnrLowDb}..{config.Fusion.SnrHighDb} dB");
        Console.WriteLine();

        int index = 0;
        await foreach (var chunk in source.CaptureAsync(captureOptions, CancellationToken.None))
        {
            bool hasSpeech = vad.HasSpeech(chunk);
            Console.WriteLine($"── chunk {index}  {chunk.Start:mm\\:ss}–{chunk.End:mm\\:ss}  " +
                              $"VAD: {(hasSpeech ? "konuşma" : "SESSİZ (atlanır)")}");

            if (!hasSpeech) { index++; Console.WriteLine(); continue; }

            var transcript = await stt.TranscribeAsync(chunk, CancellationToken.None).ConfigureAwait(false);
            var quality = QualitySignalsBuilder.Build(chunk, transcript);

            double rText = QualityWeighting.TextReliability(quality, config.Fusion);
            double rAudio = QualityWeighting.AudioReliability(quality, config.Fusion);

            // Bileşenleri tek tek göster: hangisinin sıfırladığı belli olsun.
            double wordF = Math.Clamp(quality.TranscriptWordCount / config.Fusion.MinReliableWords, 0, 1);
            double logRange = 0.0 - config.Fusion.LogProbFloor;
            double logF = Math.Clamp((quality.TranscriptAvgLogProb - config.Fusion.LogProbFloor) / logRange, 0, 1);
            double snrF = Math.Clamp((quality.AudioSnr - config.Fusion.SnrLowDb) /
                                     (config.Fusion.SnrHighDb - config.Fusion.SnrLowDb), 0, 1);
            double durF = Math.Clamp(quality.Duration.TotalSeconds / config.Fusion.MinReliableSeconds, 0, 1);

            Console.WriteLine($"   transkript      : {Trim(transcript.Text, 70)}");
            Console.WriteLine($"   dil             : {transcript.Language}");
            Console.WriteLine($"   avg-logprob     : {transcript.AvgLogProb:F4}   (olasılık ≈ {Math.Exp(transcript.AvgLogProb):F4})");
            Console.WriteLine($"   kelime sayısı   : {quality.TranscriptWordCount}");
            Console.WriteLine($"   SNR / konuşma   : {quality.AudioSnr:F2} dB / {quality.SpeechRatio:P0}");
            Console.WriteLine($"   METİN ağırlığı  : {rText:F4}  = kelime {wordF:F3} × logprob {logF:F3}" +
                              (rText <= 1e-9 ? "   ← SIFIR: metin kanalı karara katılmıyor" : ""));
            Console.WriteLine($"   SES   ağırlığı  : {rAudio:F4}  = snr {snrF:F3} × konuşma {Math.Clamp(quality.SpeechRatio, 0, 1):F3} × süre {durF:F3}" +
                              (rAudio <= 1e-9 ? "   ← SIFIR: ses kanalı karara katılmıyor" : ""));
            Console.WriteLine();
            index++;
        }

        if (index == 0) Console.WriteLine("Hiç chunk üretilmedi (dosya çok kısa olabilir).");
        return 0;
    }

    private static string Trim(string s, int max)
    {
        s = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}
