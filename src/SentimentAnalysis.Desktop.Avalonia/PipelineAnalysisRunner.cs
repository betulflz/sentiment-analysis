using Avalonia.Threading;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Application.Pipeline;
using SentimentAnalysis.Application.Vision;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using SentimentAnalysis.Infrastructure.Audio;
using SentimentAnalysis.Infrastructure.Vision;

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
    private readonly Func<IVideoCaptureSource?>? _videoFactory;
    private readonly Func<VisionAnalyzer>? _visionFactory;
    private VisionAnalyzer? _vision;
    private readonly IRoleLabeler? _roles;
    private volatile VisionAnalyzer? _activeVision;

    public PipelineAnalysisRunner(EmotionPipeline pipeline, Func<IAudioCaptureSource?> sourceFactory,
                                  AppConfig config, ICaptureModeState? captureMode = null,
                                  ISessionAudioSink? audioSink = null,
                                  IDiarizationState? diarization = null,
                                  Func<IVideoCaptureSource?>? videoFactory = null,
                                  Func<VisionAnalyzer>? visionFactory = null,
                                  IRoleLabeler? roles = null)
    {
        _roles = roles;
        // 11b: görüntü kanalı additive — fabrika yoksa ya da null dönerse (ses dosyası, mikrofon) davranış değişmez.
        _videoFactory = videoFactory;
        _visionFactory = visionFactory;
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

    /// <summary>Önizleme (11d): etkin görüntü analizcisinin son karesi; koşu bitince analizci bunu sıfırlar.</summary>
    public VideoFrame? LatestFrame => _activeVision?.LatestFrame;

    public IReadOnlyList<(int Index, FaceBox Box)> LatestFaces =>
        _activeVision?.LatestFaces ?? Array.Empty<(int, FaceBox)>();

    /// <summary>Chunk aralığındaki yüz özeti; görüntü kanalı olmayan koşuda boş.</summary>
    public IReadOnlyList<FaceWindowSummary> FacesFor(ChunkResult r) =>
        _activeVision?.Timeline.Summarize(r.TimeStart, r.TimeEnd) ?? Array.Empty<FaceWindowSummary>();

    public async Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct)
    {
        var source = _sourceFactory()
            ?? throw new InvalidOperationException(
                "Analiz için bir ses kaynağı seçilmedi. Dosya modunda bir ses dosyası seçin " +
                "veya mikrofon modunu açın.");

        // Görüntü kanalı (11b): video dosyası ya da kamera modunda ses hattıyla birlikte koşar.
        // Görüntü hatası analizi DURDURMAZ — uyarı olarak gösterilir, ses analizi sürer.
        var warnings = progress as ICaptureWarnings;
        void Warn(string message)
        {
            if (warnings is not null) Dispatcher.UIThread.Post(() => warnings.Warn(message));
        }

        _activeVision = null;
        using var visionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var visionTask = Task.CompletedTask;
        var video = _videoFactory?.Invoke();
        if (video is not null)
        {
            if (_captureMode?.Mode == CaptureMode.Kamera) video = new ClockedVideoSource(video);
            try
            {
                var vision = _vision ??= _visionFactory?.Invoke()
                    ?? throw new VideoSourceException("Görüntü analizi yapılandırılmamış.");
                _activeVision = vision;
                visionTask = Task.Run(async () =>
                {
                    try { await vision.RunAsync(video, visionCts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { Warn($"Görüntü analizi durdu: {ex.Message}"); }
                }, CancellationToken.None);
            }
            catch (VideoSourceException ex)
            {
                Warn($"Görüntü analizi başlatılamadı: {ex.Message}");
            }
            // Videonun sesi yoksa (ya da ses okunamazsa) ses hattı hemen biter; görüntü yine de
            // video boyunca sürsün diye ses kaynağı, görüntü bitene kadar "sessizce" bekletilir.
            source = new VisionTolerantAudioSource(source, visionTask, Warn);
        }

        try
        {
            // Canlı mikrofonda kısa parça = hızlı geri bildirim; dosyada uzun parça = daha iyi bağlam.
            // Video/kamera da canlı akış gibi davranır (gerçek hızda).
            var options = _captureMode?.Mode is CaptureMode.Mikrofon or CaptureMode.Kamera || video is not null
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

            // 11d: üç kaynaklı duygu — chunk, kaydedilmeden önce aynı aralığın yüz özetiyle birleştirilir.
            ChunkResult WithFace(ChunkResult r) => _activeVision is { } v
                ? r with { Fused = FaceFusion.Apply(r.Fused, v.Timeline.Summarize(r.TimeStart, r.TimeEnd), _config.Fusion) }
                : r;

            var summary = await _pipeline.RunAsync(pipelineSource, options, sessionId, startedAt, language, uiThreadProgress, ct,
                    enrich: WithFace,
                    // 12: rol ayrımı yalnız canlı modlarda — ses dosyasında tanıtılan kişi kayıtta olmayabilir.
                    roles: _captureMode?.Mode is CaptureMode.Mikrofon or CaptureMode.Kamera ? _roles : null)
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
            // Ses hattı bitti ya da "Durdur": görüntü görevini kapat ve bekle (ffmpeg süreci öldürülür).
            visionCts.Cancel();
            await visionTask.ConfigureAwait(false);

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

    /// <summary>Kamera karelerini oturum başından geçen GERÇEK süreyle damgalar. ffmpeg'in kare sırası
    /// kamera ısınmasını (4–7 sn, 11a §3) saymaz; mikrofon chunk'ları ise ilk andan itibaren sayılır —
    /// sıra-tabanlı zaman yüz notlarını birkaç saniye kaydırırdı.</summary>
    private sealed class ClockedVideoSource(IVideoCaptureSource inner) : IVideoCaptureSource
    {
        public async IAsyncEnumerable<VideoFrame> CaptureAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await foreach (var f in inner.CaptureAsync(ct).ConfigureAwait(false))
                yield return f with { Time = clock.Elapsed };
        }
    }

    /// <summary>Video dosyasının ses kaynağı hata verirse (ör. "Bu videoda ses yok") hatayı uyarıya
    /// çevirir ve görüntü bitene kadar akışı açık tutar — görüntü analizi video boyunca sürer.</summary>
    private sealed class VisionTolerantAudioSource(IAudioCaptureSource inner, Task vision, Action<string> warn)
        : IAudioCaptureSource, IDisposable
    {
        public async IAsyncEnumerable<AudioChunk> CaptureAsync(CaptureOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var e = inner.CaptureAsync(options, ct).GetAsyncEnumerator(ct);
            try
            {
                while (true)
                {
                    bool more;
                    try { more = await e.MoveNextAsync().ConfigureAwait(false); }
                    catch (VideoSourceException ex)
                    {
                        warn(ex.Message);
                        try { await vision.WaitAsync(ct).ConfigureAwait(false); } catch (OperationCanceledException) { }
                        yield break;
                    }
                    if (!more) yield break;
                    yield return e.Current;
                }
            }
            finally
            {
                await e.DisposeAsync().ConfigureAwait(false);
            }
        }

        public void Dispose() => (inner as IDisposable)?.Dispose();
    }

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
