using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>"Siz hangisiniz?" panelindeki bir konuşmacı: toplam konuşma süresi ve dinletilecek örnek aralığı.</summary>
public sealed record SpeakerChoice(SpeakerId Speaker, double TalkSeconds, TimeSpan SampleStart, TimeSpan SampleEnd);

/// <summary>Dosya modunda analizden önceki konuşmacı ayrımının sonucu. <see cref="Audio"/> yalnız bellekte tutulur
/// (oturum sonrası kişi kartları için) ve diske yazılmaz; <see cref="Truncated"/> = dosya uzunluk sınırını aştı.</summary>
public sealed record SpeakerPreparationResult(
    DiarizationOutcome Outcome, IReadOnlyList<SpeakerSegment> Segments, IReadOnlyList<SpeakerChoice> Choices,
    float[] Audio, bool Truncated)
{
    public static SpeakerPreparationResult Failed(float[] audio, bool truncated) =>
        new(DiarizationOutcome.Failed, Array.Empty<SpeakerSegment>(), Array.Empty<SpeakerChoice>(), audio, truncated);
}

/// <summary>Konuşmacı ayrımı + hayalet küme süzme + seçim listesi. Saf iş mantığı; model ömrü çağıranda.</summary>
public sealed class SpeakerPreparation(IDiarizer diarizer)
{
    public const double MaxSampleSeconds = 5.0;

    public async Task<SpeakerPreparationResult> RunAsync(float[] mono16k, int expectedSpeakers, bool truncated, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<SpeakerSegment> raw;
        try
        {
            raw = await diarizer.DiarizeAsync(mono16k, expectedSpeakers, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return SpeakerPreparationResult.Failed(mono16k, truncated); }

        var segments = GhostSpeakerFilter.Apply(raw);
        var choices = segments
            .Where(s => s.Speaker != SpeakerId.Unknown)
            .GroupBy(s => s.Speaker)
            .Select(g =>
            {
                var longest = g.OrderByDescending(s => s.End - s.Start).ThenBy(s => s.Start).First();
                var len = Math.Min(MaxSampleSeconds, (longest.End - longest.Start).TotalSeconds);
                var mid = longest.Start + (longest.End - longest.Start) / 2;
                var start = mid - TimeSpan.FromSeconds(len / 2);
                return new SpeakerChoice(g.Key, g.Sum(s => (s.End - s.Start).TotalSeconds), start, start + TimeSpan.FromSeconds(len));
            })
            .OrderByDescending(c => c.TalkSeconds).ThenBy(c => c.Speaker.Index)
            .ToArray();
        return new SpeakerPreparationResult(DiarizationOutcome.Completed, segments, choices, mono16k, truncated);
    }
}
