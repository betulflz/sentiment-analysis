using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using Whisper.net;

namespace SentimentAnalysis.Infrastructure.Stt;

/// <summary>Whisper.net (whisper.cpp) tabanlı STT. Model açılışta bir kez yüklenir, sıcak tutulur
/// (singleton). Girdi 16 kHz mono float PCM olmalıdır. İşlemler seri (bounded pipeline tek tüketici,
/// Alt-proje 06) — <see cref="_processLock"/> ile korunur.</summary>
public sealed class WhisperSpeechToText : ISpeechToText, IDisposable
{
    private readonly SttConfig _cfg;
    private readonly ISttModelProvider _models;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _processLock = new(1, 1);
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private bool _disposed;

    public WhisperSpeechToText(AppConfig config, ISttModelProvider models)
    {
        _cfg = config.Stt;
        _models = models;
    }

    public async Task<Transcript> TranscribeAsync(AudioChunk chunk, CancellationToken ct)
    {
        if (chunk.SampleRate != 16000)
            throw new ArgumentException(
                $"Whisper 16 kHz mono bekler; gelen {chunk.SampleRate} Hz. Yeniden örnekleme " +
                "yakalama katmanının (Alt-proje 05) işidir.", nameof(chunk));

        var processor = await EnsureProcessorAsync(ct).ConfigureAwait(false);

        await _processLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var segments = new List<SttSegment>();
            var samples = new ReadOnlyMemory<float>(chunk.Samples);
            await foreach (var seg in processor.ProcessAsync(samples, ct).ConfigureAwait(false))
                segments.Add(new SttSegment(seg.Text, seg.Start, seg.End, seg.Probability, seg.Language));

            var pinned = _cfg.WhisperLanguage;
            DetectedLanguage? forced = pinned is null ? null : LanguageMap.FromIso(pinned);
            return TranscriptAssembler.Assemble(segments, forced);
        }
        finally
        {
            _processLock.Release();
        }
    }

    private async Task<WhisperProcessor> EnsureProcessorAsync(CancellationToken ct)
    {
        if (_processor is not null) return _processor;
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_processor is null)
            {
                var modelPath = _models.ResolveModelPath(_cfg.LiveModel);
                _factory = WhisperFactory.FromPath(modelPath);
                var builder = _factory.CreateBuilder()
                    .WithThreads(_cfg.Threads)
                    // ZORUNLU: bu olmadan SegmentData.Probability her zaman 0 döner.
                    // Sonuç sessiz ve yıkıcıydı: TranscriptAssembler avg-prob 0 görüp
                    // avg-logprob'u tabana (log 1e-6 ≈ −13.8) kırpıyor, QualityWeighting
                    // metin güvenilirliğini 0 hesaplıyor ve füzyonda METİN KANALI HİÇ OY
                    // KULLANMIYORDU — sistem "iki kanallı" görünürken tek kanallı çalışıyordu.
                    // Ayrıca rAudio*rText = 0 olduğu için güven düzeyi asla "Yüksek" olamıyordu.
                    .WithProbabilities();

                var pinned = _cfg.WhisperLanguage;
                builder = pinned is null
                    ? builder.WithLanguageDetection()
                    : builder.WithLanguage(pinned);
                _processor = builder.Build();
            }
            return _processor;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _processor?.Dispose();
        _factory?.Dispose();
        _initLock.Dispose();
        _processLock.Dispose();
    }
}
