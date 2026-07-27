using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Tests;

/// <summary>
/// Füzyonun <b>nasıl</b> karar verdiğinin raporlanması (spec 08d Dilim 2 + saydamlık ilkesi).
/// Bir kanal sıfır ağırlık aldığında bunun görünür olması, "iki kanallı" görünüp tek kanallı
/// çalışan bir sistemin sessizce yaşamasını engeller.
/// </summary>
public class FusionTransparencyTests
{
    private static readonly AppConfig Config = AppConfig.Default;
    private static readonly EmotionFusion Fusion = new(Config);

    /// <summary>Her iki kanalın da sağlıklı olduğu kalite sinyalleri.</summary>
    private static QualitySignals GoodQuality => new(
        AudioSnr: 20.0, SpeechRatio: 0.9, Duration: TimeSpan.FromSeconds(10),
        TranscriptWordCount: 20, TranscriptAvgLogProb: -0.1);

    /// <summary>Whisper olasılıkları kapalıyken oluşan durum: avg-logprob tabana kırpılmış.</summary>
    private static QualitySignals ClampedLogProbQuality => new(
        AudioSnr: 20.0, SpeechRatio: 0.9, Duration: TimeSpan.FromSeconds(10),
        TranscriptWordCount: 20, TranscriptAvgLogProb: Math.Log(1e-6));

    [Fact]
    public void Fuse_ReportsBothChannelReliabilities()
    {
        var text = new TextEmotion(new ValenceArousal(0.5, 0.4), 0.8);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.3, 0.7), 0.6);

        var r = Fusion.Fuse(text, prosody, GoodQuality);

        Assert.True(r.TextReliability > 0.0, "sağlıklı transkriptte metin ağırlığı sıfır olmamalı");
        Assert.True(r.AudioReliability > 0.0, "sağlıklı seste ses ağırlığı sıfır olmamalı");
        Assert.False(r.TextChannelSilent);
        Assert.False(r.AudioChannelSilent);
    }

    [Fact]
    public void Fuse_CarriesRawChannelOutputs()
    {
        var text = new TextEmotion(new ValenceArousal(0.5, 0.4), 0.8);
        var prosody = new ProsodyEmotion(new ValenceArousal(-0.3, 0.7), 0.6);

        var r = Fusion.Fuse(text, prosody, GoodQuality);

        // Üçlü görünüm bu alanlardan beslenir; karışmamalılar.
        Assert.Equal(0.5, r.TextVa.Valence, precision: 6);
        Assert.Equal(0.4, r.TextVa.Arousal, precision: 6);
        Assert.Equal(-0.3, r.ProsodyVa.Valence, precision: 6);
        Assert.Equal(0.7, r.ProsodyVa.Arousal, precision: 6);
        Assert.Equal(0.8, r.TextConfidence, precision: 6);
        Assert.Equal(0.6, r.ProsodyConfidence, precision: 6);
    }

    [Fact]
    public void Fuse_ClampedLogProb_SilencesTextChannel_AndIsVisible()
    {
        // Bu, WithProbabilities() unutulduğunda oluşan durumdur. Davranış (metnin oy
        // kullanamaması) matematiksel olarak doğrudur; ASIL SORUN görünmez olmasıydı.
        var text = new TextEmotion(new ValenceArousal(-0.9, 0.3), 0.99);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.6, 0.7), 0.4);

        var r = Fusion.Fuse(text, prosody, ClampedLogProbQuality);

        Assert.True(r.TextChannelSilent, "kırpılmış logprob metin kanalını susturmalı");
        Assert.Equal(0.0, r.TextReliability, precision: 9);
        // Nihai valence tamamen prozodiden gelir.
        Assert.Equal(prosody.Va.Valence, r.Combined.Valence, precision: 6);
    }

    [Fact]
    public void Fuse_HealthyChannels_Congruent_BlendValence_TextLeaning()
    {
        // Uyumlu dal (|Δvalence| < eşik 0.8): valence ağırlıkla METİNden gelir (0.70/0.30).
        // Düzeltmenin asıl kazancı bu: nihai valence artık iki kanalın karışımı, saf prozodi değil.
        var text = new TextEmotion(new ValenceArousal(-0.4, 0.3), 0.9);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.2, 0.7), 0.5);

        var r = Fusion.Fuse(text, prosody, GoodQuality);

        Assert.False(r.Incongruent, "bu çift uyumlu dalda kalmalı (test kurgusunun ön koşulu)");
        Assert.NotEqual(prosody.Va.Valence, r.Combined.Valence, precision: 3);
        Assert.NotEqual(text.Va.Valence, r.Combined.Valence, precision: 3);
        // İki kanal da sağlıklı ve metin ağırlığı daha yüksek → sonuç metin tarafında kalmalı.
        Assert.True(r.Combined.Valence < 0.0,
            $"metin baskın olduğundan valence negatife çekilmeliydi (çıkan: {r.Combined.Valence:F3})");
    }

    [Fact]
    public void Fuse_Incongruent_LeansToProsody_ByDesign()
    {
        // Uyumsuz dal: söz olumlu, ton olumsuz → valence prozodiye yaslanır (affect leakage,
        // master §5.1). Bu bir hata değil, kasıtlı tasarım; testle kilitlenir.
        var text = new TextEmotion(new ValenceArousal(0.7, 0.4), 0.9);
        var prosody = new ProsodyEmotion(new ValenceArousal(-0.5, 0.8), 0.7);

        var r = Fusion.Fuse(text, prosody, GoodQuality);

        Assert.True(r.Incongruent);
        Assert.Equal(IncongruencePattern.Maskeleme, r.Pattern);
        Assert.True(r.Combined.Valence < 0.0,
            $"uyumsuzlukta ton kazanmalıydı (çıkan: {r.Combined.Valence:F3})");
        // Ham kanal çıktıları yine de saydam kalmalı — kullanıcı çelişkiyi görebilmeli.
        Assert.Equal(0.7, r.TextValence, precision: 6);
        Assert.Equal(-0.5, r.AudioValence, precision: 6);
    }

    [Fact]
    public void Fuse_ZeroTextReliability_PreventsHighConfidence()
    {
        // rAudio*rText = 0 → MinReliabilityForHighConfidence eşiğinin altında → asla "Yüksek".
        var text = new TextEmotion(new ValenceArousal(0.7, 0.6), 0.95);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.7, 0.6), 0.95);

        var r = Fusion.Fuse(text, prosody, ClampedLogProbQuality);

        Assert.NotEqual(Confidence.Yuksek, r.Confidence);
    }

    [Fact]
    public void Fuse_HealthyChannelsAndAgreement_CanReachHighConfidence()
    {
        var text = new TextEmotion(new ValenceArousal(0.75, 0.55), 0.9);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.78, 0.52), 0.9);

        var r = Fusion.Fuse(text, prosody, GoodQuality);

        Assert.Equal(Confidence.Yuksek, r.Confidence);
    }
}
