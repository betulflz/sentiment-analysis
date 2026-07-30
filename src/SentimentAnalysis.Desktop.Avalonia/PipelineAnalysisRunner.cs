using Avalonia.Threading;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using SentimentAnalysis.Infrastructure.Audio;

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
    private readonly ISessionAudioSink? _audioSink;
    private readonly IDiarizationState? _diarization;

    public PipelineAnalysisRunner(EmotionPipeline pipeline, Func<IAudioCaptureSource?> sourceFactory,
                                  AppConfig config, ICaptureModeState? captureMode = null,
                                  ISessionAudioSink? audioSink = null,
                                  IDiarizationState? diarization = null)
    {
        _diarization = diarization;
        _pipeline = pipeline;
        _sourceFactory = sourceFactory;
        _config = config;
        _captureMode = captureMode;
        _audioSink = audioSink;   // additive (10b): yoksa ses saklanmaz, davranış eskisi gibi
    }

    /// <summary>Son koşuda ses biriktirme üst sınırına takılındı mı (bkz.
    /// <see cref="RecordingCaptureSource.LimitReached"/>). Kullanıcıya bildirilmelidir:
    /// konuşmacı ayrımı yalnızca biriken kısmı kapsar.</summary>
    public bool AudioLimitReached { get; private set; }

    /// <summary>Son koşunun oturum kimliği — arka plan konuşmacı geçişi (10b) bunu kullanır.</summary>
    public Guid? LastSessionId { get; private set; }

    /// <summary>Son koşuda atlanan chunk sayısı (kanal taşması + drenaj sınırı/bütçesi) —
    /// bkz. <see cref="IAnalysisRunner.SkippedChunkCount"/> ve 08e düzeltme turu Fix 2.</summary>
    public int SkippedChunkCount => _pipeline.DroppedCount + _pipeline.DrainSkippedCount;

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
            //
            // NOT (08e düzeltme turu Fix 4): EmotionPipeline'ın her adımı ConfigureAwait(false)
            // kullanır, bu yüzden progress.Report(...) çağrısı UI thread'inde DEĞİL, thread-pool
            // iş parçacığında çalışır. progress, LiveViewModel'in KENDİSİ (IProgress<ChunkResult>
            // implemente eder) — Report doğrudan bound bir ObservableCollection'a Add yapar. Bunu
            // Avalonia zaten burada (Desktop.Avalonia) referanslandığından burada, Dispatcher.UIThread'e
            // post ederek çözüyoruz — SentimentAnalysis.Desktop (LiveViewModel'in yaşadığı proje)
            // Avalonia'sız, headless test edilebilir kalmalı; oraya Dispatcher eklenmez.
            var uiThreadProgress = new UiThreadProgress<ChunkResult>(progress);

            // 10b: konuşmacı ayrımı açıksa oturum sesini biriktir (arka plan geçişinin girdisi).
            // Kapalıyken kaynak SARILMAZ — kullanıcı istemediği hâlde sesini belleğe/diske
            // almayız (master §12.5, gizlilik varsayılan).
            RecordingCaptureSource? recording = null;
            IAudioCaptureSource pipelineSource = source;
            if (_diarization?.Enabled == true && _audioSink is not null)
            {
                recording = new RecordingCaptureSource(
                    source, maxSamples: _config.Diarization.MaxRecordedMinutes * 60 * 16000);
                pipelineSource = recording;
            }

            LastSessionId = sessionId;
            AudioLimitReached = false;

            var summary = await _pipeline.RunAsync(pipelineSource, options, sessionId, startedAt, language, uiThreadProgress, ct)
                .ConfigureAwait(false);

            // Ses, özet ÜRETİLDİKTEN SONRA saklanır: rapor bu yüzden gecikmez (spec 10 S2).
            // İptal edilmiş ct ile yazamayız (anında iptal olurdu) — "Durdur" normal bir bitiştir.
            // Yalnızca analiz BAŞARILI ise ses kaydı ve oturum veritabanına saklanır (NoResult durumunda saklanmaz).
            if (recording is not null && summary.Outcome == AnalysisOutcome.Success)
            {
                AudioLimitReached = recording.LimitReached;
                var pcm = recording.GetPcm16();
                if (pcm.Length > 0)
                    await _audioSink!.SaveSessionAudioAsync(sessionId, pcm, CancellationToken.None)
                        .ConfigureAwait(false);
            }

            return summary;
        }
        finally
        {
            // EmotionPipeline kaynağı elden çıkarmaz (motor sınırının dışında kalır); mikrofon
            // kaynağı yerel AudioQueue + pinlenmiş delegate tuttuğundan burada serbest bırakılmalı,
            // aksi halde her başlat/durdur döngüsünde kaynak sızar.
            (source as IDisposable)?.Dispose();
        }
    }

    public Task WarmUpAsync(CancellationToken ct) => _pipeline.WarmUpAsync(ct);

    private static DetectedLanguage ParseLanguage(string? code) => code switch
    {
        "tr" => DetectedLanguage.Turkish,
        "en" => DetectedLanguage.English,
        _ => DetectedLanguage.Other,
    };

    /// <summary>Sarılan <see cref="IProgress{T}"/>'i, çağrıldığı iş parçacığı ne olursa olsun her
    /// zaman Avalonia UI thread'ine POST ederek çağıran ince adaptör (bkz. Fix 4). <c>Progress&lt;T&gt;</c>
    /// yerine bunu kullanıyoruz çünkü <c>Progress&lt;T&gt;</c> yalnızca OLUŞTURULDUĞU anda yakalanan
    /// <see cref="SynchronizationContext"/>'e güvenir — burada niyeti (UI thread'e post) EXPLICIT
    /// kılmak, çağrıldığı bağlamdan bağımsız, daha sağlam bir garanti verir.</summary>
    private sealed class UiThreadProgress<T>(IProgress<T> inner) : IProgress<T>
    {
        public void Report(T value) => Dispatcher.UIThread.Post(() => inner.Report(value));
    }
}
