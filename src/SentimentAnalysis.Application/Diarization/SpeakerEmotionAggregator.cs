using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Bir konuşmacıya atanan chunk'ların duygu ortalaması: metin, ses ve nihai (10c).
/// <paramref name="ChunkCount"/> saydamlık için taşınır — az chunk'lı bir ortalamaya aşırı
/// güvenilmemeli.</summary>
public sealed record SpeakerEmotionSummary(
    SpeakerId Speaker,
    ValenceArousal Text,
    ValenceArousal Audio,
    ValenceArousal Combined,
    int ChunkCount);

/// <summary>Chunk-düzeyi füzyon sonuçlarını konuşmacı bölgeleriyle hizalayıp konuşmacı-başına
/// ortalama duyguya indirger. Saf ve deterministik.</summary>
public static class SpeakerEmotionAggregator
{
    /// <summary>Atanamayan (<see cref="SpeakerId.Unknown"/>) chunk'lar da dürüstçe kendi
    /// grubunda döner — sessizce atılmaz (master §12.2). Sonuç, bilinen konuşmacılar önce,
    /// Bilinmeyen en sonda olacak şekilde sıralanır.</summary>
    public static IReadOnlyList<SpeakerEmotionSummary> Aggregate(
        IReadOnlyList<ChunkResult> chunks,
        IReadOnlyList<SpeakerSegment> speakers,
        double minOverlapRatio)
    {
        var aligned = SpeakerAligner.AlignChunks(chunks, speakers, minOverlapRatio);

        return aligned
            .GroupBy(a => a.Speaker)
            .Select(g => new SpeakerEmotionSummary(
                g.Key,
                Average(g.Select(x => x.Chunk.Fused.TextVa)),
                Average(g.Select(x => x.Chunk.Fused.ProsodyVa)),
                Average(g.Select(x => x.Chunk.Fused.Combined)),
                g.Count()))
            .OrderBy(s => s.Speaker.Index < 0 ? int.MaxValue : s.Speaker.Index)
            .ToList();
    }

    private static ValenceArousal Average(IEnumerable<ValenceArousal> values)
    {
        var list = values.ToList();
        return ValenceArousal.Clamp(list.Average(v => v.Valence), list.Average(v => v.Arousal));
    }
}
