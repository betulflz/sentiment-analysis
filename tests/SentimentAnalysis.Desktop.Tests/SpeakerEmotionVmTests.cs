using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

/// <summary>10d düzeltme turu 1 / bulgu 4: <see cref="SpeakerEmotionVm"/>'in değişen sunum
/// mantığının (nötr yarıçapı, ölçülmüş prozodi geri düşüşü, füzyon sonucu) hiç testi yoktu —
/// bulgu 1'deki hata (Nihai Karar'ın ham prozodiyle beslenmesi) tam da bu boşluk yüzünden
/// sessizce yaşayabildi.</summary>
public class SpeakerEmotionVmTests
{
    private const double Temperature = 0.35; // AppConfig.Default.Fusion.DistributionTemperature

    // Notr merkezi (0, 0.5); bu nokta merkeze 0.3 uzaklıkta: neutralRadius=null'da (klasik
    // en-yakın-merkez) Notr en yakınıdır, neutralRadius=0.2'de ise diskin DIŞINDA kalır ve
    // Notr aday olmaktan çıkar; en yakın diğer duygu Sakin (0.6, 0.15) olur.
    private static readonly ValenceArousal RadiusSiniriNoktasi = new(0.3, 0.5);

    private static SpeakerEmotionSummary Summary(ValenceArousal text, ValenceArousal audio, ValenceArousal combined) =>
        new(new SpeakerId(0), text, audio, combined, ChunkCount: 3);

    [Fact]
    public void KisiKarti_HarfRenkVeGenelHava()
    {
        var s = Summary(RadiusSiniriNoktasi, RadiusSiniriNoktasi, RadiusSiniriNoktasi) with { Speaker = new SpeakerId(1) };
        var vm = new SpeakerEmotionVm(s, Temperature, neutralRadius: 0.2);

        Assert.Equal("B", vm.Initial);
        Assert.Equal(1, vm.SpeakerIndex);
        Assert.Equal("Genel olarak: sakin", vm.OverallText);
    }

    [Fact]
    public void NeutralRadiusVerilmezse_KlasikKuralaGoreNotrEtiketlenir()
    {
        var s = Summary(RadiusSiniriNoktasi, RadiusSiniriNoktasi, RadiusSiniriNoktasi);

        var vm = new SpeakerEmotionVm(s, Temperature, neutralRadius: null);

        Assert.Equal("Nötr", vm.TextAnalysis.Emotion);
        Assert.Equal("Nötr", vm.AudioAnalysis.Emotion);
        Assert.Equal("Nötr", vm.CombinedAnalysis.Emotion);
    }

    [Fact]
    public void NeutralRadiusVerilirse_UcPanelDeAyniYaricapiKullanir_FarkliEtiketUretir()
    {
        // Bulgu 4(a): canlı ekranla aynı yarıçap geçilmezse rapor farklı bir duygu etiketi
        // gösterir — bu test tam olarak o ayrışmayı yakalar.
        var s = Summary(RadiusSiniriNoktasi, RadiusSiniriNoktasi, RadiusSiniriNoktasi);

        var vm = new SpeakerEmotionVm(s, Temperature, neutralRadius: 0.2);

        Assert.Equal("Sakin", vm.TextAnalysis.Emotion);
        Assert.Equal("Sakin", vm.AudioAnalysis.Emotion);
        Assert.Equal("Sakin", vm.CombinedAnalysis.Emotion);
    }

    [Fact]
    public void ProzodiOlcumuVarsa_SesAnalizPaneli_OlculenDegeriGosterir()
    {
        // Bulgu 4(b): konuşmacının KENDİ sesinden ölçülmüş prozodi (10d) varsa "Ses Analizi"
        // paneli bunu göstermeli — chunk-hizalamalı eski değeri (s.Audio) değil.
        var chunkHizalamaliSesDegeri = new ValenceArousal(-0.5, 0.2);   // Sikilmis bölgesine yakın
        var olculmusProzodi = new ValenceArousal(0.8, 0.5);              // Mutlu bölgesine yakın
        var s = Summary(
            text: ValenceArousal.Neutral,
            audio: chunkHizalamaliSesDegeri,
            combined: ValenceArousal.Neutral);
        var prosody = new SpeakerProsodySummary(new SpeakerId(0), olculmusProzodi, AnalyzedSeconds: 12.5, RegionCount: 2);

        var vm = new SpeakerEmotionVm(s, Temperature, neutralRadius: 0.2, prosody);

        Assert.Equal("Mutlu", vm.AudioAnalysis.Emotion);       // ölçülmüş değere göre
        Assert.Equal(12.5, vm.AnalyzedSeconds);
        Assert.True(vm.HasAudioMeasurement);
        // Nihai Karar füzyon sonucundan (s.Combined) beslenir, ölçülmüş prozodiden değil.
        Assert.Equal("Nötr", vm.CombinedAnalysis.Emotion);
    }

