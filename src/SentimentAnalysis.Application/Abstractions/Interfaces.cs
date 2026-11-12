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

/// <summary>Görüntü kaynağı (video dosyası / kamera; spec 11). <b>İptal sözleşmesi</b>
/// <see cref="IAudioCaptureSource"/> ile aynıdır: iptalde kısa sürede döner, ASLA süresiz bloklanmaz
/// (11a ölçümü: kamera izni istemi açıkken ffmpeg kare göndermeden bekler — uygulama ilk kare için
/// zaman aşımı koymalıdır).</summary>
public interface IVideoCaptureSource
{
    IAsyncEnumerable<VideoFrame> CaptureAsync(CancellationToken ct);
}

/// <summary>Karedeki yüzleri bulur (YuNet; spec 11 §6.2).</summary>
public interface IFaceDetector
{
    IReadOnlyList<DetectedFace> Detect(VideoFrame frame);
}

/// <summary>Bir yüz kutusunun ifade olasılıkları (FER+; 8 sınıf, FER+ sırası).</summary>
public interface IFaceExpressionModel
{
    float[] Classify(VideoFrame frame, FaceBox box);
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

/// <summary>Geliştirici teşhis kanalı. Üretimde SESSİZDİR; gerektiğinde açılır.
/// <para><b>Neden <c>Console</c> değil:</b> paketlenmiş uygulamada (macOS <c>.app</c>, Windows GUI)
/// bağlı bir konsol yoktur — çıktı hiçbir yere gitmez, yani maliyet ödenir fayda alınmaz. Daha
/// kötüsü, günlük satırının kendisi (metin biçimleme + I/O) ses geri çağrımı gibi GERÇEK ZAMANLI
/// yollarda çalışırsa ses kopmasına yol açar; teşhis aracı, teşhis ettiği sorunu üretir.</para>
/// <para><b>Sözleşme:</b> pahalı bir mesaj kurmadan ÖNCE <see cref="IsEnabled"/> kontrol edilmelidir
/// (ör. chunk başına 160 bin örnek üzerinde RMS/tepe hesabı). Uygulamalar <see cref="Write"/>'tan
/// fırlatmamalıdır; yine de çağıranlar, teşhisin analizi çökertmemesi için hataya dayanıklıdır.</para></summary>
public interface IDiagnosticLog
{
    /// <summary>Kapalıysa çağıran, mesajı ve mesajı üretmek için gereken işi hiç yapmamalıdır.</summary>
    bool IsEnabled { get; }

    void Write(string message);
}

/// <summary>Üretim varsayılanı: hiçbir şey yapmaz, hiçbir maliyet üretmez.</summary>
public sealed class NullDiagnosticLog : IDiagnosticLog
{
    public static readonly NullDiagnosticLog Instance = new();

    public bool IsEnabled => false;

    public void Write(string message) { }
}

/// <summary><see cref="System.Diagnostics.Trace"/> üzerine yazar — konsol gerektirmez, her
/// platformda çalışır ve bir dinleyiciye (dosya, hata ayıklayıcı, DebugView) yönlendirilebilir.
/// Yalnızca açıkça istendiğinde kurulur (bkz. composition root).</summary>
public sealed class TraceDiagnosticLog : IDiagnosticLog
{
    public bool IsEnabled => true;

    public void Write(string message) => System.Diagnostics.Trace.WriteLine(message);
}

/// <summary>Raporun konuşmacı etiketli transkripti kurabilmesi için gereken OKUMA yüzeyi
/// (Alt-proje 10b). <see cref="IDiarizationStore"/>'dan ayrıdır: geçiş yazar, rapor okur.</summary>
public interface ISpeakerDataReader
{
    Task<IReadOnlyList<TranscriptSegment>> GetTranscriptSegmentsAsync(Guid sessionId, CancellationToken ct);
    Task<IReadOnlyList<SpeakerSegment>> GetSpeakerSegmentsAsync(Guid sessionId, CancellationToken ct);

    /// <summary>Konuşmacı-başına duygu özeti (10c) için chunk-düzeyi füzyon sonuçları
    /// (zaman aralığı + metin/ses/nihai VA). Oturum yoksa boş liste döner.</summary>
    Task<IReadOnlyList<ChunkResult>> GetChunkResultsAsync(Guid sessionId, CancellationToken ct);
}

/// <summary>Oturumun karar verisini saklar (11e; spec 11 §6.7): pencere göstergeleri, yüz özetleri, gösterilen
/// öneriler, profil, ağırlıklar, takip kararı. Göstergeler yeniden hesaplanmak yerine pencere başına saklanır —
/// konuşma valence'ı DB'de yok, kayıtlı chunk'lardan hesaplamak yüzü iki kez sayardı.</summary>
public interface IDecisionStore
{
    /// <summary>Oturumun önceki karar kaydının yerine yazar (yeniden çalıştırılabilir).</summary>
    Task SaveDecisionSessionAsync(Guid sessionId, DecisionSessionRecord record, CancellationToken ct);

    /// <summary>Kayıt yoksa <c>null</c>. Önerilerin <c>Ranking</c>'i saklanmaz, boş döner.</summary>
    Task<DecisionSessionRecord?> GetDecisionSessionAsync(Guid sessionId, CancellationToken ct);
}

/// <summary>Kısa bir ses parçasından konuşmacı ses imzası (embedding) çıkarır (12: canlı "Yöneten / Karşı taraf").
/// Canlı hatta pencere başına çağrılır: 12a ölçümünde 1,5 sn pencere ≈ 16 ms (TitaNet-small, M1 Air).
/// İş parçacığı güvenlidir. Model eksikse <see cref="System.IO.FileNotFoundException"/> atar — sessizce boş dönmez.</summary>
public interface ISpeakerEmbedder
{
    /// <param name="mono16k">16 kHz mono örnekler (tipik 1,5–8 sn).</param>
    float[] Embed(float[] mono16k);
}

/// <summary>Tüm kaydı görüp konuşmacı bölgeleri üretir (Alt-proje 10b; sherpa-onnx offline
/// diarization). Canlı hatta ÇAĞRILMAZ — oturum bittikten sonra arka planda bir kez çalışır
/// (spec 10 S2: sherpa'nın diarization'ı yalnızca offline'dır ve 10a ölçümünde RTF ~0,44
/// olduğundan canlı akışa giremez).</summary>
public interface IDiarizer
{
    /// <param name="mono16k">16 kHz mono, oturumun tamamı.</param>
    /// <param name="expectedSpeakers">Kullanıcının söylediği kişi sayısı; 0 = bilinmiyor (küme sayısı
    /// eşikle bulunur). Bilinirse ses tam o sayıda kişiye bölünür — benzer sesler eşikte tek kişi
    /// sayılmaz (2026-11-09 ölçümü).</param>
    Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(float[] mono16k, int expectedSpeakers, CancellationToken ct);
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
