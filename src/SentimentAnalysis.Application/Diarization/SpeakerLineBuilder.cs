using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Okunabilir bir konuşma satırı: "Konuşmacı A: merhaba nasılsın".</summary>
public sealed record SpeakerLine(SpeakerId Speaker, string Text, TimeSpan Start, TimeSpan End);

/// <summary>Hizalanmış segmentleri konuşma satırlarına indirger: ardışık AYNI konuşmacıya ait
/// segmentler tek satırda birleşir. Saf ve deterministik.
/// <para>Birleştirme yalnızca ardışıklığa bakar; konuşmacı değişip geri dönerse ayrı satır olur
/// (A→B→A üç satırdır) — çünkü aradaki B satırı zaman sırasını korumalıdır.</para></summary>
public static class SpeakerLineBuilder
{
    public static IReadOnlyList<SpeakerLine> Build(
        IReadOnlyList<(TranscriptSegment Segment, SpeakerId Speaker)> aligned)
    {
        var lines = new List<SpeakerLine>();

        foreach (var (seg, speaker) in aligned)
        {
            if (lines.Count > 0 && lines[^1].Speaker == speaker)
            {
                var last = lines[^1];
                lines[^1] = last with { Text = $"{last.Text} {seg.Text}", End = seg.End };
            }
            else
            {
                lines.Add(new SpeakerLine(speaker, seg.Text, seg.Start, seg.End));
            }
        }

        return lines;
    }
}
