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
    private readonly ILiveRecordingSession? _recordingSession;

    public PipelineAnalysisRunner(EmotionPipeline pipeline, Func<IAudioCaptureSource?> sourceFactory,
                                  AppConfig config, ICaptureModeState? captureMode = null,
                                  ISessionAudioSink? audioSink = null,
                                  IDiarizationState? diarization = null,
                                  ILiveRecordingSession? recordingSession = null)
    {
        _diarization = diarization;
        _pipeline = pipeline;
        _sourceFactory = sourceFactory;
        _config = config;
        _captureMode = captureMode;
        _audioSink = audioSink;   // additive (10b): yoksa ses saklanmaz, davranış eskisi gibi
        _recordingSession = recordingSession;
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
        if (_captureMode?.Mode == CaptureMode.Mikrofon)
        {
            var session = _recordingSession ?? new LiveRecordingSession(new MicrophoneCaptureSource());
            
            // 1. Start live recording
            await session.StartAsync(ct).ConfigureAwait(false);

            // 2. Wait until recording is cancelled via Stop button (ct is cancelled)
            try
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var reg = ct.Register(() => tcs.TrySetResult());
                await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }

            // 3. Stop recording and finalize WAV file
            var recordResult = await session.StopAsync(CancellationToken.None).ConfigureAwait(false);

            // 4. Validate WAV file (duration and peak/silence)
            // Minimum sample count check (16000 * 1 = 16000 samples -> 32000 bytes)
            if (recordResult.SampleCount < 16000)
            {
                if (File.Exists(recordResult.WavPath))
                {
                    try { File.Delete(recordResult.WavPath); } catch { }
                }
                throw new InvalidOperationException("Analiz için en az 1 saniyelik ses kaydı gereklidir.");
            }

            if (recordResult.PeakAmplitude <= 0.001)
            {
                if (File.Exists(recordResult.WavPath))
                {
                    try { File.Delete(recordResult.WavPath); } catch { }
                }
                throw new InvalidOperationException("Kayıtta analiz edilebilecek bir ses algılanamadı.");
            }

            // 5. Run the existing file analysis pipeline on this WAV file
            var fileSource = new FileCaptureSource(recordResult.WavPath);
            var fileOptions = new CaptureOptions(ChunkLength: TimeSpan.FromSeconds(30), Overlap: TimeSpan.FromSeconds(5));
            var language = ParseLanguage(_config.Stt.Language);
            var sessionId = Guid.NewGuid();
            var startedAt = DateTimeOffset.Now;

            var uiThreadProgress = new UiThreadProgress<ChunkResult>(progress);

            LastSessionId = sessionId;
            AudioLimitReached = false;

            try
            {
                // Run using CancellationToken.None so that the analysis itself is NOT cancelled by the Stop click
                var summary = await _pipeline.RunAsync(fileSource, fileOptions, sessionId, startedAt, language, uiThreadProgress, CancellationToken.None)
                    .ConfigureAwait(false);

                // Additive audio sink logging if enabled
                if (_diarization?.Enabled == true && _audioSink is not null)
                {
                    var pcm = File.ReadAllBytes(recordResult.WavPath);
                    if (pcm.Length > 44)
                    {
                        var rawPcm = new byte[pcm.Length - 44];
                        Array.Copy(pcm, 44, rawPcm, 0, rawPcm.Length);
                        await _audioSink.SaveSessionAudioAsync(sessionId, rawPcm, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                
                return summary;
            }
            finally
            {
                if (File.Exists(recordResult.WavPath))
                {
                    try { File.Delete(recordResult.WavPath); } catch { }
                }
            }
        }
        else
        {
            var source = _sourceFactory()
                ?? throw new InvalidOperationException(
                    "Analiz için bir ses kaynağı seçilmedi. Dosya modunda bir ses dosyası seçin " +
                    "veya mikrofon modunu açın.");

            try
            {
                // Canlı mikrofonda kısa parça = hızlı geri bildirim; dosyada uzun parça = daha iyi bağlam.
                var options = new CaptureOptions(ChunkLength: TimeSpan.FromSeconds(30), Overlap: TimeSpan.FromSeconds(5));
                var language = ParseLanguage(_config.Stt.Language);
                var sessionId = Guid.NewGuid();
                var startedAt = DateTimeOffset.Now;

                var uiThreadProgress = new UiThreadProgress<ChunkResult>(progress);

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

                if (recording is not null)
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
                (source as IDisposable)?.Dispose();
            }
        }
    }

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
