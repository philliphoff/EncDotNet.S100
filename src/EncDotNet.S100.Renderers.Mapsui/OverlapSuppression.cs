using Mapsui.Layers;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// One loaded chart cell's contribution to cross-cell scale-band overlap
/// suppression (issue #438 Phase 2): its base-chart layers, its EPSG:3857 data
/// coverage footprint, and the scale-band denominator used to decide which
/// cells are "finer" (smaller denominator = larger scale).
/// </summary>
public sealed class OverlapSuppressionCell
{
    /// <summary>The cell's layers whose drawing is clipped when a finer cell overlaps.</summary>
    public required IReadOnlyList<ILayer> Layers { get; init; }

    /// <summary>
    /// The cell's data-coverage footprint in EPSG:3857 (from
    /// <see cref="DatasetResult.CoverageGeometry"/>), or <see langword="null"/>
    /// when the cell declares no usable coverage (never suppresses or is
    /// suppressed).
    /// </summary>
    public Geometry? Coverage { get; init; }

    /// <summary>
    /// The cell's scale-band denominator (S-101 <c>DataCoverage.minimumDisplay
    /// Scale</c>, FC §3.1.1; S-57 DSPM compilation scale). A cell with a
    /// strictly smaller denominator is "finer" and suppresses coarser overlaps.
    /// <see langword="null"/> when unknown (excluded from suppression).
    /// </summary>
    public int? ScaleDenominator { get; init; }
}

/// <summary>
/// Computes and attaches per-cell screen-space clip regions for cross-cell
/// scale-band overlap suppression ("larger-scale-in", issue #438 Phase 2). For
/// each cell it forms <c>coverage(C) \ union(coverage(F))</c> over all loaded
/// cells <c>F</c> with a strictly finer band that overlap <c>C</c>, and attaches
/// the result (via <see cref="CoverageClip"/>) so the renderer draws the coarser
/// cell only where no finer cell provides coverage. Holes are preserved, so a
/// coarser cell still shows through gaps between finer cells.
/// </summary>
public static class OverlapSuppression
{
    /// <summary>
    /// Recomputes and attaches clip regions across all supplied loaded
    /// <paramref name="cells"/>. Any cell not suppressed (no finer overlapping
    /// coverage) is cleared so it paints in full. Call on every load / unload /
    /// scale change; callers should skip it (and use <see cref="ClearAll"/>)
    /// when the mariner has opted to ignore scale minima.
    /// </summary>
    public static void Apply(IReadOnlyList<OverlapSuppressionCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        foreach (var cell in cells)
        {
            Geometry? clip = ComputeClip(cell, cells);
            foreach (var layer in cell.Layers)
                CoverageClip.Set(layer, clip);
        }
    }

    /// <summary>
    /// Removes every clip attachment from the supplied <paramref name="cells"/>'
    /// layers so they all paint in full (used when suppression is disabled).
    /// </summary>
    public static void ClearAll(IReadOnlyList<OverlapSuppressionCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        foreach (var cell in cells)
            foreach (var layer in cell.Layers)
                CoverageClip.Set(layer, null);
    }

    /// <summary>
    /// Computes the clip region for <paramref name="cell"/>: its coverage minus
    /// the union of every strictly-finer overlapping cell's coverage. Returns
    /// <see langword="null"/> (no clip) when the cell has no coverage/scale or no
    /// finer cell overlaps it; an empty geometry when it is entirely covered.
    /// </summary>
    internal static Geometry? ComputeClip(
        OverlapSuppressionCell cell,
        IReadOnlyList<OverlapSuppressionCell> cells)
    {
        if (cell.Coverage is not { IsEmpty: false } coverage || cell.ScaleDenominator is not int denom)
            return null;

        Geometry? finerUnion = null;
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

            finerUnion = finerUnion is null ? otherCoverage : finerUnion.Union(otherCoverage);
        }

        if (finerUnion is null)
            return null;

        try
        {
            return coverage.Difference(finerUnion);
        }
        catch (TopologyException)
        {
            // Robustness fallback: a zero-width buffer normalises near-degenerate
            // coverage rings so the difference can be retaken.
            try
            {
                return coverage.Buffer(0).Difference(finerUnion.Buffer(0));
            }
            catch (TopologyException)
            {
                return null;
            }
        }
    }
}
