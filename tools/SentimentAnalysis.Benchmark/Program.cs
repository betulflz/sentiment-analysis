using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Benchmark;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Audio;
using SentimentAnalysis.Infrastructure.Diarization;
using SentimentAnalysis.Infrastructure.Emotion;
using SentimentAnalysis.Infrastructure.Vad;
using SentimentAnalysis.Infrastructure.Stt;

if (args.Length == 1 && args[0] == "--help")
{
    Console.WriteLine("transcribe|diarize|analyze --input audio.wav --output prediction.json --models DIRECTORY " +
        "[--threads 2] [--speakers 0] [--model large-v3-turbo] [--quantization q5_0]");
    return 0;
}

try
{
    var options = BenchmarkOptions.Parse(args);
    if (!string.Equals(Path.GetExtension(options.Input), ".wav", StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Pilot girdisi WAV olmalı; önce 16 kHz mono PCM WAV'a dönüştürün.");
    if (!File.Exists(options.Input)) throw new FileNotFoundException("Ses dosyası bulunamadı.", options.Input);
    if (new FileInfo(options.Input).Length > 256L * 1024 * 1024)
        throw new ArgumentException("8 GB pilot için girdi 256 MiB sınırını aşıyor; kaydı bölün.");
    if (File.Exists(options.Output)) throw new IOException("Çıktı zaten var; üzerine yazılmadı.");

    var initial = AppConfig.Default;
    var config = initial with
    {
        Stt = initial.Stt with { ModelDirectory = options.Models, LiveModel = options.Model,
                                Quantization = options.Quantization, Threads = options.Threads, Language = "tr" },
        Vad = initial.Vad with { ModelDirectory = options.Models },
        Emotion = initial.Emotion with { ModelDirectory = options.Models },
    };
    // Fail before any native session is created. No download, database, UI or emotion models.
    string[] modelPaths;
    if (options.Task is "transcribe" or "analyze")
    {
        modelPaths = [new FileSttModelProvider(config).ResolveModelPath(options.Model)];
        if (options.Task == "analyze")
        {
            var emotionModels = new FileEmotionModelProvider(config);
            modelPaths = [.. modelPaths,
                new FileVadModelProvider(config).ResolveModelPath(config.Vad.VadModelFile),
                emotionModels.ResolveModelPath(config.Emotion.ProsodyModelFile),
                emotionModels.ResolveModelPath(config.Emotion.TextModelFile),
                emotionModels.ResolveModelPath(config.Emotion.TokenizerFile)];
        }
    }
    else
    {
        var provider = new FileDiarizationModelProvider(config);
        modelPaths = [provider.SegmentationModelPath, provider.EmbeddingModelPath];
    }
    var models = modelPaths.Select(path =>
    {
        using var stream = File.OpenRead(path);
        return new { file = Path.GetFileName(path), bytes = stream.Length,
                     sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() };
    }).ToArray();

    var samples = AudioPreprocessor.ToMono16k(WavDecoder.Decode(await File.ReadAllBytesAsync(options.Input)));
    double duration = samples.Length / 16000.0;
    if (duration <= 0) throw new InvalidDataException("Ses boş.");
    object result;
    bool complete = true;
    if (options.Task == "analyze")
    {
        var emotionModels = new FileEmotionModelProvider(config);
        using var vad = new SileroVadGate(config, new FileVadModelProvider(config));
        using var prosody = new OnnxProsodyEmotionModel(config, emotionModels);
        using var text = new OnnxTextEmotionModel(config, emotionModels);
        using var stt = new WhisperSpeechToText(config, new FileSttModelProvider(config));
        var pipeline = new EmotionPipeline(new ChunkProcessor(vad, stt, prosody, text, new EmotionFusion(config)),
            new InMemorySessionStore(), new MeetingAggregator(config), config);
        var report = await AudioReportBenchmark.RunAsync(pipeline, AudioReportBenchmark.FromSamples(samples), duration, config, CancellationToken.None);
        complete = report.Complete;
        result = new { report.Transcript, report.DurationSeconds, report.Complete, report.Diagnostics,
            report.Summary, report.Chunks, report.Decisions, report.FollowUp, models,
            settings = config,
            scope = "production audio pipeline + decision report; no Avalonia, SQLite, camera or speaker pass",
            engine = "audio-report baseline; raw overlapping chunk transcript" };
    }
    else if (options.Task == "transcribe")
    {
        using var stt = new WhisperSpeechToText(config, new FileSttModelProvider(config));
        var text = new List<string>();
        var segments = new List<object>();
        // Component baseline: 30 s, no overlap, no VAD. Each sample is processed once;
        // the live pipeline's DropOldest queue must never silently truncate evaluation data.
        const int size = 30 * 16000;
        for (int start = 0; start < samples.Length; start += size)
        {
            int end = Math.Min(samples.Length, start + size);
            var chunk = new AudioChunk(samples[start..end], 16000,
                TimeSpan.FromSeconds(start / 16000.0), TimeSpan.FromSeconds(end / 16000.0));
            var transcript = await stt.TranscribeAsync(chunk, CancellationToken.None);
            text.Add(transcript.Text);
            segments.AddRange(transcript.Segments.Select(s => new
            {
                start = chunk.Start.TotalSeconds + s.Start.TotalSeconds,
                end = chunk.Start.TotalSeconds + s.End.TotalSeconds,
                text = s.Text,
            }));
        }
        result = new { transcript = string.Join(" ", text), segments, durationSeconds = duration, models,
            settings = new { language = "tr", threads = options.Threads, model = options.Model,
                             quantization = options.Quantization, chunkSeconds = 30, overlapSeconds = 0, vad = false },
            engine = "Whisper.net component baseline" };
    }
    else
    {
        using var diarizer = new SherpaDiarizer(config, new FileDiarizationModelProvider(config));
        var speakers = await diarizer.DiarizeAsync(samples, options.ExpectedSpeakers, CancellationToken.None);
        result = new { speakers = speakers.Select(s => new
            { speaker = s.Speaker.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
              start = s.Start.TotalSeconds, end = Math.Min(duration, s.End.TotalSeconds) }),
            durationSeconds = duration, models,
            settings = new { expectedSpeakers = options.ExpectedSpeakers, threshold = config.Diarization.ClusteringThreshold,
                             ghostFilter = false },
            engine = "sherpa-onnx raw component baseline" };
    }
    var output = Path.GetFullPath(options.Output);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    await using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
    await JsonSerializer.SerializeAsync(destination, result, new JsonSerializerOptions
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    });
    return complete ? 0 : 3;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}
