using EncDotNet.S100.Collections;
using EncDotNet.S100.ExchangeSets;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace EncDotNet.S100.Viewer.Services.LazyLoading;

/// <summary>
/// Pure coverage test behind lazy-load deferral (#1265): whether the part of a
/// coarse cell that is in view is already covered by finer cells, so the
/// coarse cell can load after them instead of next to them.
/// </summary>
/// <remarks>
/// <para>
/// Geometries are EPSG:4326 with x = longitude and y = latitude. Only the finer
/// cells' coverage has to be exact: a covered answer means "the finer cells
/// fill this", so their catalogue coverage polygons are required and a
/// bounding box never counts. The coarse cell itself may fall back to its
/// bounding box, which only makes the test stricter.
/// </para>
/// <para>
/// A covered cell is deferred, never skipped: it still loads once the visible
/// work is done, so a wrong answer costs a short-lived gap, not a permanent one.
/// </para>
/// </remarks>
internal static class CoveredCellGate
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    /// <summary>
    /// Builds a cell's catalogue coverage as an EPSG:4326 geometry
    /// (x = longitude), or <see langword="null"/> when there is none or it
    /// can't be used: no rings, a ring that crosses the antimeridian (longitude
    /// span over 180°), or a shape that doesn't repair into a valid polygon.
    /// </summary>
    /// <param name="coverage">The catalogue coverage, for example a NOAA product catalogue cell's panels.</param>
    /// <returns>The coverage geometry, or <see langword="null"/>.</returns>
    public static Geometry? FromCatalogueCoverage(GeoCoverage? coverage)
    {
        if (coverage is null || coverage.Polygons.Count == 0)
            return null;

        try
        {
            var polygons = new List<Polygon>(coverage.Polygons.Count);
            foreach (var polygon in coverage.Polygons)
            {
                if (ToRing(polygon.Exterior) is not { } shell)
                    return null;
                var holes = new List<LinearRing>(polygon.Holes.Count);
                foreach (var hole in polygon.Holes)
                {
                    if (ToRing(hole) is not { } ring)
                        return null;
                    holes.Add(ring);
                }
                polygons.Add(Factory.CreatePolygon(shell, holes.ToArray()));
            }

            Geometry geometry = polygons.Count == 1
                ? polygons[0]
                : Factory.CreateMultiPolygon(polygons.ToArray());
            if (!geometry.IsValid)
                geometry = geometry.Buffer(0);
            return geometry.IsEmpty || geometry.Area <= 0 ? null : geometry;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (TopologyException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the part of a coarse cell inside the viewport is covered by
    /// <paramref name="finerCoverages"/>, up to slivers narrower than
    /// <paramref name="tolerance"/> degrees.
    /// </summary>
    /// <param name="coverage">The coarse cell's coverage, or <see langword="null"/> to use <paramref name="bounds"/>.</param>
    /// <param name="bounds">The coarse cell's bounding box, used when it has no coverage.</param>
    /// <param name="finerCoverages">The coverage of finer cells that draw, or will draw, at this scale.</param>
    /// <param name="window">The viewport.</param>
    /// <param name="tolerance">The width in degrees below which an uncovered sliver is ignored.</param>
    /// <returns>
    /// <see langword="true"/> when the finer coverage fills the cell's part of the
    /// view (or the cell's coverage doesn't reach the view at all);
    /// <see langword="false"/> when any visible part is uncovered, when the cell
    /// has neither coverage nor bounds, when the view crosses the antimeridian, or
    /// when the geometry can't be evaluated.
    /// </returns>
    public static bool IsCoveredInView(
        Geometry? coverage,
        BoundingBox? bounds,
        IReadOnlyList<Geometry> finerCoverages,
        LoadWindow window,
        double tolerance)
    {
        ArgumentNullException.ThrowIfNull(finerCoverages);
        ArgumentNullException.ThrowIfNull(window);

        if (finerCoverages.Count == 0 || window.West > window.East)
            return false;

        try
        {
            var view = Factory.ToGeometry(new Envelope(window.West, window.East, window.South, window.North));
            Geometry? footprint = coverage;
            if (footprint is null)
            {
                if (bounds is null || bounds.WestBoundLongitude > bounds.EastBoundLongitude)
                    return false;
                footprint = Factory.ToGeometry(new Envelope(
                    bounds.WestBoundLongitude, bounds.EastBoundLongitude,
                    bounds.SouthBoundLatitude, bounds.NorthBoundLatitude));
            }

            var visible = footprint.Intersection(view);
            if (visible.IsEmpty || visible.Area <= 0)
                return true;

            var clipped = new List<Geometry>(finerCoverages.Count);
            foreach (var finer in finerCoverages)
            {
                if (!finer.EnvelopeInternal.Intersects(visible.EnvelopeInternal))
                    continue;
                var part = finer.Intersection(view);
                if (!part.IsEmpty)
                    clipped.Add(part);
            }
            if (clipped.Count == 0)
                return false;

            var union = CascadedPolygonUnion.Union(clipped);
            var uncovered = visible.Difference(union);
            if (uncovered.IsEmpty || uncovered.Area <= 0)
                return true;

            // Shrinking by half the tolerance removes anything thinner than the
            // tolerance: the hairline gaps catalogue polygons leave between
            // neighbouring cells, which the coarse cell fills once it loads.
            return tolerance > 0 && uncovered.Buffer(-tolerance / 2).IsEmpty;
        }
        catch (TopologyException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// The sliver tolerance in degrees for a view: <paramref name="pixels"/>
    /// screen pixels at <paramref name="mercatorResolution"/> metres per pixel,
    /// measured north–south at <paramref name="latitude"/> (the narrower of the
    /// two axes in degrees), so the tolerance is never wider than intended.
    /// </summary>
    /// <param name="mercatorResolution">The viewport's EPSG:3857 resolution.</param>
    /// <param name="latitude">The latitude to measure at.</param>
    /// <param name="pixels">The tolerance in pixels.</param>
    /// <returns>The tolerance in degrees; 0 when the resolution is unusable.</returns>
    public static double ToleranceDegrees(double mercatorResolution, double latitude, double pixels)
    {
        if (double.IsNaN(mercatorResolution) || mercatorResolution <= 0 || pixels <= 0)
            return 0;
        const double metresPerDegree = 6_378_137.0 * Math.PI / 180.0;
        return pixels * mercatorResolution * Math.Cos(latitude * Math.PI / 180.0) / metresPerDegree;
    }

    private static LinearRing? ToRing(IReadOnlyList<DataModel.GeoPosition> positions)
    {
        if (positions.Count < 3)
            return null;

        var coordinates = new List<Coordinate>(positions.Count + 1);
        var west = double.PositiveInfinity;
        var east = double.NegativeInfinity;
        foreach (var p in positions)
        {
            if (!double.IsFinite(p.Latitude) || !double.IsFinite(p.Longitude))
                return null;
            coordinates.Add(new Coordinate(p.Longitude, p.Latitude));
            west = Math.Min(west, p.Longitude);
            east = Math.Max(east, p.Longitude);
        }
        if (east - west > 180.0)
            return null;
        if (!coordinates[0].Equals2D(coordinates[^1]))
            coordinates.Add(coordinates[0].Copy());
        return coordinates.Count < 4 ? null : Factory.CreateLinearRing(coordinates.ToArray());
    }
}
