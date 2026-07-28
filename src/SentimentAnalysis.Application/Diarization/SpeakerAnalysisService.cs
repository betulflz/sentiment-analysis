using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Arka plan konuşmacı geçişinin sonucu + raporun göstermesi için gereken veri.</summary>
public sealed record SpeakerAnalysisResult(
    DiarizationOutcome Outcome,
    IReadOnlyList<TranscriptSegment> Transcript,
    IReadOnlyList<SpeakerSegment> Speakers)
{
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

    public SpeakerAnalysisService(DiarizationJob job, ISpeakerDataReader reader, IDiarizationState state)
    {
        _job = job; _reader = reader; _state = state;
    }

    // Çalışma anında okunur: kullanıcı Ayarlar'dan açtığında uygulama yeniden başlatılmadan
    // etkili olmalı.
    public bool Enabled => _state.Enabled;

    public async Task<SpeakerAnalysisResult> RunAsync(Guid sessionId, CancellationToken ct)
    {
        var outcome = await _job.RunAsync(sessionId, ct).ConfigureAwait(false);

        // Başarısızlıkta segment okumaya gerek yok: rapor konuşmacısız gösterilecek.
        if (outcome != DiarizationOutcome.Completed)
            return new SpeakerAnalysisResult(outcome, Array.Empty<TranscriptSegment>(), Array.Empty<SpeakerSegment>());

        var transcript = await _reader.GetTranscriptSegmentsAsync(sessionId, ct).ConfigureAwait(false);
        var speakers = await _reader.GetSpeakerSegmentsAsync(sessionId, ct).ConfigureAwait(false);
        return new SpeakerAnalysisResult(outcome, transcript, speakers);
    }
}
