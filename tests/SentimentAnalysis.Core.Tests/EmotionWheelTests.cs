using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Core.Tests;

public class EmotionWheelTests
{
    [Theory]
    [InlineData(0.6, 0.9, Emotion.Cosakulu)]   // yüksek valence + yüksek arousal
    [InlineData(0.8, 0.5, Emotion.Mutlu)]      // yüksek valence + orta arousal
    [InlineData(0.6, 0.15, Emotion.Sakin)]     // olumlu + düşük enerji
    [InlineData(-0.4, 0.1, Emotion.Sikilmis)]  // olumsuz + çok düşük enerji
    [InlineData(-0.6, 0.3, Emotion.Uzgun)]     // olumsuz + düşük-orta enerji
    [InlineData(-0.5, 0.8, Emotion.Gergin)]    // olumsuz + yüksek enerji
    [InlineData(-0.8, 0.9, Emotion.Kizgin)]    // çok olumsuz + yüksek enerji
    [InlineData(0.0, 0.5, Emotion.Notr)]       // merkez
    public void Map_ReturnsNearestRegion(double v, double a, Emotion expected)
    {
        Assert.Equal(expected, EmotionWheel.Map(new ValenceArousal(v, a)));
    }

    [Fact]
    public void MapWithSecondary_PrimaryAndSecondaryDiffer()
    {
        var (primary, secondary) = EmotionWheel.MapWithSecondary(new ValenceArousal(0.7, 0.7));
        Assert.NotEqual(primary, secondary);
    }

    // --- Nötr yarıçapı ---
    // Klasik "en yakın merkez" kuralında Nötr (0, 0.5) çarkın TAM ORTASINDA oturur; diğer 7
    // duygu ise kenardadır. Bu geometri Nötr'e devasa bir havza verir: a=0,5'te valence
    // −0,33…+0,40 arası her nokta Nötr'dür ve v≈0 iken arousal 1,0'a çıksa bile Nötr kazanır.
    // Yarıçap kuralı bunu tersine çevirir: Nötr artık bir merkez değil, merkez çevresinde
    // KÜÇÜK bir disktir; dışında kalan her nokta gerçek 7 duygudan birine düşer.

    [Fact]
    public void Map_YaricapIcinde_NotrDoner()
    {
        var va = new ValenceArousal(0.05, 0.52); // merkeze uzaklık ~0,054
        Assert.Equal(Emotion.Notr, EmotionWheel.Map(va, neutralRadius: 0.2));
    }

    [Fact]
    public void Map_YaricapDisinda_NotrSecilmez()
    {
        // Klasik kural bu noktaya Nötr der (Nötr'e 0,30; en yakın gerçek duygu Sakin 0,46);
        // yarıçap kuralında merkezden 0,30 uzakta olduğu için gerçek duyguya düşer.
        var va = new ValenceArousal(0.3, 0.5);
        Assert.Equal(Emotion.Notr, EmotionWheel.Map(va));                       // klasik
        Assert.Equal(Emotion.Sakin, EmotionWheel.Map(va, neutralRadius: 0.2));  // yarıçap
    }

    [Fact]
    public void Map_YaricapDisinda_YuksekArousalArtikGorunur()
    {
        // Klasik kuralın en can sıkıcı sonucu: v≈0 iken ses ne kadar heyecanlı olursa olsun
        // (a=0,95) etiket Nötr'de çakılı kalıyordu. Yarıçap kuralı tonu görünür kılar.
        var va = new ValenceArousal(0.0, 0.95);
        Assert.Equal(Emotion.Notr, EmotionWheel.Map(va));
        Assert.NotEqual(Emotion.Notr, EmotionWheel.Map(va, neutralRadius: 0.2));
    }

    [Fact]
    public void Map_YaricapVerilmezse_KlasikDavranisKorunur()
    {
        // Geriye dönük uyumluluk: yarıçap OPT-IN'dir; verilmeyince eski en-yakın-merkez kuralı.
        var va = new ValenceArousal(0.2, 0.55);
        Assert.Equal(EmotionWheel.Map(va), EmotionWheel.Map(va, neutralRadius: null));
    }

    [Fact]
    public void MapWithSecondary_YaricapDisinda_IkinciDeNotrOlamaz()
    {
        // Nötr yarıçap dışında bir ADAY değildir; ikincil olarak sızarsa UI "Nötr'e yakın"
        // diyerek aynı yanılgıyı arka kapıdan geri getirir.
        var (primary, secondary) = EmotionWheel.MapWithSecondary(
            new ValenceArousal(0.3, 0.5), neutralRadius: 0.2);
        Assert.NotEqual(Emotion.Notr, primary);
        Assert.NotEqual(Emotion.Notr, secondary);
        Assert.NotEqual(primary, secondary);
    }

    [Fact]
    public void MapWithSecondary_YaricapIcinde_IkincilEnYakinGercekDuygudur()
    {
        var (primary, secondary) = EmotionWheel.MapWithSecondary(
            new ValenceArousal(0.1, 0.55), neutralRadius: 0.2);
        Assert.Equal(Emotion.Notr, primary);
        Assert.Equal(Emotion.Cosakulu, secondary); // bu noktaya en yakın GERÇEK duygu (0,61)
    }

    [Fact]
    public void Map_YaricapNegatifse_Reddedilir()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EmotionWheel.Map(new ValenceArousal(0.0, 0.5), neutralRadius: -0.1));
    }
}
