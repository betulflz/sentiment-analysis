namespace SentimentAnalysis.Core;

/// <summary>Oturum-içi anonim konuşmacı kimliği (alt-proje 10). Toplantılar arası TAŞINMAZ:
/// kalıcı ses parmak izi saklanmaz (spec 10 S4, master §12.5 gizlilik varsayılanı) — bir sonraki
/// toplantının "Konuşmacı A"sı başka biri olabilir.
/// <para><see cref="Unknown"/> = atanamadı (örtüşen konuşma, eşik altı örtüşme veya hiç konuşmacı
/// bölgesi yok). Uydurma atama yerine dürüst belirsizlik (master §12.2).</para></summary>
public readonly record struct SpeakerId(int Index)
{
    public static readonly SpeakerId Unknown = new(-1);

    /// <summary>Kullanıcı isim vermediyse gösterilecek etiket. 26 konuşmacıdan sonra harf
    /// biter, sayıya döner (Z'den sonra bozuk karakter üretmemek için).</summary>
    public string DefaultLabel => Index < 0
        ? "Bilinmeyen"
        : Index < 26 ? $"Konuşmacı {(char)('A' + Index)}" : $"Konuşmacı {Index + 1}";
}

/// <summary>Diarization'ın ürettiği konuşmacı bölgesi. Zamanlar MUTLAK (oturum başlangıcına
/// göre) — <see cref="TranscriptSegment"/> ile aynı eksende olmalıdır, yoksa hizalama sessizce
/// yanlış sonuç verir.</summary>
public sealed record SpeakerSegment(SpeakerId Speaker, TimeSpan Start, TimeSpan End);

/// <summary>Canlı görüşmede bir konuşma diliminin rolü (12): görüşmeyi yöneten (sesi başta tanıtılan) kişi mi,
/// karşı taraf mı. <see cref="Belirsiz"/> = ses henüz tanıtılmadı.</summary>
public enum SpeakerRole { Belirsiz, Yoneten, KarsiTaraf }

/// <summary>Rolü belli bir konuşma dilimi. Zamanlar MUTLAK. <paramref name="Similarity"/> tanıtma örneğine
/// (yumuşatılmış) kosinüs benzerliğidir.</summary>
public sealed record RoleSpan(TimeSpan Start, TimeSpan End, SpeakerRole Role, double Similarity);

/// <summary>Bir ses parçasının rol çizelgesi (12). Yalnız konuşma olan dilimleri içerir (sessizlik boşluktur).
/// <paramref name="EnrollmentDoubtful"/>: tanıtma örneği kendi içinde tutarsız — tanıtma sırasında iki kişi
/// konuşmuş olabilir; kullanıcı uyarılmalıdır.</summary>
public sealed record RoleTimeline(IReadOnlyList<RoleSpan> Spans, bool Enrolled, bool EnrollmentDoubtful)
{
    public static readonly RoleTimeline NotEnrolled = new(System.Array.Empty<RoleSpan>(), false, false);

    /// <summary>[from, to) aralığında en çok konuşan rol (transkript satırını role atamak için). Hiç konuşma yoksa
    /// ya da iki rol eşitse <see cref="SpeakerRole.Belirsiz"/> — belirsiz söz belirli bir kişiye yazılmaz.</summary>
    public SpeakerRole RoleOf(TimeSpan from, TimeSpan to)
    {
        double host = Seconds(SpeakerRole.Yoneten, from, to), other = Seconds(SpeakerRole.KarsiTaraf, from, to);
        if (host > other) return SpeakerRole.Yoneten;
        if (other > host) return SpeakerRole.KarsiTaraf;
        return SpeakerRole.Belirsiz;
    }

    /// <summary>[from, to) aralığında <paramref name="role"/> ile etiketli konuşma süresi (sn).</summary>
    public double Seconds(SpeakerRole role, TimeSpan from, TimeSpan to)
    {
        double total = 0;
        foreach (var s in Spans)
            if (s.Role == role)
                total += System.Math.Max(0, (System.Math.Min(to.Ticks, s.End.Ticks) - System.Math.Max(from.Ticks, s.Start.Ticks)) / (double)System.TimeSpan.TicksPerSecond);
        return total;
    }
}
