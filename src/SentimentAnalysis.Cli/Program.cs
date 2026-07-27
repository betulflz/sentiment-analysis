using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using SentimentAnalysis.Infrastructure.Audio;
using SentimentAnalysis.Infrastructure.Emotion;
using SentimentAnalysis.Infrastructure.Stt;
using SentimentAnalysis.Infrastructure.Vad;

namespace SentimentAnalysis.Cli;

/// <summary>
/// Grafik arayüz olmadan tam analiz hattını çalıştıran komut satırı aracı.
/// <para>Üç işi var: (1) kurulumu doğrulamak (<c>kontrol</c>), (2) bir dosyayı uçtan uca analiz
/// edip sonucu okunur biçimde yazmak (<c>analiz</c>), (3) eval/kalibrasyon araçlarına makine
/// okunur çıktı vermek (<c>--json</c>).</para>
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "yardim";

        try
        {
            return command switch
            {
                "analiz" or "analyze" => await AnalyzeAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "kontrol" or "check" => CheckSetup(),
                "stt" or "tani" => await SttDiagnostics.RunAsync(args.Skip(1).ToArray(), BuildProvider).ConfigureAwait(false),
                _ => PrintUsage(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("HATA: " + ex.Message);
            if (Environment.GetEnvironmentVariable("SA_DEBUG") == "1")
                Console.Error.WriteLine(ex);
            return 1;
        }
    }

    // ------------------------------------------------------------------ analiz

    private static async Task<int> AnalyzeAsync(string[] args)
    {
        var opts = CliOptions.Parse(args);
        if (opts.AudioPath is null)
        {
            Console.Error.WriteLine("Bir ses dosyası yolu verilmeli. Örnek: sa analiz kayit.mp3");
            return 2;
        }

        var config = opts.ApplyTo(AppConfig.Default);
        using var provider = BuildProvider(config);

        var missing = MissingModels(config);
        if (missing.Count > 0)
        {
            Console.Error.WriteLine("Eksik model dosyaları:");
            foreach (var m in missing) Console.Error.WriteLine("  - " + m);
            Console.Error.WriteLine();
            Console.Error.WriteLine("Kurulum: pwsh tools/fetch-models.ps1 && python tools/export_onnx_models.py");
            return 3;
        }

        var pipeline = provider.GetRequiredService<EmotionPipeline>();
        var source = AudioFileSource.Create(opts.AudioPath);

        if (!opts.Json)
        {
            Console.WriteLine($"Dosya      : {Path.GetFullPath(opts.AudioPath)}");
            Console.WriteLine($"Dil        : {config.Stt.WhisperLanguage ?? "otomatik algılama"}");
            Console.WriteLine($"Chunk      : {opts.ChunkSeconds:0.#} sn (örtüşme {opts.OverlapSeconds:0.#} sn)");
            Console.WriteLine($"Hızlandırma: {config.Emotion.ResolvedExecutionProvider}");
            Console.WriteLine();
            Console.WriteLine("Modeller yükleniyor ve analiz başlıyor (ilk chunk en yavaşıdır)...");
            Console.WriteLine();
        }

        var captureOptions = new CaptureOptions(
            TimeSpan.FromSeconds(opts.ChunkSeconds), TimeSpan.FromSeconds(opts.OverlapSeconds));

        DetectedLanguage? languageOverride = config.Stt.WhisperLanguage switch
        {
            "tr" => DetectedLanguage.Turkish,
            "en" => DetectedLanguage.English,
            null => null,
            _ => DetectedLanguage.Other,
        };

        var progress = opts.Json ? null : new Progress<ChunkResult>(PrintChunk);
        var stopwatch = Stopwatch.StartNew();

        var summary = await pipeline.RunAsync(
            source, captureOptions, Guid.NewGuid(), DateTimeOffset.Now,
            languageOverride, progress, CancellationToken.None).ConfigureAwait(false);

        stopwatch.Stop();

        var timeline = pipeline.Timeline;
        var language = SessionLanguage.Dominant(timeline);

        if (opts.Json)
            Console.WriteLine(JsonReport.Build(summary, timeline, language, config, stopwatch.Elapsed));
        else
            PrintSummary(summary, timeline, language, config, stopwatch.Elapsed, pipeline.DroppedCount);

        return 0;
    }

    // ------------------------------------------------------------------ yazdırma

    private static void PrintChunk(ChunkResult r)
    {
        var f = r.Fused;
        var badge = f.Flagged ? $"  [{EmotionLocalizer.Pattern(f.Pattern)}]" : string.Empty;
        Console.WriteLine(
            $"{Fmt(r.TimeStart)}–{Fmt(r.TimeEnd)}  {EmotionLocalizer.Emotion(f.PrimaryEmotion),-9} " +
            $"V{f.Combined.Valence,6:+0.00;-0.00} A{f.Combined.Arousal,5:0.00}  " +
            $"{Truncate(r.Transcript, 58)}{badge}");
    }

    private static void PrintSummary(
        MeetingSummary s, IReadOnlyList<ChunkResult> timeline, DetectedLanguage language,
        AppConfig config, TimeSpan elapsed, int dropped)
    {
        Console.WriteLine();
        Console.WriteLine(new string('─', 78));
        Console.WriteLine("OTURUM ÖZETİ");
        Console.WriteLine(new string('─', 78));

        if (timeline.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Konuşma içeren hiçbir bölüm bulunamadı — VAD tüm chunk'ları sessiz saydı.");
            Console.WriteLine("Kayıt gerçekten konuşma içeriyorsa VAD eşiği düşürülebilir (VadConfig.SpeechThreshold).");
            Console.WriteLine();
            Console.WriteLine($"Süre: {elapsed.TotalSeconds:0.0} sn");
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"  Baskın duygu       : {EmotionLocalizer.Emotion(s.DominantEmotion)}");
        Console.WriteLine($"  Algılanan dil      : {LanguageName(language)}");
        Console.WriteLine($"  Ortalama V/A       : V {s.AverageVa.Valence:+0.00;-0.00} · A {s.AverageVa.Arousal:0.00}");
        Console.WriteLine($"  Valence trendi     : {s.ValenceTrend:+0.000;-0.000} ({TrendWord(s.ValenceTrend)})");
        Console.WriteLine($"  Duygusal oynaklık  : {s.EmotionalVolatility:0.000}");
        Console.WriteLine($"  Söz-ton örtüşmesi  : {s.AverageCongruence:0.00}");
        Console.WriteLine();

        Console.WriteLine("  Süre payı (baskın duyguya göre):");
        foreach (var kv in s.TimeShareByEmotion.OrderByDescending(k => k.Value))
            Console.WriteLine($"    {EmotionLocalizer.Emotion(kv.Key),-9} {Bar(kv.Value)} %{kv.Value * 100:0}");
        Console.WriteLine();

        Console.WriteLine("  Ortalama VA noktasının 8 duyguya yakınlık payı:");
        Console.WriteLine("  (olasılık değil — VA uzayındaki geometrik yakınlık)");
        var shares = EmotionWheel.Distribute(s.AverageVa, config.Fusion.DistributionTemperature);
        var percents = PercentFormatter.ToPercents(shares.Select(x => x.Share).ToList());
        for (int i = 0; i < shares.Count; i++)
            Console.WriteLine($"    {EmotionLocalizer.Emotion(shares[i].Emotion),-9} {Bar(percents[i] / 100.0)} %{percents[i]}");
        Console.WriteLine();

        var flagged = timeline.Where(c => c.Fused.Flagged).ToList();
        Console.WriteLine($"  Söz-ton uyumsuzluğu işaretli chunk: {flagged.Count}/{timeline.Count}");
        foreach (var c in flagged.Take(5))
            Console.WriteLine($"    {Fmt(c.TimeStart)}  {EmotionLocalizer.Pattern(c.Fused.Pattern),-12} " +
                              $"metin V{c.Fused.TextValence:+0.00;-0.00} / ses V{c.Fused.AudioValence:+0.00;-0.00}  " +
                              $"{Truncate(c.Transcript, 40)}");
        Console.WriteLine();

        Console.WriteLine("  Tepe anlar (yüksek arousal / düşük valence):");
        foreach (var p in s.PeakMoments)
            Console.WriteLine($"    {Fmt(p.TimeStart)}  {EmotionLocalizer.Emotion(p.Fused.PrimaryEmotion),-9} {Truncate(p.Transcript, 48)}");
        Console.WriteLine();

        Console.WriteLine($"  Özet: {s.TurkishSummary}");
        Console.WriteLine();
        if (dropped > 0)
            Console.WriteLine($"  UYARI: geri basınç nedeniyle {dropped} chunk düşürüldü.");
        Console.WriteLine($"  Analiz süresi: {elapsed.TotalSeconds:0.0} sn · {timeline.Count} konuşma chunk'ı");
    }

    // ------------------------------------------------------------------ kontrol

    private static int CheckSetup()
    {
        var config = AppConfig.Default;
        Console.WriteLine("Kurulum kontrolü");
        Console.WriteLine(new string('─', 78));
        Console.WriteLine();

        Console.WriteLine($"  Model klasörü : {FileSttModelProvider.DefaultModelDirectory()}");
        Console.WriteLine($"  Platform      : {(OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux")}");
        Console.WriteLine($"  ONNX sağlayıcı: {config.Emotion.ResolvedExecutionProvider}");
        Console.WriteLine($"  Dil ayarı     : {config.Stt.WhisperLanguage ?? "otomatik algılama"}");
        Console.WriteLine();

        Console.WriteLine("  Model dosyaları:");
        var missing = new List<string>();
        foreach (var (name, path) in ExpectedModels(config))
        {
            if (File.Exists(path))
                Console.WriteLine($"    [VAR] {name,-30} {new FileInfo(path).Length / 1024.0 / 1024.0,8:N1} MB");
            else
            {
                Console.WriteLine($"    [YOK] {name}");
                missing.Add(name);
            }
        }

        // Türkçe'ye özel SER — opsiyonel ikinci prozodi kanalı.
        var (serName, serPath) = TurkishSerModel(config);
        if (File.Exists(serPath))
            Console.WriteLine($"    [VAR] {serName,-30} {new FileInfo(serPath).Length / 1024.0 / 1024.0,8:N1} MB  (Türkçe SER)");
        else
            Console.WriteLine($"    [ops] {serName,-30}    opsiyonel — yoksa yalnızca genel prozodi modeli kullanılır");
        Console.WriteLine();

        bool turkishActive = config.Emotion.UseTurkishSer ?? File.Exists(serPath);
        Console.WriteLine("  Prozodi kanalı:");
        Console.WriteLine(turkishActive
            ? $"    audeering (çok dilli) %{config.Emotion.GeneralProsodyWeight * 100:0} + " +
              $"TurEV-DB (Türkçe) %{config.Emotion.TurkishSerWeight * 100:0} — harmanlanıyor"
            : "    yalnızca audeering (çok dilli)");
        Console.WriteLine();

        Console.WriteLine("  Ses dönüştürme aracı (WAV dışı formatlar için):");
        var plan = TranscodePlan.For("a.mp3", "b.wav", OperatingSystem.IsMacOS());
        Console.WriteLine($"    Aranan: {plan.ToolDisplayName}");
        try
        {
            _ = AudioFileSource.Create("__yok__.wav");   // FileNotFound bekleniyor
        }
        catch (FileNotFoundException) { /* beklenen */ }
        catch { /* yoksay */ }
        Console.WriteLine();

        if (missing.Count > 0)
        {
            Console.WriteLine($"  {missing.Count} model eksik. Kurulum:");
            Console.WriteLine("    pwsh tools/fetch-models.ps1");
            Console.WriteLine("    python tools/export_onnx_models.py");
            return 3;
        }

        Console.WriteLine("  Tüm modeller yerinde. Analize hazır:");
        Console.WriteLine("    sa analiz <ses-dosyasi>");
        return 0;
    }

    // ------------------------------------------------------------------ yardımcılar

    private static ServiceProvider BuildProvider(AppConfig config) =>
        new ServiceCollection()
            .AddApplicationCore(config)
            .AddFusion()
            .AddStt()
            .AddProsodyEmotion()
            .AddTextEmotion()
            .AddSileroVad()
            .AddPipeline()
            .BuildServiceProvider();

    private static IReadOnlyList<(string Name, string Path)> ExpectedModels(AppConfig config)
    {
        var sttDir = config.Stt.ModelDirectory ?? FileSttModelProvider.DefaultModelDirectory();
        var emoDir = config.Emotion.ModelDirectory ?? FileSttModelProvider.DefaultModelDirectory();
        var vadDir = config.Vad.ModelDirectory ?? FileSttModelProvider.DefaultModelDirectory();
        var whisper = $"ggml-{config.Stt.LiveModel}-{config.Stt.Quantization}.bin";

        return new[]
        {
            (whisper, Path.Combine(sttDir, whisper)),
            (config.Emotion.ProsodyModelFile, Path.Combine(emoDir, config.Emotion.ProsodyModelFile)),
            (config.Emotion.TextModelFile, Path.Combine(emoDir, config.Emotion.TextModelFile)),
            (config.Emotion.TokenizerFile, Path.Combine(emoDir, config.Emotion.TokenizerFile)),
            (config.Vad.VadModelFile, Path.Combine(vadDir, config.Vad.VadModelFile)),
        };
    }

    /// <summary>Türkçe SER opsiyoneldir: yoksa sistem genel prozodi modeliyle çalışır,
    /// varsa otomatik devreye girer. Bu yüzden "eksik model" sayılmaz.</summary>
    private static (string Name, string Path) TurkishSerModel(AppConfig config)
    {
        var dir = config.Emotion.ModelDirectory ?? FileSttModelProvider.DefaultModelDirectory();
        return (config.Emotion.TurkishSerModelFile,
                System.IO.Path.Combine(dir, config.Emotion.TurkishSerModelFile));
    }

    private static List<string> MissingModels(AppConfig config) =>
        ExpectedModels(config).Where(m => !File.Exists(m.Path)).Select(m => m.Name).ToList();

    private static string LanguageName(DetectedLanguage l) => l switch
    {
        DetectedLanguage.Turkish => "Türkçe",
        DetectedLanguage.English => "İngilizce",
        _ => "belirlenemedi / diğer",
    };

    private static string TrendWord(double slope) =>
        slope > 0.05 ? "ısındı" : slope < -0.05 ? "soğudu" : "sabit";

    private static string Bar(double ratio, int width = 20)
    {
        int filled = (int)Math.Round(Math.Clamp(ratio, 0, 1) * width);
        return "[" + new string('█', filled) + new string('·', width - filled) + "]";
    }

    private static string Fmt(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";

    private static string Truncate(string s, int max)
    {
        s = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    private static int PrintUsage()
    {
        Console.WriteLine("""
            SentimentAnalysis CLI — yerel ses duygu analizi

            Kullanım:
              sa kontrol                       kurulumu ve model dosyalarını doğrula
              sa analiz <dosya> [seçenekler]   bir ses/video dosyasını analiz et
              sa stt <dosya>                   ham STT + kalite ağırlıklarını göster (teşhis)

            Seçenekler:
              --dil <auto|tr|en>    konuşma dili (varsayılan: auto — Whisper kendisi algılar)
              --chunk <saniye>      chunk uzunluğu (varsayılan: 30)
              --ortusme <saniye>    chunk örtüşmesi (varsayılan: 5)
              --vad-esik <0..1>     konuşma tespiti eşiği (varsayılan: 0.5)
              --json                makine okunur çıktı (eval araçları için)

            Örnekler:
              sa kontrol
              sa analiz kayit.mp3
              sa analiz toplanti.wav --dil tr --chunk 15
              sa analiz kayit.m4a --json > sonuc.json

            Desteklenen formatlar: wav (doğrudan) · mp3, m4a, aac, flac, ogg, opus, wma,
            mp4, mkv, webm (ffmpeg ile dönüştürülür).
            """);
        return 0;
    }
}
