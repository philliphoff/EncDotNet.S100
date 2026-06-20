using System;
using System.Collections.Generic;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Identifies one base-plane tile in the <see cref="TileGrid"/>: a
/// power-of-two resolution <paramref name="Band"/> and the tile's
/// <paramref name="X"/>/<paramref name="Y"/> index within the origin-anchored
/// EPSG:3857 grid for that band (XYZ convention: <c>X</c> increases east,
/// <c>Y</c> increases south). See
/// <c>docs/design/S100-Render-Subsystem-Design.md</c> §3.2.
/// </summary>
internal readonly record struct TileKey(int Band, int X, int Y);

/// <summary>
/// A screen-space rectangle in device-independent pixels (DIP), corners
/// measured from the viewport's top-left. Kept Skia-free so the tile math is
/// unit-testable without a graphics surface.
/// </summary>
internal readonly record struct ScreenRect(double Left, double Top, double Right, double Bottom)
{
    /// <summary>Width in DIP (may be negative for an inverted rect).</summary>
    public double Width => Right - Left;

    /// <summary>Height in DIP.</summary>
    public double Height => Bottom - Top;

    /// <summary>True when this rect overlaps the half-open viewport box.</summary>
    public bool IntersectsViewport(double widthDip, double heightDip) =>
        Right > 0 && Bottom > 0 && Left < widthDip && Top < heightDip;
}

/// <summary>
/// Pure, origin-anchored EPSG:3857 tile-grid math for the tiled base plane
/// (S-100 render subsystem, Phase&#160;2). Uses the standard Web-Mercator
/// power-of-two pyramid (256-DIP tiles, the same scheme Mapsui's own tile
/// layers use), so a constant-zoom pan reuses every interior tile and only the
/// newly-exposed perimeter is rasterised. All methods are static and free of
/// SkiaSharp/Mapsui so they can be unit-tested directly.
/// </summary>
/// <remarks>
/// The grid is anchored to the world origin (not the viewport), which is what
/// makes tiles pan-stable: the same world position always falls in the same
/// tile at a given band, so a pan never re-keys interior tiles.
/// </remarks>
internal static class TileGrid
{
    /// <summary>Tile edge length in device-independent pixels.</summary>
    public const int TileSizeDip = 256;

    /// <summary>
    /// Half the EPSG:3857 projected world extent in metres
    /// (<c>π · 6378137</c>); the grid spans <c>[-Extent, +Extent]</c> on both
    /// axes.
    /// </summary>
    public const double Extent = Math.PI * WebMercator.EarthRadius;

    /// <summary>
    /// EPSG:3857 resolution (m/px) at band 0 for 256-px tiles:
    /// <c>2 · Extent / TileSizeDip</c> ≈ 156543.034.
    /// </summary>
    public const double Band0Resolution = 2.0 * Extent / TileSizeDip;

    /// <summary>Smallest (most zoomed-out) band the grid emits.</summary>
    public const int MinBand = 0;

    /// <summary>Largest (most zoomed-in) band the grid emits.</summary>
    public const int MaxBand = 24;

    /// <summary>The canonical EPSG:3857 resolution (m/px) for a band.</summary>
    public static double ResolutionForBand(int band) => Band0Resolution / Math.Pow(2.0, band);

    /// <summary>The world size, in metres, of one tile at a band.</summary>
    public static double TileWorldSize(int band) => 2.0 * Extent / Math.Pow(2.0, band);

    /// <summary>The number of tiles along one axis at a band (<c>2^band</c>).</summary>
    public static int TilesPerAxis(int band) => 1 << band;

    /// <summary>
    /// Selects the band whose canonical resolution is closest (in log-space, so
    /// the choice is symmetric across the octave) to <paramref name="resolution"/>,
    /// clamped to <see cref="MinBand"/>..<see cref="MaxBand"/>. A live viewport
    /// at an arbitrary resolution snaps to this band; the composite scales the
    /// band's tiles by <c>ResolutionForBand(band) / resolution</c> to fit.
    /// </summary>
    public static int BandForResolution(double resolution)
    {
        if (resolution <= 0 || double.IsNaN(resolution) || double.IsInfinity(resolution))
        {
            return MinBand;
        }

        var band = (int)Math.Round(Math.Log2(Band0Resolution / resolution));
        return Math.Clamp(band, MinBand, MaxBand);
    }

