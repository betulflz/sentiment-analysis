using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Bir duygunun lokalize adı ve tam sayı yüzdesi (toplamları tam 100).
/// <para><see cref="EmotionShareVm"/>'den farkı: o, toplantı-düzeyi <b>süre oranını</b> (double)
/// taşır; bu ise VA uzayındaki <b>yakınlık payının</b> yuvarlanmış yüzdesidir.</para></summary>
public sealed record EmotionPercentVm(string Emotion, int Percent);

/// <summary>
/// Tek bir analiz kaynağının (Metin · Ses · Nihai) sunum görünümü: baskın duygu, V/A ve
/// 8 duyguya yakınlık payı (spec 08d, Dilim 2). Salt-okunur ve saf — UI'sız test edilebilir.
/// <para><b>Yüzdeler olasılık değildir</b>: VA uzayındaki geometrik yakınlıktan türetilir
/// (bkz. <see cref="EmotionWheel.Distribute"/>). UI dilinde "yakınlık payı" denir.</para>
/// </summary>
public sealed class SourceAnalysisVm
{
    /// <summary>Sütun başlığı: "Metin Analizi" | "Ses Analizi" | "Nihai Karar".</summary>
    public string Title { get; }

    /// <summary>Bu kaynağın VA noktasının haritalandığı baskın duygu (lokalize).</summary>
    public string Emotion { get; }

    public double Valence { get; }
    public double Arousal { get; }

    /// <summary>8 duygu, azalan pay sırasında; yüzdeler toplamı tam 100.</summary>
    public IReadOnlyList<EmotionPercentVm> Shares { get; }

    /// <summary>Modelin kendi güveni (0..1). Nihai sütunda kullanılmaz.</summary>
    public double ModelConfidence { get; }

    /// <summary>Füzyonda bu kanala verilen kalite ağırlığı (0..1). Nihai sütunda 1.</summary>
    public double Reliability { get; }

    /// <summary>Bu kanal karara hiç katılmadıysa true — UI bunu açıkça göstermeli,
    /// aksi halde ekranda dolu görünen bir panel aslında sonuca etki etmemiş olur.</summary>
    public bool Silent { get; }

    /// <summary>Boş/başlangıç durumu (henüz chunk gelmedi).</summary>
    public static SourceAnalysisVm Empty(string title) => new(title);

    private SourceAnalysisVm(string title)
    {
        Title = title;
        Emotion = "—";
        Shares = Array.Empty<EmotionPercentVm>();
        Reliability = 1.0;
    }

    public SourceAnalysisVm(
        string title, ValenceArousal va, double temperature,
        double modelConfidence = 1.0, double reliability = 1.0, bool silent = false)
    {
        Title = title;
        Valence = va.Valence;
        Arousal = va.Arousal;
        Emotion = EmotionLocalizer.Emotion(EmotionWheel.Map(va));
        ModelConfidence = modelConfidence;
        Reliability = reliability;
        Silent = silent;

        var shares = EmotionWheel.Distribute(va, temperature);
        var percents = PercentFormatter.ToPercents(shares.Select(s => s.Share).ToList());
        Shares = shares
            .Select((s, i) => new EmotionPercentVm(EmotionLocalizer.Emotion(s.Emotion), percents[i]))
            .ToList();
    }

    /// <summary>Bir chunk sonucundan üç sütunu birden üretir.</summary>
    public static (SourceAnalysisVm Text, SourceAnalysisVm Audio, SourceAnalysisVm Final) FromChunk(
        ChunkResult r, double temperature)
    {
        var f = r.Fused;
        return (
            new SourceAnalysisVm("Metin Analizi", f.TextVa, temperature,
                f.TextConfidence, f.TextReliability, f.TextChannelSilent),
            new SourceAnalysisVm("Ses Analizi", f.ProsodyVa, temperature,
                f.ProsodyConfidence, f.AudioReliability, f.AudioChannelSilent),
            new SourceAnalysisVm("Nihai Karar", f.Combined, temperature));
    }
}
