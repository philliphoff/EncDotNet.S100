using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Breaks the slider's track at each collapsed gap with a skewed block
/// (#708, handoff C2), so a collapsed stretch never reads as continuous time.
/// Drawn over the slider; not hit-testable.
/// </summary>
internal sealed class GapBreakControl : Control
{
    /// <summary>The collapsed gaps.</summary>
    public static readonly StyledProperty<IReadOnlyList<NormalizedGap>?> GapsProperty =
        AvaloniaProperty.Register<GapBreakControl, IReadOnlyList<NormalizedGap>?>(nameof(Gaps));

    /// <summary>The block's fill: the panel's background.</summary>
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<GapBreakControl, IBrush?>(nameof(Fill));

    /// <summary>The block's slanted edges.</summary>
    public static readonly StyledProperty<IBrush?> EdgeProperty =
        AvaloniaProperty.Register<GapBreakControl, IBrush?>(nameof(Edge));

    static GapBreakControl()
    {
        AffectsRender<GapBreakControl>(GapsProperty, FillProperty, EdgeProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<GapBreakControl>(false);
    }

    /// <inheritdoc cref="GapsProperty"/>
    public IReadOnlyList<NormalizedGap>? Gaps
    {
        get => GetValue(GapsProperty);
        set => SetValue(GapsProperty, value);
    }

    /// <inheritdoc cref="FillProperty"/>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <inheritdoc cref="EdgeProperty"/>
    public IBrush? Edge
    {
        get => GetValue(EdgeProperty);
        set => SetValue(EdgeProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Gaps is not { Count: > 0 } gaps || Fill is not { } fill)
            return;
        var width = Bounds.Width;
        var height = Bounds.Height;
        var mid = height / 2;
        const double half = 5;
        const double skew = 3;
        var edge = Edge is { } brush ? new Pen(brush, 1.5) : null;
        foreach (var gap in gaps)
        {
            // A short break centred in the gap, not the whole gap.
            var centre = (gap.Start + gap.Width / 2) * width;
            var left = centre - 4;
            var right = centre + 4;
            var block = new StreamGeometry();
            using (var g = block.Open())
            {
                g.BeginFigure(new Point(left + skew, mid - half), true);
                g.LineTo(new Point(right + skew, mid - half));
                g.LineTo(new Point(right - skew, mid + half));
                g.LineTo(new Point(left - skew, mid + half));
                g.EndFigure(true);
            }
            context.DrawGeometry(fill, null, block);
            if (edge is not null)
            {
                context.DrawLine(edge, new Point(left + skew, mid - half), new Point(left - skew, mid + half));
                context.DrawLine(edge, new Point(right + skew, mid - half), new Point(right - skew, mid + half));
            }
        }
    }
}

/// <summary>
/// The labels under the Timeline's axis (#708, handoff C3): gap labels in
/// italic muted text, days and 6-hour marks in the foreground.
/// </summary>
internal sealed class AxisLabelsControl : Control
{
    /// <summary>The labels to draw.</summary>
    public static readonly StyledProperty<IReadOnlyList<AxisLabel>?> LabelsProperty =
        AvaloniaProperty.Register<AxisLabelsControl, IReadOnlyList<AxisLabel>?>(nameof(Labels));

    /// <summary>The text brush for days and hours.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<AxisLabelsControl, IBrush?>(nameof(Foreground));

    /// <summary>The text brush for gap labels.</summary>
    public static readonly StyledProperty<IBrush?> MutedProperty =
        AvaloniaProperty.Register<AxisLabelsControl, IBrush?>(nameof(Muted));

    static AxisLabelsControl()
    {
        AffectsRender<AxisLabelsControl>(LabelsProperty, ForegroundProperty, MutedProperty);
    }

