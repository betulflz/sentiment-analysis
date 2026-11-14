using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Okunabilir bir konuşma satırı: "Konuşmacı A: merhaba nasılsın".
/// <paramref name="Uncertain"/> true ise atama eşik-altı örtüşmeye dayanır ve UI bunu
/// görünür kılmalıdır.</summary>
public sealed record SpeakerLine(SpeakerId Speaker, string Text, TimeSpan Start, TimeSpan End, bool Uncertain);

/// <summary>Hizalanmış segmentleri konuşma satırlarına çevirir: her cümle kendi satırı, kendi saati
/// ve kendi kesinliğiyle. Saf ve deterministik.
/// <para>Ardışık aynı-konuşmacı cümleleri BİRLEŞTİRİLMEZ (2026-11-09): birleştirme, yanlış atanmış
/// bir dakikalık konuşmayı tek dev balona çeviriyor, cümle saatlerini de siliyordu.</para></summary>
public static class SpeakerLineBuilder
{
    public static IReadOnlyList<SpeakerLine> Build(IReadOnlyList<AlignedSegment> aligned)
    {
        var lines = new List<SpeakerLine>();

        foreach (var a in aligned)
            lines.Add(new SpeakerLine(a.Speaker, a.Segment.Text, a.Segment.Start, a.Segment.End, a.Uncertain));
        return lines;
    }
}
