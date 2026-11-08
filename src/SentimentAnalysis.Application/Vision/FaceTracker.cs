using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Vision;

/// <summary>Kareler arası kararlı yüz indeksi (oturum içi, kimlik YOK). Her yüz, kutu örtüşmesi (IoU &gt; 0,3)
/// ya da merkez yakınlığıyla (yarım kutu genişliği) bilinen bir ize bağlanır; olmazsa bu karede görülmemiş bir
/// ize daha geniş yarıçapla (1,5 kutu genişliği) bağlanmayı dener — hızlı baş hareketi yeni numara açmasın.
/// Eşleşmeler boyu benzer kutularla sınırlıdır (½–2 kat): küçük bir sahte tespit kayıp yüzün numarasını çalamaz.
/// <para>Yeni yüz, ardışık 3 analiz karesinin en az 2'sinde görülmeden numara almaz ve raporlanmaz: YuNet'in tek
/// karelik yanlış pozitifleri canlı kamerada 2 kişiye 15 numara açtırıyordu (2026-10-27 ölçümü). Doğrulanan
/// yüzler sırayla (aynı karede soldan sağa) 0,1,2… alır. İzler silinmez — kamera geçişiyle kaybolan yüz geri
/// gelince aynı indeksi alır.</para></summary>
public sealed class FaceTracker
{
    private const float IouMatch = 0.3f;
    private const float CenterMatchRatio = 0.5f;
    private const float ReacquireRatio = 1.5f;
    private const float MinSizeRatio = 0.5f;
    private const int ConfirmHits = 2;
    private const int CandidateMaxAge = 2; // aday en son bu kadar kare önce görüldüyse hâlâ geçerli

    private readonly List<FaceBox> _tracks = new();
    private readonly List<Candidate> _candidates = new();
    private int _frame;

    private sealed class Candidate(FaceBox box, int seen)
    {
        public FaceBox Box = box;
        public int Hits = 1;
        public int LastSeen = seen;
    }

    public IReadOnlyList<(int Index, DetectedFace Face)> Assign(IReadOnlyList<DetectedFace> faces)
    {
        _frame++;
        var faceToTrack = new int?[faces.Count];
        var usedTracks = new HashSet<int>();

        Match(faces, faceToTrack, _tracks, usedTracks,
            (a, b) => Iou(a, b) > IouMatch || Distance(a, b) < CenterMatchRatio * a.W);
        Match(faces, faceToTrack, _tracks, usedTracks, (a, b) => Distance(a, b) < ReacquireRatio * a.W);

        // Eşleşmeyen yüzler aday olur; ikinci görülüşte numara alır.
        _candidates.RemoveAll(c => _frame - c.LastSeen > CandidateMaxAge);
        var faceToCandidate = new int?[faces.Count];
        var usedCandidates = new HashSet<int>();
        var unassigned = new int?[faces.Count];
        for (int i = 0; i < faces.Count; i++) unassigned[i] = faceToTrack[i] is null ? null : -1;
        Match(faces, unassigned, _candidates.Select(c => c.Box).ToList(), usedCandidates,
            (a, b) => Iou(a, b) > IouMatch || Distance(a, b) < CenterMatchRatio * a.W);

        var promoted = new List<(int Face, Candidate Candidate)>();
        for (int i = 0; i < faces.Count; i++)
        {
            if (faceToTrack[i] is not null) continue;
            if (unassigned[i] is int c)
            {
                var candidate = _candidates[c];
                candidate.Box = faces[i].Box;
                candidate.Hits++;
                candidate.LastSeen = _frame;
                if (candidate.Hits >= ConfirmHits) promoted.Add((i, candidate));
            }
            else _candidates.Add(new Candidate(faces[i].Box, _frame));
        }
        foreach (var (i, candidate) in promoted.OrderBy(p => faces[p.Face].Box.X))
        {
            _candidates.Remove(candidate);
            _tracks.Add(faces[i].Box);
            faceToTrack[i] = _tracks.Count - 1;
        }

        var result = new List<(int, DetectedFace)>(faces.Count);
        for (int i = 0; i < faces.Count; i++)
        {
            if (faceToTrack[i] is not int k) continue;
            _tracks[k] = faces[i].Box;
            result.Add((k, faces[i]));
        }
        return result;
    }

    public void Reset()
    {
        _tracks.Clear();
        _candidates.Clear();
        _frame = 0;
    }

    /// <summary>Atanmamış yüzleri (<paramref name="assigned"/>[i] null) boştaki hedeflere açgözlü bağlar:
    /// önce en yüksek örtüşme, sonra en yakın merkez.</summary>
    private static void Match(IReadOnlyList<DetectedFace> faces, int?[] assigned, IReadOnlyList<FaceBox> targets,
        HashSet<int> used, Func<FaceBox, FaceBox, bool> close)
    {
        var pairs = new List<(int Face, int Target, float Iou, float Dist)>();
        for (int i = 0; i < faces.Count; i++)
        {
            if (assigned[i] is not null) continue;
            for (int k = 0; k < targets.Count; k++)
            {
                if (used.Contains(k)) continue;
                var a = faces[i].Box;
                var b = targets[k];
                if (SimilarSize(a, b) && close(a, b)) pairs.Add((i, k, Iou(a, b), Distance(a, b)));
            }
        }
        foreach (var p in pairs.OrderByDescending(p => p.Iou).ThenBy(p => p.Dist))
        {
            if (assigned[p.Face] is not null || used.Contains(p.Target)) continue;
            assigned[p.Face] = p.Target;
            used.Add(p.Target);
        }
    }

    private static bool SimilarSize(FaceBox a, FaceBox b) =>
        Math.Min(a.H, b.H) >= MinSizeRatio * Math.Max(a.H, b.H);

    private static float Distance(FaceBox a, FaceBox b) =>
        MathF.Sqrt(MathF.Pow(a.CenterX - b.CenterX, 2) + MathF.Pow(a.CenterY - b.CenterY, 2));

    private static float Iou(FaceBox a, FaceBox b)
    {
        float x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
        float x2 = Math.Min(a.X + a.W, b.X + b.W), y2 = Math.Min(a.Y + a.H, b.Y + b.H);
        float inter = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        float union = a.W * a.H + b.W * b.H - inter;
        return union <= 0 ? 0 : inter / union;
    }
}
