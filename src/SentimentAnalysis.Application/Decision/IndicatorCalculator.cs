using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Decision;

/// <summary>Bir ses penceresinin füzyon sonucu + aynı aralıktaki yüz özetlerinden K1..K6 ihtiyaç
/// değerlerini üretir (spec 11 §6.3). Saf, deterministik; her değer 0..1.
/// <para>Yüze bağlı kriterlerde (K1–K4) yüzler arası EN YÜKSEK değer alınır — en kötü durumdaki kişi
/// kararı sürükler ve gerekçede adıyla anılır. Ses/metin bir yüze bağlanamadığı için her yüzün
/// değerine aynı biçimde katılır. Yüz ifadesinin K1'deki payı düşüktür (0,3): 11a ölçümünde kaş
/// çatma "kızgın" değil "üzgün" okundu, gerilimin asıl taşıyıcısı sestir.</para></summary>
public static class IndicatorCalculator
{
    /// <summary>Eğilim (K6) serisine eklenecek konuşma valence'ı: ses tanıtıldıysa KARŞI TARAFIN; karşı taraf bu parçada
    /// konuşmadıysa null (yönetenin sesi karşı tarafın gidişatına yazılmaz).</summary>
    public static double? TrendValence(ChunkResult chunk) =>
        chunk.Roles is not null && chunk.Counterpart is null ? null : (chunk.Counterpart ?? chunk.Fused).SpeechValence;

