using System;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Fusion;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

public class EmotionFusionTests
{
    private static readonly EmotionFusion Fusion = new(AppConfig.Default);

    // Yüksek kaliteli girdi (güvenilirlik ~1) → ağırlıklar tam etkili.
    private static QualitySignals Clean(int words = 20) =>
        new(AudioSnr: 20, SpeechRatio: 1.0, Duration: TimeSpan.FromSeconds(10),
            TranscriptWordCount: words, TranscriptAvgLogProb: 0.0);

    [Fact]
    public void Arousal_LeansOnAudio()
    {
        // ses yüksek arousal (0.9), metin düşük (0.1) → sonuç sese yakın (>0.5)
        var text = new TextEmotion(new ValenceArousal(0.0, 0.1), ModelConfidence: 0.8);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.0, 0.9), ModelConfidence: 0.8);
        var r = Fusion.Fuse(text, prosody, Clean());
        Assert.True(r.Combined.Arousal > 0.5, $"arousal sese yaslanmalı, gelen {r.Combined.Arousal}");
    }

    [Fact]
    public void Valence_LeansOnText_WhenCongruent()
    {
        // metin olumlu (0.8), ses hafif olumlu (0.2), uyumlu → sonuç metne yakın (>0.5)
        var text = new TextEmotion(new ValenceArousal(0.8, 0.5), ModelConfidence: 0.8);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.2, 0.5), ModelConfidence: 0.8);
        var r = Fusion.Fuse(text, prosody, Clean());
        Assert.False(r.Incongruent);
        Assert.True(r.Combined.Valence > 0.5, $"valence metne yaslanmalı, gelen {r.Combined.Valence}");
    }

    [Fact]
    public void Incongruent_Masking_ValenceGoesNegative()
    {
        // Ağlamaklı "çok mutluyum": metin +0.6, ses −0.6 → uyumsuz, valence prozodiye yaslanır (negatif)
        var text = new TextEmotion(new ValenceArousal(0.6, 0.5), ModelConfidence: 0.8);
        var prosody = new ProsodyEmotion(new ValenceArousal(-0.6, 0.6), ModelConfidence: 0.8);
        var r = Fusion.Fuse(text, prosody, Clean());
        Assert.True(r.Incongruent);
        Assert.Equal(IncongruencePattern.Maskeleme, r.Pattern);
        Assert.True(r.Combined.Valence < 0, $"maskeleme: valence negatif olmalı, gelen {r.Combined.Valence}");
        Assert.Equal(0.6, r.TextVa.Valence, precision: 10);
        Assert.Equal(-0.6, r.ProsodyVa.Valence, precision: 10);
    }

    [Fact]
    public void Incongruent_ZeroAudioReliability_ValenceFallsBackToText()
    {
        // Uyumsuz ama ses çöp (rAudio≈0), metin güvenilir: valence güvenilmez prozodiye
        // değil metne yaslanmalı (master §5). Metin +0.6, ses −0.6.
        var q = new QualitySignals(AudioSnr: 0, SpeechRatio: 0, Duration: TimeSpan.Zero,
            TranscriptWordCount: 20, TranscriptAvgLogProb: 0.0); // rAudio=0, rText=1
        var text = new TextEmotion(new ValenceArousal(0.6, 0.5), ModelConfidence: 0.8);
        var prosody = new ProsodyEmotion(new ValenceArousal(-0.6, 0.6), ModelConfidence: 0.8);
        var r = Fusion.Fuse(text, prosody, q);
        Assert.True(r.Incongruent);
        Assert.True(r.Combined.Valence > 0,
            $"ses güvenilmezken valence metne (pozitif) yaslanmalı, gelen {r.Combined.Valence}");
    }

    [Fact]
    public void Belirsiz_OppositeValenceAndBothLowConfidence_YieldsDusuk()
    {
        // zıt valence (+0.2 / −0.2) + iki model de düşük güven → belirsiz = Confidence.Dusuk
        var text = new TextEmotion(new ValenceArousal(0.2, 0.5), ModelConfidence: 0.2);
        var prosody = new ProsodyEmotion(new ValenceArousal(-0.2, 0.5), ModelConfidence: 0.2);
        var r = Fusion.Fuse(text, prosody, Clean());
        Assert.Equal(Confidence.Dusuk, r.Confidence);
    }

    [Fact]
    public void Agreement_HighConfidence_WhenBothPointSameEmotion()
    {
        // iki model de aynı duyguya (mutlu bölgesi) ve yüksek güven → agreement + Yuksek
        var text = new TextEmotion(new ValenceArousal(0.8, 0.5), ModelConfidence: 0.9);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.8, 0.5), ModelConfidence: 0.9);
        var r = Fusion.Fuse(text, prosody, Clean());
        Assert.True(r.Agreement);
        Assert.Equal(Confidence.Yuksek, r.Confidence);
        Assert.True(r.Congruence > 0.9);
    }

    [Fact]
    public void PrimaryAndSecondaryEmotionsPopulated()
    {
        var text = new TextEmotion(new ValenceArousal(0.7, 0.7), ModelConfidence: 0.8);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.7, 0.7), ModelConfidence: 0.8);
        var r = Fusion.Fuse(text, prosody, Clean());
        Assert.NotEqual(r.PrimaryEmotion, r.SecondaryEmotion);
    }

    [Fact]
    public void ZeroReliabilityBothSides_FallsBackToNeutralArousal()
    {
        // süre 0 → ses güvenilirliği 0; kelime 0 + logprob taban → metin güvenilirliği 0
        var q = new QualitySignals(AudioSnr: 0, SpeechRatio: 0, Duration: TimeSpan.Zero,
            TranscriptWordCount: 0, TranscriptAvgLogProb: -1.0);
        var text = new TextEmotion(new ValenceArousal(0.0, 0.9), ModelConfidence: 0.5);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.0, 0.9), ModelConfidence: 0.5);
        var r = Fusion.Fuse(text, prosody, q);
        Assert.Equal(0.5, r.Combined.Arousal, precision: 10); // Neutral.Arousal fallback
    }

    [Fact]
    public void Deterministic_SameInputSameOutput()
    {
        var text = new TextEmotion(new ValenceArousal(0.3, 0.6), ModelConfidence: 0.7);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.1, 0.7), ModelConfidence: 0.6);
        var a = Fusion.Fuse(text, prosody, Clean());
        var b = Fusion.Fuse(text, prosody, Clean());
        Assert.Equal(a, b); // record değer eşitliği
    }

    [Fact]
    public void Fuse_CarriesBothModelsVaAndConfidence_WithoutSwapping()
    {
        // Her alan tekil olarak ayırt edilebilsin diye metin/ses VA'ları ve ModelConfidence'ları
        // birbirinden ve olası bir çapraz-atama (swap) hatasından ayrılacak şekilde farklı seçildi.
        var text = new TextEmotion(new ValenceArousal(0.6, 0.2), ModelConfidence: 0.8);
        var prosody = new ProsodyEmotion(new ValenceArousal(-0.6, 0.9), ModelConfidence: 0.3);
        var r = Fusion.Fuse(text, prosody, Clean());

        Assert.Equal(0.6, r.TextVa.Valence, precision: 10);
        Assert.Equal(0.2, r.TextVa.Arousal, precision: 10);
        Assert.Equal(-0.6, r.ProsodyVa.Valence, precision: 10);
        Assert.Equal(0.9, r.ProsodyVa.Arousal, precision: 10);
        Assert.Equal(0.8, r.TextConfidence, precision: 10);
        Assert.Equal(0.3, r.ProsodyConfidence, precision: 10);
    }

    [Fact]
    public void Confidence_NotHigh_WhenInputQualityLow()
    {
        // İki model de aynı duyguya + yüksek ModelConfidence, AMA ses çöp (rAudio≈0):
        // girdi kalitesi düşük olduğu için Yuksek güven verilmemeli (master §12.2).
        var q = new QualitySignals(AudioSnr: 0, SpeechRatio: 0, Duration: TimeSpan.Zero,
            TranscriptWordCount: 20, TranscriptAvgLogProb: 0.0); // rAudio=0 → çarpım 0
        var text = new TextEmotion(new ValenceArousal(0.8, 0.5), ModelConfidence: 0.95);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.8, 0.5), ModelConfidence: 0.95);
        var r = Fusion.Fuse(text, prosody, q);
        Assert.NotEqual(Confidence.Yuksek, r.Confidence);
    }

    // --- Nötr yarıçapı (EmotionWheel yarıçap kuralının füzyona bağlanması) ---

    [Fact]
    public void Etiket_NotrYaricapiniUygular()
    {
        // Birleşik nokta ~(0.29, 0.62): klasik en-yakın-merkez kuralında Nötr, yarıçap
        // kuralında gerçek bir duygu. Aynı girdi, iki config → etiket ayrışmalı.
        var text = new TextEmotion(new ValenceArousal(0.3, 0.5), ModelConfidence: 0.5);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.25, 0.65), ModelConfidence: 0.5);

        var withRadius = new EmotionFusion(WithNeutralRadius(0.2)).Fuse(text, prosody, Clean());
        var classic = new EmotionFusion(WithNeutralRadius(null)).Fuse(text, prosody, Clean());

        Assert.Equal(Emotion.Notr, classic.PrimaryEmotion);
        Assert.NotEqual(Emotion.Notr, withRadius.PrimaryEmotion);
    }

    [Fact]
    public void Etiket_YaricapIcindeKalanNokta_HalaNotr()
    {
        // Yarıçap kuralı "Nötr'ü yok et" demek değildir: gerçekten nötr girdi Nötr kalmalı.
        var text = new TextEmotion(new ValenceArousal(0.0, 0.5), ModelConfidence: 0.5);
        var prosody = new ProsodyEmotion(new ValenceArousal(0.05, 0.52), ModelConfidence: 0.5);
        var r = new EmotionFusion(WithNeutralRadius(0.2)).Fuse(text, prosody, Clean());
        Assert.Equal(Emotion.Notr, r.PrimaryEmotion);
    }

    [Fact]
    public void Varsayilan_NotrYaricapiAcik()
    {
        // Varsayılan config yarıçap kuralını KULLANMALI — yoksa iyileştirme kullanıcıya ulaşmaz.
        Assert.NotNull(AppConfig.Default.Fusion.NeutralRadius);
        Assert.InRange(AppConfig.Default.Fusion.NeutralRadius!.Value, 0.01, 0.5);
    }

    // --- Teşhis: kalite güvenilirlikleri sonuçta taşınır ---
    // "Neden Nötr çıktı?" sorusunun cevabı çoğu zaman ağırlıklarda değil, GİRDİ KALİTESİNDEdir
    // (rAudio·rText). Bu sayılar füzyonun içinde hesaplanıp atılırsa kullanıcı da biz de
    // körüz — sonuçla birlikte dışarı çıkmalılar.

    [Fact]
    public void Sonuc_KaliteGuvenilirliklerinToplar()
    {
        var r = Fusion.Fuse(
            new TextEmotion(new ValenceArousal(0.5, 0.5), 0.8),
            new ProsodyEmotion(new ValenceArousal(0.5, 0.5), 0.8),
            Clean());

        Assert.Equal(1.0, r.AudioReliability, precision: 6);
        Assert.Equal(1.0, r.TextReliability, precision: 6);
    }

    [Fact]
    public void Sonuc_CopSesi_DusukSesGuvenilirligiOlarakRaporlar()
    {
        // rAudio=0 (sessiz/kısa), rText=1 (uzun ve net transkript)
        var q = new QualitySignals(AudioSnr: 0, SpeechRatio: 0, Duration: TimeSpan.Zero,
            TranscriptWordCount: 20, TranscriptAvgLogProb: 0.0);

        var r = Fusion.Fuse(
            new TextEmotion(new ValenceArousal(0.5, 0.5), 0.8),
            new ProsodyEmotion(new ValenceArousal(0.5, 0.5), 0.8),
            q);

        Assert.Equal(0.0, r.AudioReliability, precision: 6);
        Assert.Equal(1.0, r.TextReliability, precision: 6);
    }

    [Fact]
    public void Sonuc_KisaTranskript_DusukMetinGuvenilirligiOlarakRaporlar()
    {
        // 1 kelime → wordF = 1/3; logprob temiz → rText ≈ 0,333
        var q = new QualitySignals(AudioSnr: 20, SpeechRatio: 1.0, Duration: TimeSpan.FromSeconds(10),
            TranscriptWordCount: 1, TranscriptAvgLogProb: 0.0);

        var r = Fusion.Fuse(
            new TextEmotion(new ValenceArousal(0.5, 0.5), 0.8),
            new ProsodyEmotion(new ValenceArousal(0.5, 0.5), 0.8),
            q);

        Assert.Equal(1.0 / 3.0, r.TextReliability, precision: 6);
    }

    private static AppConfig WithNeutralRadius(double? radius) =>
        AppConfig.Default with { Fusion = AppConfig.Default.Fusion with { NeutralRadius = radius } };
}
