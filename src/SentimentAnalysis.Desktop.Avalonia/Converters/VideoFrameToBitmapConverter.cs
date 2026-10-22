using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop.Avalonia.Converters;

/// <summary>The view owns and disposes the returned bitmap.</summary>
public sealed class VideoFrameToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not VideoFrame frame) return null;
        var pixels = PreviewPixels.ToBgra(frame);
        var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var buffer = bitmap.Lock();
        var rowBytes = frame.Width * 4;
        for (var y = 0; y < frame.Height; y++)
            Marshal.Copy(pixels, y * rowBytes, IntPtr.Add(buffer.Address, y * buffer.RowBytes), rowBytes);
        return bitmap;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
