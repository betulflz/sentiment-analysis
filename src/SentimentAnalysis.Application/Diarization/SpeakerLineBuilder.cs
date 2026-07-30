using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Okunabilir bir konuşma satırı: "Konuşmacı A: merhaba nasılsın".
/// <paramref name="Uncertain"/> true ise atama eşik-altı örtüşmeye dayanır ve UI bunu
/// görünür kılmalıdır.</summary>
public sealed record SpeakerLine(SpeakerId Speaker, string Text, TimeSpan Start, TimeSpan End, bool Uncertain);

/// <summary>Hizalanmış segmentleri konuşma satırlarına indirger: ardışık, AYNI konuşmacıya ait
/// ve AYNI kesinlikteki segmentler tek satırda birleşir. Saf ve deterministik.
/// <para>Kesinlik de birleştirme anahtarıdır: emin bir cümle belirsiz bir cümleyle aynı satıra
/// karışırsa satırın hangi kısmına güvenileceği kaybolur.</para>
/// <para>Birleştirme yalnızca ardışıklığa bakar; konuşmacı değişip geri dönerse ayrı satır olur
/// (A→B→A üç satırdır) — çünkü aradaki B satırı zaman sırasını korumalıdır.</para></summary>
public static class SpeakerLineBuilder
{
    public static IReadOnlyList<SpeakerLine> Build(IReadOnlyList<AlignedSegment> aligned)
    {
        var lines = new List<SpeakerLine>();

        foreach (var a in aligned)
        {
            if (lines.Count > 0 && lines[^1].Speaker == a.Speaker && lines[^1].Uncertain == a.Uncertain)
            {
                var last = lines[^1];
                lines[^1] = last with { Text = $"{last.Text} {a.Segment.Text}", End = a.Segment.End };
            }
            else
            {
                lines.Add(new SpeakerLine(a.Speaker, a.Segment.Text, a.Segment.Start, a.Segment.End, a.Uncertain));
            }
        }

        return lines;
    }
}
