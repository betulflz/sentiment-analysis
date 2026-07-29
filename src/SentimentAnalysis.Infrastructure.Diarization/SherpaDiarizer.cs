using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Diarization;

/// <summary>sherpa-onnx offline speaker diarization sarmalayıcısı (alt-proje 10b). Native tipler
/// bu sınıfın içinde kalır (mimari kural: motor sınırı). Model ilk kullanımda yüklenir ve sıcak
/// tutulur.
/// <para><b>Ölçülmüş ayarlar:</b> embedding modeli <c>wespeaker_en_voxceleb_resnet34_LM</c> ile
/// <c>Threshold = 0.5</c> — iki kişilik gerçek bir kayıtta 0,3–0,5 aralığı tam 2 küme buluyor,
/// 0,6 ve üstü tek kümeye çöküyor. (Önceki 3dspeaker/zh-cn embedding modeliyle ölçülen 0,8
/// bu embedding uzayına taşınmaz — eşik, kullanılan embedding modeline özgüdür.)
/// <c>NumClusters = 0</c>: gerçek toplantıda kaç kişi olduğunu bilmeyiz, küme sayısı eşikle bulunur.</para>
/// <para><b>Hız:</b> 10a'da RTF ≈ 0,44 ölçüldü (120 sn ses → ~54 sn işlem). Bu yüzden canlı
/// hatta değil, oturum sonrası arka plan geçişinde çalışır.</para></summary>
public sealed class SherpaDiarizer : IDiarizer, IDisposable
{
    private readonly DiarizationConfig _cfg;
    private readonly IDiarizationModelProvider _models;
    private readonly object _sync = new();
    private SherpaOnnx.OfflineSpeakerDiarization? _sd;
    private bool _disposed;

    public SherpaDiarizer(AppConfig config, IDiarizationModelProvider models)
    {
        _cfg = config.Diarization;
        _models = models;
    }

    public Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(float[] mono16k, CancellationToken ct)
    {
        // Boş ses: modeli hiç yükleme. Modelsiz ortamda da çalışır ve gereksiz ~700 MB
        // yükleme maliyeti ödenmez.
        if (mono16k.Length == 0)
            return Task.FromResult<IReadOnlyList<SpeakerSegment>>(Array.Empty<SpeakerSegment>());

        ct.ThrowIfCancellationRequested();

        // sherpa API'si senkron ve uzun sürer (RTF ~0,44) — havuz iş parçacığına alınır ki
        // çağıran (arka plan işi) bloklanmasın. Kilit, tek modelin sıcak tutulmasını korur.
        return Task.Run<IReadOnlyList<SpeakerSegment>>(() =>
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _sd ??= Create();

                var raw = _sd.Process(mono16k);
                var list = new List<SpeakerSegment>(raw.Length);
                foreach (var s in raw)
                    list.Add(new SpeakerSegment(
                        new SpeakerId(s.Speaker),
                        TimeSpan.FromSeconds(s.Start),
                        TimeSpan.FromSeconds(s.End)));
                return list;
            }
        }, ct);
    }

    private SherpaOnnx.OfflineSpeakerDiarization Create()
    {
        var segmentation = _models.SegmentationModelPath;
        var embedding = _models.EmbeddingModelPath;

        // KRİTİK (uygulama sırasında ölçüldü): sherpa-onnx, model dosyası bulunamadığında
        // istisna FIRLATMAZ — hata basıp SÜRECİ SONLANDIRIR (native abort). Test host'unu
        // çökerttiği için fark edildi. Üretimde bu, uygulamanın sessizce kapanması demektir
        // ve çağırandaki try/catch bunu YAKALAYAMAZ. Bu yüzden dosya varlığı motora girmeden
        // ÖNCE burada doğrulanır — sağlayıcının kendi kontrolüne ek bir savunma katmanıdır
        // (dosya, çözümle-kullan arasında silinmiş de olabilir).
        RequireFile(segmentation, "pyannote segmentation modeli");
        RequireFile(embedding, "konuşmacı embedding modeli");

        var cfg = new SherpaOnnx.OfflineSpeakerDiarizationConfig();
        cfg.Segmentation.Pyannote.Model = segmentation;
        cfg.Embedding.Model = embedding;
        cfg.Clustering.NumClusters = 0;                            // bilinmiyor → eşikle bul
        cfg.Clustering.Threshold = (float)_cfg.ClusteringThreshold;
        return new SherpaOnnx.OfflineSpeakerDiarization(cfg);
    }

    private static void RequireFile(string path, string what)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"{what} bulunamadı: {path}. Konuşmacı ayrımı çalıştırılamaz.", path);
    }

    /// <summary>Modelin beklediği örnekleme hızı. Girdi bununla eşleşmiyorsa sonuç anlamsız
    /// olur — çağıran kontrol etmelidir.</summary>
    public int SampleRate
    {
        get
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _sd ??= Create();
                return _sd.SampleRate;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync) { _disposed = true; _sd = null; }
    }
}
