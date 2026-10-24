using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Bir konuşmacının toplam konuşma süresi ve payı (0..1).</summary>
public sealed record TalkShare(SpeakerId Speaker, TimeSpan Duration, double Share);

/// <summary>Konuşma dengesi (spec 11 §6.6): konuşmacı bölgelerinin sürelerinden kim ne kadar konuştu.
/// <see cref="SpeakerId.Unknown"/> paya girmez — atanamayan süre kimseye yazılmaz. Saf.</summary>
public static class TalkBalance
{
    /// <summary>Süreye göre azalan; eşitlikte konuşmacı sırası.</summary>
    public static IReadOnlyList<TalkShare> Compute(IReadOnlyList<SpeakerSegment> segments)
    {
        var bySpeaker = segments
            .Where(s => s.Speaker.Index >= 0 && s.End > s.Start)
            .GroupBy(s => s.Speaker)
            .Select(g => (Speaker: g.Key, Duration: TimeSpan.FromTicks(g.Sum(s => (s.End - s.Start).Ticks))))
            .ToArray();
        double total = bySpeaker.Sum(x => x.Duration.TotalSeconds);
        if (total <= 0) return Array.Empty<TalkShare>();

        return bySpeaker
            .OrderByDescending(x => x.Duration).ThenBy(x => x.Speaker.Index)
            .Select(x => new TalkShare(x.Speaker, x.Duration, x.Duration.TotalSeconds / total))
            .ToArray();
    }
}
