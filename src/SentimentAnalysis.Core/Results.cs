using System.Collections.Generic;

namespace SentimentAnalysis.Core;

/// <summary>Füzyon sonucu (chunk düzeyi). İki ham valence de saydamlık için taşınır.</summary>
public sealed record FusedResult(
    ValenceArousal Combined,
    Emotion PrimaryEmotion,
    Emotion SecondaryEmotion,
    Confidence Confidence,
    bool Agreement,
    bool Incongruent,
    IncongruencePattern Pattern,
    double Congruence,
    double TextValence,
    double AudioValence)
{
    /// <summary>Bu chunk "işaretli/şüpheli" mi? Söz-ton uyumsuzluğu eşiği aşıldıysa
    /// (<see cref="Incongruent"/>) VEYA adı konmuş bir örüntü varsa (<see cref="Pattern"/> ≠ Yok,
    /// ör. eşik-altı ironi) true. Aşağı akış (UI/depolama) "şüpheli chunk" filtrelemesini TEK
    /// noktadan bununla yapmalı — yalnızca <see cref="Incongruent"/>'a bakmak eşik-altı ironiyi
    /// kaçırır (master §5.1: ironi "bayraklı" bir sinyaldir).</summary>
    public bool Flagged => Incongruent || Pattern != IncongruencePattern.Yok;
}

/// <summary>Bir chunk'ın tam sonucu (zaman çizelgesi öğesi).</summary>
public sealed record ChunkResult(
    TimeSpan TimeStart,
    TimeSpan TimeEnd,
    FusedResult Fused,
    string Transcript);

/// <summary>Bir toplantı/oturum: chunk listesi + üst-veriler. Toplantı-düzeyi
/// agregasyon (baskın duygu, trend, oynaklık, tepe anlar) Füzyon motorunda
/// (Alt-proje 02) hesaplanır; burada ham veri tutulur.</summary>
public sealed record MeetingSession(
    Guid Id,
    DateTimeOffset StartedAt,
    DetectedLanguage Language,
    IReadOnlyList<ChunkResult> Chunks);
