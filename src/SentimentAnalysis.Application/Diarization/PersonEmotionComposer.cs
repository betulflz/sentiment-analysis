using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Bir analiz penceresinde görülen yüzlerin özetleri (karar penceresinden türetilir).</summary>
public sealed record FaceWindow(TimeSpan Start, TimeSpan End, IReadOnlyList<FaceWindowSummary> Faces);

/// <summary>Bir konuşmacının üç kaynaklı duygusu: kendi sesi + kendi sözleri + eşleştirilen yüzü.
/// <paramref name="FaceChunkCount"/>: <paramref name="ChunkCount"/> parçadan kaçında yüz gerçekten katıldı.
/// <paramref name="WithoutFace"/>: yüz hiç katılmasaydı (yalnız ses+söz) ortalama; <paramref name="AverageFace"/>:
/// yüzün katıldığı parçalardaki ortalama ifadesi (null = yüz katılmadı). İkisi, yüzün etkisini açıklamak içindir.</summary>
public sealed record PersonEmotion(
    SpeakerId Speaker, ValenceArousal Combined, int ChunkCount, int FaceChunkCount,
    ValenceArousal WithoutFace, FaceWindowSummary? AverageFace);

/// <summary>Konuşmacı–yüz eşlemesi verilince (kullanıcı seçer) kişiye özel üç kaynaklı duygu üretir.
/// <para>Canlı füzyon her parçaya karedeki EN BELİRGİN yüzü karıştırır; iki kişide bu çoğu zaman konuşanın
/// yüzü değildir. Bu yüzden her parça önce yüzsüz haline (<see cref="FusedResult.SpeechVa"/>) döndürülür,
/// sonra yalnızca konuşmacının kendi yüzüyle yeniden birleştirilir. Eşlemesi olmayan konuşmacı sonuçta
/// yoktur (rapor ona dokunmaz). Saf ve deterministik.</para></summary>
public static class PersonEmotionComposer
{
    public static IReadOnlyList<PersonEmotion> Compose(
        IReadOnlyList<ChunkResult> chunks,
        IReadOnlyList<SpeakerSegment> speakers,
        IReadOnlyList<FaceWindow> windows,
        IReadOnlyDictionary<SpeakerId, int> faceBySpeaker,
        FusionConfig fusion,
        double minOverlapRatio)
    {
        var aligned = SpeakerAligner.AlignChunks(chunks, speakers, minOverlapRatio);
        var result = new List<PersonEmotion>();
        foreach (var group in aligned.Where(a => faceBySpeaker.ContainsKey(a.Speaker)).GroupBy(a => a.Speaker))
        {
            int face = faceBySpeaker[group.Key];
            int withFace = 0;
            var combined = new List<ValenceArousal>();
            var speechOnly = new List<ValenceArousal>();
            var used = new List<FaceWindowSummary>();
            foreach (var a in group)
            {
                var fused = a.Chunk.Fused;
                var speech = fused with { Combined = fused.SpeechVa ?? fused.Combined, SpeechVa = null, FaceValence = null, FaceWeight = 0 };
                var faces = BestWindow(windows, a.Chunk)?.Faces.Where(f => f.FaceIndex == face).ToArray()
                            ?? Array.Empty<FaceWindowSummary>();
                var composed = FaceFusion.Apply(speech, faces, fusion);
                if (composed.FaceValence is not null) { withFace++; used.Add(faces[0]); }
                combined.Add(composed.Combined);
                speechOnly.Add(speech.Combined);
            }
            result.Add(new PersonEmotion(group.Key,
                ValenceArousal.Clamp(combined.Average(v => v.Valence), combined.Average(v => v.Arousal)),
                combined.Count, withFace,
                ValenceArousal.Clamp(speechOnly.Average(v => v.Valence), speechOnly.Average(v => v.Arousal)),
                used.Count == 0 ? null : new FaceWindowSummary(face,
                    used.Average(f => f.VisibleRatio), used.Average(f => f.LookAwayRatio), used.Average(f => f.ValenceShift),
                    used.Average(f => f.NegativeExpression), used.Average(f => f.SmileRatio), used.Average(f => f.Reliability))));
        }
        return result;
    }

    /// <summary>Parçayla en çok zaman örtüşen pencere; hiç örtüşen yoksa null.</summary>
    private static FaceWindow? BestWindow(IReadOnlyList<FaceWindow> windows, ChunkResult chunk)
    {
        FaceWindow? best = null;
        double bestOverlap = 0;
        foreach (var w in windows)
        {
            double overlap = (TimeSpan.FromTicks(Math.Min(w.End.Ticks, chunk.TimeEnd.Ticks))
                              - TimeSpan.FromTicks(Math.Max(w.Start.Ticks, chunk.TimeStart.Ticks))).TotalSeconds;
            if (overlap > bestOverlap) { bestOverlap = overlap; best = w; }
        }
        return best;
    }
}
