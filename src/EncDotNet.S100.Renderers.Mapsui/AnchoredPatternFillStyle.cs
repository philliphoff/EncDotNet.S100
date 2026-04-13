using Mapsui.Styles;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A custom Mapsui style that fills a polygon with a tiled pattern bitmap
/// anchored to the geometry's bounding box, so the pattern moves with the
/// polygon during panning.
/// </summary>
public sealed class AnchoredPatternFillStyle : BaseStyle
{
    /// <summary>The pre-rasterized pattern tile as PNG bytes.</summary>
    public required byte[] TilePng { get; init; }

    /// <summary>Optional outline pen for the polygon border.</summary>
    public Color OutlineColor { get; init; } = new Color(0, 0, 0, 40);

    /// <summary>Outline width in pixels.</summary>
    public double OutlineWidth { get; init; } = 0.5;
}
