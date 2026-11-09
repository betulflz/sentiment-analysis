using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Ayrım modelinin uydurduğu "hayalet" konuşmacıları ayıklar: toplam konuşmanın %8'inden
/// azını VE 6 sn'den kısasını alan küme gerçek bir kişi sayılmaz, bölgeleri
/// <see cref="SpeakerId.Unknown"/> olur; kalanlar ilk konuşma sırasına göre yeniden numaralanır.
/// <para>Neden: 2026-11-09 canlı testinde 2 kişiye 4 konuşmacı çıktı (2 sn %3 ve 5 sn %6 kırıntı).
/// Uydurma kişi göstermek yerine dürüst "?" (master §12.2). Uzun konuşan ama payı küçük gerçek bir
/// katılımcı (6 sn ve üstü) korunur.</para> Saf.</summary>
public static class GhostSpeakerFilter
{
    private const double MinShare = 0.08;
    private static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(6);

    public static IReadOnlyList<SpeakerSegment> Apply(IReadOnlyList<SpeakerSegment> segments)
    {
        var shares = TalkBalance.Compute(segments);
        if (shares.Count <= 1) return segments;
        var ghosts = shares.Where(s => s.Share < MinShare && s.Duration < MinDuration)
            .Select(s => s.Speaker).ToHashSet();
        if (ghosts.Count == 0) return segments;
        var kept = segments.Select(s => ghosts.Contains(s.Speaker) ? s with { Speaker = SpeakerId.Unknown } : s).ToList();
        return SpeakerRenumberer.ByFirstAppearance(kept);
    }
}
