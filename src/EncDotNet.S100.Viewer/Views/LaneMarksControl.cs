using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Draws a Timeline lane's marks over its band (design note
/// <c>vector-weather.md</c> V14): a tall tick at each step (a storm's
/// forecast points), a solid diamond at the issue time and a hollow diamond
/// at the next issue expected.
/// </summary>
internal sealed class LaneMarksControl : Control
{
    /// <summary>The marks, positioned as fractions of the width.</summary>
    public static readonly StyledProperty<IReadOnlyList<TimelineLaneMark>?> MarksProperty =
        AvaloniaProperty.Register<LaneMarksControl, IReadOnlyList<TimelineLaneMark>?>(nameof(Marks));

    /// <summary>The brush the marks are drawn in.</summary>
    public static readonly StyledProperty<IBrush?> MarkBrushProperty =
        AvaloniaProperty.Register<LaneMarksControl, IBrush?>(nameof(MarkBrush));

    /// <summary>The diamonds' width and height, in pixels.</summary>
    public static readonly StyledProperty<double> DiamondSizeProperty =
        AvaloniaProperty.Register<LaneMarksControl, double>(nameof(DiamondSize), 8d);

    static LaneMarksControl()
    {
        AffectsRender<LaneMarksControl>(MarksProperty, MarkBrushProperty, DiamondSizeProperty);
    }

    /// <summary>The marks, positioned as fractions of the width.</summary>
    public IReadOnlyList<TimelineLaneMark>? Marks
    {
        get => GetValue(MarksProperty);
        set => SetValue(MarksProperty, value);
    }

    /// <summary>The brush the marks are drawn in.</summary>
    public IBrush? MarkBrush
    {
        get => GetValue(MarkBrushProperty);
        set => SetValue(MarkBrushProperty, value);
    }

    /// <summary>The diamonds' width and height, in pixels.</summary>
    public double DiamondSize
    {
        get => GetValue(DiamondSizeProperty);
        set => SetValue(DiamondSizeProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0 || Marks is not { Count: > 0 } marks || MarkBrush is not { } brush)
            return;

        // Ticks span the band; diamonds sit on its lower edge, as in the mock-ups.
        var half = Math.Min(DiamondSize, height) / 2;
        var tickTop = 1d;
        var tickBottom = height - half - 1;
        var diamondY = height - half;
        var outline = new Pen(brush, 1.5);
        foreach (var mark in marks)
        {
            var x = Math.Clamp(mark.Position, 0, 1) * width;
            x = Math.Clamp(x, half, Math.Max(width - half, half));
            switch (mark.Kind)
            {
                case TimelineLaneMarkKind.Step:
                    context.DrawRectangle(brush, null, new Rect(x - 0.75, tickTop, 1.5, Math.Max(tickBottom - tickTop, 1)));
                    break;
                case TimelineLaneMarkKind.Issued:
                    context.DrawGeometry(brush, null, Diamond(x, diamondY, half));
                    break;
                case TimelineLaneMarkKind.NextIssue:
                    context.DrawGeometry(null, outline, Diamond(x, diamondY, half - 0.75));
                    break;
            }
        }
    }

    private static StreamGeometry Diamond(double x, double y, double half)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(x, y - half), isFilled: true);
            g.LineTo(new Point(x + half, y));
            g.LineTo(new Point(x, y + half));
            g.LineTo(new Point(x - half, y));
            g.EndFigure(isClosed: true);
        }

        return geometry;
    }
}
