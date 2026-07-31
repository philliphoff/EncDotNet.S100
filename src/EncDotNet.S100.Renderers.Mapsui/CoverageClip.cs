using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using NetTopologySuite.Geometries;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// One finer, overlapping cell's contribution to a coarser cell's cross-cell
/// scale-band overlap suppression ("larger-scale-in", issue #438 Phase 2): the
/// finer cell's EPSG:3857 (Web Mercator) data-coverage footprint and the live
/// viewport resolution beyond which the finer cell stops drawing its own content
/// (derived from the finer cell's scale denominator, matching the resolution the
/// renderer clamps its layers to — see
/// <see cref="OverlapSuppression.CollectFinerCoverages"/>).
/// </summary>
/// <remarks>
/// The cutoff makes suppression <em>zoom-aware</em>: a finer cell only clips the
/// coarser cell at resolutions where the finer cell is actually drawing. Once the
/// viewport zooms out past <see cref="CutoffResolution"/>, the finer cell's
/// content is hidden (its layers stop drawing out of scale band) and it must stop
/// suppressing, otherwise the coarser cell would be clipped to a blank hole with
/// nothing drawn in it.
/// </remarks>
internal readonly record struct FinerCoverage(Geometry Coverage, double CutoffResolution);

/// <summary>
/// Screen-space clip regions for cross-cell scale-band overlap suppression
/// ("larger-scale-in", issue #438 Phase 2). Each entry maps a base-chart
/// <see cref="ILayer"/> to the set of finer, overlapping in-band cells
/// (<see cref="FinerCoverage"/>) whose coverage it must not over-draw. The custom
/// layer renderers (<see cref="S100VectorTileRenderer"/>,
/// <see cref="S100VectorSnapshotRenderer"/>) look the set up per frame, project
/// the finer coverages that are still visible at the live resolution to the
/// viewport, and remove each from the drawable region with
/// <see cref="SKCanvas.ClipPath(SKPath, SKClipOperation, bool)"/> using
/// <see cref="SKClipOperation.Difference"/> — so a coarser cell no longer
/// over-draws where a finer, currently-visible cell provides coverage.
/// </summary>
/// <remarks>
/// The clip is applied purely in screen space, so it never invalidates a cached
/// tile (the tile bitmaps are unchanged — only the composite clip region varies
/// per frame) and produces a sharp coverage-edge cut consistent with the S-52
/// coverage boundary. Regions are stored in world coordinates so a constant-zoom
/// pan needs no recomputation, and each finer coverage carries its own zoom-out
/// cutoff so the subtraction relaxes automatically as finer cells drop out of
/// their scale band. Attach with <see cref="Set"/>; clearing (a
/// <see langword="null"/> or empty set) removes the clip so the cell paints in
/// full again.
/// </remarks>
internal static class CoverageClip
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ILayer, FinerCoverage[]> Regions = new();

    /// <summary>
    /// Attaches the set of finer overlapping coverages that clip
    /// <paramref name="layer"/>. Passing <see langword="null"/> or an empty set
    /// removes any attachment so the layer paints unclipped.
    /// </summary>
    public static void Set(ILayer layer, IReadOnlyList<FinerCoverage>? finerCoverages)
    {
        ArgumentNullException.ThrowIfNull(layer);

        Regions.Remove(layer);
        if (finerCoverages is { Count: > 0 })
            Regions.Add(layer, finerCoverages as FinerCoverage[] ?? [.. finerCoverages]);
    }

    /// <summary>
    /// Gets the finer overlapping coverages attached to <paramref name="layer"/>,
    /// or <see langword="null"/> when the layer paints unclipped.
    /// </summary>
    public static FinerCoverage[]? Get(ILayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return Regions.TryGetValue(layer, out var regions) ? regions : null;
    }

    /// <summary>
    /// Builds the screen-space (DIP) difference-clip <see cref="SKPath"/>s for the
    /// finer coverages attached to <paramref name="layer"/> that are still visible
    /// at <paramref name="resolution"/> under <paramref name="viewport"/>. The
    /// caller removes each returned path from the drawable region with
    /// <see cref="SKClipOperation.Difference"/>. Returns an empty list when the
    /// layer has no attachment or every finer cell has dropped out of its scale
    /// band (so the coarser cell paints in full). Each path uses the even-odd fill
    /// rule so a finer cell's interior no-coverage rings punch holes — the coarser
    /// cell still shows through a finer cell's gaps.
    /// </summary>
    public static IReadOnlyList<SKPath> BuildActiveDifferencePaths(ILayer layer, Viewport viewport, double resolution)
    {
        var regions = Get(layer);
        if (regions is null || regions.Length == 0)
            return [];

        List<SKPath>? paths = null;
        foreach (var region in regions)
        {
            // Skip a finer cell that has itself zoomed out of its scale band: its
            // content is hidden (its layers stop drawing past this resolution), so
            // it must not clip the coarser cell (which would leave a blank hole).
            if (resolution > region.CutoffResolution)
                continue;
            if (region.Coverage.IsEmpty)
                continue;

            var path = new SKPath { FillType = SKPathFillType.EvenOdd };
            AddGeometry(path, region.Coverage, viewport);
            if (path.PointCount == 0)
            {
                path.Dispose();
                continue;
            }

            (paths ??= []).Add(path);
        }

        return (IReadOnlyList<SKPath>?)paths ?? [];
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
