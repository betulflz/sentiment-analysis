using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop.Avalonia.Controls;

/// <summary>Payları tek yatay şerit olarak çizer; her parça payı kadar geniş. Duygu payı (<see cref="TimeShareVm"/>)
/// duygunun, konuşma payı (<see cref="TalkShareVm"/>) konuşmacının renginde. Yüzde/süre altındaki açıklamada yazar.</summary>
public sealed class ShareBar : Control
{
    public static readonly StyledProperty<IEnumerable?> ItemsProperty =
        AvaloniaProperty.Register<ShareBar, IEnumerable?>(nameof(Items));

    static ShareBar() => AffectsRender<ShareBar>(ItemsProperty);

    public IEnumerable? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var items = Items;
        double w = Bounds.Width, h = Bounds.Height;
        if (items is null || w <= 0 || h <= 0) return;

        using (context.PushClip(new RoundedRect(new Rect(0, 0, w, h), 8)))
        {
            double x = 0;
            foreach (var item in items)
            {
                var (share, brush) = item switch
                {
                    TimeShareVm t => (t.Share, EmotionColors.Brush(t.Key)),
                    TalkShareVm t => (t.Share, SpeakerColors.Brush(t.SpeakerIndex)),
                    _ => (0.0, null),
                };
                if (brush is null) continue;
                context.FillRectangle(brush, new Rect(x, 0, share * w, h));
                x += share * w;
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

/// <summary>Konuşmacı → renk (sıraya göre döner); bilinmeyen gri. <paramref name="soft"/>: balon zemini için açık ton.</summary>
public static class SpeakerColors
{
    private static readonly string[] Palette = { "#7C5CFF", "#F08A5D", "#2FA3B5", "#D9A21B", "#C46BD6" };

    public static IBrush Brush(int speakerIndex, bool soft = false)
    {
        var color = Color.Parse(speakerIndex < 0 ? "#A39DBD" : Palette[speakerIndex % Palette.Length]);
        return new SolidColorBrush(color, soft ? 0.14 : 1.0);
    }
}
