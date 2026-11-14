namespace SentimentAnalysis.Benchmark;

public sealed record BenchmarkOptions(string Task, string Input, string Output, string Models,
    int Threads, int ExpectedSpeakers, string Model, string Quantization = "q5_0", string Diarizer = "windowed")
{
    public static BenchmarkOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("transcribe" or "diarize" or "analyze"))
            throw new ArgumentException("İlk seçenek transcribe, diarize veya analyze olmalı.");
        var values = new Dictionary<string, string>();
        var accepted = new[] { "--input", "--output", "--models", "--threads", "--speakers", "--model", "--quantization", "--diarizer" };
        for (int i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !accepted.Contains(args[i]) || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException($"Eksik, yinelenen veya bilinmeyen seçenek: {args[i]}");
        }
        string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"Gerekli seçenek: {key}");
        int Number(string key, int fallback, int min, int max)
        {
            if (!values.TryGetValue(key, out var value)) return fallback;
            if (!int.TryParse(value, out var number) || number < min || number > max)
                throw new ArgumentException($"{key}: {min}–{max} arası tam sayı gerekli.");
            return number;
        }
        var model = values.GetValueOrDefault("--model", "large-v3-turbo");
        if (model is not ("large-v3-turbo" or "large-v3" or "medium" or "small" or "base" or "tiny"))
            throw new ArgumentException("Desteklenen Whisper modelleri: large-v3-turbo, large-v3, medium, small, base, tiny.");
        var quantization = values.GetValueOrDefault("--quantization", "q5_0");
        if (quantization is not ("q5_0" or "q5_1" or "q8_0"))
            throw new ArgumentException("Desteklenen kuantizasyonlar: q5_0, q5_1, q8_0.");
        var diarizer = values.GetValueOrDefault("--diarizer", "windowed");
        if (diarizer is not ("windowed" or "pyannote"))
            throw new ArgumentException("Desteklenen konuşmacı ayrımı yöntemleri: windowed (varsayılan), pyannote (eski temel çizgi).");
        return new(args[0], Required("--input"), Required("--output"), Required("--models"),
            Number("--threads", 2, 1, 4), Number("--speakers", 0, 0, 8), model, quantization, diarizer);
    }
}
