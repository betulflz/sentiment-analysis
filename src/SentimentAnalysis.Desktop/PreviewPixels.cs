using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Önizleme karesi dönüşümü: ffmpeg'in RGB24 karesi → ekranın BGRA8888 düzeni (A = 255).
/// Yalnızca bellekte; kare diske yazılmaz (spec 11 §4).</summary>
public static class PreviewPixels
{
    public static byte[] ToBgra(VideoFrame frame)
    {
        var bgra = new byte[frame.Width * frame.Height * 4];
        for (int y = 0; y < frame.Height; y++)
            WriteBgraRow(frame, y, bgra.AsSpan(y * frame.Width * 4, frame.Width * 4));
        return bgra;
    }

    /// <summary>Karenin <paramref name="y"/>. satırını BGRA olarak <paramref name="row"/>'a yazar (en az
    /// genişlik × 4 bayt). Önizleme tek satırlık tamponu tekrar kullanır — kare başına tam kare ayırmaz.</summary>
    public static void WriteBgraRow(VideoFrame frame, int y, Span<byte> row)
    {
        var rgb = frame.Rgb24.AsSpan(y * frame.Width * 3, frame.Width * 3);
        for (int x = 0; x < frame.Width; x++)
        {
            row[4 * x] = rgb[3 * x + 2];
            row[4 * x + 1] = rgb[3 * x + 1];
            row[4 * x + 2] = rgb[3 * x];
            row[4 * x + 3] = 255;
        }
    }
}

/// <summary>Önizlemedeki bir yüz kutusu (kare pikselinde) ve etiketi ("Yüz 1").</summary>
public sealed record FaceBoxVm(double X, double Y, double W, double H, string Label);
