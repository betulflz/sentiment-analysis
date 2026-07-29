using SentimentAnalysis.Application.Configuration;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

/// <summary>Duygu modellerinin execution provider varsayılanını kilitler.
/// <para><b>Neden test var:</b> Bu değer bir zevk meselesi değil, ÖLÇÜLMÜŞ bir karardır
/// (2026-07-28, M1 Air 8 GB):</para>
/// <list type="bullet">
/// <item>metin duygu (XLM-R, 1,1 GB): coreml <b>36,6–44,4 sn</b> vs cpu <b>2,5 sn</b></item>
/// <item>prozodi (msp-dim): coreml 2,5–3,1 sn vs cpu 1,4 sn</item>
/// <item>çıktılar birebir aynı (va = 0.80/0.70) — CoreML doğruluk kazancı YOK</item>
/// </list>
/// <para>CoreML derleme maliyeti süreçler arası önbelleklenmiyor, yani her açılışta yeniden
/// ödeniyor; bir koşuda "I/O error" ile de patladı. Sonuç: canlı mikrofonda ilk chunk 40+ sn
/// sürüyor, "Durdur" sonrası 30 sn'lik drenaj bütçesini aşıyor ve kullanıcı SIFIR sonuç ve
/// SIFIR açıklama görüyordu (hata <c>EmotionPipeline</c>'da sessizce yutuluyor).</para>
/// <para><b>Bu testi değiştirmeden önce ölçün.</b> Master §8'deki "macOS → CoreML EP" varsayımı
/// bu ölçümle çürütüldü (master §12.4: kararlar ölçülür, keyfi verilmez).</para></summary>
public class ExecutionProviderDefaultTests
{
    [Fact]
    public void Varsayilan_CpuOlmali_CoreMLDegil()
    {
        Assert.Equal("cpu", AppConfig.Default.Emotion.ExecutionProvider);
    }

    [Fact]
    public void Varsayilan_AppConfigDefaultIleTutarli()
    {
        // EmotionConfig'in kendi varsayılanı ile AppConfig.Default sessizce ayrışmasın.
        Assert.Equal(new EmotionConfig().ExecutionProvider, AppConfig.Default.Emotion.ExecutionProvider);
    }

    /// <summary>Prozodi kazancı varsayılanı 1,0 OLAMAZ: kazançsız msp-dim çıktısı nötr
    /// havzasının içinde kalır ve ses kanalı etiketi hiçbir zaman değiştiremez. Bu değer
    /// KALİBRE EDİLMEMİŞ bir başlangıçtır (eval-09) ama kimlik olmadığı testle sabittir.</summary>
    [Fact]
    public void ProzodiKazanci_VarsayilanKimlikDegil()
    {
        Assert.True(AppConfig.Default.Emotion.ProsodyValenceGain > 1.0,
            "kazançsız prozodi nötr havzasından çıkamaz");
        Assert.True(AppConfig.Default.Emotion.ProsodyArousalGain > 1.0,
            "kazançsız prozodi nötr havzasından çıkamaz");
    }
}
