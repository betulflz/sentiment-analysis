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

    /// <summary>İt.2: <see cref="Align"/> gibi, ama iki (ya da daha çok) kişinin sırayla konuştuğu uzun parçayı
    /// tek kişiye yazmak yerine BÖLER. Kelime zamanı olmadığından bölme YAKLAŞIKTIR: kelimeler konuşma sıralarına
    /// süreleriyle orantılı dağıtılır (eşit konuşma hızı varsayımı; sınırdaki bir-iki kelime yanlış tarafa düşebilir).
    /// <paramref name="minTurnSeconds"/>'dan kısa sıralar (ör. "hı hı") bölmez. Metin kaybı yoktur: kelimeler sırasıyla
    /// korunur, her parça en az bir kelime alır.</summary>
    public static IReadOnlyList<AlignedSegment> AlignSplitting(
        IReadOnlyList<TranscriptSegment> transcript,
        IReadOnlyList<SpeakerSegment> speakers,
        double minOverlapRatio,
        double minTurnSeconds = 1.0)
    {
        var result = new List<AlignedSegment>(transcript.Count);
        foreach (var seg in transcript)
        {
            var turns = Turns(seg, speakers, minTurnSeconds);
            var words = seg.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (turns.Count < 2 || words.Length < turns.Count)
            {
                var (speaker, uncertain) = BestSpeaker(seg.Start, seg.End, speakers, minOverlapRatio);
                result.Add(new AlignedSegment(seg, speaker, uncertain));
                continue;
            }

            double total = turns.Sum(t => t.End - t.Start);
            int given = 0;
            double cumulative = 0;
            for (int i = 0; i < turns.Count; i++)
            {
                cumulative += turns[i].End - turns[i].Start;
                int upto = i == turns.Count - 1 ? words.Length
                    : (int)Math.Round(words.Length * cumulative / total, MidpointRounding.AwayFromZero);
                // Her parça ≥1 kelime; kalan parçalara da en az birer kelime bırakılır.
                upto = Math.Clamp(upto, given + 1, words.Length - (turns.Count - 1 - i));
                var from = TimeSpan.FromSeconds(i == 0 ? seg.Start.TotalSeconds : turns[i].Start);
                var to = TimeSpan.FromSeconds(i == turns.Count - 1 ? seg.End.TotalSeconds : turns[i + 1].Start);
                var part = new TranscriptSegment(string.Join(' ', words[given..upto]), from, to);
                var (speaker, uncertain) = BestSpeaker(from, to, speakers, minOverlapRatio);
                result.Add(new AlignedSegment(part, speaker, uncertain));
                given = upto;
            }
        }
        return result;
    }

    /// <summary>Parça içindeki konuşma sıraları: bilinen kişilerin bölgeleri kesilip zamana göre dizilir, kısa sıralar
    /// atılır, ardışık aynı kişi birleşir.</summary>
    private static List<(SpeakerId Speaker, double Start, double End)> Turns(
        TranscriptSegment seg, IReadOnlyList<SpeakerSegment> speakers, double minTurnSeconds)
    {
        double a = seg.Start.TotalSeconds, b = seg.End.TotalSeconds;
        var pieces = speakers.Where(s => s.Speaker.Index >= 0)
            .Select(s => (s.Speaker, Start: Math.Max(a, s.Start.TotalSeconds), End: Math.Min(b, s.End.TotalSeconds)))
            .Where(p => p.End - p.Start >= minTurnSeconds)
            .OrderBy(p => p.Start).ToList();
        var turns = new List<(SpeakerId Speaker, double Start, double End)>();
        foreach (var p in pieces)
        {
            if (turns.Count > 0 && turns[^1].Speaker == p.Speaker)
                turns[^1] = (p.Speaker, turns[^1].Start, Math.Max(turns[^1].End, p.End));
            else turns.Add(p);
        }
        return turns;
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

    /// <summary>Kişi başına tüm bölgelerin birleşim süresini karşılaştırır. Yinelenen veya
    /// örtüşen aynı-kişi bölgeleri bir kez sayılır. Beraberlikte ilk kişi seçilir ama atama
    /// belirsizdir; Unknown bölgeleri gerçek bir kişinin kanıtını bastırmaz.</summary>
    private static (SpeakerId Speaker, bool Uncertain) BestSpeaker(
        TimeSpan start, TimeSpan end, IReadOnlyList<SpeakerSegment> speakers, double minOverlapRatio)
    {
        double duration = (end - start).TotalSeconds;
        if (duration <= 0) return (SpeakerId.Unknown, true);

        SpeakerId best = SpeakerId.Unknown;
        double bestOverlap = 0;
        bool tied = false;

        foreach (var group in speakers.Where(s => s.Speaker.Index >= 0).GroupBy(s => s.Speaker))
        {
            var intervals = group.Select(s => (
                    Start: Math.Max(start.TotalSeconds, s.Start.TotalSeconds),
                    End: Math.Min(end.TotalSeconds, s.End.TotalSeconds)))
                .Where(s => s.End > s.Start).OrderBy(s => s.Start);
            double overlap = 0, regionStart = 0, regionEnd = 0;
            bool hasRegion = false;
            foreach (var interval in intervals)
            {
                if (!hasRegion || interval.Start > regionEnd)
                {
                    if (hasRegion) overlap += regionEnd - regionStart;
                    regionStart = interval.Start;
                    regionEnd = interval.End;
                    hasRegion = true;
                }
                else regionEnd = Math.Max(regionEnd, interval.End);
            }
            if (hasRegion) overlap += regionEnd - regionStart;
            if (overlap > bestOverlap + 1e-9)
            {
                bestOverlap = overlap;
                best = group.Key;
                tied = false;
            }
            else if (overlap > 0 && Math.Abs(overlap - bestOverlap) <= 1e-9) tied = true;
        }

        if (bestOverlap <= 0) return (SpeakerId.Unknown, true);   // aday yok
        return (best, tied || bestOverlap / duration < minOverlapRatio);
    }
}
