using EncDotNet.S100.DataModel;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// One finer, overlapping cell's contribution to a coarser cell's cross-cell
/// scale-band overlap suppression ("larger-scale-in", issue #438 Phase 2): the
/// finer cell's EPSG:3857 (Web Mercator) data-coverage footprint and the zoom
/// level past which the finer cell stops drawing its own content.
/// </summary>
/// <remarks>
/// The cutoff makes suppression <em>zoom-aware</em>: a finer cell only hides the
/// coarser cell while the finer cell is actually drawing. Once the display zooms
/// out past <see cref="Cutoff"/>, the finer cell's content is hidden and it must
/// stop suppressing, otherwise the coarser cell would be clipped to a blank hole
/// with nothing drawn in it.
/// </remarks>
/// <param name="Coverage">The finer cell's data coverage, in EPSG:3857 metres.</param>
/// <param name="Cutoff">
/// The zoom measure past which the finer cell stops drawing, larger meaning
/// further zoomed out. Each renderer picks the measure it tests visibility with:
/// the Mapsui viewer uses the viewport resolution (metres per pixel), the
/// headless compositor the viewport scale denominator. The coverage is active
/// while the live measure is at or below it.
/// </param>
public readonly record struct FinerCoverage(Geometry Coverage, double Cutoff);

/// <summary>
/// One loaded chart cell's inputs to cross-cell scale-band overlap suppression
/// (issue #438 Phase 2): its EPSG:3857 data-coverage footprint, the scale-band
/// denominator used to decide which cells are "finer" (smaller denominator =
/// larger scale), and the denominator from which the cell's zoom-out cutoff is
/// derived.
/// </summary>
public class CoverageOverlapCell
{
    /// <summary>
    /// The cell's data-coverage footprint in EPSG:3857 (see
    /// <see cref="CoverageOverlap.ToWebMercator"/>), or <see langword="null"/>
    /// when the cell declares no usable coverage (never suppresses or is
    /// suppressed).
    /// </summary>
    public Geometry? Coverage { get; init; }

    /// <summary>
    /// The cell's ranking scale denominator (S-101 <c>DataCoverage.minimum
    /// DisplayScale</c>, FC §3.1.1; S-57 DSPM compilation scale). A cell with a
    /// strictly smaller denominator is "finer" and suppresses coarser overlaps.
    /// <see langword="null"/> when unknown (excluded from suppression).
    /// </summary>
    /// <remarks>
    /// Unless <see cref="CutoffScaleDenominator"/> is set, this is also the
    /// denominator the renderer stops drawing the cell's content at, so a finer
    /// cell's suppression cutoff is derived from it.
    /// </remarks>
    public int? ScaleDenominator { get; init; }

    /// <summary>
    /// The whole-cell zoom-out window denominator the cell is drawn to, when it
    /// differs from <see cref="ScaleDenominator"/> — e.g. an S-57 cell ranked by
    /// its compilation scale but drawn out to its largest <c>SCAMIN</c>. A finer
    /// cell keeps suppressing coarser overlaps until the display zooms out past
    /// this denominator. <see langword="null"/> falls back to
    /// <see cref="ScaleDenominator"/>.
    /// </summary>
    public int? CutoffScaleDenominator { get; init; }
}

/// <summary>
/// The renderer-neutral part of cross-cell scale-band overlap suppression
/// ("larger-scale-in", issue #438 Phase 2; issue #859): which loaded cells are
/// finer than, and overlap, a given cell, so a renderer can hide the coarser
/// cell under the finer cells' coverage while they draw. The Mapsui viewer and
/// the headless compositor both rank cells this way.
/// </summary>
public static class CoverageOverlap
{
    /// <summary>
    /// Collects the finer, overlapping coverages that hide <paramref name="cell"/>:
    /// every other cell with a strictly smaller scale denominator whose coverage
    /// intersects this cell's coverage, each paired with the cutoff
    /// <paramref name="cutoff"/> computes for that finer cell. Returns
    /// <see langword="null"/> when the cell has no coverage or scale, or no finer
    /// cell overlaps it.
    /// </summary>
    /// <typeparam name="TCell">The cell type.</typeparam>
    /// <param name="cell">The cell that may be hidden.</param>
    /// <param name="cells">All loaded cells; may include <paramref name="cell"/>.</param>
    /// <param name="cutoff">
    /// Computes a finer cell's <see cref="FinerCoverage.Cutoff"/>. It is only
    /// called for cells with a coverage and a scale denominator.
    /// </param>
    /// <returns>The finer coverages, or <see langword="null"/> when there are none.</returns>
    public static IReadOnlyList<FinerCoverage>? CollectFinerCoverages<TCell>(
        TCell cell,
        IReadOnlyList<TCell> cells,
        Func<TCell, double> cutoff)
        where TCell : CoverageOverlapCell
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(cutoff);

