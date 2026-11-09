using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Arka plan konuşmacı geçişinin sonucu + raporun göstermesi için gereken veri.</summary>
public sealed record SpeakerAnalysisResult(
    DiarizationOutcome Outcome,
    IReadOnlyList<TranscriptSegment> Transcript,
    IReadOnlyList<SpeakerSegment> Speakers,
    IReadOnlyList<ChunkResult>? Chunks = null)
{
    /// <summary>Konuşmacı-başına duygu özeti (10c) için. Additive: eski 3-parametreli çağrılar
    /// boş listeyle çalışmaya devam eder (bkz. <c>ChunkResult.Segments</c>'in aynı deseni).</summary>
    public IReadOnlyList<ChunkResult> Chunks { get; init; } = Chunks ?? Array.Empty<ChunkResult>();

    /// <summary>Konuşmacının KENDİ sesinden ölçülmüş prozodi duygusu (10d). Additive:
    /// varsayılan boş liste, eski çağrılar etkilenmez.</summary>
    public IReadOnlyList<SpeakerProsodySummary> SpeakerProsody { get; init; } =
        Array.Empty<SpeakerProsodySummary>();

    public static SpeakerAnalysisResult Disabled { get; } =
        new(DiarizationOutcome.NoAudio, Array.Empty<TranscriptSegment>(), Array.Empty<SpeakerSegment>());
}

/// <summary>Bir oturum için konuşmacı ayrımını uçtan uca çalıştırır: diarize et, sakla, raporun
/// ihtiyaç duyduğu segmentleri geri oku. UI katmanının tek dokunacağı yüzey budur.</summary>
public interface ISpeakerAnalysis
{
    /// <summary>Özellik açık mı? Kapalıyken UI hiçbir konuşmacı durumu göstermemelidir.</summary>
    bool Enabled { get; }

    Task<SpeakerAnalysisResult> RunAsync(Guid sessionId, CancellationToken ct);
}

/// <inheritdoc cref="ISpeakerAnalysis"/>
public sealed class SpeakerAnalysisService : ISpeakerAnalysis
{
    private readonly DiarizationJob _job;
    private readonly ISpeakerDataReader _reader;
    private readonly IDiarizationState _state;
    private readonly SpeakerProsodyAnalyzer _prosody;

    // NOT (düzeltme turu 1 / bulgu 3): IDiarizationStore burada KASITLI olarak alınmıyor.
    // Ses artık _job.RunWithAudioAsync üzerinden TEK seferde okunuyor; ayrı bir store bağımlılığı
    // ikinci bir GetSessionAudioAsync çağrısına (dolayısıyla ikinci bir şifre-çözme + tahsis) davetiye
    // çıkarırdı. Segment kaydı zaten DiarizationJob içinde yapılıyor; bu sınıfın store'a doğrudan
    // ihtiyacı yok.
    public SpeakerAnalysisService(
        DiarizationJob job, ISpeakerDataReader reader, IDiarizationState state,
        SpeakerProsodyAnalyzer prosody)
    {
        _job = job; _reader = reader; _state = state;
        _prosody = prosody;
    }

    // Çalışma anında okunur: kullanıcı Ayarlar'dan açtığında uygulama yeniden başlatılmadan
    // etkili olmalı.
    public bool Enabled => _state.Enabled;

    public async Task<SpeakerAnalysisResult> RunAsync(Guid sessionId, CancellationToken ct)
    {
        // Ses BURADA değil, RunWithAudioAsync içinde okunur — tek okuma, iki tüketici (diarizer +
        // prozodi analizörü). Bkz. DiarizationJob.RunWithAudioAsync dokümantasyonu (bulgu 3).
        var (outcome, audio) = await _job.RunWithAudioAsync(sessionId, ct).ConfigureAwait(false);

        // Başarısızlıkta segment okumaya gerek yok: rapor konuşmacısız gösterilecek.
        if (outcome != DiarizationOutcome.Completed)
            return new SpeakerAnalysisResult(outcome, Array.Empty<TranscriptSegment>(), Array.Empty<SpeakerSegment>());

        // Rapor ve ses tonu ölçümü aynı temiz veriyi görür: örtüşmeden gelen tekrar satırlar ve
        // hayalet konuşmacılar burada, tek yerde ayıklanır.
        var transcript = TranscriptDeduper.Dedupe(
            await _reader.GetTranscriptSegmentsAsync(sessionId, ct).ConfigureAwait(false));
        var speakers = GhostSpeakerFilter.Apply(
            await _reader.GetSpeakerSegmentsAsync(sessionId, ct).ConfigureAwait(false));
        var chunks = await _reader.GetChunkResultsAsync(sessionId, ct).ConfigureAwait(false);

        // Konuşmacı duygusu, kişinin KENDİ sesinden ölçülür (10d). outcome==Completed olduğu
        // için audio DEĞİŞMEZ OLARAK doludur (bkz. RunWithAudioAsync sözleşmesi) — ikinci bir
        // null/boş kontrolüne gerek yoktur.
        var prosody = await _prosody.AnalyzeAsync(audio, speakers, ct).ConfigureAwait(false);

        return new SpeakerAnalysisResult(outcome, transcript, speakers, chunks) { SpeakerProsody = prosody };
    }
}
