using Mapsui.Styles;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A custom Mapsui style that fills a polygon with a tiled pattern anchored to
/// a fixed world origin, so the pattern moves with the polygon during panning.
/// The tile is a resolution-independent <see cref="SKPicture"/> (recorded in
/// millimetre units) rather than a fixed-resolution bitmap, so the pattern stays
/// crisp at any zoom level and on HiDPI/Retina surfaces.
/// </summary>
public sealed class AnchoredPatternFillStyle : BaseStyle
{
    /// <summary>The pattern tile as a vector picture, recorded in millimetre units.</summary>
    public required SKPicture Tile { get; init; }

    /// <summary>The tile's repeat rectangle, in millimetres (origin at 0,0).</summary>
    public required SKRect TileRect { get; init; }

    /// <summary>Optional outline pen for the polygon border.</summary>
    public Color OutlineColor { get; init; } = new Color(0, 0, 0, 40);

    /// <summary>Outline width in pixels.</summary>
    public double OutlineWidth { get; init; } = 0.5;
}
