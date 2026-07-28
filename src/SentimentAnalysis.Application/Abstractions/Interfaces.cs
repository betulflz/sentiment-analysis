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

/// <summary>Arka plan konuşmacı geçişinin ihtiyaç duyduğu dar depolama yüzeyi (Alt-proje 10b).
/// <see cref="ISessionStore"/>'dan ayrı tutulur: geçiş yalnızca "sesi oku" ve "konuşmacıları yaz"
/// yeteneklerini görmelidir.</summary>
public interface IDiarizationStore
{
    /// <summary>Oturumun saklanmış sesi (16 kHz mono). Ses yoksa <c>null</c> — bu bir hata
    /// değildir (oturum konuşmacı ayrımı kapalıyken kaydedilmiş olabilir).</summary>
    Task<float[]?> GetSessionAudioAsync(Guid sessionId, CancellationToken ct);

    Task SaveSpeakerSegmentsAsync(Guid sessionId, IReadOnlyList<SpeakerSegment> segments, CancellationToken ct);
}

/// <summary>Konuşmacı ayrımının ÇALIŞMA ANI durumu (Alt-proje 10b). <see cref="AppConfig"/>
/// immutable bir singleton olduğundan, Ayarlar'dan yapılan açma/kapama onun üzerinden
/// yürüyemez; bu küçük değiştirilebilir servis (mevcut <c>ICaptureModeState</c> deseninin
/// aynısı) başlangıç değerini config'ten alır ve kullanıcı değiştirdiğinde günceller.</summary>
public interface IDiarizationState
{
    bool Enabled { get; set; }
}

/// <inheritdoc cref="IDiarizationState"/>
public sealed class DiarizationState : IDiarizationState
{
    public DiarizationState(Configuration.AppConfig config) => Enabled = config.Diarization.Enabled;
    public bool Enabled { get; set; }
}

/// <summary>Raporun konuşmacı etiketli transkripti kurabilmesi için gereken OKUMA yüzeyi
/// (Alt-proje 10b). <see cref="IDiarizationStore"/>'dan ayrıdır: geçiş yazar, rapor okur.</summary>
public interface ISpeakerDataReader
{
    Task<IReadOnlyList<TranscriptSegment>> GetTranscriptSegmentsAsync(Guid sessionId, CancellationToken ct);
    Task<IReadOnlyList<SpeakerSegment>> GetSpeakerSegmentsAsync(Guid sessionId, CancellationToken ct);
}

/// <summary>Tüm kaydı görüp konuşmacı bölgeleri üretir (Alt-proje 10b; sherpa-onnx offline
/// diarization). Canlı hatta ÇAĞRILMAZ — oturum bittikten sonra arka planda bir kez çalışır
/// (spec 10 S2: sherpa'nın diarization'ı yalnızca offline'dır ve 10a ölçümünde RTF ~0,44
/// olduğundan canlı akışa giremez).</summary>
public interface IDiarizer
{
    /// <param name="mono16k">16 kHz mono, oturumun tamamı.</param>
    Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(float[] mono16k, CancellationToken ct);
}

/// <summary>Oturumun ham sesini saklar — arka plan konuşmacı ayrımının (Alt-proje 10b) girdisi.
/// <see cref="ISessionStore"/>'dan ayrı tutulur: canlı koşu yalnızca "sesi yaz" yeteneğine
/// ihtiyaç duyar, oturum sorgulama/temizlik yüzeyine değil.
/// <para>Yalnızca <c>DiarizationConfig.Enabled</c> iken çağrılır — kullanıcı konuşmacı ayrımını
/// istemediyse sesi diske yazmayız (master §12.5, gizlilik varsayılan).</para></summary>
public interface ISessionAudioSink
{
    /// <param name="pcm16">16 kHz mono, 16-bit little-endian PCM.</param>
    Task SaveSessionAudioAsync(Guid sessionId, byte[] pcm16, CancellationToken ct);
}

/// <summary>Toplantı-düzeyi agregasyon (baskın duygu, trend, oynaklık, tepe
/// anlar, ortalama congruence + Türkçe özet). Saf, deterministik (Alt-proje 02).</summary>
public interface IMeetingAggregator
{
    MeetingSummary Aggregate(MeetingSession session);
}
