using EncDotNet.S100.DataModel;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// One AML cell as <see cref="AmlScaleBandVisibility"/> sees it: its product,
/// its scale band and its coverage.
/// </summary>
/// <param name="Product">
/// The product the cell belongs to, such as <c>CLB</c>. Bands only compete
/// within a product: a fine SBO cell never hides a coarse RAL cell.
/// </param>
/// <param name="ScaleBand">The cell's scale band (0–9), or <see langword="null"/> when it has none.</param>
/// <param name="Coverage">The cell's coverage polygons, WGS-84; empty when it has none.</param>
public sealed record AmlBandCell(string Product, int? ScaleBand, IReadOnlyList<CoverageArea> Coverage);

/// <summary>
/// The scales at which one AML cell shows while scale bands are followed: up
/// to its band's coarsest display scale, and down to the scale at which finer
/// cells of its product cover it.
/// </summary>
/// <param name="ZoomOutLimit">
/// The coarsest scale denominator it shows at (its band's
/// <see cref="AmlScaleBandRange.Minimum"/>), or <see langword="null"/> for no
/// limit (band 0, or no band).
/// </param>
/// <param name="CoveredAtOrBelow">
/// The scale denominator at or below which finer cells of the same product
/// cover it, so it gives way to them; <see langword="null"/> when they never do.
/// </param>
public readonly record struct AmlBandWindow(int? ZoomOutLimit, int? CoveredAtOrBelow)
{
    /// <summary>A window that shows at every scale.</summary>
    public static AmlBandWindow Always { get; } = new(null, null);

    /// <summary>Whether the cell shows at the scale 1:<paramref name="scaleDenominator"/>.</summary>
    /// <param name="scaleDenominator">The map scale denominator; a non-positive or NaN one shows everything.</param>
    public bool ShowsAt(double scaleDenominator)
    {
        if (double.IsNaN(scaleDenominator) || scaleDenominator <= 0)
            return true;
        if (ZoomOutLimit is { } limit && scaleDenominator > limit)
            return false;
        return CoveredAtOrBelow is not { } covered || scaleDenominator > covered;
    }
}

/// <summary>
/// Which AML cells show at a map scale when the scale bands drive visibility
/// (AML handoff decision 26), the way the S-57 cell display window does (#621).
/// </summary>
/// <remarks>
/// <para>
/// A cell shows from its band's finest scale out to the band's coarsest
/// display scale (<see cref="AmlScaleBands.DisplayRange"/>), and is hidden
/// when zoomed out past that. Zoomed in past its band it keeps showing, as an
/// S-57 cell does, unless a finer band covers it.
/// </para>
/// <para>
/// The finer band wins where bands overlap: a cell gives way to the finer
/// cells of its own product once they show and their coverage covers its
/// coverage. A cell only partly covered by finer cells stays, so zooming in
/// never blanks an area a finer cell doesn't reach; the renderer has no
/// per-product coverage clipping to cut the overlap away.
/// </para>
/// <para>
/// Band 0 (unscaled) and cells without a band show at every scale and never
/// give way.
/// </para>
/// </remarks>
public static class AmlScaleBandVisibility
{
    // Share of a coarser cell's coverage finer cells must cover for it to give
    // way: allows for coverage edges digitised a little differently.
    private const double CoveredShare = 0.99;

    private static readonly GeometryFactory Factory = new();

    /// <summary>The window of each of <paramref name="cells"/>, in order.</summary>
    /// <param name="cells">The AML cells shown on the map (hidden ones never cover others).</param>
    /// <returns>One window per cell.</returns>
    public static IReadOnlyList<AmlBandWindow> Resolve(IReadOnlyList<AmlBandCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var footprints = cells.Select(c => Footprint(c.Coverage)).ToArray();
        var windows = new AmlBandWindow[cells.Count];
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (Limit(cell.ScaleBand) is not { } limit)
            {
                windows[i] = AmlBandWindow.Always;
                continue;
            }

            windows[i] = new AmlBandWindow(limit, CoveredAtOrBelow(i, cells, footprints));
        }

