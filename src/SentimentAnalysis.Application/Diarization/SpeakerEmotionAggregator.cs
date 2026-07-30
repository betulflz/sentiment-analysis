using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Bir konuşmacıya atanan chunk'ların duygu ortalaması: metin, ses ve nihai (10c).
/// <paramref name="ChunkCount"/> saydamlık için taşınır — az chunk'lı bir ortalamaya aşırı
/// güvenilmemeli.
/// <para><paramref name="UncertainChunkCount"/>: bu ortalamaya giren chunk'lardan kaçının
/// konuşmacı ataması eşik-altı örtüşmeye dayandığı (<see cref="AlignedChunk.Uncertain"/>).
/// <b>Neden ayrı sayılır:</b> hizalama artık eşik-altı chunk'ları elemek yerine en iyi adaya
/// atıyor; bu sayı taşınmazsa eskiden düşen chunk'lar ortalamaya hiçbir iz bırakmadan girer ve
/// kullanıcı ortalamanın ne kadarının tahmine dayandığını göremezdi (master §12.2 — sistem
/// emin olmadığı yerde emin gibi davranmaz). <paramref name="ChunkCount"/> semantiği değişmez:
/// o hâlâ TÜM chunk'ların sayısıdır, belirsizler onun bir alt kümesidir.</para></summary>
public sealed record SpeakerEmotionSummary(
    SpeakerId Speaker,
    ValenceArousal Text,
    ValenceArousal Audio,
    ValenceArousal Combined,
    int ChunkCount,
    int UncertainChunkCount = 0);

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
                g.Count(),
                // Belirsiz atamalar ortalamaya KATILIR (veri kaybı olmaz), ama sayıları
                // ayrıca taşınır ki rapor "bu ortalamanın N ölçümü tahmine dayanıyor" diyebilsin.
                g.Count(x => x.Uncertain)))
            .OrderBy(s => s.Speaker.Index < 0 ? int.MaxValue : s.Speaker.Index)
            .ToList();
    }

    private static ValenceArousal Average(IEnumerable<ValenceArousal> values)
    {
        var list = values.ToList();
        return ValenceArousal.Clamp(list.Average(v => v.Valence), list.Average(v => v.Arousal));
    }
}
