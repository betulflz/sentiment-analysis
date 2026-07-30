using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Bir transkript segmentinin konuşmacı ataması. <paramref name="Uncertain"/> true ise
/// atama eşik-altı örtüşmeye dayanır: kullanıcıya gösterilirken bu belirsizlik GÖRÜNÜR
/// kılınmalıdır (master §12.2 — sistem emin olmadığı yerde emin gibi davranmaz).</summary>
public sealed record AlignedSegment(TranscriptSegment Segment, SpeakerId Speaker, bool Uncertain);

/// <summary>Bir chunk'ın konuşmacı ataması. Bkz. <see cref="AlignedSegment"/>.</summary>
public sealed record AlignedChunk(ChunkResult Chunk, SpeakerId Speaker, bool Uncertain);

/// <summary>Zaman damgalı transkript segmentlerini konuşmacı bölgeleriyle eşler (alt-proje 10b).
/// Saf ve deterministik: diarization'ın belirsizliği motor sınırında kalır, "bu cümle kimin?"
/// kararı burada test edilebilir matematiktir.
/// <para><b>Üç durum vardır:</b> eşik üstü örtüşme → kesin atama; eşik altı ama sıfırdan büyük
/// örtüşme → en iyi adaya atama + <c>Uncertain</c>; hiç örtüşme yok → <see cref="SpeakerId.Unknown"/>
/// (aday bile yoktur, atamak uydurmak olurdu).</para>
/// <para><b>Zaman ekseni:</b> her iki taraf da MUTLAK zamanlı olmalıdır. Transkript segmentleri
/// <c>ChunkProcessor</c>'da chunk-göreliden mutlağa çevrilir; diarization zaten mutlak üretir.
/// Eksenler karışırsa hizalama sessizce yanlış konuşmacıya metin atar.</para></summary>
public static class SpeakerAligner
{
    /// <summary>Her transkript segmentini en çok örtüştüğü konuşmacıya atar.</summary>
    /// <param name="minOverlapRatio">Segment süresinin en az bu oranı bir konuşmacı bölgesiyle
    /// örtüşürse atama KESİN sayılır (eşik dahil); altında kalırsa yine en iyi adaya atanır
    /// ama <see cref="AlignedSegment.Uncertain"/> true işaretlenir — uydurma atama yerine
    /// dürüst belirsizlik (spec 10 S6, master §12.2).</param>
    /// <returns>Girdiyle AYNI sırada ve AYNI sayıda öğe. Atanamayan segment listeden düşmez,
    /// sessiz veri kaybı olmaz.</returns>
    public static IReadOnlyList<AlignedSegment> Align(
        IReadOnlyList<TranscriptSegment> transcript,
        IReadOnlyList<SpeakerSegment> speakers,
        double minOverlapRatio)
    {
        var result = new List<AlignedSegment>(transcript.Count);
        foreach (var seg in transcript)
        {
            var (speaker, uncertain) = BestSpeaker(seg.Start, seg.End, speakers, minOverlapRatio);
            result.Add(new AlignedSegment(seg, speaker, uncertain));
        }
        return result;
    }

    /// <summary>Aynı örtüşme mantığıyla chunk'ları (duygu sonucu taşıyan zaman aralıkları)
    /// konuşmacılara atar — rapor bunu konuşmacı-başına duygu özetine indirger (10c).</summary>
    public static IReadOnlyList<AlignedChunk> AlignChunks(
        IReadOnlyList<ChunkResult> chunks,
        IReadOnlyList<SpeakerSegment> speakers,
        double minOverlapRatio)
    {
        var result = new List<AlignedChunk>(chunks.Count);
        foreach (var chunk in chunks)
        {
            var (speaker, uncertain) = BestSpeaker(chunk.TimeStart, chunk.TimeEnd, speakers, minOverlapRatio);
            result.Add(new AlignedChunk(chunk, speaker, uncertain));
        }
        return result;
    }

    /// <summary>En çok örtüşen konuşmacıyı ve atamanın kesin olup olmadığını döndürür.
    /// Beraberlikte İLK bölge kazanır (kesin ">" karşılaştırması) — sonuç koşudan koşuya
    /// değişmesin diye.</summary>
    private static (SpeakerId Speaker, bool Uncertain) BestSpeaker(
        TimeSpan start, TimeSpan end, IReadOnlyList<SpeakerSegment> speakers, double minOverlapRatio)
    {
        double duration = (end - start).TotalSeconds;
        if (duration <= 0) return (SpeakerId.Unknown, true);

        SpeakerId best = SpeakerId.Unknown;
        double bestOverlap = 0;

        foreach (var sp in speakers)
        {
            double overlap =
                Math.Min(end.TotalSeconds, sp.End.TotalSeconds) -
                Math.Max(start.TotalSeconds, sp.Start.TotalSeconds);
            if (overlap > bestOverlap) { bestOverlap = overlap; best = sp.Speaker; }
        }

        if (bestOverlap <= 0) return (SpeakerId.Unknown, true);   // aday yok
        return (best, bestOverlap / duration < minOverlapRatio);
    }
}
