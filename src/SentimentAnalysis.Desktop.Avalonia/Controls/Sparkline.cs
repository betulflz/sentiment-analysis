using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SentimentAnalysis.Desktop.Avalonia.Controls;

/// <summary>0..1 değer dizisini çizgi + altı yarı saydam dolgu olarak çizer. Dikey ölçek SABİT 0..1'dir —
/// düz bir seri tüm yüksekliği kaplamaz, satırlar birbirleriyle kıyaslanabilir kalır.</summary>
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke), Brushes.Gray);

    static Sparkline() => AffectsRender<Sparkline>(ValuesProperty, StrokeProperty);

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var values = Values;
        double w = Bounds.Width, h = Bounds.Height;
        if (values is null || values.Count == 0 || w <= 0 || h <= 0) return;

        const double pad = 2;
        double Y(double v) => pad + (1 - System.Math.Clamp(v, 0, 1)) * (h - 2 * pad);
        double X(int i) => values.Count == 1 ? w / 2 : i * w / (values.Count - 1);

        // Tek pencere: çizgi çizilemez, değeri yatay bir çizgiyle göster.
        var points = values.Count == 1
            ? new List<Point> { new(0, Y(values[0])), new(w, Y(values[0])) }
            : new List<Point>(values.Count);
        if (values.Count > 1)
            for (int i = 0; i < values.Count; i++) points.Add(new Point(X(i), Y(values[i])));

        var stroke = Stroke ?? Brushes.Gray;
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(points[0], false);
            for (int i = 1; i < points.Count; i++) g.LineTo(points[i]);
            g.EndFigure(false);
        }
        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(points[0].X, h), true);
            foreach (var p in points) g.LineTo(p);
            g.LineTo(new Point(points[^1].X, h));
            g.EndFigure(true);
        }

        var fill = stroke is ISolidColorBrush s ? new SolidColorBrush(s.Color, 0.15) : null;
        context.DrawGeometry(fill, null, area);
        context.DrawGeometry(null, new Pen(stroke, 2.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
    }
}
