using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Zaman damgalı transkript segmentlerini konuşmacı bölgeleriyle eşler (alt-proje 10b).
/// Saf ve deterministik: diarization'ın belirsizliği motor sınırında kalır, "bu cümle kimin?"
/// kararı burada test edilebilir matematiktir.
/// <para><b>Zaman ekseni:</b> her iki taraf da MUTLAK zamanlı olmalıdır. Transkript segmentleri
/// <c>ChunkProcessor</c>'da chunk-göreliden mutlağa çevrilir; diarization zaten mutlak üretir.
/// Eksenler karışırsa hizalama sessizce yanlış konuşmacıya metin atar.</para></summary>
public static class SpeakerAligner
{
    /// <summary>Her transkript segmentini en çok örtüştüğü konuşmacıya atar.</summary>
    /// <param name="minOverlapRatio">Segment süresinin en az bu oranı bir konuşmacı bölgesiyle
    /// örtüşmelidir (eşik dahil); altında kalırsa <see cref="SpeakerId.Unknown"/> döner. Örtüşen
    /// konuşma ve sınır artıkları bu kapıdan elenir — uydurma atama yerine dürüst belirsizlik
    /// (spec 10 S6, master §12.2).</param>
    /// <returns>Girdiyle AYNI sırada ve AYNI sayıda öğe. Atanamayan segment listeden düşmez,
    /// <see cref="SpeakerId.Unknown"/> ile döner — sessiz veri kaybı olmaz.</returns>
    public static IReadOnlyList<(TranscriptSegment Segment, SpeakerId Speaker)> Align(
        IReadOnlyList<TranscriptSegment> transcript,
        IReadOnlyList<SpeakerSegment> speakers,
        double minOverlapRatio)
    {
        var result = new List<(TranscriptSegment, SpeakerId)>(transcript.Count);

        foreach (var seg in transcript)
        {
            double duration = (seg.End - seg.Start).TotalSeconds;
            if (duration <= 0) { result.Add((seg, SpeakerId.Unknown)); continue; }

            SpeakerId best = SpeakerId.Unknown;
            double bestOverlap = 0;

            // Beraberlikte İLK bölge kazanır (kesin ">" karşılaştırması) — davranış deterministik
            // olsun diye; sıraya güvenmeyiz ama sonucun koşudan koşuya değişmemesi şarttır.
            foreach (var sp in speakers)
            {
                double overlap =
                    Math.Min(seg.End.TotalSeconds, sp.End.TotalSeconds) -
                    Math.Max(seg.Start.TotalSeconds, sp.Start.TotalSeconds);
                if (overlap > bestOverlap) { bestOverlap = overlap; best = sp.Speaker; }
            }

            result.Add(bestOverlap / duration >= minOverlapRatio ? (seg, best) : (seg, SpeakerId.Unknown));
        }

        return result;
    }
}
