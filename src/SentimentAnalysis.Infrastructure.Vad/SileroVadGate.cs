using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Vad;

/// <summary>Silero VAD v5 ONNX ile chunk-düzeyi konuşma kapısı. Session açılışta bir kez yüklenir,
/// sıcak tutulur (singleton). Chunk (16 kHz mono) → 512-örnek pencereler; her pencereye önceki
/// pencereden 64-örnek <b>context</b> eklenir (Silero v5 gerçek girdisi = 64+512 = 576) → state chunk
/// içinde zincirlenir (her çağrıda sıfırdan) → max pencere prob ≥ eşik ise konuşma. Senkron arayüz
/// (IVadGate) olduğundan tek kilit init+run+dispose'u korur.
/// <para>DÜZELTME (2026-07-26, gerçek konuşma örneğiyle): önceki sürüm context'siz [1,512] besliyordu;
/// bu, gerçek konuşmada bile prob≈0 veriyordu (ilk spike sessizlik/gürültü/ton ile bunu yakalayamadı —
/// hepsi context'siz de ~0 verir). 64-örnek context ile resmi silero-vad paketiyle birebir (max prob 1.0).</para></summary>
public sealed class SileroVadGate : IVadGate, IDisposable
{
    private const string InputName = "input";
    private const string StateName = "state";
    private const string SrName = "sr";
    private const string ProbOutName = "output";
    private const string StateOutName = "stateN";
    private const int StateLen = 2 * 1 * 128;
    private const int ContextSize = 64;                              // Silero v5 16 kHz context
    private const int InputSize = ContextSize + VadWindower.FrameSize; // 64 + 512 = 576

    private readonly VadConfig _cfg;
    private readonly IVadModelProvider _models;
    private readonly object _sync = new();
    private InferenceSession? _session;
    private bool _disposed;

    public SileroVadGate(AppConfig config, IVadModelProvider models)
    {
        _cfg = config.Vad;
        _models = models;
    }

    public bool HasSpeech(AudioChunk chunk)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (chunk.SampleRate != 16000)
            throw new ArgumentException(
                $"Silero VAD 16 kHz mono bekler; gelen {chunk.SampleRate} Hz.", nameof(chunk));

        var frames = VadWindower.ToFrames(chunk.Samples);
        if (frames.Count == 0) return false;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var session = _session ??= new InferenceSession(_models.ResolveModelPath(_cfg.VadModelFile));

            var state = new float[StateLen]; // sıfır → chunk başında reset
            var context = new float[ContextSize]; // sıfır → ilk pencerenin context'i
            var sr = new DenseTensor<long>(new long[] { 16000 }, new[] { 1 });
            double maxProb = 0;

            foreach (var frame in frames)
            {
                // Girdi = önceki 64-örnek context + bu 512-pencere (Silero v5 gerçek girdisi, 576).
                var buffer = new float[InputSize];
                Array.Copy(context, 0, buffer, 0, ContextSize);
                Array.Copy(frame, 0, buffer, ContextSize, VadWindower.FrameSize);
                // Sonraki context = bu pencerenin son 64 örneği.
                Array.Copy(frame, VadWindower.FrameSize - ContextSize, context, 0, ContextSize);

                var input = new DenseTensor<float>(buffer, new[] { 1, InputSize });
                var stateT = new DenseTensor<float>(state, new[] { 2, 1, 128 });
                var feeds = new[]
                {
                    NamedOnnxValue.CreateFromTensor(InputName, input),
                    NamedOnnxValue.CreateFromTensor(StateName, stateT),
                    NamedOnnxValue.CreateFromTensor(SrName, sr),
                };
                using var results = session.Run(feeds);
                float prob = results.First(r => r.Name == ProbOutName).AsTensor<float>().ToArray()[0];
                state = results.First(r => r.Name == StateOutName).AsTensor<float>().ToArray();
                if (prob > maxProb) maxProb = prob;
            }

            return maxProb >= _cfg.SpeechThreshold;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _session?.Dispose();
        }
    }
}
