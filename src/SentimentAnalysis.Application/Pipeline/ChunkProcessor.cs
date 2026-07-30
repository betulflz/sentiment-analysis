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

    public async Task WarmUpAsync(CancellationToken ct)
    {
        // 1. VAD (Silero)
        try
        {
            var dummyChunk = new AudioChunk(new float[512], 16000, TimeSpan.Zero, TimeSpan.FromSeconds(0.032));
            _vad.HasSpeech(dummyChunk);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"VAD (Silero) modeli yüklenemedi: {ex.Message}", ex);
        }

        // 2. Prosody (Wav2Vec2)
        try
        {
            var dummyChunk = new AudioChunk(new float[1600], 16000, TimeSpan.Zero, TimeSpan.FromSeconds(0.1));
            await _prosody.AnalyzeAsync(dummyChunk, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Ses Prozodi (Wav2Vec2) modeli yüklenemedi: {ex.Message}", ex);
        }

        // 3. Speech-To-Text (Whisper)
        try
        {
            var dummyChunk = new AudioChunk(new float[1600], 16000, TimeSpan.Zero, TimeSpan.FromSeconds(0.1));
            await _stt.TranscribeAsync(dummyChunk, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"STT (Whisper) modeli yüklenemedi: {ex.Message}", ex);
        }

        // 4. Text Emotion (XLM-R)
        try
        {
            var dummyTranscript = new Transcript(string.Empty, DetectedLanguage.Other, 0.0, new List<TranscriptSegment>());
            await _text.AnalyzeAsync(dummyTranscript, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Metin Duygu (XLM-R) modeli yüklenemedi: {ex.Message}", ex);
        }
    }
}
