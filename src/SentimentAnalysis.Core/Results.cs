using System.Collections.Generic;

namespace SentimentAnalysis.Core;

/// <summary>Füzyon sonucu (chunk düzeyi). Her iki modelin ham VA'sı ve güveni saydamlık için
/// taşınır — UI bunları "Metin Analizi" ve "Ses Analizi" sütunlarında ayrı ayrı gösterir.</summary>
public sealed record FusedResult(
    ValenceArousal Combined,
    Emotion PrimaryEmotion,
    Emotion SecondaryEmotion,
    Confidence Confidence,
    bool Agreement,
    bool Incongruent,
    IncongruencePattern Pattern,
    double Congruence,
    ValenceArousal TextVa,
    ValenceArousal ProsodyVa,
    double TextConfidence,
    double ProsodyConfidence,
    // Girdi KALİTESİNDEN türeyen 0..1 güvenilirlikler (füzyon ağırlıklarını modüle edenler).
    // Model güveninden (TextConfidence/ProsodyConfidence) farklıdır: onlar "model ne kadar
    // emin", bunlar "girdi ne kadar analiz edilebilir". "Neden Nötr çıktı?" sorusunun cevabı
    // çoğu zaman burada olduğu için sonuçla birlikte dışarı taşınır. Additive: varsayılan 1,0.
    double AudioReliability = 1.0,
    double TextReliability = 1.0,
    // Üç kaynaklı füzyon (11d, spec 11 §3.2): yüz eklendiyse Combined/PrimaryEmotion üç kaynaklıdır;
    // SpeechVa yüzden ÖNCEKİ (metin+ses) sonuçtur. Karar göstergeleri yüzü kendileri kattığından
    // SpeechValence'ı kullanır (çift sayım olmasın). Additive: yüz yoksa null / 0.
    ValenceArousal? SpeechVa = null,
    double? FaceValence = null,
    double FaceWeight = 0.0)
{
    /// <summary>Yalnızca metin + ses valence'ı (yüz eklenmeden önce).</summary>
    public double SpeechValence => (SpeechVa ?? Combined).Valence;

    /// <summary>Bu chunk "işaretli/şüpheli" mi? Söz-ton uyumsuzluğu eşiği aşıldıysa
    /// (<see cref="Incongruent"/>) VEYA adı konmuş bir örüntü varsa (<see cref="Pattern"/> ≠ Yok,
    /// ör. eşik-altı ironi) true. Aşağı akış (UI/depolama) "şüpheli chunk" filtrelemesini TEK
    /// noktadan bununla yapmalı — yalnızca <see cref="Incongruent"/>'a bakmak eşik-altı ironiyi
    /// kaçırır (master §5.1: ironi "bayraklı" bir sinyaldir).</summary>
    public bool Flagged => Incongruent || Pattern != IncongruencePattern.Yok;
}

/// <summary>Bir chunk'ın tam sonucu (zaman çizelgesi öğesi).
/// <para><paramref name="Segments"/> MUTLAK zamanlıdır (chunk başlangıcı eklenmiş) — STT
/// motoru segmentleri chunk'a göreli üretir, çevirmeyi <c>ChunkProcessor</c> yapar. Konuşmacı
/// hizalaması (alt-proje 10b) bu mutlak zamanlara dayanır; göreli zaman sızarsa hizalama
/// sessizce yanlış konuşmacıya metin atar. Additive: varsayılan boş liste.</para></summary>
public sealed record ChunkResult(
    TimeSpan TimeStart,
    TimeSpan TimeEnd,
    FusedResult Fused,
    string Transcript,
    IReadOnlyList<TranscriptSegment>? Segments = null)
{
    public IReadOnlyList<TranscriptSegment> Segments { get; init; } = Segments ?? Array.Empty<TranscriptSegment>();
}

/// <summary>Bir toplantı/oturum: chunk listesi + üst-veriler. Toplantı-düzeyi
/// agregasyon (baskın duygu, trend, oynaklık, tepe anlar) Füzyon motorunda
/// (Alt-proje 02) hesaplanır; burada ham veri tutulur.</summary>
public sealed record MeetingSession(
    Guid Id,
    DateTimeOffset StartedAt,
    DetectedLanguage Language,
    IReadOnlyList<ChunkResult> Chunks);
