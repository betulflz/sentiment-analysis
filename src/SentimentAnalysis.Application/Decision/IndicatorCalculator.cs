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
    public static Indicators Compute(ChunkResult chunk, IReadOnlyList<FaceWindowSummary> faces,
        IReadOnlyList<double> previousValences, DecisionConfig cfg)
    {
        var f = chunk.Fused;
        // Yüz K1–K4'e aşağıda yüz özetiyle girer; üç kaynaklı Combined değil konuşma valence'ı alınır (çift sayım yok).
        double v = f.SpeechValence, a = f.Combined.Arousal;
        double aq = Math.Max(f.AudioReliability, f.TextReliability);
        double audioTension = C01((a - 0.5) * 2) * C01(-v);

        var values = new double[6];
        var source = new int?[6];

        if (faces.Count == 0)
        {
            values[(int)Criterion.Gerilim] = audioTension;
            values[(int)Criterion.IlgiKaybi] = 0;
            values[(int)Criterion.OlumsuzDuygu] = C01(-v);
            values[(int)Criterion.Uyumsuzluk] = C01(1 - f.Congruence);
        }
        else
        {
            MaxOverFaces(faces, Criterion.Gerilim, values, source,
                x => 0.7 * audioTension + 0.3 * x.NegativeExpression * x.Reliability);
            MaxOverFaces(faces, Criterion.IlgiKaybi, values, source,
                x => x.LookAwayRatio * C01(x.VisibleRatio / 0.5));
            MaxOverFaces(faces, Criterion.OlumsuzDuygu, values, source, x =>
            {
                double wf = 0.5 * x.Reliability, den = aq + wf;
                return C01(-(den <= 0 ? 0 : (aq * v + wf * x.ValenceShift) / den));
            });
            MaxOverFaces(faces, Criterion.Uyumsuzluk, values, source,
                x => Math.Max(1 - f.Congruence, C01(Math.Abs(x.ValenceShift - v)) * x.Reliability));
        }

        double seconds = (chunk.TimeEnd - chunk.TimeStart).TotalSeconds;
        int words = chunk.Transcript.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        double wps = seconds <= 0 ? 0 : words / seconds;
        values[(int)Criterion.DusukEnerji] = C01(0.5 * C01((0.5 - a) * 2) + 0.5 * C01(1 - wps / 2));

        var series = previousValences.Append(v).TakeLast(cfg.TrendWindows).ToArray();
        values[(int)Criterion.Kotulesme] = series.Length < 3 ? 0 : C01(-Slope(series) * 5);

        // En iyi görülen yüz: anlık yanlış algılanan bir "hayalet" yüz ortalamayı (ve kararı) düşürmesin.
        double faceQ = faces.Count == 0 ? 0 : faces.Max(x => C01(x.VisibleRatio) * C01(x.Reliability));
        double quality = C01(0.6 * C01(aq) + 0.4 * faceQ);

        return new Indicators(values.Select(C01).ToArray(), source, quality);
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
