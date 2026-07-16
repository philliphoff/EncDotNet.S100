using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Paints the timeline's data-coverage band: one filled rounded rectangle
/// per <see cref="NormalizedCoverageBand"/>, positioned by its
/// <see cref="NormalizedCoverageBand.Start"/>/<see cref="NormalizedCoverageBand.Width"/>
/// fractions across the control's width. Ranges without a band are
/// "no data". A short vertical <b>boundary tick</b> rises above each
/// segment's start and end edge, so contiguous data sections are clearly
/// distinguished from the compressed gaps between them. The control is
/// width-responsive (it re-renders on bounds changes) so it stays aligned
/// with the slider above it.
/// </summary>
internal sealed class CoverageBandControl : Control
{
    /// <summary>
    /// The normalized coverage bands to paint. Fractions in <c>[0,1]</c>
    /// of the control's width.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<NormalizedCoverageBand>?> BandsProperty =
        AvaloniaProperty.Register<CoverageBandControl, IReadOnlyList<NormalizedCoverageBand>?>(nameof(Bands));

    /// <summary>Brush used to fill covered ranges.</summary>
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<CoverageBandControl, IBrush?>(nameof(Fill));

    /// <summary>
    /// Brush used to fill the full-width track behind the covered ranges,
    /// representing "no data". Drawn first so gaps between coverage bands
    /// remain visible. When <c>null</c>, no track is drawn.
    /// </summary>
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<CoverageBandControl, IBrush?>(nameof(TrackBrush));

    /// <summary>Corner radius of each painted band, in device pixels.</summary>
    public static readonly StyledProperty<double> BandCornerRadiusProperty =
        AvaloniaProperty.Register<CoverageBandControl, double>(nameof(BandCornerRadius), 2d);

    /// <summary>
    /// Brush used for the boundary ticks at each segment's start/end. When
    /// <c>null</c>, <see cref="Fill"/> is used.
    /// </summary>
    public static readonly StyledProperty<IBrush?> TickBrushProperty =
        AvaloniaProperty.Register<CoverageBandControl, IBrush?>(nameof(TickBrush));

    /// <summary>Width of each boundary tick, in device pixels.</summary>
    public static readonly StyledProperty<double> TickThicknessProperty =
        AvaloniaProperty.Register<CoverageBandControl, double>(nameof(TickThickness), 1.5d);

    /// <summary>
    /// How far (in device pixels) the boundary ticks rise above the top of
    /// the band fill. The band fill is inset from the top by this amount so
    /// the ticks have room to show.
    /// </summary>
    public static readonly StyledProperty<double> TickRiseProperty =
        AvaloniaProperty.Register<CoverageBandControl, double>(nameof(TickRise), 4d);

    static CoverageBandControl()
    {
        AffectsRender<CoverageBandControl>(
            BandsProperty, FillProperty, TrackBrushProperty, BandCornerRadiusProperty,
            TickBrushProperty, TickThicknessProperty, TickRiseProperty);
    }

    public IReadOnlyList<NormalizedCoverageBand>? Bands
    {
        get => GetValue(BandsProperty);
        set => SetValue(BandsProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public double BandCornerRadius
    {
        get => GetValue(BandCornerRadiusProperty);
        set => SetValue(BandCornerRadiusProperty, value);
    }

    public IBrush? TickBrush
    {
        get => GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    public double TickThickness
    {
        get => GetValue(TickThicknessProperty);
        set => SetValue(TickThicknessProperty, value);
    }

    public double TickRise
    {
        get => GetValue(TickRiseProperty);
        set => SetValue(TickRiseProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        // Reserve room at the top for the boundary ticks to rise above the
        // band fill; the fill (and "no data" track) occupy the remainder.
        double rise = Math.Clamp(TickRise, 0d, height);
        double bandTop = rise;
        double bandHeight = Math.Max(height - rise, 1d);
        double radius = Math.Min(BandCornerRadius, bandHeight / 2);

        // "No data" track behind the coverage bands.
        if (TrackBrush is { } track)
            context.DrawRectangle(track, null, new Rect(0, bandTop, width, bandHeight), radius, radius);

        var bands = Bands;
        if (bands is null || bands.Count == 0) return;
        if (Fill is not { } brush) return;

        var tickBrush = TickBrush ?? brush;
        double thickness = Math.Max(TickThickness, 0.5);

        foreach (var band in bands)
        {
            double start = Math.Clamp(band.Start, 0d, 1d);
            double w = Math.Clamp(band.Width, 0d, 1d - start);
            if (w <= 0) continue;

            double x = start * width;
            double pixelWidth = Math.Max(w * width, 1d);
            context.DrawRectangle(brush, null, new Rect(x, bandTop, pixelWidth, bandHeight), radius, radius);

            // Boundary ticks at the segment's start and end, rising the full
            // control height so they extend above the band fill. Clamped so
            // ticks at the extremes stay fully visible.
            DrawTick(context, tickBrush, x, thickness, width, height);
            DrawTick(context, tickBrush, x + pixelWidth, thickness, width, height);
        }
    }

    private static void DrawTick(DrawingContext context, IBrush brush, double centerX, double thickness, double width, double height)
    {
        double left = Math.Clamp(centerX - thickness / 2, 0d, Math.Max(width - thickness, 0d));
        context.DrawRectangle(brush, null, new Rect(left, 0, thickness, height));
    }
}
