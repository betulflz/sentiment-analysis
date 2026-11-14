using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Fusion;

/// <summary>
/// Eksen-başına güvenilirlik-ağırlıklı füzyon (master §5): arousal ağırlıkla
/// sesten, valence ağırlıkla metinden; ağırlıklar kalite ile modüle edilir.
/// Uyumsuzlukta valence prozodiye yaslanır (affect leakage). Saf, deterministik.
/// </summary>
public sealed class EmotionFusion : IEmotionFusion
{
    private readonly FusionConfig _cfg;

    public EmotionFusion(AppConfig config) => _cfg = config.Fusion;

    public FusedResult Fuse(TextEmotion text, ProsodyEmotion prosody, QualitySignals quality)
    {
        double rAudio = QualityWeighting.AudioReliability(quality, _cfg);
        double rText = QualityWeighting.TextReliability(quality, _cfg);

        double vt = text.Va.Valence;
        double va = prosody.Va.Valence;

        // --- Arousal: ses baskın ---
        double arousalFinal = WeightedAverage(
            prosody.Va.Arousal, _cfg.AudioArousalWeight * rAudio,
            text.Va.Arousal, _cfg.TextArousalWeight * rText,
            fallback: ValenceArousal.Neutral.Arousal);

        // --- Uyumsuzluk ---
        bool incongruent = IncongruenceAnalysis.IsIncongruent(vt, va, _cfg);
        var pattern = IncongruenceAnalysis.Classify(text.Va, prosody.Va, incongruent, _cfg);
        double congruence = IncongruenceAnalysis.Congruence(vt, va);

        // --- Valence: uyumluda metin baskın; uyumsuzda prozodiye yaslan ---
        // Uyumsuz dalda da ağırlıklar kaliteyle modüle edilir (master §5: gürültülü/kısa/sessiz
        // ses → ağırlığı düşer). rAudio=rText=1 iken bu tam olarak 0.75/0.25'e indirgenir;
        // ses güvenilmezken (rAudio→0) valence metne kayar.
        double valenceFinal = incongruent
            ? WeightedAverage(
                va, _cfg.IncongruentValenceProsodyWeight * rAudio,
                vt, (1.0 - _cfg.IncongruentValenceProsodyWeight) * rText,
                fallback: 0.0)
            : WeightedAverage(
                vt, _cfg.TextValenceWeight * rText,
                va, _cfg.AudioValenceWeight * rAudio,
                fallback: 0.0);

        var combined = ValenceArousal.Clamp(valenceFinal, arousalFinal);
        var (primary, secondary) = EmotionWheel.MapWithSecondary(combined, _cfg.NeutralRadius);

        // Uyuşma da AYNI kuralla ölçülür: iki kanalı klasik kuralla, sonucu yarıçap kuralıyla
        // etiketlemek "iki model aynı diyor ama sonuç başka" gibi sessiz tutarsızlıklar üretir.
        bool agreement = EmotionWheel.Map(text.Va, _cfg.NeutralRadius)
                      == EmotionWheel.Map(prosody.Va, _cfg.NeutralRadius);
        var confidence = ResolveConfidence(text, prosody, agreement, incongruent, vt, va, rAudio * rText);

        return new FusedResult(
            Combined: combined,
            PrimaryEmotion: primary,
            SecondaryEmotion: secondary,
            Confidence: confidence,
            Agreement: agreement,
            Incongruent: incongruent,
            Pattern: pattern,
            Congruence: congruence,
            TextVa: text.Va,
            ProsodyVa: prosody.Va,
            TextConfidence: text.ModelConfidence,
            ProsodyConfidence: prosody.ModelConfidence,
            AudioReliability: rAudio,
            TextReliability: rText);
    }

    /// <summary>Ağırlıklı ortalama; toplam ağırlık ~0 ise fallback döner (sıfıra bölmeyi önler).</summary>
    private static double WeightedAverage(double x, double wx, double y, double wy, double fallback)
    {
        double sum = wx + wy;
        return sum <= 1e-9 ? fallback : (wx * x + wy * y) / sum;
    }

    /// <summary>Güven düzeyi. Zıt valence + iki model de düşük güven → belirsiz (Dusuk).
    /// Girdi kalitesi (rAudio*rText) düşükse "Yuksek" güven verilmez (master §5, §12.2:
    /// gürültülü/kısa/sessiz girdi → dürüstçe güven düşer). Aksi halde uyum + yüksek ortalama
    /// model güveni → Yuksek; çok düşükse Dusuk; yoksa Orta.</summary>
    private Confidence ResolveConfidence(
        TextEmotion text, ProsodyEmotion prosody, bool agreement, bool incongruent,
        double textValence, double audioValence, double reliability)
    {
        // Sonlu olmayan güven (bozuk model çıktısı) hiçbir karşılaştırmadan geçmez; Orta/Yuksek'e
        // sızmasın diye açıkça Dusuk.
        if (!double.IsFinite(text.ModelConfidence) || !double.IsFinite(prosody.ModelConfidence))
            return Confidence.Dusuk;

        bool bothLow = text.ModelConfidence < _cfg.LowConfidenceThreshold
                    && prosody.ModelConfidence < _cfg.LowConfidenceThreshold;
        bool oppositeValence = textValence * audioValence < 0.0;
        if (oppositeValence && bothLow) return Confidence.Dusuk; // belirsiz (master §5.1)

        double avgConf = (text.ModelConfidence + prosody.ModelConfidence) / 2.0;

        // Girdi kalitesi çok düşükse yüksek güven iddia etme.
        if (reliability < _cfg.MinReliabilityForHighConfidence)
            return avgConf < _cfg.LowConfidenceThreshold ? Confidence.Dusuk : Confidence.Orta;

        if (agreement && !incongruent && avgConf >= _cfg.HighConfidenceThreshold)
            return Confidence.Yuksek;
        if (avgConf < _cfg.LowConfidenceThreshold)
            return Confidence.Dusuk;
        return Confidence.Orta;
    }
}
