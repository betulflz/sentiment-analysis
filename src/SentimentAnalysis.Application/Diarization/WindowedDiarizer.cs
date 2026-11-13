using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Pencereli konuşmacı ayrımı (İterasyon 2, 2026-11-13). pyannote segmentasyonu karşılıklı konuşmayı 13–17 sn'lik
/// tek parça kestiği için atlanır: ses 1,5 sn pencere / 0,5 sn adımla taranır, konuşma olan dilimlerin pencerelerinden
/// ses imzası (TitaNet) çıkarılır, kümelenir, komşu çoğunluğuyla yumuşatılır.
/// <para><b>Ölçüm</b> (5 etiketli iki kişilik kayıt, etiketli sürede doğru kişi payı, scratchpad/diar-eval):
/// sherpa+pyannote %71 (hangi embedding modeliyle olursa olsun) → pencereli %85; 5 kaydın tamamı 13 sn.
/// Bkz. docs/research/2026-11-13-pencereli-konusmaci-ayrimi.md.</para></summary>
public sealed class WindowedDiarizer(ISpeakerEmbedder embedder, IVadGate vad, AppConfig config) : IDiarizer
{
    private const int Sr = 16000;
    private readonly DiarizationConfig _cfg = config.Diarization;

    public Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(float[] mono16k, int expectedSpeakers, CancellationToken ct) =>
        Task.Run<IReadOnlyList<SpeakerSegment>>(() => Diarize(mono16k, expectedSpeakers, ct), ct);

    private List<SpeakerSegment> Diarize(float[] x, int expectedSpeakers, CancellationToken ct)
    {
        int len = (int)(_cfg.WindowSeconds * Sr), hop = (int)(_cfg.HopSeconds * Sr);
        var slices = new List<(int A, int B)>();
        var embeddings = new List<float[]>();
        for (int from = 0; from + len <= x.Length; from += hop)
        {
            ct.ThrowIfCancellationRequested();
            int mid = from + len / 2, a = mid - hop / 2, b = a + hop;
            // Konuşma kararı pencerenin temsil ettiği dilimde (SpeakerRoleTracker ile aynı ders: kenarlar taşmasın).
            if (!vad.HasSpeech(new AudioChunk(x[a..b], Sr, TimeSpan.FromSeconds((double)a / Sr), TimeSpan.FromSeconds((double)b / Sr))))
                continue;
            var e = embedder.Embed(x[from..(from + len)]);
            Normalize(e);
            slices.Add((a, b));
            embeddings.Add(e);
        }
        if (embeddings.Count == 0) return new List<SpeakerSegment>();

        var labels = expectedSpeakers > 0
            ? SpeakerClustering.Cluster(embeddings, expectedSpeakers)
            : SpeakerClustering.ClusterAuto(embeddings, _cfg.MaxAutoSpeakers, _cfg.SameSpeakerCosine);
        labels = Smooth(labels, _cfg.SmoothWindows);

        // Bitişik aynı kişili dilimler birleşir; sessizlik (atlanan dilim) bölgeleri ayırır.
        var segs = new List<SpeakerSegment>();
        int start = slices[0].A, end = slices[0].B, who = labels[0];
        for (int i = 1; i < slices.Count; i++)
        {
            if (labels[i] == who && slices[i].A <= end) { end = slices[i].B; continue; }
            segs.Add(Seg(who, start, end));
            (start, end, who) = (slices[i].A, slices[i].B, labels[i]);
        }
        segs.Add(Seg(who, start, end));
        return segs;
    }

    private static SpeakerSegment Seg(int who, int a, int b) =>
        new(new SpeakerId(who), TimeSpan.FromSeconds((double)a / Sr), TimeSpan.FromSeconds((double)b / Sr));

    /// <summary>Komşu pencerelerde çoğunluk (tek pencerelik sıçramaları bastırır); eşitlikte kendi etiketi.</summary>
    internal static int[] Smooth(int[] labels, int window)
    {
        if (window <= 1) return labels;
        var r = new int[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            var votes = new Dictionary<int, int>();
            for (int j = Math.Max(0, i - window / 2); j <= Math.Min(labels.Length - 1, i + window / 2); j++)
                votes[labels[j]] = votes.GetValueOrDefault(labels[j]) + 1;
            int top = votes.Max(v => v.Value);
            r[i] = votes[labels[i]] == top ? labels[i] : votes.First(v => v.Value == top).Key;
        }
        return r;
    }

    private static void Normalize(float[] v)
    {
        double n = Math.Sqrt(v.Sum(f => (double)f * f));
        if (n > 0) for (int i = 0; i < v.Length; i++) v[i] = (float)(v[i] / n);
    }
}
