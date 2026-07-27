using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Fusion;

/// <summary>
/// Söz (metin valence) ile ton (ses valence) arasındaki örtüşme/çelişkiyi ölçer.
/// Uyumsuzluk gürültü değil, birinci sınıf sinyaldir (master §5.1, §12.3).
/// Saf ve deterministik.
/// </summary>
public static class IncongruenceAnalysis
{
    /// <summary>Söylenen ile hissedilenin örtüşmesi (0..1). 1 = tam uyum,
    /// 0 = zıt uçlar. Valence aralığı 2 birim olduğu için farkı 2'ye böleriz.</summary>
    public static double Congruence(double textValence, double audioValence) =>
        Math.Clamp(1.0 - Math.Abs(textValence - audioValence) / 2.0, 0.0, 1.0);

    /// <summary>Metin ve ses valence'ı eşiği aşacak kadar ıraksıyor mu?</summary>
    public static bool IsIncongruent(double textValence, double audioValence, FusionConfig cfg) =>
        Math.Abs(textValence - audioValence) >= cfg.IncongruenceThreshold;

    /// <summary>Uyumsuzluk örüntüsünü sınıflandırır (master §5.1 tablosu).</summary>
    public static IncongruencePattern Classify(
        ValenceArousal text, ValenceArousal prosody, bool incongruent, FusionConfig cfg)
    {
        if (!incongruent)
        {
            // Eşiği aşmayan ama olası ironi: olumlu söz + düz/düşük-enerjili, net-olumsuz-olmayan ton.
            bool positiveWords = text.Valence >= cfg.IronyValenceFloor;
            bool flatDelivery = prosody.Arousal <= cfg.IronyArousalCeiling;
            bool toneNotClearlyPositive = prosody.Valence <= cfg.IronyAudioValenceCeiling;
            if (positiveWords && flatDelivery && toneNotClearlyPositive)
                return IncongruencePattern.OlasiIroni;
            return IncongruencePattern.Yok;
        }

        if (text.Valence > 0 && prosody.Valence < 0) return IncongruencePattern.Maskeleme; // olumlu söz / olumsuz ton
        if (text.Valence < 0 && prosody.Valence > 0) return IncongruencePattern.Saka;      // olumsuz söz / neşeli ton
        // Adı konmamış diğer ıraksama (ör. olumsuz söz + nötr ton): OlasiIroni'yi kirletme.
        // Uyumsuzluk zaten `Incongruent = true` bayrağıyla raporlanır; Pattern isim iddia etmez.
        return IncongruencePattern.Yok;
    }
}
