using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Dosya modunda rol etiketi (karşı tarafa odak, 2026-11-13): analizden ÖNCE yapılan konuşmacı ayrımının
/// bölgelerinden, kullanıcının "Siz hangisiniz?" seçimiyle (<paramref name="host"/>) rol çizelgesi üretir.
/// Seçilen = <see cref="SpeakerRole.Yoneten"/>, diğer konuşmacılar = <see cref="SpeakerRole.KarsiTaraf"/>;
/// <see cref="SpeakerId.Unknown"/> (örtüşme/kırıntı) rolsüz bırakılır — yönetenin sesi karşı tarafa karışmasın.</summary>
public sealed class DiarizationRoleLabeler(IReadOnlyList<SpeakerSegment> segments, SpeakerId host, TimeSpan? coveredUntil = null) : IRoleLabeler
{
    /// <summary><paramref name="coveredUntil"/>: ayrımın kapsadığı son an (uzun dosya sınırda kırpıldıysa). Sonrasındaki
    /// parçalar rolsüz döner → karışık yol ve rapordaki "kısmen ayrıldı" notu (kuyruk sessizce özetten düşmesin).</summary>
    public RoleTimeline Label(AudioChunk chunk)
    {
        if (coveredUntil is TimeSpan limit && chunk.Start >= limit)
            return new RoleTimeline(Array.Empty<RoleSpan>(), Enrolled: false, EnrollmentDoubtful: false);
        var spans = new List<RoleSpan>();
        foreach (var s in segments)
        {
            if (s.Speaker == SpeakerId.Unknown) continue;
            var start = s.Start > chunk.Start ? s.Start : chunk.Start;
            var end = s.End < chunk.End ? s.End : chunk.End;
            if (end <= start) continue;
            spans.Add(new RoleSpan(start, end, s.Speaker == host ? SpeakerRole.Yoneten : SpeakerRole.KarsiTaraf, 1.0));
        }
        spans.Sort((x, y) => x.Start.CompareTo(y.Start));
        return new RoleTimeline(spans, Enrolled: true, EnrollmentDoubtful: false);
    }

    public void Reset() { }
}
