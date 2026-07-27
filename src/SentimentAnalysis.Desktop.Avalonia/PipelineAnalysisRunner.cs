using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;

namespace SentimentAnalysis.Desktop.Avalonia;

/// <summary>
/// <see cref="IAnalysisRunner"/>'ın ince app-katmanı adaptörü: <see cref="EmotionPipeline"/>'ı sarar.
/// Canlı sistem-sesi yakalama Alt-proje 05b'de gelecek; dosya seçimi (sürükle-bırak) 08b'de.
/// Bu turda bir ses-kaynağı fabrikası (<paramref name="sourceFactory"/>) alır — şu an hiçbir UI akışı
/// bunu doldurmuyor (varsayılan olarak <c>null</c> döner), yani shell derlenir + tepsi/pencere çalışır
/// ama "Başlat" gerçek bir kaynak seçilene dek sahte veri UYDURMAZ; bunun yerine anlaşılır bir
/// <see cref="InvalidOperationException"/> fırlatır. Gerçek uçtan-uca canlı akış 05b + 08b'de biter.
/// </summary>
public sealed class PipelineAnalysisRunner : IAnalysisRunner
{
    private readonly EmotionPipeline _pipeline;
    private readonly Func<IAudioCaptureSource?> _sourceFactory;
    private readonly AppConfig _config;

    public PipelineAnalysisRunner(EmotionPipeline pipeline, Func<IAudioCaptureSource?> sourceFactory, AppConfig config)
    {
        _pipeline = pipeline;
        _sourceFactory = sourceFactory;
        _config = config;
    }

    public Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct)
    {
        var source = _sourceFactory()
            ?? throw new InvalidOperationException(
                "Analiz için bir ses kaynağı seçilmedi. Canlı sistem-sesi yakalama 05b'de eklenecek; " +
                "bu turda dosya seçimi (sürükle-bırak) 08b'ye bağlı. Sahte/uydurma sonuç üretilmez.");

        var options = new CaptureOptions(ChunkLength: TimeSpan.FromSeconds(30), Overlap: TimeSpan.FromSeconds(5));

        // Dil ayarlarda sabitlenmişse onu kullan; "otomatik" ise null geç — pipeline oturumun
        // dilini gerçek transkriptlerden türetir. (Önceki sürüm her durumda config'ten türetiyordu;
        // otomatik modda bu sessizce DetectedLanguage.Other üretiyordu.)
        DetectedLanguage? languageOverride = ParseLanguage(_config.Stt.WhisperLanguage);

        return _pipeline.RunAsync(source, options, Guid.NewGuid(), DateTimeOffset.Now, languageOverride, progress, ct);
    }

    private static DetectedLanguage? ParseLanguage(string? code) => code switch
    {
        "tr" => DetectedLanguage.Turkish,
        "en" => DetectedLanguage.English,
        null => null,                       // otomatik algılama
        _ => DetectedLanguage.Other,        // desteklenen listede olmayan ama sabitlenmiş bir dil
    };
}
