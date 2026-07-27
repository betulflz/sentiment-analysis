using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>XLM-R çok-dilli 11-duygu sınıflandırıcıyı ONNX Runtime ile çalıştırır. Session + SentencePiece
/// tokenizer açılışta bir kez yüklenir, sıcak tutulur (singleton). Boru: transcript → tokenize (spec §04b) →
/// input_ids/attention_mask (int64) → logits[1,11] → <see cref="TextVaLookup"/> → TextEmotion.</summary>
public sealed class OnnxTextEmotionModel : ITextEmotionModel, IDisposable
{
    private const string InputIdsName = "input_ids";
    private const string AttentionMaskName = "attention_mask";
    private const string OutputName = "logits";

    private readonly EmotionConfig _cfg;
    private readonly IEmotionModelProvider _models;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private InferenceSession? _session;
    private XlmrTokenizerAdapter? _tokenizer;
    private bool _disposed;

    public OnnxTextEmotionModel(AppConfig config, IEmotionModelProvider models)
    {
        _cfg = config.Emotion;
        _models = models;
    }

    public async Task<TextEmotion> AnalyzeAsync(Transcript transcript, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        await _runLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Tokenizer + session are shared disposable native state; both must be touched only
            // while holding _runLock, after re-checking _disposed (04a discipline). Encode is fast
            // relative to inference, so serializing it here costs little.
            ObjectDisposedException.ThrowIf(_disposed, this);
            var input = _tokenizer!.Encode(transcript.Text);
            var idTensor = new DenseTensor<long>(input.InputIds, new[] { 1, input.InputIds.Length });
            var maskTensor = new DenseTensor<long>(input.AttentionMask, new[] { 1, input.AttentionMask.Length });
            var inputs = new[]
            {
                NamedOnnxValue.CreateFromTensor(InputIdsName, idTensor),
                NamedOnnxValue.CreateFromTensor(AttentionMaskName, maskTensor),
            };
            using var results = _session!.Run(inputs);
            var logits = results.First(r => r.Name == OutputName).AsTensor<float>().ToArray();
            return TextVaLookup.Map(logits, XlmrEmotionLabels.Ordered);
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_session is not null && _tokenizer is not null) return;
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_tokenizer is null)
            {
                var spmPath = _models.ResolveModelPath(_cfg.TokenizerFile);
                using var spmStream = File.OpenRead(spmPath);
                _tokenizer = new XlmrTokenizerAdapter(spmStream);
            }
            if (_session is null)
            {
                var modelPath = _models.ResolveModelPath(_cfg.TextModelFile);
                using var options = new SessionOptions();
                if (string.Equals(_cfg.ExecutionProvider, "coreml", StringComparison.OrdinalIgnoreCase))
                    TryAppendCoreML(options);
                _session = new InferenceSession(modelPath, options);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>CoreML EP; bu ORT sürümünde/makinede yoksa sessizce CPU'ya düşer (correctness CPU'da da sağlanır).</summary>
    private static void TryAppendCoreML(SessionOptions options)
    {
        try { options.AppendExecutionProvider_CoreML(); }
        catch { /* CoreML yoksa CPU fallback */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Devam eden Init/Run biterken native kaynakları serbest bırak (04a deseni).
        _initLock.Wait();
        try
        {
            _runLock.Wait();
            try
            {
                _session?.Dispose();
                _tokenizer?.Dispose();
            }
            finally { _runLock.Release(); }
        }
        finally
        {
            _initLock.Release();
            _initLock.Dispose();
            _runLock.Dispose();
        }
    }
}
