using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Decision;

/// <summary>Karar motorunun Türkçe adları ve gerekçe metni. Core dil-nötr kalır (TurkishSummaryWriter
/// deseni). Gerekçe GÖZLEMİ söyler, kişiyi etiketlemez (spec §4 anti-hedef).</summary>
public static class DecisionText
{
    public static string Name(Criterion c) => c switch
    {
        Criterion.Gerilim => "gerilim",
        Criterion.IlgiKaybi => "ilgi kaybı",
        Criterion.OlumsuzDuygu => "olumsuz duygu",
        Criterion.Uyumsuzluk => "söz–ton–yüz uyumsuzluğu",
        Criterion.DusukEnerji => "düşük enerji",
        Criterion.Kotulesme => "kötüleşen gidişat",
        _ => c.ToString(),
    };

    public static string Name(LiveAction a) => a switch
    {
        LiveAction.DevamEt => "Devam et",
        LiveAction.KonuyuDegistir => "Konuyu değiştir",
        LiveAction.SoruSor => "Açık uçlu / netleştirici soru sor",
        LiveAction.EmpatiKur => "Empati kur, sakinleştir",
        LiveAction.AraVer => "Kısa ara ver",
        LiveAction.KapanisaGec => "Kapanışa geç",
        _ => a.ToString(),
    };

    public static string Name(FollowUpAction a) => a switch
    {
        FollowUpAction.TeklifGonder => "Teklif gönder",
        FollowUpAction.IkinciGorusme => "İkinci görüşme ayarla",
        FollowUpAction.ItirazBilgisi => "İtirazları ele alan bilgi gönder",
        FollowUpAction.Beklet => "Beklet, acele etme",
        FollowUpAction.DegerlendirmeyeGec => "Değerlendirmeye geç",
        FollowUpAction.EkGorusme => "Ek / teknik görüşme planla",
        FollowUpAction.GorusmeyiTekrarla => "Görüşmeyi daha rahat bir ortamda tekrarla",
        FollowUpAction.KararEpostasi => "Karar e-postası gönder",
        FollowUpAction.BireBirGorusme => "Birebir görüşme ayarla",
        FollowUpAction.TakipToplantisi => "Takip toplantısı planla",
        _ => a.ToString(),
    };

    public static string Name(MeetingProfile p) => p switch
    {
        MeetingProfile.Satis => "Satış / müşteri",
        MeetingProfile.Mulakat => "Mülakat",
        MeetingProfile.Toplanti => "Toplantı",
        _ => p.ToString(),
    };

    public static string Subject(int? face) => face is int i ? $"Yüz {i + 1}" : "Görüşme geneli";

    /// <summary>"{Kişi}: {kriter} %{değer}, {kriter} %{değer}." — ikinci kriter 0,2'nin altındaysa yazılmaz.</summary>
    public static string Reason(Indicators ind, IReadOnlyList<Criterion> top)
    {
        if (top.Count == 0) return "Belirgin bir sorun yok.";
        var first = top[0];
        var parts = new List<string> { Part(ind, first) };
        if (top.Count > 1 && ind.Values[(int)top[1]] >= 0.2) parts.Add(Part(ind, top[1]));
        return $"{Subject(ind.SourceFace[(int)first])}: {string.Join(", ", parts)}.";
    }

    private static string Part(Indicators ind, Criterion c) =>
        $"{Name(c)} %{Math.Round(100 * ind.Values[(int)c], MidpointRounding.AwayFromZero)}";
}
