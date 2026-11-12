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

    [Fact]
    public void TanitmaIcinYetersizKonusma_RolVermez()
    {
        var (sut, _) = Make();

        var t = sut.Label(Chunk(0, (4, Host), (6, Quiet)));

        Assert.False(t.Enrolled);
        Assert.Empty(t.Spans);
    }

    [Fact]
    public void IlkAltiSaniyeYoneten_SonrasiKarsiTaraf_DogruEtiketlenir()
    {
        var (sut, _) = Make();

        var t = sut.Label(Chunk(20, (6, Host), (4, Other)));

        Assert.True(t.Enrolled);
        Assert.False(t.EnrollmentDoubtful);
        var from = TimeSpan.FromSeconds(20);
        Assert.InRange(t.Seconds(SpeakerRole.Yoneten, from, from + TimeSpan.FromSeconds(10)), 5.5, 6.75);
        Assert.InRange(t.Seconds(SpeakerRole.KarsiTaraf, from, from + TimeSpan.FromSeconds(10)), 3.25, 4.5);
        // Zamanlar mutlak: ilk dilim parçanın başında (20 sn), son dilim sonunda (30 sn).
        Assert.Equal(from, t.Spans[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(30), t.Spans[^1].End);
        Assert.Equal(SpeakerRole.Yoneten, t.Spans[0].Role);
        Assert.Equal(SpeakerRole.KarsiTaraf, t.Spans[^1].Role);
    }

    [Fact]
    public void TanitmaSirasindaKarsiTarafKonusursa_SupheliIsaretlenir()
    {
        var (sut, _) = Make();

        var t = sut.Label(Chunk(0, (3, Host), (7, Other)));

        Assert.True(t.Enrolled);
        Assert.True(t.EnrollmentDoubtful);
    }

    [Fact]
    public void ParcalarArasiOrtusme_TanitmadaIkiKezSayilmaz()
    {
        var (sut, _) = Make();
        Assert.False(sut.Label(Chunk(0, (5, Host))).Enrolled);       // dilimler 0–5 sn'yi döşer → 5 sn toplandı

        // 3–5,5 sn tekrar geliyor (örtüşme): dilimleri 3, 4 ve 4,5 sn'de başlar, hepsi imleçten (5 sn) önce → +0.
        // Tekrar sayılsaydı +2,5 sn ile tanıtma (6 sn) tamamlanırdı.
        var t = sut.Label(Chunk(3, (2.5, Host)));

        Assert.False(t.Enrolled);
    }

    [Fact]
    public void TanitmaParcalarArasindaTamamlanir()
    {
        var (sut, _) = Make();
        sut.Label(Chunk(0, (4, Host), (6, Quiet)));

        var t = sut.Label(Chunk(8, (2, Quiet), (4, Host), (4, Other)));

        Assert.True(t.Enrolled);
        Assert.False(t.EnrollmentDoubtful);
        Assert.Contains(t.Spans, s => s.Role == SpeakerRole.KarsiTaraf);
    }

    [Fact]
    public void Sessizlik_DilimUretmez()
    {
        var (sut, _) = Make();
        sut.Label(Chunk(0, (10, Host)));

        var t = sut.Label(Chunk(10, (3, Other), (4, Quiet), (3, Other)));

        Assert.All(t.Spans, s => Assert.Equal(SpeakerRole.KarsiTaraf, s.Role));
        Assert.Equal(2, t.Spans.Count);   // sessizlik iki konuşma koşusunu ayırır, birleştirmez
        Assert.InRange(t.Seconds(SpeakerRole.KarsiTaraf, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)), 5.5, 6.5);
    }

    [Fact]
    public void ModelHatasi_KosuyuDurdurmaz_OzellikKapanir()
    {
        var (sut, emb) = Make();
        emb.Throw = true;

        var t = sut.Label(Chunk(0, (10, Host)));

        Assert.False(t.Enrolled);
        Assert.Equal("model yok", sut.Failure);
        emb.Throw = false;
        Assert.False(sut.Label(Chunk(10, (10, Host))).Enrolled);   // bu koşu için kapalı kalır
    }

    [Fact]
    public void Reset_TanitmayiVeHatayiSifirlar()
    {
        var (sut, _) = Make();
        sut.Label(Chunk(0, (10, Host)));
        Assert.True(sut.Enrolled);

        sut.Reset();

        Assert.False(sut.Enrolled);
        Assert.Null(sut.Failure);
        Assert.False(sut.Label(Chunk(0, (4, Other))).Enrolled);
    }

    [Fact]
    public void Kapaliyken_ModeleHicDokunmaz()
    {
        var (sut, emb) = Make(new RoleConfig(Enabled: false));

        var t = sut.Label(Chunk(0, (10, Host)));

        Assert.False(t.Enrolled);
        Assert.Equal(0, emb.Calls);
    }
}
