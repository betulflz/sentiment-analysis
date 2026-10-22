using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Vision;

/// <summary>Kareler arası kararlı yüz indeksi (oturum içi, kimlik YOK). İlk karede soldan sağa
/// 0,1,2…; sonra her yüz, kutu örtüşmesi (IoU &gt; 0,3) ya da merkez yakınlığıyla (yarım kutu
/// genişliği) bilinen bir ize bağlanır. İzler silinmez — kamera geçişiyle kaybolan yüz geri gelince
/// aynı indeksi alır. 11a: mülakat videosunda yüzlerin x konumu sabitti, bu basit kural yeterli.</summary>
public sealed class FaceTracker
{
    private const float IouMatch = 0.3f;
    private const float CenterMatchRatio = 0.5f;
    private readonly List<FaceBox> _tracks = new();

    public IReadOnlyList<(int Index, DetectedFace Face)> Assign(IReadOnlyList<DetectedFace> faces)
    {
        var candidates = new List<(int Face, int Track, float Iou, float Dist)>();
        for (int i = 0; i < faces.Count; i++)
            for (int k = 0; k < _tracks.Count; k++)
            {
                var a = faces[i].Box;
                var b = _tracks[k];
                float iou = Iou(a, b);
                float dist = MathF.Sqrt(MathF.Pow(a.CenterX - b.CenterX, 2) + MathF.Pow(a.CenterY - b.CenterY, 2));
                if (iou > IouMatch || dist < CenterMatchRatio * a.W)
                    candidates.Add((i, k, iou, dist));
            }

        var faceToTrack = new int?[faces.Count];
        var usedTracks = new HashSet<int>();
        foreach (var c in candidates.OrderByDescending(c => c.Iou).ThenBy(c => c.Dist))
        {
            if (faceToTrack[c.Face] is not null || usedTracks.Contains(c.Track)) continue;
            faceToTrack[c.Face] = c.Track;
            usedTracks.Add(c.Track);
        }

        // Eşleşmeyen yüzler yeni iz alır, soldan sağa.
        foreach (var i in Enumerable.Range(0, faces.Count).Where(i => faceToTrack[i] is null).OrderBy(i => faces[i].Box.X))
        {
            _tracks.Add(faces[i].Box);
            faceToTrack[i] = _tracks.Count - 1;
        }

        var result = new List<(int, DetectedFace)>(faces.Count);
        for (int i = 0; i < faces.Count; i++)
        {
            int k = faceToTrack[i]!.Value;
            _tracks[k] = faces[i].Box;
            result.Add((k, faces[i]));
        }
        return result;
    }

    public void Reset() => _tracks.Clear();

    private static float Iou(FaceBox a, FaceBox b)
    {
        float x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
        float x2 = Math.Min(a.X + a.W, b.X + b.W), y2 = Math.Min(a.Y + a.H, b.Y + b.H);
        float inter = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        float union = a.W * a.H + b.W * b.H - inter;
        return union <= 0 ? 0 : inter / union;
    }
}
