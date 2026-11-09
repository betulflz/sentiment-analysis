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
        var shares = TalkBalance.Compute(segments);          // süresiz bölgeler paya girmez
        var measurable = shares.Select(s => s.Speaker).ToHashSet();
        // Ölçülebilir süresi hiç olmayan kimlik de hayalettir; tek kişi varsa paya bakılmaz.
        var ghosts = shares.Count <= 1
            ? new HashSet<SpeakerId>()
            : shares.Where(s => s.Share < MinShare && s.Duration < MinDuration).Select(s => s.Speaker).ToHashSet();
        var cleaned = segments.Select(s =>
            s.Speaker.Index >= 0 && (ghosts.Contains(s.Speaker) || !measurable.Contains(s.Speaker))
                ? s with { Speaker = SpeakerId.Unknown } : s).ToList();
        return SpeakerRenumberer.ByFirstAppearance(cleaned);  // her yolda kanonik numara
    }
}
