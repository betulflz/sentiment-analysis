using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Infrastructure.Diarization;

/// <summary>sherpa-onnx <c>SpeakerEmbeddingExtractor</c> sarmalayıcısı (12). Model ilk çağrıda yüklenir ve sıcak
/// tutulur; native tipler bu sınıfta kalır.
/// <para><b>KRİTİK:</b> sherpa-onnx model dosyası yoksa istisna atmaz, SÜRECİ SONLANDIRIR (10b'de ölçüldü). Bu yüzden
/// dosya varlığı motora girmeden önce sağlayıcıda doğrulanır (bkz. <see cref="SherpaDiarizer"/>).</para></summary>
public sealed class SherpaSpeakerEmbedder : ISpeakerEmbedder, IDisposable
{
    private const int SampleRate = 16000;
    private readonly ISpeakerRoleModelProvider _models;
    private readonly object _sync = new();
    private SherpaOnnx.SpeakerEmbeddingExtractor? _extractor;
    private bool _disposed;

    public SherpaSpeakerEmbedder(ISpeakerRoleModelProvider models) => _models = models;

    public float[] Embed(float[] mono16k)
    {
        ArgumentNullException.ThrowIfNull(mono16k);
        if (mono16k.Length == 0) throw new ArgumentException("Boş ses parçasından ses imzası çıkarılamaz.", nameof(mono16k));
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _extractor ??= Create();
            using var stream = _extractor.CreateStream();
            stream.AcceptWaveform(SampleRate, mono16k);
            stream.InputFinished();
            return _extractor.Compute(stream);
        }
    }

    private SherpaOnnx.SpeakerEmbeddingExtractor Create()
    {
        var path = _models.RoleEmbeddingModelPath;   // yoksa/yanlışsa burada yönetilen istisna atar
        if (!File.Exists(path))
            throw new FileNotFoundException($"Konuşmacı rolü modeli bulunamadı: {path}", path);
        return new SherpaOnnx.SpeakerEmbeddingExtractor(
            new SherpaOnnx.SpeakerEmbeddingExtractorConfig { Model = path, NumThreads = 2 });
    }

    public void Dispose()
    {
        lock (_sync) { _disposed = true; _extractor = null; }
    }
}
