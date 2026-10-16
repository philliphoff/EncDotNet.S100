using EncDotNet.S100.Renderers.Skia.Scene;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia.SkiaStyles;
using Mapsui.Styles;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// The Mapsui style for an S-104 water-level station gauge; drawn by
/// <see cref="WaterLevelGaugeRenderer"/>.
/// </summary>
public sealed class WaterLevelGaugeStyle : BaseStyle
{
    /// <summary>The gauge to draw.</summary>
    public required SkiaWaterLevelGauge Gauge { get; init; }
}

/// <summary>
/// Draws <see cref="WaterLevelGaugeStyle"/> point features with
/// <see cref="WaterLevelGaugePainter"/>, upright at any map rotation, with the
/// value label shown only at its scale.
/// </summary>
public sealed class WaterLevelGaugeRenderer : ISkiaStyleRenderer
{
    /// <summary>Singleton instance for registration.</summary>
    public static WaterLevelGaugeRenderer Instance { get; } = new();

    internal static void Register() =>
        global::Mapsui.Rendering.Skia.MapRenderer.RegisterStyleRenderer(typeof(WaterLevelGaugeStyle), Instance);

    /// <inheritdoc/>
    public bool Draw(SKCanvas canvas, Viewport viewport, ILayer layer, IFeature feature, IStyle style, RenderService renderService, long iteration)
    {
        if (style is not WaterLevelGaugeStyle { Gauge: var gauge })
            return false;

        var position = feature.Extent?.Centroid;
        if (position is null)
            return false;

        var (x, y) = viewport.WorldToScreenXY(position.X, position.Y);
        var (_, latitude) = SphericalMercator.ToLonLat(position.X, position.Y);
        double scaleDenominator = viewport.Resolution * Math.Cos(latitude * Math.PI / 180.0)
            / MapsuiDisplayListRenderer.DenomToResolutionMetres;

        float opacity = (float)Math.Clamp(layer.Opacity * style.Opacity, 0, 1);
        using var layerPaint = opacity < 1f
            ? new SKPaint { Color = SKColors.Black.WithAlpha((byte)Math.Round(opacity * 255)) }
            : null;
        int save = layerPaint is not null ? canvas.SaveLayer(layerPaint) : canvas.Save();
        try
        {
            WaterLevelGaugePainter.Draw(
                canvas,
                (float)x,
                (float)y,
                gauge.Mark,
                scaleDenominator <= gauge.LabelMaxScaleDenominator ? gauge.Label : null,
                gauge.Ink.ToSkia(),
                gauge.Fill.ToSkia(),
                gauge.Halo.ToSkia());
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
        return true;
    }
}

/// <summary>
/// The Mapsui style carrying an <see cref="IsolineScene"/>; drawn by
/// <see cref="IsolineRenderer"/>. Attach it to one feature covering the scene's
/// extent so Mapsui fetches it whenever the extent is in view.
/// </summary>
public sealed class IsolineStyle : BaseStyle
{
    /// <summary>The isolines to draw.</summary>
    public required IsolineScene Scene { get; init; }
}

/// <summary>
/// Draws an <see cref="IsolineStyle"/> in screen space each frame, so dashes and
/// label spacing stay the same at every zoom.
/// </summary>
public sealed class IsolineRenderer : ISkiaStyleRenderer
{
    /// <summary>Singleton instance for registration.</summary>
    public static IsolineRenderer Instance { get; } = new();

    internal static void Register() =>
        global::Mapsui.Rendering.Skia.MapRenderer.RegisterStyleRenderer(typeof(IsolineStyle), Instance);

    /// <inheritdoc/>
    public bool Draw(SKCanvas canvas, Viewport viewport, ILayer layer, IFeature feature, IStyle style, RenderService renderService, long iteration)
    {
        if (style is not IsolineStyle { Scene: var scene })
            return false;

        scene.Draw(
            canvas,
            (x, y) =>
            {
                var (sx, sy) = viewport.WorldToScreenXY(x, y);
                return new SKPoint((float)sx, (float)sy);
            },
            (float)Math.Clamp(layer.Opacity * style.Opacity, 0, 1));
        return true;
    }
}
