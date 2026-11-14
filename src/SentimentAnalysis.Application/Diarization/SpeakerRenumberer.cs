using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Diarization motorunun keyfi küme kimliklerini konuşma sırasına göre kanonik
/// numaralara çevirir: ilk konuşan 0 ("Konuşmacı A"), ikinci 1 ("Konuşmacı B")… Boşluk bırakmaz.
/// <para><b>Neden gerekli:</b> sherpa-onnx küme kimliklerini keyfi verir; ilk konuşana 0
/// verdiği garanti değildir. Yeniden numaralandırılmazsa ilk konuşan kişi "Konuşmacı F"
/// olarak görünür ve kullanıcı bunu rastgele isim atama sanır.</para>
/// <para><see cref="SpeakerId.Unknown"/> değişmeden geçer ve numara TÜKETMEZ — o bir
/// konuşmacı değil, "atanamadı" durumudur.</para>
/// Saf ve deterministik.</summary>
public static class SpeakerRenumberer
{
    public static IReadOnlyList<SpeakerSegment> ByFirstAppearance(IReadOnlyList<SpeakerSegment> segments)
    {
        var map = new Dictionary<int, int>();
        int next = 0;

        // Numaralandırma sırası ZAMANA göredir; girdi listesinin sırasına güvenilmez.
        foreach (var seg in segments.OrderBy(s => s.Start).ThenBy(s => s.End))
        {
            if (seg.Speaker.Index < 0) continue;              // Bilinmeyen numara tüketmez
            if (map.ContainsKey(seg.Speaker.Index)) continue;
            map[seg.Speaker.Index] = next++;
        }

        // Çıktı GİRDİ sırasını korur: yalnızca kimlikler değişir, sıralama çağıranın işidir.
        return segments
            .Select(s => s.Speaker.Index < 0 ? s : s with { Speaker = new SpeakerId(map[s.Speaker.Index]) })
            .ToList();
    }
}
