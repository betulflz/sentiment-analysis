using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;

namespace SentimentAnalysis.Desktop.Avalonia;

/// <summary>
/// <see cref="IAnalysisRunner"/>'ın ince app-katmanı adaptörü: <see cref="EmotionPipeline"/>'ı sarar.
/// Ses-kaynağı fabrikası (<paramref name="sourceFactory"/>) çağrılana kadar hangi kaynağın
/// üretileceğini bilmez — <see cref="ICaptureModeState"/>'e göre dosya veya mikrofon seçilir.
/// "Başlat" gerçek bir kaynak seçilene dek sahte veri UYDURMAZ; bunun yerine anlaşılır bir
/// <see cref="InvalidOperationException"/> fırlatır.
/// </summary>
public sealed class PipelineAnalysisRunner : IAnalysisRunner
{
    private readonly EmotionPipeline _pipeline;
    private readonly Func<IAudioCaptureSource?> _sourceFactory;
    private readonly AppConfig _config;
    private readonly ICaptureModeState? _captureMode;

    public PipelineAnalysisRunner(EmotionPipeline pipeline, Func<IAudioCaptureSource?> sourceFactory,
                                  AppConfig config, ICaptureModeState? captureMode = null)
    {
        _pipeline = pipeline;
        _sourceFactory = sourceFactory;
        _config = config;
        _captureMode = captureMode;
    }

    public async Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct)
    {
        var source = _sourceFactory()
            ?? throw new InvalidOperationException(
                "Analiz için bir ses kaynağı seçilmedi. Dosya modunda bir ses dosyası seçin " +
                "veya mikrofon modunu açın.");

        try
        {
            // Canlı mikrofonda kısa parça = hızlı geri bildirim; dosyada uzun parça = daha iyi bağlam.
            var options = _captureMode?.Mode == CaptureMode.Mikrofon
                ? new CaptureOptions(ChunkLength: TimeSpan.FromSeconds(10), Overlap: TimeSpan.FromSeconds(2))
                : new CaptureOptions(ChunkLength: TimeSpan.FromSeconds(30), Overlap: TimeSpan.FromSeconds(5));
            var language = ParseLanguage(_config.Stt.Language);
            var sessionId = Guid.NewGuid();
            var startedAt = DateTimeOffset.Now;

            // NOT: EmotionPipeline.RunAsync artık "Durdur" (ct iptali) sonrasında
            // OperationCanceledException FIRLATMAZ — motor bunu kendi içinde normal bir bitiş
            // sayıp kanalda kalan chunk'ları drene edip bir MeetingSummary ile döner (görev 5
            // düzeltme turu 2). Bu yüzden burada artık ayrı bir catch(OperationCanceledException)
            // + BuildSummary çağrısına gerek yok — önceki turda eklenen o yol artık ölü koddu,
            // kaldırıldı (BuildSummary'nin kendisi EmotionPipeline içinde hâlâ kullanılıyor).
            return await _pipeline.RunAsync(source, options, sessionId, startedAt, language, progress, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            // EmotionPipeline kaynağı elden çıkarmaz (motor sınırının dışında kalır); mikrofon
            // kaynağı yerel AudioQueue + pinlenmiş delegate tuttuğundan burada serbest bırakılmalı,
            // aksi halde her başlat/durdur döngüsünde kaynak sızar.
            (source as IDisposable)?.Dispose();
        }
    }

    private static DetectedLanguage ParseLanguage(string? code) => code switch
    {
        "tr" => DetectedLanguage.Turkish,
        "en" => DetectedLanguage.English,
        _ => DetectedLanguage.Other,
    };
}
