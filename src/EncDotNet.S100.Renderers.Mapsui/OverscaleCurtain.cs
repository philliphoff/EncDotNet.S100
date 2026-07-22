using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// One overscaled cell's on-chart overscale "curtain" region (issue #441,
/// S-52 / S-101 <c>AP(OVERSC01)</c> overscale indication, Form A): the cell's
/// name, its overscale factor, and the EPSG:3857 (Web Mercator) area over which
/// the curtain pattern should be painted.
/// </summary>
/// <param name="Name">The overscaled cell's display name.</param>
/// <param name="Factor">
/// The overscale factor (<c>compilationDenominator / displayDenominator</c>),
/// always strictly greater than 1 — the same value the status-bar indicator
/// shows (<see cref="OverscaleCell.Factor"/>).
/// </param>
/// <param name="Region">
/// The EPSG:3857 area to paint the curtain over: the cell's data-coverage
/// footprint with the coverage of every strictly-finer (larger-scale)
/// overlapping cell subtracted, so a finer cell's in-scale footprint stays
/// curtain-free (S-52: the curtain marks only genuinely overscaled area).
/// </param>
public sealed record OverscaleRegion(string Name, double Factor, Geometry Region);

/// <summary>
/// Computes the on-chart overscale-curtain regions for the current view
/// (issue #441, Form A — the classic S-52 <c>AP(OVERSC01)</c> vertical-line
/// "curtain"). This is the geometric companion to <see cref="OverscaleEvaluator"/>:
/// the evaluator answers <em>whether and by how much</em> each cell is
/// overscaled (for the status-bar indicator); this class answers <em>where</em>
/// the curtain pattern should be painted.
/// </summary>
/// <remarks>
/// <para>
/// A cell is overscaled when the mariner has zoomed in past its compilation
/// scale (<see cref="OverscaleCellInput.CompilationScaleDenominator"/>, S-101 FC
/// §3.1.1 <c>DataCoverage.maximumDisplayScale</c>). The curtain, however, must
/// only cover the part of an overscaled cell that is actually the topmost data:
/// where a strictly-finer (larger-scale) cell overlaps, that finer cell draws on
/// top — so the coarser cell's curtain is subtracted there (the finer cell, if
/// itself overscaled, contributes its own, smaller-factor, curtain). This mirrors
/// the screen-space coverage clip the renderer already applies
/// (<see cref="OverlapSuppression"/> / <see cref="CoverageClip"/>), so the curtain
/// lines up with what is actually drawn.
/// </para>
/// <para>
/// The result depends only on the loaded cells and the viewport resolution — not
/// on pan — because each region is expressed in world (EPSG:3857) coordinates;
/// the renderer projects and clips it per frame. Callers therefore only need to
/// recompute when the resolution or the set of loaded cells changes.
/// </para>
/// </remarks>
public static class OverscaleCurtain
{
    /// <summary>
    /// Computes the overscale-curtain regions for <paramref name="cells"/> at the
    /// given EPSG:3857 <paramref name="viewportResolution"/> (Web-Mercator
    /// metres per pixel). Each overscaled cell yields at most one
    /// <see cref="OverscaleRegion"/>: its coverage with every strictly-finer
    /// overlapping cell's coverage subtracted. Cells that are in-scale (factor
    /// at or below <see cref="OverscaleEvaluator.OverscaleThreshold"/>), carry no
    /// usable coverage, or are entirely covered by finer cells yield nothing.
    /// </summary>
    /// <param name="cells">The loaded, drawing, scale-bearing cells.</param>
    /// <param name="viewportResolution">
    /// The current viewport resolution in Web-Mercator metres per pixel (must be
    /// positive and finite; otherwise an empty list is returned).
    /// </param>
    /// <returns>
    /// The curtain regions (never <see langword="null"/>), ordered by descending
    /// overscale factor (worst offender first) to match the status-bar list.
    /// </returns>
    public static IReadOnlyList<OverscaleRegion> ComputeRegions(
        IReadOnlyList<OverscaleCellInput> cells,
        double viewportResolution)
    {
        ArgumentNullException.ThrowIfNull(cells);

        if (double.IsNaN(viewportResolution) || viewportResolution <= 0)
            return [];

        List<OverscaleRegion>? regions = null;
        foreach (var cell in cells)
        {
            if (cell.Coverage is not { IsEmpty: false } coverage || cell.CompilationScaleDenominator <= 0)
                continue;

            var envelope = coverage.EnvelopeInternal;
            var latitudeRadians = MapsuiDisplayListRenderer.WebMercatorYToLatitudeRadians(
                (envelope.MinY + envelope.MaxY) / 2.0);
            var compilationResolution = MapsuiDisplayListRenderer.DenominatorToResolution(
                cell.CompilationScaleDenominator, latitudeRadians);
            var factor = compilationResolution / viewportResolution;
            if (factor <= OverscaleEvaluator.OverscaleThreshold)
                continue;

            var region = SubtractFinerCoverages(cell, coverage, cells);
            if (region is null || region.IsEmpty)
                continue;

            (regions ??= []).Add(new OverscaleRegion(cell.Name, factor, region));
        }

        if (regions is null)
            return [];

        // Worst offender first; tie-break by name so the overlay ordering is
        // stable as the view pans (matches OverscaleEvaluator).
        regions.Sort(static (a, b) =>
        {
            var byFactor = b.Factor.CompareTo(a.Factor);
            return byFactor != 0 ? byFactor : string.CompareOrdinal(a.Name, b.Name);
        });

        return regions;
    }

    /// <summary>
    /// Subtracts from <paramref name="coverage"/> the coverage of every other
    /// cell that is strictly finer (a smaller compilation-scale denominator = a
    /// larger scale) and overlaps it — the finer cells that draw on top of
    /// <paramref name="cell"/>. Returns <paramref name="coverage"/> unchanged when
    /// no finer cell overlaps, or the difference geometry (possibly empty) when
    /// finer cells cover part or all of it.
    /// </summary>
    private static Geometry? SubtractFinerCoverages(
        OverscaleCellInput cell,
        Geometry coverage,
        IReadOnlyList<OverscaleCellInput> cells)
    {
        Geometry region = coverage;
        foreach (var other in cells)
        {
            if (ReferenceEquals(other, cell))
                continue;
            if (other.Coverage is not { IsEmpty: false } otherCoverage)
                continue;
            if (other.CompilationScaleDenominator <= 0)
                continue;
            // Strictly finer band only, so equal-band siblings never subtract
            // from one another (which would erase their shared overlap from both).
            if (other.CompilationScaleDenominator >= cell.CompilationScaleDenominator)
                continue;
            if (!region.EnvelopeInternal.Intersects(otherCoverage.EnvelopeInternal))
                continue;
            if (!region.Intersects(otherCoverage))
                continue;

            region = region.Difference(otherCoverage);
            if (region.IsEmpty)
                break;
        }

        return region;
    }
}
