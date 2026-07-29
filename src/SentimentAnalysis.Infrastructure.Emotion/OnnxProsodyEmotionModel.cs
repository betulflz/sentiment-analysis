using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>audeering wav2vec2 boyutsal SER'i ONNX Runtime ile çalıştırır. Session açılışta
/// bir kez yüklenir, sıcak tutulur (singleton). Girdi 16 kHz mono float; zero-mean/unit-var
/// standardize edilir. Çıktı logits=[arousal,dominance,valence] → ProsodyEmotion.</summary>
public sealed class OnnxProsodyEmotionModel : IProsodyEmotionModel, IDisposable
{
    private const string InputName = "signal";
    private const string OutputName = "logits";

    private readonly EmotionConfig _cfg;
    private readonly IEmotionModelProvider _models;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private InferenceSession? _session;
    private bool _disposed;

    public OnnxProsodyEmotionModel(AppConfig config, IEmotionModelProvider models)
    {
        _cfg = config.Emotion;
        _models = models;
    }

    public async Task<ProsodyEmotion> AnalyzeAsync(AudioChunk chunk, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (chunk.SampleRate != 16000)
            throw new ArgumentException(
                $"Prozodi modeli 16 kHz mono bekler; gelen {chunk.SampleRate} Hz.", nameof(chunk));

        var session = await EnsureSessionAsync(ct).ConfigureAwait(false);
        var normalized = AudioNormalizer.Standardize(chunk.Samples);

        await _runLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var input = new DenseTensor<float>(normalized, new[] { 1, normalized.Length });
            var inputs = new[] { NamedOnnxValue.CreateFromTensor(InputName, input) };
            using var results = session.Run(inputs);
            var logits = results.First(r => r.Name == OutputName).AsTensor<float>().ToArray();
            return ProsodyOutputMapper.Map(logits, _cfg.ProsodyValenceGain, _cfg.ProsodyArousalGain);
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task<InferenceSession> EnsureSessionAsync(CancellationToken ct)
    {
        if (_session is not null) return _session;
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_session is null)
            {
                var modelPath = _models.ResolveModelPath(_cfg.ProsodyModelFile);
                using var options = new SessionOptions();
                if (string.Equals(_cfg.ExecutionProvider, "coreml", StringComparison.OrdinalIgnoreCase))
                    TryAppendCoreML(options);
                _session = new InferenceSession(modelPath, options);
            }
            return _session;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>CoreML EP'yi ekler; bu ORT sürümünde/makinede yoksa sessizce CPU'ya düşer
    /// (correctness CPU'da da sağlanır, CoreML yalnızca hızlandırma).</summary>
    private static void TryAppendCoreML(SessionOptions options)
    {
        try { options.AppendExecutionProvider_CoreML(); }
        catch { /* CoreML yoksa CPU fallback */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Devam eden Run/Init tamamlanana kadar bekle; native session'ı çalışırken serbest bırakma.
        _initLock.Wait();
        _runLock.Wait();
        try
        {
            _session?.Dispose();
            _session = null;
        }
        finally
        {
            _runLock.Release();
            _initLock.Release();
        }
        _initLock.Dispose();
        _runLock.Dispose();
    }
}
