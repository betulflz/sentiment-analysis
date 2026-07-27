using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Abstractions;

/// <summary>Konuşma → metin (Whisper.net; Alt-proje 03).</summary>
public interface ISpeechToText
{
    Task<Transcript> TranscribeAsync(AudioChunk chunk, CancellationToken ct);
}

/// <summary>Metnin duygusu → valence-arousal (ONNX; Alt-proje 04).
/// Dil-başına takılabilir (çok-dilli-hazır).</summary>
public interface ITextEmotionModel
{
    Task<TextEmotion> AnalyzeAsync(Transcript transcript, CancellationToken ct);
}

/// <summary>Sesin prozodisi → valence-arousal (VAD-regresyon ONNX; Alt-proje 04).</summary>
public interface IProsodyEmotionModel
{
    Task<ProsodyEmotion> AnalyzeAsync(AudioChunk chunk, CancellationToken ct);
}

/// <summary>İki modeli birleştiren saf, deterministik füzyon (Alt-proje 02).</summary>
public interface IEmotionFusion
{
    FusedResult Fuse(TextEmotion text, ProsodyEmotion prosody, QualitySignals quality);
}

/// <summary>Canlı sistem-sesi / dosya yakalama (Alt-proje 05).
/// <para><b>İptal sözleşmesi</b> (08e düzeltme turu Fix 5): implementasyonlar <paramref name="ct"/>
/// iptal olduğu AN kısa sürede dönmelidir. İki kabul edilebilir yol vardır: (1) enumerasyonu
/// normal bitirmek — istenirse tamponda kalan artığı (tam olmayan son bir chunk) önce yield edip
/// sonra bitmek (bkz. <c>MicrophoneCaptureSource</c>: iptali kendisi yutar, tamponu drene eder);
/// veya (2) <see cref="OperationCanceledException"/> fırlatmak (bkz. <c>FileCaptureSource</c>).
/// Ne yapılırsa yapılsın, SONSUZA DEK BLOKLANMAMALIDIR — <see cref="SentimentAnalysis.Application.Pipeline.EmotionPipeline"/>
/// üreticiyi (<see cref="CaptureAsync"/> görevini) "Durdur" akışının bir parçası olarak sonuna
/// kadar (`finally` içinde, zaman aşımı olmadan) bekler; kaynak burada takılırsa iptal bile
/// hattı artık kıramaz ve uygulama sonsuza dek "Durdur"da asılı kalır. Gelecekte eklenecek bir
/// implementasyon (ör. Windows/WASAPI) bu kuralı bozarsa hiçbir test bunu yakalamaz — bu yüzden
/// sözleşme burada, arayüzün kendisinde kayıtlıdır.</para></summary>
public interface IAudioCaptureSource
{
    IAsyncEnumerable<AudioChunk> CaptureAsync(CaptureOptions options, CancellationToken ct);
}

/// <summary>Yakalama seçenekleri.</summary>
public sealed record CaptureOptions(TimeSpan ChunkLength, TimeSpan Overlap);

/// <summary>Sessiz chunk'ları eleyen VAD kapısı (Silero; Alt-proje 05).</summary>
public interface IVadGate
{
    bool HasSpeech(AudioChunk chunk);
}

/// <summary>Oturum kalıcılığı ve saklama politikası (Alt-proje 07).</summary>
public interface ISessionStore
{
    Task SaveChunkAsync(Guid sessionId, ChunkResult result, CancellationToken ct);
    Task<MeetingSession?> GetSessionAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken ct);
    Task PurgeExpiredAudioAsync(CancellationToken ct);
}

/// <summary>Toplantı-düzeyi agregasyon (baskın duygu, trend, oynaklık, tepe
/// anlar, ortalama congruence + Türkçe özet). Saf, deterministik (Alt-proje 02).</summary>
public interface IMeetingAggregator
{
    MeetingSummary Aggregate(MeetingSession session);
}
