using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Önizleme karesi dönüşümü: ffmpeg'in RGB24 karesi → ekranın BGRA8888 düzeni (A = 255).
/// Yalnızca bellekte; kare diske yazılmaz (spec 11 §4).</summary>
public static class PreviewPixels
{
    public static byte[] ToBgra(VideoFrame frame)
    {
        int pixels = frame.Width * frame.Height;
        var bgra = new byte[pixels * 4];
        var rgb = frame.Rgb24;
        for (int i = 0; i < pixels; i++)
        {
            bgra[4 * i] = rgb[3 * i + 2];
            bgra[4 * i + 1] = rgb[3 * i + 1];
            bgra[4 * i + 2] = rgb[3 * i];
            bgra[4 * i + 3] = 255;
        }
        return bgra;
    }
}

/// <summary>Önizlemedeki bir yüz kutusu (kare pikselinde) ve etiketi ("Yüz 1").</summary>
public sealed record FaceBoxVm(double X, double Y, double W, double H, string Label);
