namespace SentimentAnalysis.Core;

/// <summary>
/// Bir duyguya düşen <b>yakınlık payı</b> (0..1). Tüm payların toplamı 1.0'dır.
/// </summary>
/// <remarks>
/// DİKKAT: Bu bir <b>olasılık değildir</b>. Valence-Arousal uzayındaki geometrik yakınlıktan
/// türetilmiş, kalibre edilmemiş bir paydır. Sistemde 8 duygu üzerinde olasılık üreten bir
/// model yoktur. UI ve raporlarda "olasılık" veya "ihtimal" olarak sunulmamalıdır
/// (master §12: dürüstlük > kesinlik gösterisi). Kalibrasyon alt-proje 09'un konusudur.
/// </remarks>
public readonly record struct EmotionShare(Emotion Emotion, double Share);
