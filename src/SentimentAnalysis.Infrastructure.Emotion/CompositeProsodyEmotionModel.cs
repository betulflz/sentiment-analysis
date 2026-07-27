using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>
/// İki prozodi modelini tek bir <see cref="IProsodyEmotionModel"/> arkasında birleştirir:
/// <list type="bullet">
///   <item>audeering wav2vec2 (MSP-Podcast, boyutsal) — genel amaçlı, çok dilli</item>
///   <item>SeaBenSea HuBERT (TurEV-DB, 4 sınıf → VA) — <b>Türkçe'ye özel eğitilmiş</b></item>
/// </list>
/// VA noktaları, yapılandırılabilir taban ağırlık × modelin kendi güveni ile ağırlıklı
/// ortalanır. İki model çeliştiğinde ikisi de emin değilse sonuç nötre yaklaşır — bu
/// istenen davranıştır (dürüstlük > kesinlik gösterisi).
///
/// <para><b>Neden ikisi birden, dile göre seçim değil?</b> Prozodi, STT ile <i>paralel</i>
/// çalışır (bkz. ChunkProcessor); chunk'ın dili prozodi çalışırken henüz bilinmez. Dile göre
/// model seçmek, prozodiyi transkripti beklemeye zorlar ve paralelliği bozardı. Akustik
/// duygu ipuçları büyük ölçüde dilden bağımsız olduğu için ikisini birden çalıştırıp
/// harmanlamak hem daha basit hem de daha sağlam.</para>
///
/// <para><b>Ağırlıklar kalibre edilmemiştir</b> (eval-09). Varsayılan eşit paydır.</para>
/// </summary>
public sealed class CompositeProsodyEmotionModel : IProsodyEmotionModel, IDisposable
{
    private readonly IProsodyEmotionModel _general;
    private readonly IProsodyEmotionModel _turkish;
    private readonly double _generalWeight;
    private readonly double _turkishWeight;
    private bool _disposed;

    public CompositeProsodyEmotionModel(
        IProsodyEmotionModel general, IProsodyEmotionModel turkish, AppConfig config)
    {
        _general = general;
        _turkish = turkish;
        _generalWeight = Math.Max(0.0, config.Emotion.GeneralProsodyWeight);
        _turkishWeight = Math.Max(0.0, config.Emotion.TurkishSerWeight);
    }

    public async Task<ProsodyEmotion> AnalyzeAsync(AudioChunk chunk, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // İkisi paralel: aynı ham chunk'ı alırlar, birbirlerine bağlı değiller.
        var generalTask = _general.AnalyzeAsync(chunk, ct);
        var turkishTask = _turkish.AnalyzeAsync(chunk, ct);
        await Task.WhenAll(generalTask, turkishTask).ConfigureAwait(false);

        return Blend(generalTask.Result, turkishTask.Result, _generalWeight, _turkishWeight);
    }

    /// <summary>
    /// İki boyutsal çıktıyı harmanlar. Saf ve deterministik — birim testlenebilir olması için
    /// ayrı ve statik.
    /// </summary>
    public static ProsodyEmotion Blend(
        ProsodyEmotion general, ProsodyEmotion turkish, double generalWeight, double turkishWeight)
    {
        // Etkin ağırlık = taban payı × modelin kendi güveni. Emin olmayan model az söz sahibi olur.
        double wg = generalWeight * Math.Clamp(general.ModelConfidence, 0.0, 1.0);
        double wt = turkishWeight * Math.Clamp(turkish.ModelConfidence, 0.0, 1.0);
        double sum = wg + wt;

        if (sum <= 1e-9)
        {
            // İkisi de tamamen kararsız → nötr merkez, sıfır güven. Uydurma bir duygu iddia edilmez.
            return new ProsodyEmotion(ValenceArousal.Neutral, 0.0);
        }

        var va = ValenceArousal.Clamp(
            (wg * general.Va.Valence + wt * turkish.Va.Valence) / sum,
            (wg * general.Va.Arousal + wt * turkish.Va.Arousal) / sum);

        // Birleşik güven: aynı ağırlıklarla harmanlanmış model güveni. İki model AYNI şeyi
        // söylüyorsa güven korunur; ıraksıyorlarsa düşürülür (anlaşmazlık = belirsizlik).
        double blendedConfidence = (wg * general.ModelConfidence + wt * turkish.ModelConfidence) / sum;
        double agreement = 1.0 - Math.Clamp(general.Va.DistanceTo(turkish.Va) / MaxVaDistance, 0.0, 1.0);

        return new ProsodyEmotion(va, Math.Clamp(blendedConfidence * agreement, 0.0, 1.0));
    }

    /// <summary>VA uzayında iki nokta arasındaki azami uzaklık: valence 2 birim, arousal 1 birim.</summary>
    private static readonly double MaxVaDistance = Math.Sqrt(4.0 + 1.0);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        (_general as IDisposable)?.Dispose();
        (_turkish as IDisposable)?.Dispose();
    }
}