    /// <summary>
    /// The EPSG:3857 world bounds (metres) of a tile, gutter excluded.
    /// </summary>
    public static (double MinX, double MinY, double MaxX, double MaxY) TileWorldBounds(TileKey key)
    {
        var size = TileWorldSize(key.Band);
        var minX = -Extent + key.X * size;
        var maxY = Extent - key.Y * size;
        return (minX, maxY - size, minX + size, maxY);
    }

    /// <summary>
    /// Enumerates every tile at <paramref name="band"/> whose bounds intersect
    /// the north-up viewport centred at (<paramref name="centerX"/>,
    /// <paramref name="centerY"/>) in EPSG:3857, sized
    /// <paramref name="widthDip"/> × <paramref name="heightDip"/> DIP at
    /// <paramref name="resolution"/> m/px. Indices are clamped to the band's
    /// valid range, so a viewport overhanging the world edge yields no
    /// out-of-range keys.
    /// </summary>
    public static IReadOnlyList<TileKey> VisibleTiles(
        double centerX, double centerY, double widthDip, double heightDip, double resolution, int band)
    {
        var result = new List<TileKey>();
        if (widthDip <= 0 || heightDip <= 0 || resolution <= 0)
        {
            return result;
        }

        var halfW = widthDip * 0.5 * resolution;
        var halfH = heightDip * 0.5 * resolution;
        var minX = centerX - halfW;
        var maxX = centerX + halfW;
        var minY = centerY - halfH;
        var maxY = centerY + halfH;

        var size = TileWorldSize(band);
        var perAxis = TilesPerAxis(band);

        var xStart = (int)Math.Floor((minX + Extent) / size);
        var xEnd = (int)Math.Floor((maxX + Extent) / size);
        // Y is inverted (XYZ): the top row (Y=0) is the northernmost.
        var yStart = (int)Math.Floor((Extent - maxY) / size);
        var yEnd = (int)Math.Floor((Extent - minY) / size);

        xStart = Math.Clamp(xStart, 0, perAxis - 1);
        xEnd = Math.Clamp(xEnd, 0, perAxis - 1);
        yStart = Math.Clamp(yStart, 0, perAxis - 1);
        yEnd = Math.Clamp(yEnd, 0, perAxis - 1);

        for (var y = yStart; y <= yEnd; y++)
        {
            for (var x = xStart; x <= xEnd; x++)
            {
                result.Add(new TileKey(band, x, y));
            }
        }

        return result;
    }

    /// <summary>
    /// Projects EPSG:3857 world bounds to the north-up viewport's DIP screen
    /// rectangle (top-left origin, +Y down). Used both to place a tile's core
    /// and to place its guttered image.
    /// </summary>
    public static ScreenRect WorldToScreenRect(
        double worldMinX, double worldMinY, double worldMaxX, double worldMaxY,
        double centerX, double centerY, double widthDip, double heightDip, double resolution)
    {
        var halfW = widthDip * 0.5;
        var halfH = heightDip * 0.5;
        var left = halfW + (worldMinX - centerX) / resolution;
        var right = halfW + (worldMaxX - centerX) / resolution;
        var top = halfH - (worldMaxY - centerY) / resolution;
        var bottom = halfH - (worldMinY - centerY) / resolution;
        return new ScreenRect(left, top, right, bottom);
    }

    /// <summary>The DIP screen rect of a tile's core (gutter excluded).</summary>
    public static ScreenRect TileCoreScreenRect(
        TileKey key, double centerX, double centerY, double widthDip, double heightDip, double resolution)
    {
        var (minX, minY, maxX, maxY) = TileWorldBounds(key);
        return WorldToScreenRect(minX, minY, maxX, maxY, centerX, centerY, widthDip, heightDip, resolution);
    }
}
