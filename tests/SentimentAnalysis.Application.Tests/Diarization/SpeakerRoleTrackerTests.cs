using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Tests.Diarization;

/// <summary>Sahte ses: yönetenin sesi +0,3, karşı tarafın sesi −0,3, sessizlik 0 (sabit örnekler). Sahte imza
/// [yöneten payı, karşı taraf payı] olduğundan karışık pencerenin benzerliği payla orantılıdır.</summary>
public class SpeakerRoleTrackerTests
{
    private const int Sr = 16000;
    private const float Host = 0.3f, Other = -0.3f, Quiet = 0f;

    private sealed class FakeEmbedder : ISpeakerEmbedder
    {
        public int Calls;
        public bool Throw;
        public float[] Embed(float[] x)
        {
            Calls++;
            if (Throw) throw new FileNotFoundException("model yok");
            float h = x.Count(v => v > 0.1f), o = x.Count(v => v < -0.1f);
            return new[] { h, o };
        }
    }

    private sealed class FakeVad : IVadGate
    {
        public bool HasSpeech(AudioChunk c) => c.Samples.Average(v => Math.Abs(v)) > 0.1;
    }

    /// <summary>[başlangıç sn, (süre sn, değer)…] → parça.</summary>
    private static AudioChunk Chunk(double start, params (double Seconds, float Value)[] parts)
    {
        var samples = parts.SelectMany(p => Enumerable.Repeat(p.Value, (int)(p.Seconds * Sr))).ToArray();
        return new AudioChunk(samples, Sr, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(start + samples.Length / (double)Sr));
    }

    private static (SpeakerRoleTracker Sut, FakeEmbedder Emb) Make(RoleConfig? cfg = null)
    {
        var emb = new FakeEmbedder();
        var config = AppConfig.Default with { Role = cfg ?? new RoleConfig() };
        return (new SpeakerRoleTracker(emb, new FakeVad(), config), emb);
    }

    private static float[] Pcm(params (double Seconds, float Value)[] parts) => Chunk(0, parts).Samples;

    private static (SpeakerRoleTracker Sut, FakeEmbedder Emb) Enrolled()
    {
        var (sut, emb) = Make();
        Assert.True(sut.Enroll(Pcm((8, Host))).Success);
        return (sut, emb);
    }

    [Fact]
    public void TanitmaYokken_RolVermez_ModeleDokunmaz()
    {
        var (sut, emb) = Make();

        var t = sut.Label(Chunk(0, (10, Host)));

        Assert.False(t.Enrolled);
        Assert.Empty(t.Spans);
        Assert.Equal(0, emb.Calls);
    }

    [Fact]
    public void Tanitma_SessizligiAyiklar_YetersizKonusmadaReddeder()
    {
        var (sut, _) = Make();

        var r = sut.Enroll(Pcm((3, Host), (5, Quiet)));

        Assert.False(r.Success);
        Assert.InRange(r.SpeechSeconds, 2.5, 3.5);
        Assert.False(sut.Enrolled);
    }

    [Fact]
    public void Tanitma_SessizlikliAmaYeterliKonusma_Basarili()
    {
        var (sut, _) = Make();

        var r = sut.Enroll(Pcm((2.5, Host), (2, Quiet), (3, Host)));

        Assert.True(r.Success);
        Assert.False(r.Doubtful);
        Assert.InRange(r.SpeechSeconds, 5, 6);
        Assert.True(sut.Enrolled);
    }

    [Fact]
    public void TanitmadaIkiSes_SupheliAmaTanitilir()
    {
        var (sut, _) = Make();

        var r = sut.Enroll(Pcm((4, Host), (4, Other)));

        Assert.True(r.Success);
        Assert.True(r.Doubtful);
        Assert.True(sut.Enrolled);
    }

    [Fact]
    public void BasarisizTanitma_OncekiTanitmayiKorur()
    {
        var (sut, _) = Enrolled();

        Assert.False(sut.Enroll(Pcm((1, Other), (7, Quiet))).Success);

        Assert.True(sut.Enrolled);
        Assert.Equal(SpeakerRole.Yoneten, sut.Label(Chunk(0, (10, Host))).Spans.Single().Role);
    }

    [Fact]
    public void YonetenVeKarsiTaraf_DogruEtiketlenir_ZamanlarMutlak()
    {
        var (sut, _) = Enrolled();

        var t = sut.Label(Chunk(20, (6, Host), (4, Other)));

        Assert.True(t.Enrolled);
        var from = TimeSpan.FromSeconds(20);
        Assert.InRange(t.Seconds(SpeakerRole.Yoneten, from, from + TimeSpan.FromSeconds(10)), 5.5, 6.5);
        Assert.InRange(t.Seconds(SpeakerRole.KarsiTaraf, from, from + TimeSpan.FromSeconds(10)), 3.5, 4.5);
        Assert.Equal(from, t.Spans[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(30), t.Spans[^1].End);
        Assert.Equal(SpeakerRole.Yoneten, t.Spans[0].Role);
        Assert.Equal(SpeakerRole.KarsiTaraf, t.Spans[^1].Role);
    }

    [Fact]
    public void Sessizlik_DilimUretmez_KonusmaSuresiSismez()
    {
        var (sut, _) = Enrolled();

        var t = sut.Label(Chunk(10, (3, Other), (4, Quiet), (3, Other)));

        Assert.All(t.Spans, s => Assert.Equal(SpeakerRole.KarsiTaraf, s.Role));
        Assert.Equal(2, t.Spans.Count);   // sessizlik iki konuşma koşusunu ayırır, birleştirmez
        Assert.InRange(t.Seconds(SpeakerRole.KarsiTaraf, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)), 5.5, 6.5);
    }

    [Fact]
    public void ModelHatasi_KosuyuDurdurmaz_OzellikKosuBoyuncaKapanir()
    {
        var (sut, emb) = Enrolled();
        emb.Throw = true;

        var t = sut.Label(Chunk(0, (10, Host)));

        Assert.False(t.Enrolled);
        Assert.Equal("model yok", sut.Failure);
        emb.Throw = false;
        Assert.False(sut.Label(Chunk(10, (10, Host))).Enrolled);
    }

    [Fact]
    public void Reset_HatayiSifirlar_TanitmayiKorur()
    {
        var (sut, emb) = Enrolled();
        emb.Throw = true;
        sut.Label(Chunk(0, (10, Host)));
        emb.Throw = false;

        sut.Reset();

        Assert.Null(sut.Failure);
        Assert.True(sut.Label(Chunk(0, (10, Other))).Enrolled);
    }

    [Fact]
    public void ClearEnrollment_RolleriKapatir()
    {
        var (sut, _) = Enrolled();

        sut.ClearEnrollment();

        Assert.False(sut.Enrolled);
        Assert.False(sut.Label(Chunk(0, (10, Host))).Enrolled);
    }

    [Fact]
    public void Kapaliyken_ModeleHicDokunmaz()
    {
        var (sut, emb) = Make(new RoleConfig(Enabled: false));
        sut.Enroll(Pcm((8, Host)));
        int afterEnroll = emb.Calls;

        var t = sut.Label(Chunk(0, (10, Host)));

        Assert.False(t.Enrolled);
        Assert.Equal(afterEnroll, emb.Calls);
    }
}
