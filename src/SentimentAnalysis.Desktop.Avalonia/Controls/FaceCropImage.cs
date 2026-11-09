using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SentimentAnalysis.Desktop;

namespace SentimentAnalysis.Desktop.Avalonia.Controls;

/// <summary>Önizleme karesinden yüzü, sabit 4:3 bir çerçeveye tamamen dolduracak biçimde kırpıp çizer
/// (siyah boşluk kalmaz). Çerçeve panelde ortalanır ve çok geniş panelde de belli bir boydan büyümez.
/// Kare bellekte kalır; kırpma yalnızca çizim sırasındadır.</summary>
public sealed class FaceCropImage : Control
{
    private const double Aspect = 4.0 / 3.0;      // çerçeve genişlik / yükseklik
    private const double FaceFill = 1.7;          // kırpma, yüz kutusunun kaç katı (yüzün çevresindeki pay)
    private const double MaxCropHeight = 240;

    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<FaceCropImage, IImage?>(nameof(Source));

    public static readonly StyledProperty<FaceBoxVm?> BoxProperty =
        AvaloniaProperty.Register<FaceCropImage, FaceBoxVm?>(nameof(Box));

    static FaceCropImage() => AffectsRender<FaceCropImage>(SourceProperty, BoxProperty);

    public IImage? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public FaceBoxVm? Box { get => GetValue(BoxProperty); set => SetValue(BoxProperty, value); }

    /// <summary>Yüzün merkezinde, <see cref="Aspect"/> oranında, kareyi aşmayan kırpma.</summary>
    private static Rect CropRect(FaceBoxVm box, Size image)
    {
        double h = Math.Max(box.H * FaceFill, box.W * FaceFill / Aspect);
        double w = h * Aspect;
        if (h > image.Height) { h = image.Height; w = h * Aspect; }
        if (w > image.Width) { w = image.Width; h = w / Aspect; }
        double cx = box.X + box.W / 2, cy = box.Y + box.H / 2;
        double x = Math.Clamp(cx - w / 2, 0, image.Width - w);
        double y = Math.Clamp(cy - h / 2, 0, image.Height - h);
        return new Rect(x, y, w, h);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double availW = double.IsInfinity(availableSize.Width) ? MaxCropHeight * Aspect : availableSize.Width;
        double h = Math.Min(MaxCropHeight, availW / Aspect);
        return new Size(h * Aspect, h);
    }

    public override void Render(DrawingContext context)
    {
        var source = Source;
        var box = Box;
        if (source is null || box is null || box.W <= 0 || box.H <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        using (context.PushClip(new RoundedRect(new Rect(Bounds.Size), 8)))
            context.DrawImage(source, CropRect(box, source.Size), new Rect(Bounds.Size));
    }
}
