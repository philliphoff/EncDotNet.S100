using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using NetTopologySuite.Geometries;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Screen-space clip regions for cross-cell scale-band overlap suppression
/// ("larger-scale-in", issue #438 Phase 2). Each entry maps a base-chart
/// <see cref="ILayer"/> to the EPSG:3857 (Web Mercator) region it is still
/// allowed to paint — its data coverage minus the union of finer, overlapping
/// in-band cells' coverage. The custom layer renderers
/// (<see cref="S100VectorTileRenderer"/>, <see cref="S100VectorSnapshotRenderer"/>)
/// look the region up per frame, project it to the live viewport, and wrap
/// their drawing in an <see cref="SKCanvas.ClipPath(SKPath)"/> so a coarser
/// cell no longer over-draws where a finer cell provides coverage.
/// </summary>
/// <remarks>
/// The clip is applied purely in screen space, so it never invalidates a
/// cached tile (the tile bitmaps are unchanged — only the composite clip region
/// varies per frame) and produces a sharp coverage-edge cut consistent with the
/// S-52 coverage boundary. Regions are stored in world coordinates so a
/// constant-zoom pan needs no recomputation. Attach with <see cref="Set"/>;
/// clearing (a <see langword="null"/> region) removes the clip so the cell paints
/// in full again.
/// </remarks>
internal static class CoverageClip
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ILayer, Geometry> s_regions = new();

    /// <summary>
    /// Attaches the EPSG:3857 clip <paramref name="region"/> for
    /// <paramref name="layer"/>. Passing <see langword="null"/> removes any
    /// attachment so the layer paints unclipped; passing an <em>empty</em>
    /// geometry attaches a clip-to-nothing region so a fully-covered coarser
    /// cell paints nothing (distinct from "no clip").
    /// </summary>
    public static void Set(ILayer layer, Geometry? region)
    {
        ArgumentNullException.ThrowIfNull(layer);

        s_regions.Remove(layer);
        if (region is not null)
            s_regions.AddOrUpdate(layer, region);
    }

    /// <summary>
    /// Gets the EPSG:3857 clip region attached to <paramref name="layer"/>, or
    /// <see langword="null"/> when the layer paints unclipped.
    /// </summary>
    public static Geometry? Get(ILayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return s_regions.TryGetValue(layer, out var region) ? region : null;
    }

    /// <summary>
    /// Builds a screen-space (DIP) <see cref="SKPath"/> for the clip region of
    /// <paramref name="layer"/> under <paramref name="viewport"/>, or
    /// <see langword="null"/> when the layer has no attached region. The path
    /// uses the even-odd fill rule so interior rings punch holes — a coarser
    /// cell still shows through a finer cell's no-coverage gaps.
    /// </summary>
    public static SKPath? BuildScreenPath(ILayer layer, Viewport viewport)
    {
        var region = Get(layer);
        if (region is null)
            return null;

        // An empty region means the cell is fully covered by finer cells: return
        // an empty path so ClipPath draws nothing (rather than "no clip").
        var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        if (region.IsEmpty)
            return path;

        AddGeometry(path, region, viewport);
        return path;
    }

    private static void AddGeometry(SKPath path, Geometry geometry, Viewport viewport)
    {
        switch (geometry)
        {
            case Polygon polygon:
                AddPolygon(path, polygon, viewport);
                break;
            case MultiPolygon multi:
                foreach (var g in multi.Geometries)
                    AddGeometry(path, g, viewport);
                break;
            case GeometryCollection collection:
                foreach (var g in collection.Geometries)
                    AddGeometry(path, g, viewport);
                break;
        }
    }

    private static void AddPolygon(SKPath path, Polygon polygon, Viewport viewport)
    {
        AddRing(path, polygon.ExteriorRing, viewport);
        foreach (var hole in polygon.InteriorRings)
            AddRing(path, hole, viewport);
    }

    private static void AddRing(SKPath path, LineString ring, Viewport viewport)
    {
        var coordinates = ring.Coordinates;
        if (coordinates.Length < 3)
            return;

        var (x0, y0) = viewport.WorldToScreenXY(coordinates[0].X, coordinates[0].Y);
        path.MoveTo((float)x0, (float)y0);
        for (var i = 1; i < coordinates.Length; i++)
        {
            var (x, y) = viewport.WorldToScreenXY(coordinates[i].X, coordinates[i].Y);
            path.LineTo((float)x, (float)y);
        }

        path.Close();
    }
}
