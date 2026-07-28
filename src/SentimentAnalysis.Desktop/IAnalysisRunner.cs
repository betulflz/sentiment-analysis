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
}
