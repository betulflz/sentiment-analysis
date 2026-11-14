using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Decision;

/// <summary>Konuşma payı ve tempo anlık görüntüsü (12). <paramref name="HostShare"/>: son
/// <see cref="DecisionConfig.TalkWindowSeconds"/> sn'de konuşmanın yönetene düşen payı (0..1); yeterli konuşma yoksa null.
/// <paramref name="HostWordsPerSecond"/>: son parçada yönetenin konuşma hızı (kelime / konuşma sn); az konuştuysa null.</summary>
public sealed record TalkSnapshot(double? HostShare, double HostSeconds, double CounterpartSeconds, double? HostWordsPerSecond)
{
    public static readonly TalkSnapshot Unknown = new(null, 0, 0, null);
}

/// <summary>Canlı konuşma payı ölçer (12): parçaların rol çizelgelerinden kayan pencerede yöneten / karşı taraf
/// konuşma sürelerini toplar. Parçalar ~2 sn örtüştüğü için her saniye BİR kez sayılır (sayılan son zamandan sonrası).
/// Durumlu; görüşme başında <see cref="Reset"/>. Rolsüz parça (ses tanıtılmamış) payı değiştirmez.</summary>
public sealed class TalkMeter
{
    private const double MinRateSeconds = 2.0;   // hız için en az bu kadar konuşma (kısa "evet"ler hızı bozmasın)
    private readonly DecisionConfig _cfg;
    private readonly List<(TimeSpan End, double Host, double Other)> _slices = new();
    private TimeSpan _countedUntil = TimeSpan.MinValue;

    public TalkMeter(DecisionConfig cfg) => _cfg = cfg;

    public void Reset()
    {
        _slices.Clear();
        _countedUntil = TimeSpan.MinValue;
    }

    public TalkSnapshot Add(ChunkResult chunk)
    {
        if (chunk.Roles is not { Enrolled: true } roles) return TalkSnapshot.Unknown;

        var from = chunk.TimeStart > _countedUntil ? chunk.TimeStart : _countedUntil;
        if (chunk.TimeEnd > from)
        {
            _slices.Add((chunk.TimeEnd, roles.Seconds(SpeakerRole.Yoneten, from, chunk.TimeEnd),
                roles.Seconds(SpeakerRole.KarsiTaraf, from, chunk.TimeEnd)));
            _countedUntil = chunk.TimeEnd;
        }
        var horizon = chunk.TimeEnd - TimeSpan.FromSeconds(_cfg.TalkWindowSeconds);
        _slices.RemoveAll(s => s.End <= horizon);

        double host = _slices.Sum(s => s.Host), other = _slices.Sum(s => s.Other);
        double? share = host + other >= _cfg.TalkMinSeconds ? host / (host + other) : null;

        double hostSec = roles.Seconds(SpeakerRole.Yoneten, chunk.TimeStart, chunk.TimeEnd);
        int hostWords = chunk.Segments.Where(s => roles.RoleOf(s.Start, s.End) == SpeakerRole.Yoneten)
            .Sum(s => s.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
        double? rate = hostSec >= MinRateSeconds ? hostWords / hostSec : null;

        return new TalkSnapshot(share, host, other, rate);
    }
}