    /// <inheritdoc cref="LabelsProperty"/>
    public IReadOnlyList<AxisLabel>? Labels
    {
        get => GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    /// <inheritdoc cref="ForegroundProperty"/>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <inheritdoc cref="MutedProperty"/>
    public IBrush? Muted
    {
        get => GetValue(MutedProperty);
        set => SetValue(MutedProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Labels is not { Count: > 0 } labels)
            return;
        var width = Bounds.Width;

        // Labels are laid out by priority (gaps, then days, then hours); one
        // that would touch a label already placed is dropped, so text never
        // overlaps whatever the panel's width.
        var placed = new List<(double Left, double Right)>();
        foreach (var label in labels.OrderBy(l => l.Kind))
        {
            var isGap = label.Kind == AxisLabelKind.Gap;
            var text = new FormattedText(
                label.Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, isGap ? FontStyle.Italic : FontStyle.Normal, label.Kind == AxisLabelKind.Day ? FontWeight.SemiBold : FontWeight.Normal),
                10.5,
                (isGap ? Muted : Foreground) ?? Brushes.Gray);
            var x = Math.Clamp(label.Position * width - text.Width / 2, 0, Math.Max(0, width - text.Width));
            const double padding = 8;
            if (placed.Any(p => x < p.Right + padding && x + text.Width > p.Left - padding))
                continue;
            placed.Add((x, x + text.Width));
            context.DrawText(text, new Point(x, 0));
        }
    }
}

/// <summary>
/// The Timeline's overview strip (#708, handoff C5): every loaded window in
/// grey, and a dark bracket marking the part the axis shows.
/// </summary>
internal sealed class OverviewStripControl : Control
{
    /// <summary>The loaded windows on the overview.</summary>
    public static readonly StyledProperty<IReadOnlyList<NormalizedCoverageBand>?> BandsProperty =
        AvaloniaProperty.Register<OverviewStripControl, IReadOnlyList<NormalizedCoverageBand>?>(nameof(Bands));

    /// <summary>The visible window's start (0–1).</summary>
    public static readonly StyledProperty<double> WindowStartProperty =
        AvaloniaProperty.Register<OverviewStripControl, double>(nameof(WindowStart));

    /// <summary>The visible window's width (0–1).</summary>
    public static readonly StyledProperty<double> WindowWidthProperty =
        AvaloniaProperty.Register<OverviewStripControl, double>(nameof(WindowWidth), 1d);

    /// <summary>The loaded windows' brush.</summary>
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<OverviewStripControl, IBrush?>(nameof(Fill));

    /// <summary>The bracket's brush.</summary>
    public static readonly StyledProperty<IBrush?> BracketProperty =
        AvaloniaProperty.Register<OverviewStripControl, IBrush?>(nameof(Bracket));

    static OverviewStripControl()
    {
        AffectsRender<OverviewStripControl>(BandsProperty, WindowStartProperty, WindowWidthProperty, FillProperty, BracketProperty);
    }

    /// <inheritdoc cref="BandsProperty"/>
    public IReadOnlyList<NormalizedCoverageBand>? Bands
    {
        get => GetValue(BandsProperty);
        set => SetValue(BandsProperty, value);
    }

    /// <inheritdoc cref="WindowStartProperty"/>
    public double WindowStart
    {
        get => GetValue(WindowStartProperty);
        set => SetValue(WindowStartProperty, value);
    }

    /// <inheritdoc cref="WindowWidthProperty"/>
    public double WindowWidth
    {
        get => GetValue(WindowWidthProperty);
        set => SetValue(WindowWidthProperty, value);
    }

    /// <inheritdoc cref="FillProperty"/>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <inheritdoc cref="BracketProperty"/>
    public IBrush? Bracket
    {
        get => GetValue(BracketProperty);
        set => SetValue(BracketProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return;
        if (Fill is { } fill && Bands is { } bands)
        {
            foreach (var band in bands)
                context.DrawRectangle(fill, null, new Rect(band.Start * width, 1, Math.Max(band.Width * width, 1), height - 2), 1.5, 1.5);
        }
        if (Bracket is { } bracket)
        {
            var rect = new Rect(WindowStart * width, 0, Math.Max(WindowWidth * width, 2), height).Deflate(0.5);
            context.DrawRectangle(null, new Pen(bracket, 1), rect, 2, 2);
        }
    }
}