    /// <param name="talk">12: konuşma payı / yönetenin hızı (bağlam ipuçlarına girer); null = bilinmiyor.</param>
    public static Indicators Compute(ChunkResult chunk, IReadOnlyList<FaceWindowSummary> faces,
        IReadOnlyList<double> previousValences, DecisionConfig cfg, TalkSnapshot? talk = null)
    {
        // 12: ses tanıtıldıysa ses/metin göstergeleri KARŞI TARAFIN sesinden; bu parçada konuşmadıysa ses kanalı nötr
        // (değeri yok) — yönetenin gerginliği "karşı taraf gergin" diye okunmaz. Veri kalitesi yine parçanın ses
        // kalitesidir: kayıt iyi, yalnızca karşı taraf susuyor.
        var roles = chunk.Roles;
        bool counterpartSilent = roles is not null && chunk.Counterpart is null;
        var f = chunk.Counterpart ?? chunk.Fused;
        // Yüz K1–K4'e aşağıda yüz özetiyle girer; üç kaynaklı Combined değil konuşma valence'ı alınır (çift sayım yok).
        double a = counterpartSilent ? 0.5 : f.Combined.Arousal;
        // Ses modeli yüksek sesli/hızlı konuşmayı (öfke dahil) "olumlu" okuyabiliyor (gerçek kayıtta "Sinirliyim"
        // derken ses valence'ı +0,7…+1,0, metin −0,7). Söz açıkça olumsuzsa ve metin güvenilirse olumsuzluk
        // metinden alınır: konuşma valence'ı metnin altına inebilir ama sesin şişirdiği pozitife güvenilmez.
        double v = counterpartSilent ? 0 : f.SpeechValence;
        if (!counterpartSilent && f.TextReliability >= 0.15 && f.TextVa.Valence <= -0.2) v = Math.Min(v, f.TextVa.Valence);
        double congruence = counterpartSilent ? 1 : f.Congruence;
        double aq = Math.Max(chunk.Fused.AudioReliability, chunk.Fused.TextReliability);
        if (!counterpartSilent) aq = Math.Max(f.AudioReliability, f.TextReliability);
        // Konuşma valence'ının yüze karşı ağırlığı: karşı taraf susuyorsa ses bilgisi yok, yüz tek kaynak.
        double speechWeight = counterpartSilent ? 0 : aq;
        double audioTension = C01((a - 0.5) * 2) * C01(-v);

        var values = new double[6];
        var source = new int?[6];

        if (faces.Count == 0)
        {
            values[(int)Criterion.Gerilim] = audioTension;
            values[(int)Criterion.IlgiKaybi] = 0;
            values[(int)Criterion.OlumsuzDuygu] = C01(-v);
            values[(int)Criterion.Uyumsuzluk] = C01(1 - congruence);
        }
        else
        {
            MaxOverFaces(faces, Criterion.Gerilim, values, source,
                x => 0.7 * audioTension + 0.3 * x.NegativeExpression * x.Reliability);
            MaxOverFaces(faces, Criterion.IlgiKaybi, values, source,
                x => x.LookAwayRatio * C01(x.VisibleRatio / 0.5));
            MaxOverFaces(faces, Criterion.OlumsuzDuygu, values, source, x =>
            {
                double wf = 0.5 * x.Reliability, den = speechWeight + wf;
                return C01(-(den <= 0 ? 0 : (speechWeight * v + wf * x.ValenceShift) / den));
            });
            MaxOverFaces(faces, Criterion.Uyumsuzluk, values, source,
                x => Math.Max(1 - congruence, counterpartSilent ? 0 : C01(Math.Abs(x.ValenceShift - v)) * x.Reliability));
        }

        double wps;
        if (roles is null)
        {
            double seconds = (chunk.TimeEnd - chunk.TimeStart).TotalSeconds;
            int words = chunk.Transcript.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            wps = seconds <= 0 ? 0 : words / seconds;
        }
        else
        {
            // Karşı tarafın konuşma hızı: kendi sözleri / kendi konuşma süresi (yönetenin sözü sayılmaz).
            double sec = roles.Seconds(SpeakerRole.KarsiTaraf, chunk.TimeStart, chunk.TimeEnd);
            int words = chunk.Segments.Where(x => roles.RoleOf(x.Start, x.End) == SpeakerRole.KarsiTaraf)
                .Sum(x => x.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
            wps = sec < 1 ? 0 : words / sec;
        }
        // Karşı taraf susuyorsa "düşük enerji" ölçülemez (dinliyor olabilir); konuşma payı ayrıca ele alınır.
        values[(int)Criterion.DusukEnerji] = counterpartSilent ? 0 : C01(0.5 * C01((0.5 - a) * 2) + 0.5 * C01(1 - wps / 2));

        var series = (counterpartSilent ? previousValences : previousValences.Append(v)).TakeLast(cfg.TrendWindows).ToArray();
        values[(int)Criterion.Kotulesme] = series.Length < 3 ? 0 : C01(-Slope(series) * 5);

        // En iyi görülen yüz: anlık yanlış algılanan bir "hayalet" yüz ortalamayı (ve kararı) düşürmesin.
        double faceQ = faces.Count == 0 ? 0 : faces.Max(x => C01(x.VisibleRatio) * C01(x.Reliability));
        double quality = C01(0.6 * C01(aq) + 0.4 * faceQ);

        // Olumluluk: konuşma valence'ı (ses+söz) ve en çok gülümseyen güvenilir yüz. Yüz yoksa yalnız konuşma.
        double smile = faces.Count == 0 ? 0 : faces.Max(x => C01(x.SmileRatio) * C01(x.Reliability));
        double positivity = faces.Count == 0 ? C01(v) : 0.6 * C01(v) + 0.4 * smile;

        var cues = new ContextCues(counterpartSilent ? Emotion.Notr : CueEmotion(f.PrimaryEmotion, v, a), C01(a), Math.Clamp(v, -1, 1), wps,
            faces.Count == 0 ? 0 : faces.Max(x => C01(x.LookAwayRatio) * C01(x.VisibleRatio / 0.5)),
            smile,
            faces.Count == 0 ? 0 : faces.Max(x => C01(x.NegativeExpression) * C01(x.Reliability)),
            faces.Count > 0)
        {
            HostShare = talk?.HostShare,
            HostWordsPerSecond = talk?.HostWordsPerSecond,
        };

        return new Indicators(values.Select(C01).ToArray(), source, quality) { Positivity = C01(positivity), Cues = cues };
    }

    /// <summary>Baskın duygu: konuşma açıkça olumsuzsa (söz) enerjiye göre kızgın/gergin/üzgün; aksi halde füzyonun duygusu.
    /// Ses modeli yüksek sesi "coşkulu" gösterse bile öfkeli söz öfke olarak okunur.</summary>
    private static Emotion CueEmotion(Emotion fused, double v, double a)
    {
        if (v > -0.3) return fused;
        return a >= 0.75 ? Emotion.Kizgin : a >= 0.5 ? Emotion.Gergin : Emotion.Uzgun;
    }

    // Bir yüz, değeri NÖTR bir yüzün alacağı değeri en az bu kadar aşıyorsa kaynak olarak anılır.
    // Aksi halde değeri ses/metin sürüklüyordur ve gerekçe "Görüşme geneli" der — sesten gelen gerilim,
    // yüzünde hiçbir şey görünmeyen birine yazılmaz (spec §4: kişi hakkında hüküm yok).
    private const double FaceAttributionMargin = 0.05;
    private static readonly FaceWindowSummary NeutralFace = new(-1, 1, 0, 0, 0, 0, 0);

    private static void MaxOverFaces(IReadOnlyList<FaceWindowSummary> faces, Criterion c,
        double[] values, int?[] source, Func<FaceWindowSummary, double> value)
    {
        double best = double.MinValue;
        int? bestFace = null;
        foreach (var face in faces.OrderBy(x => x.FaceIndex))
        {
            double x = C01(value(face));
            if (x > best) { best = x; bestFace = face.FaceIndex; }
        }
        values[(int)c] = best;
        source[(int)c] = best >= C01(value(NeutralFace)) + FaceAttributionMargin ? bestFace : null;
    }

    /// <summary>En küçük kareler eğimi, x = 0,1,2…</summary>
    private static double Slope(double[] y)
    {
        int n = y.Length;
        double mx = (n - 1) / 2.0, my = y.Average(), num = 0, den = 0;
        for (int i = 0; i < n; i++) { num += (i - mx) * (y[i] - my); den += (i - mx) * (i - mx); }
        return den == 0 ? 0 : num / den;
    }

    private static double C01(double x) => double.IsFinite(x) ? Math.Clamp(x, 0, 1) : 0;
}
