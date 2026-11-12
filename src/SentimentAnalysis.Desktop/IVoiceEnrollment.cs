using SentimentAnalysis.Application.Diarization;

namespace SentimentAnalysis.Desktop;

/// <summary>"Sesimi tanıt" (12): görüşmeyi yöneten kişinin kısa tanıtma kaydı. Canlı "Yöneten / Karşı taraf" ayrımı
/// bu kayda dayanır; kayıt yoksa ayrım yapılmaz. Ses imzası yalnız bellekte tutulur.</summary>
public interface IVoiceEnrollment
{
    bool IsEnrolled { get; }

    /// <summary>Kayıt süresi (sn) — arayüz geri sayım gösterir.</summary>
    double RecordSeconds { get; }

    /// <summary>Mikrofondan kayıt alıp ses imzasını çıkarır. Hata fırlatmaz: sonucu/nedeni döndürür.</summary>
    Task<EnrollmentResult> RecordAsync(CancellationToken ct);

    /// <summary>Ses imzasını bellekten siler; sonraki görüşmede rol ayrımı yapılmaz.</summary>
    void Clear();
}
