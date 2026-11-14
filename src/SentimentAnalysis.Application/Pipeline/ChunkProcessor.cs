using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
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
        ITextEmotionModel text, IEmotionFusion fusion, AppConfig? config = null)
    {
        _vad = vad; _stt = stt; _prosody = prosody; _text = text; _fusion = fusion;
        _roleCfg = (config ?? AppConfig.Default).Role;
    }

    private readonly RoleConfig _roleCfg;

    public Task<ChunkResult?> ProcessAsync(AudioChunk chunk, CancellationToken ct) => ProcessAsync(chunk, null, ct);

    public async Task<ChunkResult?> ProcessAsync(AudioChunk chunk, IRoleLabeler? roles, CancellationToken ct)
    {
        if (!_vad.HasSpeech(chunk)) return null;

        // Prozodi ∥ STT ∥ rol çizelgesi (hepsi aynı ham chunk'ı alır; rol ~0,3 sn, STT'nin gölgesinde kalır).
        var prosodyTask = _prosody.AnalyzeAsync(chunk, ct);
        var sttTask = _stt.TranscribeAsync(chunk, ct);
        var rolesTask = roles is null ? Task.FromResult<RoleTimeline?>(null) : Task.Run<RoleTimeline?>(() => roles.Label(chunk), ct);
        await Task.WhenAll(prosodyTask, sttTask, rolesTask).ConfigureAwait(false);

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

        var result = new ChunkResult(chunk.Start, chunk.End, fused, transcript.Text, absolute);
        if (rolesTask.Result is not { Enrolled: true } timeline) return result;
        return result with
        {
            Roles = timeline,
            Counterpart = await CounterpartAsync(chunk, transcript, absolute, timeline, fused, ct).ConfigureAwait(false),
        };
    }

    /// <summary>Karşı tarafın duygusu: parça karışıksa karşı tarafın dilimleri birleştirilip prozodi yeniden çalışır,
    /// metin duygusu yalnız karşı tarafa düşen transkript satırlarından hesaplanır. Yönetenin sesi karşı tarafın
    /// gerilim/olumsuzluk göstergelerine böylece karışmaz.</summary>
    private async Task<FusedResult?> CounterpartAsync(AudioChunk chunk, Transcript transcript,
        IReadOnlyList<TranscriptSegment> absolute, RoleTimeline timeline, FusedResult fused, CancellationToken ct)
    {
        double other = timeline.Seconds(SpeakerRole.KarsiTaraf, chunk.Start, chunk.End);
        double host = timeline.Seconds(SpeakerRole.Yoneten, chunk.Start, chunk.End);
        if (other < _roleCfg.MinCounterpartSeconds) return null;              // karşı taraf (neredeyse) konuşmadı
        if (host < _roleCfg.NegligibleHostSeconds) return fused;              // parça zaten karşı tarafın

        var samples = new List<float>();
        foreach (var span in timeline.Spans.Where(s => s.Role == SpeakerRole.KarsiTaraf))
        {
            int from = Math.Clamp((int)((span.Start - chunk.Start).TotalSeconds * chunk.SampleRate), 0, chunk.Samples.Length);
            int to = Math.Clamp((int)((span.End - chunk.Start).TotalSeconds * chunk.SampleRate), from, chunk.Samples.Length);
            samples.AddRange(chunk.Samples.AsSpan(from, to - from).ToArray());
        }
        var otherChunk = new AudioChunk(samples.ToArray(), chunk.SampleRate, chunk.Start,
            chunk.Start + TimeSpan.FromSeconds(samples.Count / (double)chunk.SampleRate));

        // Satır, en çok örtüştüğü role aittir.
        var otherSegments = absolute.Where(seg => timeline.RoleOf(seg.Start, seg.End) == SpeakerRole.KarsiTaraf).ToList();
        var otherTranscript = transcript with
        {
            Text = string.Join(" ", otherSegments.Select(x => x.Text.Trim())).Trim(),
            Segments = otherSegments,
        };

        var prosodyTask = _prosody.AnalyzeAsync(otherChunk, ct);
        var textTask = _text.AnalyzeAsync(otherTranscript, ct);
        await Task.WhenAll(prosodyTask, textTask).ConfigureAwait(false);
        var quality = QualitySignalsBuilder.Build(otherChunk, otherTranscript);
        return _fusion.Fuse(textTask.Result, prosodyTask.Result, quality);
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