        if (cell.Coverage is not { IsEmpty: false } coverage || cell.ScaleDenominator is not int denom)
            return null;

        List<FinerCoverage>? finer = null;
        foreach (var other in cells)
        {
            if (ReferenceEquals(other, cell))
                continue;
            if (other.Coverage is not { IsEmpty: false } otherCoverage)
                continue;
            if (other.ScaleDenominator is not int otherDenom)
                continue;
            // Strictly finer band only, so equal-band siblings never mutually
            // clip (which would erase their shared border from both).
            if (otherDenom >= denom)
                continue;
            if (!coverage.EnvelopeInternal.Intersects(otherCoverage.EnvelopeInternal))
                continue;
            if (!coverage.Intersects(otherCoverage))
                continue;

            (finer ??= []).Add(new FinerCoverage(otherCoverage, cutoff(other)));
        }

        return finer;
    }

    /// <summary>
    /// The denominator a cell stops drawing its content at:
    /// <see cref="CoverageOverlapCell.CutoffScaleDenominator"/>, falling back to
    /// <see cref="CoverageOverlapCell.ScaleDenominator"/>.
    /// </summary>
    /// <param name="cell">The cell.</param>
    /// <returns>The cutoff denominator, or <see langword="null"/> when the cell has neither.</returns>
    public static int? CutoffScaleDenominator(CoverageOverlapCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return cell.CutoffScaleDenominator ?? cell.ScaleDenominator;
    }

    /// <summary>
    /// Converts a dataset's declared data coverage (EPSG:4326 rings) to one
    /// EPSG:3857 footprint, or <see langword="null"/> when it has no usable
    /// area. The rings are normalised with a zero-width buffer, which also
    /// unions overlapping parts, so the result is safe for intersection tests
    /// and clipping.
    /// </summary>
    /// <param name="areas">The coverage areas.</param>
    /// <returns>The footprint, or <see langword="null"/>.</returns>
    public static Geometry? ToWebMercator(IReadOnlyList<CoverageArea> areas)
    {
        ArgumentNullException.ThrowIfNull(areas);

        if (areas.Count == 0)
            return null;

        var polygons = new List<Polygon>(areas.Count);
        foreach (var area in areas)
        {
            var shell = ToWebMercatorRing(area.ExteriorRing);
            if (shell is null)
                continue;

            LinearRing[]? holes = null;
            if (area.InteriorRings.Count > 0)
            {
                var holeList = new List<LinearRing>(area.InteriorRings.Count);
                foreach (var interior in area.InteriorRings)
                {
                    var hole = ToWebMercatorRing(interior);
                    if (hole is not null)
                        holeList.Add(hole);
                }

                if (holeList.Count > 0)
                    holes = holeList.ToArray();
            }

            polygons.Add(new Polygon(shell, holes));
        }

        if (polygons.Count == 0)
            return null;

        Geometry geometry = polygons.Count == 1
            ? polygons[0]
            : new MultiPolygon(polygons.ToArray());

        // Coverage rings can be self-touching or slightly non-simple after
        // projection; a zero-width buffer normalises them and unions the parts
        // into a clean footprint for reliable clip algebra.
        try
        {
            var normalized = geometry.Buffer(0);
            // A degenerate / zero-area coverage normalises to an empty geometry;
            // treat that as "no usable coverage" (return null so suppression is
            // simply disabled for the cell) rather than propagating the raw,
            // possibly non-simple geometry into Intersects / clipping, where it
            // risks TopologyExceptions or incorrect clipping.
            return normalized.IsEmpty ? null : normalized;
        }
        catch (TopologyException)
        {
            // Normalisation failed outright — disable suppression for this cell
            // rather than feed an invalid geometry downstream.
            return null;
        }
    }

    /// <summary>
    /// Projects a single EPSG:4326 coverage ring (lat/lon per S-100 Part 10b
    /// §6.2) to a closed EPSG:3857 <see cref="LinearRing"/>, or
    /// <see langword="null"/> when it has fewer than three distinct positions.
    /// </summary>
    private static LinearRing? ToWebMercatorRing(IReadOnlyList<GeoPosition> ring)
    {
        if (ring.Count < 3)
            return null;

        var coordinates = new List<Coordinate>(ring.Count + 1);
        foreach (var position in ring)
        {
            var (x, y) = WebMercator.FromLonLat(position.Longitude, position.Latitude);
            coordinates.Add(new Coordinate(x, y));
        }

        // Ensure the ring is explicitly closed for NTS.
        if (!coordinates[0].Equals2D(coordinates[^1]))
            coordinates.Add(coordinates[0].Copy());

        if (coordinates.Count < 4)
            return null;

        return new LinearRing(coordinates.ToArray());
    }
}
