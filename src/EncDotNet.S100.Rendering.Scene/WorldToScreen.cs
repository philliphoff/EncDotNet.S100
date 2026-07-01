using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// A linear <b>EPSG:3857-world → screen-pixel</b> affine derived from a
/// <see cref="Viewport"/> — the second half of the S-100 Part 9 projection
/// (<c>EPSG:3857 → screen pixels</c>) that a <see cref="PaintOp"/> deliberately
/// leaves to each rendering backend (see the unit contract on
/// <see cref="PaintOp"/>).
/// </summary>
/// <remarks>
/// <para>The viewport's geographic bounds are projected to EPSG:3857 via
/// <see cref="WebMercator.FromLonLat(double, double)"/> and mapped linearly to
/// the pixel rectangle with origin top-left and screen-space <c>+Y</c> pointing
/// <i>down</i> (northing decreases downward). The transform is north-up only —
/// it applies no rotation.</para>
/// <para>This helper is intentionally framework-neutral (it depends only on
/// <see cref="Viewport"/> and <see cref="WebMercator"/>), so a rendering
/// backend that does not use SkiaSharp or Mapsui can project the world
/// coordinates carried on each <see cref="PaintOp"/> to output pixels without
/// taking a dependency on either backend. It provides only the geometry
/// (world → pixel) projection; <i>size</i> values on the IR are already in
/// display pixels and must <b>not</b> be passed through this transform.</para>
/// </remarks>
public readonly struct WorldToScreen
{
    private readonly double _minX;
    private readonly double _maxY;
    private readonly double _scaleX;
    private readonly double _scaleY;

    private WorldToScreen(double minX, double maxY, double scaleX, double scaleY)
    {
        _minX = minX;
        _maxY = maxY;
        _scaleX = scaleX;
        _scaleY = scaleY;
    }

    /// <summary>
    /// Builds the world → screen affine for the supplied <paramref name="viewport"/>.
    /// The viewport's geographic corners are projected to EPSG:3857 and mapped to
    /// the <c>[0, WidthPixels] × [0, HeightPixels]</c> pixel rectangle. A viewport
    /// with a zero-width or zero-height projected span yields a zero scale on that
    /// axis (all coordinates collapse to the axis origin).
    /// </summary>
    /// <param name="viewport">The display viewport (geographic bounds + pixel size).</param>
    /// <returns>The world → screen affine.</returns>
    public static WorldToScreen Create(Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        var (minX, minY) = WebMercator.FromLonLat(viewport.MinLongitude, viewport.MinLatitude);
        var (maxX, maxY) = WebMercator.FromLonLat(viewport.MaxLongitude, viewport.MaxLatitude);

        double spanX = maxX - minX;
        double spanY = maxY - minY;
        double scaleX = spanX != 0 ? viewport.WidthPixels / spanX : 0;
        double scaleY = spanY != 0 ? viewport.HeightPixels / spanY : 0;
        return new WorldToScreen(minX, maxY, scaleX, scaleY);
    }

    /// <summary>
    /// Projects an EPSG:3857 world coordinate to a screen pixel (origin top-left,
    /// <c>+Y</c> down).
    /// </summary>
    /// <param name="world">The world coordinate in EPSG:3857 metres.</param>
    /// <returns>The screen pixel coordinate.</returns>
    public (float X, float Y) Project((double X, double Y) world)
    {
        float sx = (float)((world.X - _minX) * _scaleX);
        float sy = (float)((_maxY - world.Y) * _scaleY);
        return (sx, sy);
    }
}
