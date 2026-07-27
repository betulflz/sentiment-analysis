using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Cli;

/// <summary>Komut satırı seçeneklerinin ayrıştırılması. Saf ve test edilebilir.</summary>
public sealed record CliOptions(
    string? AudioPath,
    string? Language,
    double ChunkSeconds,
    double OverlapSeconds,
    double? VadThreshold,
    bool Json)
{
    public const double DefaultChunkSeconds = 30.0;
    public const double DefaultOverlapSeconds = 5.0;

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        string? path = null, language = null;
        double chunk = DefaultChunkSeconds, overlap = DefaultOverlapSeconds;
        double? vad = null;
        bool json = false;

        for (int i = 0; i < args.Count; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--json":
                    json = true;
                    break;
                case "--dil" or "--language":
                    language = Next(args, ref i, a);
                    break;
                case "--chunk":
                    chunk = ParsePositive(Next(args, ref i, a), a);
                    break;
                case "--ortusme" or "--overlap":
                    overlap = ParseNonNegative(Next(args, ref i, a), a);
                    break;
                case "--vad-esik" or "--vad-threshold":
                    vad = ParseUnit(Next(args, ref i, a), a);
                    break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"Bilinmeyen seçenek: {a}");
                    path ??= a;
                    break;
            }
        }

        if (overlap >= chunk)
            throw new ArgumentException(
                $"Örtüşme ({overlap:0.#} sn) chunk uzunluğundan ({chunk:0.#} sn) küçük olmalı; " +
                "aksi halde chunk'lar ilerlemez.");

        return new CliOptions(path, language, chunk, overlap, vad, json);
    }

    /// <summary>Seçenekleri yapılandırmaya uygular (verilmeyenler varsayılanda kalır).</summary>
    public AppConfig ApplyTo(AppConfig config)
    {
        var result = config;

        if (Language is not null)
            result = result with { Stt = result.Stt with { Language = Language } };

        if (VadThreshold is { } t)
            result = result with { Vad = result.Vad with { SpeechThreshold = t } };

        return result;
    }

    private static string Next(IReadOnlyList<string> args, ref int i, string flag)
    {
        if (i + 1 >= args.Count) throw new ArgumentException($"{flag} bir değer bekliyor.");
        return args[++i];
    }

    private static double ParsePositive(string s, string flag)
    {
        var v = ParseDouble(s, flag);
        if (v <= 0) throw new ArgumentException($"{flag} pozitif olmalı (verilen: {s}).");
        return v;
    }

    private static double ParseNonNegative(string s, string flag)
    {
        var v = ParseDouble(s, flag);
        if (v < 0) throw new ArgumentException($"{flag} negatif olamaz (verilen: {s}).");
        return v;
    }

    private static double ParseUnit(string s, string flag)
    {
        var v = ParseDouble(s, flag);
        if (v is < 0 or > 1) throw new ArgumentException($"{flag} 0 ile 1 arasında olmalı (verilen: {s}).");
        return v;
    }

    /// <summary>Kültürden bağımsız ayrıştırma: Türkçe yerelde "0,5" de "0.5" de kabul edilir
    /// (aksi halde ondalık ayırıcı yüzünden komut sessizce yanlış değer alır).</summary>
    private static double ParseDouble(string s, string flag)
    {
        var normalized = s.Replace(',', '.');
        if (!double.TryParse(normalized, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            throw new ArgumentException($"{flag} sayısal bir değer bekliyor (verilen: {s}).");
        return v;
    }
}
