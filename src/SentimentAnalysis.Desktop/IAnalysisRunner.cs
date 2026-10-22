using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Canlı analiz koşusunu soyutlar (VM `EmotionPipeline`'ı doğrudan bilmez — MVVM sınırı).
/// App katmanı bunu `EmotionPipeline` + seçili `IAudioCaptureSource` üzerine implemente eder.</summary>
public interface IAnalysisRunner
{
    Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct);

    /// <summary>Son koşuda, kanal taşması (overflow) veya "Durdur" sonrası drenaj sınırı/zaman
    /// bütçesi yüzünden İŞLENEMEDEN atlanan chunk sayısı (bkz. <c>EmotionPipeline.DroppedCount</c> +
    /// <c>DrainSkippedCount</c>). Varsayılan uygulama 0 döner — bu, arayüze SONRADAN eklenen
    /// additive bir üye olduğundan mevcut sahte (fake) implementasyonların hiçbirini bozmaz.</summary>
    int SkippedChunkCount => 0;

    /// <summary>Son koşuya ait oturum kimliği — arka plan konuşmacı geçişi (10b) bunu kullanır.
    /// Additive üye: varsayılan <c>null</c> döndüğünden mevcut sahte implementasyonlar bozulmaz
    /// (null = "bu koşunun kalıcı bir oturumu yok", konuşmacı ayrımı denenmez).</summary>
    Guid? LastSessionId => null;

    /// <summary>Modelleri asenkron olarak warm-up eder ve ilk çıkarım gecikmesini önler.
    /// Additive üye: varsayılan olarak no-op döner, sahte implementasyonları bozmaz.</summary>
    Task WarmUpAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Bu chunk'ın zaman aralığında görülen yüzlerin özeti (11b; video/kamera modu).
    /// Additive üye: varsayılan boş — görüntü kanalı olmayan koşularda satırda yüz notu çıkmaz.</summary>
    IReadOnlyList<FaceWindowSummary> FacesFor(ChunkResult r) => Array.Empty<FaceWindowSummary>();

    /// <summary>Önizleme için son alınan kare (11d). Additive: varsayılan <c>null</c> = görüntü yok.
    /// Koşu bitince runner bunu <c>null</c>'a çeker — bitmiş oturumun karesi gösterilmez.</summary>
    VideoFrame? LatestFrame => null;

    /// <summary>Son analiz edilen karedeki yüz kutuları (11d önizlemesi). Additive, varsayılan boş.</summary>
    IReadOnlyList<(int Index, FaceBox Box)> LatestFaces => Array.Empty<(int, FaceBox)>();
}

/// <summary>Koşuyu DURDURMAYAN uyarılar (görüntü kanalı hatası, videoda ses olmaması) için kanal.
/// <see cref="LiveViewModel"/> bunu implemente eder; runner, kendisine verilen progress nesnesi
/// bunu destekliyorsa kullanır (VM ↔ runner DI döngüsü kurmadan).</summary>
public interface ICaptureWarnings
{
    void Warn(string message);
}
