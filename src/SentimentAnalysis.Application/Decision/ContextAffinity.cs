using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Decision;

/// <summary>Bir eylemin o anki duygu / tempo / hareket bağlamına ne kadar uyduğu (−0,15..1). TOPSIS yalnız 6 göstergeye
/// baktığı için hep aynı birkaç eylemi (özellikle genel "soru sor") öne çıkarıyordu; bu katman eylemi
/// baskın duyguya (kızgın, üzgün, gergin, sıkılmış…), konuşma hızına, baş çevirmeye ve gülümsemeye göre ayırır.
/// <para>Uzman varsayımı, KALİBRE EDİLMEMİŞ. Deterministik ve durumsuzdur.</para></summary>
public static class ContextAffinity
{
    private const double FastSpeech = 2.8;   // kelime/sn: bunun üstü "hızlı konuşuyor"
    private const double SlowSpeech = 0.9;   // bunun altı "yavaş / kısık"

    public static double Of(LiveAction action, ContextCues c, double incongruence)
    {
        double emo = ByEmotion(action, c.Primary);
        double fast = c.WordsPerSecond >= FastSpeech ? 1 : 0;
        double slow = c.WordsPerSecond <= SlowSpeech && c.Arousal < 0.5 ? 1 : 0;
        double away = c.LookAway;
        double mixed = c.Smile >= 0.4 && (c.Valence < -0.15 || c.FaceNegative >= 0.3) ? 1 : 0;   // gülüyor ama içi olumsuz

        double motion = action switch
        {
            LiveAction.GozTemasiKur      => away,
            LiveAction.KonuyuDegistir    => 0.7 * away,
            LiveAction.EnerjiYukselt     => Math.Max(0.6 * away, slow),
            LiveAction.KisaTut           => Math.Max(0.5 * away, 0.7 * slow),
            LiveAction.FaydayaDon        => 0.6 * away,
            LiveAction.SozVer            => 0.8 * away,
            LiveAction.OrnekleAnlat      => 0.5 * away,
            LiveAction.YavaslaKonus      => fast,
            LiveAction.SakinOl           => 0.7 * fast,
            LiveAction.KolaySoruSor      => slow,
            LiveAction.SusVeDinle        => 0.5 * slow,
            LiveAction.DuyguyuAdlandir   => mixed,
            LiveAction.TereddudunuSor    => 0.8 * mixed,
            _ => 0,
        };

        // Açık bir olumsuz duygu varken (kızgın/gergin/üzgün) çelişki bir ses artefaktıdır; soru değil duyguya yanıt verilir.
        bool clear = c.Primary is not (Emotion.Kizgin or Emotion.Gergin or Emotion.Uzgun);

        // Genel "soru sor" yalnız söz–his çelişkisi belirginse; yoksa hafif geride kalır (hep aynı öneri çıkmasın).
        double generic = action switch
        {
            LiveAction.SoruSor => clear && incongruence >= 0.5 ? 0.5 * incongruence : -0.15,
            LiveAction.TereddudunuSor or LiveAction.YenidenAnlat => clear && incongruence >= 0.5 ? 0.3 * incongruence : -0.1,
            _ => 0,
        };

        return Math.Max(-0.15, Math.Min(1, Math.Max(emo, motion) + generic));
    }

    private static double ByEmotion(LiveAction a, Emotion e) => e switch
    {
        Emotion.Kizgin => a switch
        {
            LiveAction.SakinOl => 1, LiveAction.ItiraziKabulEt => 1, LiveAction.OzetleDinle => 0.8,
            LiveAction.YavaslaKonus => 0.7, LiveAction.AraVer => 0.6, LiveAction.EmpatiKur => 0.6, _ => 0,
        },
        Emotion.Gergin => a switch
        {
            LiveAction.GuvenceVer => 1, LiveAction.SakinOl => 0.8, LiveAction.YavaslaKonus => 0.8,
            LiveAction.MizahlaGevset => 0.7, LiveAction.AraVer => 0.6, LiveAction.TebessumEt => 0.5, _ => 0,
        },
        Emotion.Uzgun => a switch
        {
            LiveAction.EmpatiKur => 1, LiveAction.SusVeDinle => 0.9, LiveAction.DuyguyuAdlandir => 0.8,
            LiveAction.TebessumEt => 0.6, LiveAction.GuvenceVer => 0.6, LiveAction.KolaySoruSor => 0.5, _ => 0,
        },
        Emotion.Sikilmis => a switch
        {
            LiveAction.EnerjiYukselt => 1, LiveAction.KonuyuDegistir => 0.8, LiveAction.OrnekleAnlat => 0.8,
            LiveAction.MizahlaGevset => 0.7, LiveAction.KisaTut => 0.7, LiveAction.GozTemasiKur => 0.6,
            LiveAction.SozVer => 0.6, LiveAction.FaydayaDon => 0.6, _ => 0,
        },
        Emotion.Notr => a switch   // belirsiz / donuk: nazik merak
        {
            LiveAction.TereddudunuSor => 0.6, LiveAction.DuyguyuAdlandir => 0.5, LiveAction.OrnekleAnlat => 0.4,
            LiveAction.AciklayiciOl => 0.4, _ => 0,
        },
        Emotion.Sakin => a switch
        {
            LiveAction.AciklayiciOl => 0.6, LiveAction.OrtakNoktaHatirlat => 0.5, LiveAction.OrnekleAnlat => 0.4, _ => 0,
        },
        Emotion.Mutlu or Emotion.Cosakulu => a switch
        {
            LiveAction.OrtakNoktaHatirlat => 0.8, LiveAction.KapanisaGec => 0.7, LiveAction.AciklayiciOl => 0.4, _ => 0,
        },
        _ => 0,
    };
}
