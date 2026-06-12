using System;
using System.Collections.Generic;
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
/// "no data". The control is width-responsive (it re-renders on bounds
/// changes) so it stays aligned with the slider above it.
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

    /// <summary>Corner radius of each painted band, in device pixels.</summary>
    public static readonly StyledProperty<double> BandCornerRadiusProperty =
        AvaloniaProperty.Register<CoverageBandControl, double>(nameof(BandCornerRadius), 2d);

    static CoverageBandControl()
    {
        AffectsRender<CoverageBandControl>(BandsProperty, FillProperty, BandCornerRadiusProperty);
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

    public double BandCornerRadius
    {
        get => GetValue(BandCornerRadiusProperty);
        set => SetValue(BandCornerRadiusProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bands = Bands;
        if (bands is null || bands.Count == 0) return;
        if (Fill is not { } brush) return;

        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        double radius = Math.Min(BandCornerRadius, height / 2);

        foreach (var band in bands)
        {
            double start = Math.Clamp(band.Start, 0d, 1d);
            double w = Math.Clamp(band.Width, 0d, 1d - start);
            if (w <= 0) continue;

            double x = start * width;
            double pixelWidth = Math.Max(w * width, 1d);
            var rect = new Rect(x, 0, pixelWidth, height);
            context.DrawRectangle(brush, null, rect, radius, radius);
        }
    }
}
