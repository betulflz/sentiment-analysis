using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Bir konuşmacının kendi sesinden ölçülmüş prozodi duygusu.
/// <paramref name="AnalyzedSeconds"/> saydamlık için taşınır: az sese dayanan bir ortalamaya
/// fazla güvenilmemelidir (master §12.2).</summary>
public sealed record SpeakerProsodySummary(
    SpeakerId Speaker, ValenceArousal Va, double AnalyzedSeconds, int RegionCount);

/// <summary>Bir konuşmacının hangi konuşma bölgelerinin analiz edileceğini seçer. Saf ve
/// deterministik: uzun bölgeler önce (daha çok bağlam), süre bütçesi dolunca durulur.
/// <para><b>Neden bütçeye sığmayan bölge atılmaz, KIRPILIR:</b> eski kural "sığmıyorsa atla"
/// idi. Tek konuşma bölgesi bütçeden uzun olan bir konuşmacıda (ör. 90 sn'lik tek bölge, 60 sn
/// bütçe) bu kural bölgeyi eler, başka bölge de olmadığı için o konuşmacı için HİÇ prozodi
/// üretilmezdi — yani en çok konuşan, dolayısıyla en çok sese sahip kişi ölçümsüz kalırdı.
/// Tasarım "60 saniyeyi aşmayacak şekilde durulur" der; "sığmayanı tamamen at" demez.</para></summary>
public static class SpeakerRegionSelector
{
    /// <summary>Bölgeleri uzundan kısaya (eşitlikte erken başlayan önce) tarar. Kalan bütçeye
    /// tam sığan bölge olduğu gibi, sığmayan bölge ise kalan bütçe kadar KIRPILMIŞ bir önek
    /// olarak (bölgenin başından itibaren) alınır ve seçim orada DURUR — kırpma zaten bütçeyi
    /// doldurmuştur. Kırpılmış önek <paramref name="minSeconds"/>'tan kısa kalıyorsa alınmaz
    /// (o kadar kısa sesten güvenilir prozodi çıkmaz) ve seçim yine durur.</summary>
    /// <returns>Kırpma, döndürülen <see cref="SpeakerSegment"/>'in <c>End</c> değerine yansır:
    /// bildirilen süre ile gerçekten analiz edilen ses aynı olur.</returns>
    public static IReadOnlyList<SpeakerSegment> Select(
        IEnumerable<SpeakerSegment> regions, double minSeconds, double budgetSeconds)
    {
        var selected = new List<SpeakerSegment>();
        double used = 0;

        // Uzun önce; eşitlikte erken başlayan önce → sonuç koşudan koşuya değişmez.
        foreach (var r in regions
            .Where(r => (r.End - r.Start).TotalSeconds >= minSeconds)
            .OrderByDescending(r => (r.End - r.Start).TotalSeconds)
            .ThenBy(r => r.Start))
        {
            double length = (r.End - r.Start).TotalSeconds;

            if (used + length <= budgetSeconds)
            {
                selected.Add(r);
                used += length;
                continue;
            }

            // Sığmıyor: kalan bütçe kadar önek al ve DUR.
            double remaining = budgetSeconds - used;
            if (remaining >= minSeconds)
                selected.Add(r with { End = r.Start + TimeSpan.FromSeconds(remaining) });

            break;
        }

        return selected;
    }
}

/// <summary>Konuşmacı-başına duyguyu, kişinin KENDİ konuşma bölgelerinin sesinden ölçer
/// (10d). Oturum sonrası arka plan geçişinde çalışır — canlı hattı yavaşlatmaz.
/// <para><b>Neden chunk ortalaması değil:</b> 10–30 sn'lik chunk'lar gerçek bir diyalogda
/// birden fazla kişiyi içerir; hiçbirine örtüşme eşiği yetişmediğinde chunk ölçümden tamamen
/// düşerdi — yani en çok karşılıklı konuşulan anlar sistematik olarak elenirdi.</para></summary>
public sealed class SpeakerProsodyAnalyzer
{
    private const int SampleRate = 16000;

    private readonly IProsodyEmotionModel _prosody;
    private readonly DiarizationConfig _cfg;

    public SpeakerProsodyAnalyzer(IProsodyEmotionModel prosody, AppConfig config)
    {
        _prosody = prosody;
        _cfg = config.Diarization;
    }

    public async Task<IReadOnlyList<SpeakerProsodySummary>> AnalyzeAsync(
        float[] mono16k, IReadOnlyList<SpeakerSegment> speakers, CancellationToken ct)
    {
        var summaries = new List<SpeakerProsodySummary>();

        // Bilinmeyen bir konuşmacı değil, "atanamadı" durumudur: sesi ölçülmez.
        foreach (var group in speakers
            .Where(s => s.Speaker.Index >= 0)
            .GroupBy(s => s.Speaker)
            .OrderBy(g => g.Key.Index))
        {
            ct.ThrowIfCancellationRequested();

            var regions = SpeakerRegionSelector.Select(
                group, _cfg.SpeakerProsodyMinRegionSeconds, _cfg.SpeakerProsodyBudgetSeconds);

            double weightedValence = 0, weightedArousal = 0, totalSeconds = 0;
            int used = 0;

            foreach (var region in regions)
            {
                var slice = Slice(mono16k, region);
                if (slice.Length < _cfg.SpeakerProsodyMinRegionSeconds * SampleRate) continue;

                var chunk = new AudioChunk(slice, SampleRate, region.Start, region.End);
                var emotion = await _prosody.AnalyzeAsync(chunk, ct).ConfigureAwait(false);

                double seconds = slice.Length / (double)SampleRate;
                weightedValence += emotion.Va.Valence * seconds;
                weightedArousal += emotion.Va.Arousal * seconds;
                totalSeconds += seconds;
                used++;
            }

            // Yeterli sesi olmayan konuşmacı için sonuç ÜRETİLMEZ — uydurma ortalama yerine yokluk.
            if (used == 0 || totalSeconds <= 0) continue;

            summaries.Add(new SpeakerProsodySummary(
                group.Key,
                ValenceArousal.Clamp(weightedValence / totalSeconds, weightedArousal / totalSeconds),
                totalSeconds,
                used));
        }

        return summaries;
    }

    /// <summary>Bölgeyi ses dizisinden keser; sınır dışına taşan bölge kırpılır (diarization
    /// zaman damgaları saklanan sesten uzun olabilir).</summary>
    private static float[] Slice(float[] mono16k, SpeakerSegment region)
    {
        int start = Math.Clamp((int)(region.Start.TotalSeconds * SampleRate), 0, mono16k.Length);
        int end = Math.Clamp((int)(region.End.TotalSeconds * SampleRate), start, mono16k.Length);
        return mono16k[start..end];
    }
}
