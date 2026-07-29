using System;
using System.Linq;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class ProsodyMathTests
{
    [Fact]
    public void Standardize_ProducesZeroMeanUnitVariance()
    {
        var x = new float[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var y = AudioNormalizer.Standardize(x);
        double mean = y.Average();
        double var = y.Select(v => (v - mean) * (v - mean)).Average();
        Assert.True(Math.Abs(mean) < 1e-4, $"ort ~0 bekleniyor, {mean}");
        Assert.True(Math.Abs(var - 1.0) < 1e-2, $"varyans ~1 bekleniyor, {var}");
    }

    [Fact]
    public void Standardize_ConstantSignal_ReturnsZeros()
    {
        var y = AudioNormalizer.Standardize(new float[] { 0.4f, 0.4f, 0.4f });
        Assert.All(y, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Standardize_Empty_ReturnsEmpty()
    {
        Assert.Empty(AudioNormalizer.Standardize(Array.Empty<float>()));
    }

    [Fact]
    public void Map_ValenceScaledToMinusOneOne_ArousalDirect()
    {
        // logits = [arousal, dominance, valence]
        var p = ProsodyOutputMapper.Map(new float[] { 0.9f, 0.5f, 0.75f });
        Assert.Equal(0.5, p.Va.Valence, precision: 5);   // 0.75*2-1 = 0.5
        Assert.Equal(0.9, p.Va.Arousal, precision: 5);   // direkt
    }

    [Fact]
    public void Map_NeutralOutput_LowConfidence()
    {
        // arousal 0.5, valence 0.5 → domain (0, 0.5) = nötr merkez → güven ~0
        var p = ProsodyOutputMapper.Map(new float[] { 0.5f, 0.5f, 0.5f });
        Assert.True(p.ModelConfidence < 0.05, $"nötr → düşük güven, {p.ModelConfidence}");
    }

    [Fact]
    public void Map_ExtremeOutput_HighConfidence()
    {
        // arousal 0.95, valence 1.0 → merkezden uzak → yüksek güven
        var p = ProsodyOutputMapper.Map(new float[] { 0.95f, 0.5f, 1.0f });
        Assert.True(p.ModelConfidence > 0.8, $"uç → yüksek güven, {p.ModelConfidence}");
    }

    [Fact]
    public void Map_ClampsOutOfRangeLogits()
    {
        var p = ProsodyOutputMapper.Map(new float[] { 1.3f, 0.5f, -0.2f });
        Assert.InRange(p.Va.Arousal, 0.0, 1.0);
        Assert.InRange(p.Va.Valence, -1.0, 1.0);
        Assert.InRange(p.ModelConfidence, 0.0, 1.0);
    }

    // --- Kazanç (gain) ---
    // audeering msp-dim ham valence'ı pratikte 0,35–0,65 bandında üretir; ×2−1 sonrası
    // −0,3…+0,3 çıkar ve bu aralık EmotionWheel'in nötr havzasının TAMAMEN içindedir.
    // Kazanç, nötr merkez (0, 0.5) etrafında doğrusal genişletmedir — model çıktısının
    // İŞARETİNİ değil, YAYILIMINI düzeltir.

    [Fact]
    public void Map_ValenceGain_NotrMerkezEtrafindaGenisletir()
    {
        // ham 0,65 → kazançsız +0,30; kazanç 2,0 ile +0,60
        var p = ProsodyOutputMapper.Map(
            new float[] { 0.5f, 0.5f, 0.65f }, valenceGain: 2.0, arousalGain: 1.0);
        Assert.Equal(0.6, p.Va.Valence, precision: 5);
    }

    [Fact]
    public void Map_ArousalGain_YarimEtrafindaGenisletir()
    {
        // ham 0,65 → kazançsız 0,65; kazanç 2,0 ile 0,5 + 2·0,15 = 0,80
        var p = ProsodyOutputMapper.Map(
            new float[] { 0.65f, 0.5f, 0.5f }, valenceGain: 1.0, arousalGain: 2.0);
        Assert.Equal(0.8, p.Va.Arousal, precision: 5);
    }

    [Fact]
    public void Map_Kazanc_NotrGirdiyiNotrBirakir()
    {
        // Kazanç merkez etrafında simetriktir: tam nötr girdi kazançla da nötr kalmalı,
        // yoksa sessiz bir yanlılık (bias) enjekte etmiş oluruz.
        var p = ProsodyOutputMapper.Map(
            new float[] { 0.5f, 0.5f, 0.5f }, valenceGain: 2.5, arousalGain: 2.5);
        Assert.Equal(0.0, p.Va.Valence, precision: 5);
        Assert.Equal(0.5, p.Va.Arousal, precision: 5);
    }

    [Fact]
    public void Map_Kazanc_AlanSinirlarinaKirpar()
    {
        var p = ProsodyOutputMapper.Map(
            new float[] { 0.9f, 0.5f, 0.9f }, valenceGain: 3.0, arousalGain: 3.0);
        Assert.Equal(1.0, p.Va.Valence, precision: 5);
        Assert.Equal(1.0, p.Va.Arousal, precision: 5);
    }

    [Fact]
    public void Map_KazancVarsayilani_BireBirdir()
    {
        // Kazanç POLİTİKASI EmotionConfig'te yaşar; saf matematik varsayılanı kimliktir.
        var withDefault = ProsodyOutputMapper.Map(new float[] { 0.7f, 0.5f, 0.65f });
        var explicitUnity = ProsodyOutputMapper.Map(
            new float[] { 0.7f, 0.5f, 0.65f }, valenceGain: 1.0, arousalGain: 1.0);
        Assert.Equal(explicitUnity.Va.Valence, withDefault.Va.Valence, precision: 9);
        Assert.Equal(explicitUnity.Va.Arousal, withDefault.Va.Arousal, precision: 9);
    }

    [Fact]
    public void Map_Kazanc_GuveniDeArtirir()
    {
        // Güven merkeze uzaklıktan türediği için genişletilmiş VA daha yüksek güven vermeli.
        var flat = ProsodyOutputMapper.Map(new float[] { 0.6f, 0.5f, 0.6f });
        var expanded = ProsodyOutputMapper.Map(
            new float[] { 0.6f, 0.5f, 0.6f }, valenceGain: 2.0, arousalGain: 2.0);
        Assert.True(expanded.ModelConfidence > flat.ModelConfidence,
            $"genişletilmiş güven ({expanded.ModelConfidence}) > düz ({flat.ModelConfidence}) olmalı");
    }
}
