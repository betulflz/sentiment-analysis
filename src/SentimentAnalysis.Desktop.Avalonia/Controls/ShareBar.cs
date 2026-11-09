using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop.Avalonia.Controls;

/// <summary>Duygu paylarını tek yatay şerit olarak çizer; her parça payı kadar geniş, duygunun renginde.
/// Yüzde ve süre şeridin altındaki açıklamada yazar.</summary>
public sealed class ShareBar : Control
{
    public static readonly StyledProperty<IReadOnlyList<TimeShareVm>?> ItemsProperty =
        AvaloniaProperty.Register<ShareBar, IReadOnlyList<TimeShareVm>?>(nameof(Items));

    static ShareBar() => AffectsRender<ShareBar>(ItemsProperty);

    public IReadOnlyList<TimeShareVm>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var items = Items;
        double w = Bounds.Width, h = Bounds.Height;
        if (items is null || items.Count == 0 || w <= 0 || h <= 0) return;

        using (context.PushClip(new RoundedRect(new Rect(0, 0, w, h), 8)))
        {
            double x = 0;
            foreach (var item in items)
            {
                double width = item.Share * w;
                context.FillRectangle(EmotionColors.Brush(item.Key), new Rect(x, 0, width, h));
                x += width;
            }
        }
    }
}

/// <summary>Duygu → renk. Olumlu duygular sıcak/yeşil, olumsuzlar mavi/kırmızı, nötr gri.</summary>
public static class EmotionColors
{
    public static IBrush Brush(Emotion e) => new SolidColorBrush(Color.Parse(e switch
    {
        Emotion.Cosakulu => "#F07A3C",
        Emotion.Mutlu => "#F2B13C",
        Emotion.Sakin => "#3FB27F",
        Emotion.Sikilmis => "#9AA3B5",
        Emotion.Uzgun => "#7C8BE0",
        Emotion.Gergin => "#C46BD6",
        Emotion.Kizgin => "#E0485E",
        _ => "#B8B3C9",
    }));
}
