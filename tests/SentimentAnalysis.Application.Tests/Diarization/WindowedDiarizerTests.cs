using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

/// <summary>Pencereli konuşmacı ayrımı (İterasyon 2, 2026-11-13): pyannote segmentasyonu yerine 1,5 sn pencere imzaları + kümeleme.</summary>
public class WindowedDiarizerTests
{
    private const int Sr = 16000;

    // Sahte ses: her örneğin değeri konuşmacıyı kodlar (0 = sessizlik, 0.1 = A, 0.2 = B, 0.3 = C).
    private sealed class Embedder : ISpeakerEmbedder
    {
        public int Calls;
        public Func<int>? OnCall;
        public float[] Embed(float[] x)
        {
            Calls++; OnCall?.Invoke();
            // pencerenin baskın değeri → o kişinin yönü (küçük gürültüyle)
            var v = x.Where(s => s > 0).GroupBy(s => MathF.Round(s, 1)).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            int who = (int)MathF.Round(v * 10);
            var e = new float[4];
            e[Math.Clamp(who, 1, 3)] = 1f;
            e[0] = 0.05f * (Calls % 3);
            return e;
        }
    }
    private sealed class Vad : IVadGate
    {
        public bool HasSpeech(AudioChunk c) => c.Samples.Count(s => s > 0) > c.Samples.Length / 2;
    }

    private static float[] Audio(params (float Who, double Seconds)[] parts) =>
        parts.SelectMany(p => Enumerable.Repeat(p.Who, (int)(p.Seconds * Sr))).ToArray();

    private static WindowedDiarizer Make(Embedder? e = null) => new(e ?? new Embedder(), new Vad(), AppConfig.Default);

    private static double Talk(IReadOnlyList<SpeakerSegment> segs, SpeakerId s) =>
        segs.Where(x => x.Speaker == s).Sum(x => (x.End - x.Start).TotalSeconds);

    [Fact]
    public async Task IkiKisi_SinirdaAyrilir()
    {
        var segs = await Make().DiarizeAsync(Audio((0.1f, 10), (0.2f, 10)), 2, default);
        var speakers = segs.Select(s => s.Speaker).Distinct().ToArray();
        Assert.Equal(2, speakers.Length);
        var first = segs.OrderBy(s => s.Start).First().Speaker;
        var boundary = segs.Where(s => s.Speaker == first).Max(s => s.End).TotalSeconds;
        Assert.InRange(boundary, 9.0, 11.0);
        Assert.All(segs, s => Assert.True(s.End > s.Start));
    }

    [Fact]
    public async Task Sessizlik_BolgeUretmez()
    {
        var segs = await Make().DiarizeAsync(Audio((0.1f, 6), (0f, 6), (0.2f, 6)), 2, default);
        Assert.DoesNotContain(segs, s => s.Start.TotalSeconds > 7.5 && s.End.TotalSeconds < 10.5);
    }

    [Fact]
    public async Task TekPencerelikSicrama_Bastirilir()
    {
        // A'nın ortasında tek pencerelik B benzeri parazit: komşu çoğunluğu A'ya döndürür.
        var segs = await Make().DiarizeAsync(Audio((0.1f, 8), (0.2f, 0.5), (0.1f, 8), (0.2f, 8)), 2, default);
        Assert.Equal(2, segs.Select(s => s.Speaker).Distinct().Count());
        var a = segs.OrderBy(s => s.Start).First().Speaker;
        Assert.True(Talk(segs, a) > 14);
    }

    [Fact]
    public async Task KisiSayisiBilinmiyor_IkiFarkliSes_IkiKisi()
    {
        var segs = await Make().DiarizeAsync(Audio((0.1f, 8), (0.2f, 8), (0.1f, 8)), 0, default);
        Assert.Equal(2, segs.Select(s => s.Speaker).Distinct().Count());
    }

    [Fact]
    public async Task KisiSayisiBilinmiyor_TekSes_TekKisi()
    {
        var segs = await Make().DiarizeAsync(Audio((0.1f, 20)), 0, default);
        Assert.Single(segs.Select(s => s.Speaker).Distinct());
    }

    [Fact]
    public async Task UcKisi_Verilirse_UcKume()
    {
        var segs = await Make().DiarizeAsync(Audio((0.1f, 8), (0.2f, 8), (0.3f, 8)), 3, default);
        Assert.Equal(3, segs.Select(s => s.Speaker).Distinct().Count());
    }

    [Fact]
    public async Task BosVeKisaSes_Bos()
    {
        Assert.Empty(await Make().DiarizeAsync(Array.Empty<float>(), 2, default));
        Assert.Empty(await Make().DiarizeAsync(Audio((0f, 3)), 2, default));
    }

    [Fact]
    public async Task Iptal_PencereArasindaDurur()
    {
        using var cts = new CancellationTokenSource();
        var e = new Embedder();
        e.OnCall = () => { if (e.Calls == 3) cts.Cancel(); return 0; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Make(e).DiarizeAsync(Audio((0.1f, 30)), 2, cts.Token));
        Assert.True(e.Calls < 10);
    }
}

public class SpeakerClusteringTests
{
    private static float[] V(params float[] x) { var n = MathF.Sqrt(x.Sum(v => v * v)); return x.Select(v => v / n).ToArray(); }

    [Fact]
    public void IkiGrup_Ayrilir()
    {
        var e = new List<float[]>();
        for (int i = 0; i < 10; i++) e.Add(V(1, 0.1f * (i % 3), 0));
        for (int i = 0; i < 10; i++) e.Add(V(0, 0.1f * (i % 3), 1));
        var l = SpeakerClustering.Cluster(e, 2);
        Assert.Single(l.Take(10).Distinct());
        Assert.Single(l.Skip(10).Distinct());
        Assert.NotEqual(l[0], l[10]);
    }

    [Fact]
    public void AykiriTekNokta_AyriKisiOlmaz()
    {
        // Gerçek iki grup + tek aykırı: aykırı kendi kümesini almaz (eski tek başlangıçlı k-means hatası, 6339a9fa).
        var e = new List<float[]>();
        for (int i = 0; i < 20; i++) e.Add(V(1, 0.3f + 0.02f * i, 0));
        for (int i = 0; i < 20; i++) e.Add(V(1, 0, 0.3f + 0.02f * i));
        e.Add(V(-1, 0, 0));
        var l = SpeakerClustering.Cluster(e, 2);
        Assert.NotEqual(l[0], l[20]);
    }

    [Fact]
    public void Otomatik_Benzerler_TekKume()
    {
        var e = Enumerable.Range(0, 20).Select(i => V(1, 0.05f * (i % 4), 0.05f * (i % 3))).ToList();
        Assert.Single(SpeakerClustering.ClusterAuto(e, maxK: 4, sameSpeakerCosine: 0.55).Distinct());
    }
}