    [Fact]
    public void ProzodiOlcumuYoksa_SesAnalizPaneli_EskiChunkHizalamaliDegereDuser_OlculmediginiBildirir()
    {
        // Bulgu 4(c): konuşmacının prozodi için yeterli sesi yoktu (SpeakerProsodyAnalyzer bu
        // konuşmacı için hiç özet üretmedi) — panel uydurmak yerine chunk-hizalamalı eski
        // değere (s.Audio) düşer, ama AnalyzedSeconds=0 / HasAudioMeasurement=false ile bunun
        // bir ÖLÇÜM olmadığını dürüstçe bildirir.
        var chunkHizalamaliSesDegeri = new ValenceArousal(-0.5, 0.2);
        var s = Summary(
            text: ValenceArousal.Neutral,
            audio: chunkHizalamaliSesDegeri,
            combined: ValenceArousal.Neutral);

        var vm = new SpeakerEmotionVm(s, Temperature, neutralRadius: 0.2, prosody: null);

        Assert.Equal(0, vm.AnalyzedSeconds);
        Assert.False(vm.HasAudioMeasurement);
        // Chunk-hizalamalı değer korunur — sessizce sıfırlanmaz veya nötrleştirilmez.
        Assert.Equal("Sıkılmış", vm.AudioAnalysis.Emotion);
    }

    [Fact]
    public void ProzodiOlcumuVarsa_AciklayiciNot_OlcumunKacSaniyeyeDayandiginiSoyler()
    {
        // Final inceleme / bulgu 2: "ölçüldü" ile "ölçülemedi" panelde AYIRT EDİLEBİLİR olmalı.
        var s = Summary(ValenceArousal.Neutral, ValenceArousal.Neutral, ValenceArousal.Neutral);
        var prosody = new SpeakerProsodySummary(
            // 12,4 → "12": yuvarlama modundan bağımsız tek bir doğru cevabı olan bir değer seçildi.
            new SpeakerId(0), new ValenceArousal(0.8, 0.5), AnalyzedSeconds: 12.4, RegionCount: 2);

        var vm = new SpeakerEmotionVm(s, Temperature, neutralRadius: 0.2, prosody);

        Assert.Equal("Ses tonu bu kişinin 12 sn konuşmasından ölçüldü", vm.AudioMeasurementNote);
    }

    [Fact]
    public void ProzodiOlcumuYoksa_AciklayiciNot_YeterliSesOlmadiginiSoyler()
    {
        // Eskiden bu satır GİZLENİYORDU: kullanıcı için "ölçüldü" ve "ölçülemedi" tıpatıp aynı
        // görünüyordu. Artık her iki durumda da bir not gösterilir.
        var s = Summary(ValenceArousal.Neutral, ValenceArousal.Neutral, ValenceArousal.Neutral);

        var vm = new SpeakerEmotionVm(s, Temperature, neutralRadius: 0.2, prosody: null);

        Assert.Equal("Bu kişinin kendi sesinden ölçüm için yeterli ses yok; genel ortalama kullanıldı", vm.AudioMeasurementNote);
    }

    [Fact]
    public void BelirsizChunkVarsa_VmBunuGorunurKilar()
    {
        // Final inceleme / bulgu 3: eşik-altı atamalar ortalamaya giriyor; sayısı gösterilmeli.
        var s = new SpeakerEmotionSummary(
            new SpeakerId(0), ValenceArousal.Neutral, ValenceArousal.Neutral, ValenceArousal.Neutral,
            ChunkCount: 5, UncertainChunkCount: 2);

        var vm = new SpeakerEmotionVm(s, Temperature, neutralRadius: 0.2);

        Assert.True(vm.HasUncertainChunks);
        Assert.Equal("2 bölümde konuşanın bu kişi olduğu kesin değil", vm.UncertainChunkNote);
    }

    [Fact]
    public void BelirsizChunkYoksa_UyariGosterilmez()
    {
        var s = Summary(ValenceArousal.Neutral, ValenceArousal.Neutral, ValenceArousal.Neutral);

        var vm = new SpeakerEmotionVm(s, Temperature, neutralRadius: 0.2);

        Assert.False(vm.HasUncertainChunks);
    }
}