        return windows;
    }

    // The coarsest scale a band shows at; null for band 0, no band or an unknown one.
    private static int? Limit(int? band) =>
        band is { } b && b > 0 && AmlScaleBands.DisplayRange(b) is { Minimum: { } minimum } ? minimum : null;

    // Finer cells of the product show from their own zoom-out limits inwards.
    // Adding them coarsest-limit first, the cell is covered from the limit of
    // the one that completes the cover.
    private static int? CoveredAtOrBelow(int index, IReadOnlyList<AmlBandCell> cells, Geometry?[] footprints)
    {
        var cell = cells[index];
        if (footprints[index] is not { IsEmpty: false } own || own.Area <= 0)
            return null;

        var finer = Enumerable.Range(0, cells.Count)
            .Where(j => j != index
                && string.Equals(cells[j].Product, cell.Product, StringComparison.OrdinalIgnoreCase)
                && cells[j].ScaleBand > cell.ScaleBand
                && Limit(cells[j].ScaleBand) is not null
                && footprints[j] is { IsEmpty: false } other
                && other.Intersects(own))
            .OrderByDescending(j => Limit(cells[j].ScaleBand))
            .ToList();
        if (finer.Count == 0)
            return null;

        try
        {
            Geometry? union = null;
            foreach (var j in finer)
            {
                // Cells that only touch give lines or points: keep the areas.
                if (Polygonal(footprints[j]!.Intersection(own)) is not { } clipped)
                    continue;
                union = union is null ? clipped : Polygonal(union.Union(clipped)) ?? union;
                if (union.Area >= own.Area * CoveredShare)
                    return Limit(cells[j].ScaleBand);
            }
        }
        catch (TopologyException)
        {
            // Coverage too broken to measure: the cell doesn't give way.
        }

        return null;
    }

    // The polygons of a geometry (an overlay may return a collection with
    // lines and points too), or null when it has none.
    private static Geometry? Polygonal(Geometry geometry)
    {
        if (geometry is Polygon or MultiPolygon)
            return geometry.IsEmpty ? null : geometry;
        var polygons = NetTopologySuite.Geometries.Utilities.PolygonExtracter.GetPolygons(geometry)
            .OfType<Polygon>()
            .Where(p => !p.IsEmpty)
            .ToArray();
        return polygons.Length switch
        {
            0 => null,
            1 => polygons[0],
            _ => Factory.CreateMultiPolygon(polygons).Union(),
        };
    }

    private static Geometry? Footprint(IReadOnlyList<CoverageArea> areas)
    {
        var polygons = new List<Polygon>(areas.Count);
        foreach (var area in areas)
        {
            if (Ring(area.ExteriorRing) is not { } shell)
                continue;
            var holes = area.InteriorRings.Select(Ring).OfType<LinearRing>().ToArray();
            var polygon = Factory.CreatePolygon(shell, holes);
            if (!polygon.IsValid)
            {
                var fixedGeometry = NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(polygon);
                polygons.AddRange(fixedGeometry is MultiPolygon multi
                    ? multi.Geometries.OfType<Polygon>()
                    : fixedGeometry is Polygon single ? [single] : []);
                continue;
            }

            polygons.Add(polygon);
        }

        return polygons.Count switch
        {
            0 => null,
            1 => polygons[0],
            _ => Factory.CreateMultiPolygon([.. polygons]).Union(),
        };
    }

    private static LinearRing? Ring(IReadOnlyList<GeoPosition> points)
    {
        if (points.Count < 3)
            return null;
        var coordinates = points.Select(p => new Coordinate(p.Longitude, p.Latitude)).ToList();
        if (!coordinates[0].Equals2D(coordinates[^1]))
            coordinates.Add(coordinates[0].Copy());
        return coordinates.Count < 4 ? null : Factory.CreateLinearRing([.. coordinates]);
    }
}
