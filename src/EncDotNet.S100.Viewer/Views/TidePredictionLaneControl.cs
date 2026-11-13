using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Paints the Timeline's Tide predictions lane (tide predictions design Q6,
/// D14): a dashed band across the whole track with open ends (‹ ›), the
/// predicted curve dashed inside it, and a tick with "H 3.4" or "L −0.2" at
/// each high and low. Dashed means predicted (D7).
/// </summary>
internal sealed class TidePredictionLaneControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<Point>?> CurveProperty =
        AvaloniaProperty.Register<TidePredictionLaneControl, IReadOnlyList<Point>?>(nameof(Curve));

    public static readonly StyledProperty<IReadOnlyList<TideLaneMark>?> MarksProperty =
        AvaloniaProperty.Register<TidePredictionLaneControl, IReadOnlyList<TideLaneMark>?>(nameof(Marks));

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<TidePredictionLaneControl, IBrush?>(nameof(Accent));

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<TidePredictionLaneControl, IBrush?>(nameof(TextBrush));

    private const double BandHeight = 22;

    static TidePredictionLaneControl()
    {
        AffectsRender<TidePredictionLaneControl>(CurveProperty, MarksProperty, AccentProperty, TextBrushProperty);
    }

    public IReadOnlyList<Point>? Curve
    {
        get => GetValue(CurveProperty);
        set => SetValue(CurveProperty, value);
    }

    public IReadOnlyList<TideLaneMark>? Marks
    {
        get => GetValue(MarksProperty);
        set => SetValue(MarksProperty, value);
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, BandHeight + 4);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        var accent = Accent ?? Brushes.SteelBlue;
        var color = accent is ISolidColorBrush solid ? solid.Color : Colors.SteelBlue;
        var top = (height - BandHeight) / 2;
        var band = new Rect(0, top, width, BandHeight);
        var fill = new SolidColorBrush(color, 0.08);
        var edge = new Pen(new SolidColorBrush(color, 0.6), 1, new DashStyle([3, 2], 0));
        context.DrawRectangle(fill, null, band, 3, 3);
        context.DrawLine(edge, band.TopLeft, band.TopRight);
        context.DrawLine(edge, band.BottomLeft, band.BottomRight);

        // Open ends: predictions have no start or end.
        var text = TextBrush ?? Brushes.Gray;
        DrawText(context, "‹", new Point(2, top + 3), text, 12);
        DrawText(context, "›", new Point(width - 8, top + 3), text, 12);

        if (Curve is { Count: > 1 } curve)
        {
            var inner = new Rect(10, top + 3, Math.Max(0, width - 20), BandHeight - 6);
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(Map(curve[0], inner), false);
                for (var i = 1; i < curve.Count; i++)
                    g.LineTo(Map(curve[i], inner));
                g.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(accent, 1.5, new DashStyle([4, 2.5], 0)), geometry);

            // Ticks for every high and low; a label only where it doesn't run into the last one.
            double highRight = double.NegativeInfinity, lowRight = double.NegativeInfinity;
            foreach (var mark in Marks ?? [])
            {
                if (mark.Position < 0 || mark.Position > 1)
                    continue;
                var x = inner.X + (mark.Position * inner.Width);
                context.DrawLine(new Pen(accent, 1), new Point(x, top + (mark.IsHigh ? 0 : BandHeight - 4)), new Point(x, top + (mark.IsHigh ? 4 : BandHeight)));
                var label = new FormattedText(mark.Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 9, text);
                ref var right = ref mark.IsHigh ? ref highRight : ref lowRight;
                if (x + 2 < right + 4 || x + 2 + label.Width > width - 10)
                    continue;
                context.DrawText(label, new Point(x + 2, mark.IsHigh ? top + 1 : top + BandHeight - 12));
                right = x + 2 + label.Width;
            }
        }
    }

    private static Point Map(Point p, Rect r) => new(r.X + (p.X * r.Width), r.Bottom - (p.Y * r.Height));

    private static void DrawText(DrawingContext context, string s, Point at, IBrush brush, double size)
    {
        var formatted = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);
        context.DrawText(formatted, at);
    }
}
