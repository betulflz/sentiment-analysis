using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Pipeline;

/// <summary>Per-chunk DAG: VAD kapısı → (prozodi ∥ STT paralel) → metin-duygu (transcript'e bağlı) →
/// <see cref="QualitySignalsBuilder"/> → füzyon → <see cref="ChunkResult"/>. Durumsuz.</summary>
public sealed class ChunkProcessor : IChunkProcessor
{
    private readonly IVadGate _vad;
    private readonly ISpeechToText _stt;
    private readonly IProsodyEmotionModel _prosody;
    private readonly ITextEmotionModel _text;
    private readonly IEmotionFusion _fusion;

    public ChunkProcessor(
        IVadGate vad, ISpeechToText stt, IProsodyEmotionModel prosody,
        ITextEmotionModel text, IEmotionFusion fusion)
    {
        _vad = vad; _stt = stt; _prosody = prosody; _text = text; _fusion = fusion;
    }

    public async Task<ChunkResult?> ProcessAsync(AudioChunk chunk, CancellationToken ct)
    {
        if (!_vad.HasSpeech(chunk)) return null;

        // Prozodi ∥ STT (ikisi de aynı ham chunk'ı alır).
        var prosodyTask = _prosody.AnalyzeAsync(chunk, ct);
        var sttTask = _stt.TranscribeAsync(chunk, ct);
        await Task.WhenAll(prosodyTask, sttTask).ConfigureAwait(false);

        var prosody = prosodyTask.Result;
        var transcript = sttTask.Result;

        var text = await _text.AnalyzeAsync(transcript, ct).ConfigureAwait(false); // transcript'e bağlı
        var quality = QualitySignalsBuilder.Build(chunk, transcript);
        var fused = _fusion.Fuse(text, prosody, quality);

        // STT segmentleri chunk'a GÖRELİ gelir; diarization (10b) mutlak zaman üretir.
        // Kaydırmayı burada, kaynağa en yakın yerde yaparız — böylece Core'dan dışarı çıkan
        // her ChunkResult tek bir zaman ekseninde konuşur.
        var absolute = new List<TranscriptSegment>(transcript.Segments.Count);
        foreach (var s in transcript.Segments)
            absolute.Add(new TranscriptSegment(s.Text, chunk.Start + s.Start, chunk.Start + s.End));

        return new ChunkResult(chunk.Start, chunk.End, fused, transcript.Text, absolute);
    }
}
