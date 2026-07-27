using System.Collections.Generic;

namespace SentimentAnalysis.Core;

/// <summary>Füzyon sonucu (chunk düzeyi). İki kanalın ham çıktısı ve onlara verilen ağırlıklar
/// saydamlık için taşınır: nihai sayı kadar, o sayının <b>nasıl</b> oluştuğu da raporlanabilir olmalı
/// (spec 08d, Dilim 2 — Metin/Ses/Nihai üçlü görünümü bu alanlarla beslenir).</summary>
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
    double AudioValence,
    // --- Aşağıdakiler eklemeli (varsayılanlı): mevcut çağrı yerleri ve eski kayıtlar bozulmaz. ---
    double TextArousal = 0.5,
    double AudioArousal = 0.5,
    /// <summary>Metin modelinin kendi güveni (entropi tabanlı).</summary>
    double TextConfidence = 0.0,
    /// <summary>Prozodi modelinin kendi güveni (nötr merkeze uzaklık).</summary>
    double ProsodyConfidence = 0.0,
    /// <summary>Füzyonda metin kanalına verilen kalite güvenilirliği (0..1):
    /// kelime sayısı × Whisper avg-logprob. <b>0 ise metin kanalı karara hiç katılmamıştır.</b></summary>
    double TextReliability = 0.0,
    /// <summary>Füzyonda ses kanalına verilen kalite güvenilirliği (0..1): SNR × konuşma oranı × süre.</summary>
    double AudioReliability = 0.0)
{
    /// <summary>Bu chunk "işaretli/şüpheli" mi? Söz-ton uyumsuzluğu eşiği aşıldıysa
    /// (<see cref="Incongruent"/>) VEYA adı konmuş bir örüntü varsa (<see cref="Pattern"/> ≠ Yok,
    /// ör. eşik-altı ironi) true. Aşağı akış (UI/depolama) "şüpheli chunk" filtrelemesini TEK
    /// noktadan bununla yapmalı — yalnızca <see cref="Incongruent"/>'a bakmak eşik-altı ironiyi
    /// kaçırır (master §5.1: ironi "bayraklı" bir sinyaldir).</summary>
    public bool Flagged => Incongruent || Pattern != IncongruencePattern.Yok;

    /// <summary>Metin kanalının ham VA noktası (üçlü görünümün "Metin Analizi" sütunu).</summary>
    public ValenceArousal TextVa => new(TextValence, TextArousal);

    /// <summary>Prozodi kanalının ham VA noktası (üçlü görünümün "Ses Analizi" sütunu).</summary>
    public ValenceArousal ProsodyVa => new(AudioValence, AudioArousal);

    /// <summary>Bir kanal karara hiç katılmadıysa true — "iki kanal da baktı" iddiası
    /// bu durumda dürüst olmaz.</summary>
    public bool TextChannelSilent => TextReliability <= 1e-9;

    /// <inheritdoc cref="TextChannelSilent"/>
    public bool AudioChannelSilent => AudioReliability <= 1e-9;
}

/// <summary>Bir chunk'ın tam sonucu (zaman çizelgesi öğesi).
/// <para><see cref="Language"/> bu chunk'ta Whisper'ın algıladığı dildir. Oturum genelinde tek bir
/// dil varsayılmaz: bir toplantı dil değiştirebilir (kod değiştirme) ve baskın dil
/// <see cref="SessionLanguage.Dominant"/> ile süreye göre türetilir. Varsayılan
/// <see cref="DetectedLanguage.Other"/>'dır; dili taşımayan çağrı yerleri (testler, eski kayıtlar)
/// uydurma bir dil iddia etmez.</para></summary>
public sealed record ChunkResult(
    TimeSpan TimeStart,
    TimeSpan TimeEnd,
    FusedResult Fused,
    string Transcript,
    DetectedLanguage Language = DetectedLanguage.Other);

/// <summary>Bir toplantı/oturum: chunk listesi + üst-veriler. Toplantı-düzeyi
/// agregasyon (baskın duygu, trend, oynaklık, tepe anlar) Füzyon motorunda
/// (Alt-proje 02) hesaplanır; burada ham veri tutulur.</summary>
public sealed record MeetingSession(
    Guid Id,
    DateTimeOffset StartedAt,
    DetectedLanguage Language,
    IReadOnlyList<ChunkResult> Chunks);
