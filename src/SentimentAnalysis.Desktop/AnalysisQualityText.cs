using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

internal static class AnalysisQualityText
{
    public static string Format(AnalysisDiagnostics? diagnostics)
    {
        if (diagnostics is null) return "Bu raporda işlenen girdi kapsamı ölçülmemiş.";
        var parts = new List<string>
        {
            diagnostics.CoverageRatio is double ratio
                ? $"İşlenen girdi: %{Math.Round(ratio * 100)} (konuşma bulunmayan bölümler dahil)."
                : "Değerlendirilecek girdi alınamadı."
        };
        void Count(ChunkDisposition disposition, string label)
        {
            int count = diagnostics.Chunks.Count(c => c.Disposition == disposition);
            if (count > 0) parts.Add($"{label}: {count}.");
        }
        Count(ChunkDisposition.NoSpeech, "Konuşma bulunmayan parça");
        Count(ChunkDisposition.ProcessingFailed, "İşleme hatası");
        Count(ChunkDisposition.QueueDropped, "Kuyrukta atlanan parça");
        Count(ChunkDisposition.DrainSkipped, "Durdurma sonrası atlanan parça");
        Count(ChunkDisposition.Cancelled, "İşlenirken iptal edilen parça");
        Count(ChunkDisposition.Unprocessed, "Diğer işlenemeyen parça");
        int enrich = diagnostics.Chunks.Count(c => c.EnrichmentFailureType is not null);
        if (enrich > 0) parts.Add($"Görüntü katkısı eklenemeyen parça: {enrich}.");
        if (diagnostics.SourceFailureType is not null) parts.Add("Ses kaynağında hata oluştu.");
        if (diagnostics.StorageFailureType is not null) parts.Add("Analiz kaydında hata oluştu.");
        parts.Add("Bu oran duygu doğruluğunu göstermez.");
        return string.Join(" ", parts);
    }
}
