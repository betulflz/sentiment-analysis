using System;
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Bir duygunun lokalize adı ve yakınlık payı yüzdesi (toplamları tam 100).</summary>
public sealed record EmotionShareVm(string Emotion, int Percent);

/// <summary>
/// Tek bir analiz kaynağının (Metin / Ses / Nihai) görünüm modeli: baskın duygu, V/A ekseni ve
/// 8 duygunun yakınlık payı dağılımı. Üç sütun da bu tipi kullanır — salt-okunur, saf.
/// </summary>
/// <remarks>
/// Yüzdeler <b>olasılık değildir</b>; VA uzayındaki geometrik yakınlıktan türetilmiştir
/// (bkz. <see cref="EmotionShare"/>). UI bu ayrımı açıkça belirtmelidir.
/// </remarks>
public sealed class SourceAnalysisVm
{
    public string Title { get; }
    public string Emotion { get; }
    public double Valence { get; }
    public double Arousal { get; }
    public IReadOnlyList<EmotionShareVm> Shares { get; }

    /// <summary>Henüz ölçüm gelmediyse false — UI boş durumu dürüstçe gösterir.</summary>
    public bool HasData { get; }

    /// <summary>Valence/Arousal'ın jargonsuz, düz Türkçe özeti (örn. "Olumlu · Hareketli").
    /// Boş durumda boş dizedir.</summary>
    public string AxisSummary { get; }

    public SourceAnalysisVm(string title, ValenceArousal va, double temperature)
    {
        Title = title;
        Valence = va.Valence;
        Arousal = va.Arousal;
        HasData = true;
        AxisSummary = $"{EmotionLocalizer.ValenceText(Valence)} · {EmotionLocalizer.ArousalText(Arousal)}";

        var shares = EmotionWheel.Distribute(va, temperature);
        Emotion = EmotionLocalizer.Emotion(shares[0].Emotion);

        var percents = PercentFormatter.ToPercents(shares.Select(s => s.Share).ToArray());
        Shares = shares
            .Select((s, i) => new EmotionShareVm(EmotionLocalizer.Emotion(s.Emotion), percents[i]))
            .ToArray();
    }

    private SourceAnalysisVm(string title)
    {
        Title = title;
        Emotion = "—";
        Shares = Array.Empty<EmotionShareVm>();
        HasData = false;
        AxisSummary = string.Empty;
    }

    /// <summary>Analiz başlamadan önceki boş durum.</summary>
    public static SourceAnalysisVm Empty(string title) => new(title);
}
