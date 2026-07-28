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
