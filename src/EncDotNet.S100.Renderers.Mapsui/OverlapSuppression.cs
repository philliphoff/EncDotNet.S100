using Mapsui.Layers;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// One loaded chart cell's contribution to cross-cell scale-band overlap
/// suppression (issue #438 Phase 2): its base-chart layers, its EPSG:3857 data
/// coverage footprint, and the scale-band denominator used both to decide which
/// cells are "finer" (smaller denominator = larger scale) and to derive each
/// finer cell's zoom-out cutoff (the resolution past which it stops drawing, so
/// it must stop suppressing — computed per finer cell in
/// <see cref="OverlapSuppression.CollectFinerCoverages"/>).
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
    /// <remarks>
    /// This is the same denominator the renderer clamps the cell's layers to
    /// (<c>MapsuiDatasetRenderer.ApplyCellScaleWindow</c> / the per-feature
    /// out-of-scale-band cap), so converting it to a resolution yields exactly
    /// the zoom-out point at which the cell stops drawing its content. The
    /// suppressor's cutoff is derived from it (see
    /// <see cref="OverlapSuppression.CollectFinerCoverages"/>) rather than from a
    /// separately-recorded window that can be absent for standalone-loaded cells.
    /// </remarks>
    public int? ScaleDenominator { get; init; }
}

/// <summary>
/// Computes and attaches per-cell screen-space clip contributions for cross-cell
/// scale-band overlap suppression ("larger-scale-in", issue #438 Phase 2). For
/// each cell it gathers every loaded, strictly-finer cell whose coverage overlaps
/// it and attaches those finer coverages (via <see cref="CoverageClip"/>) so the
/// renderer subtracts each — but only while the finer cell is itself visible at
/// the live resolution — from the coarser cell's drawable region. Holes are
/// preserved, so a coarser cell still shows through gaps between finer cells, and
/// the subtraction relaxes as finer cells zoom out of their scale band.
/// </summary>
public static class OverlapSuppression
{
    /// <summary>
    /// Recomputes and attaches clip contributions across all supplied loaded
    /// <paramref name="cells"/>. Any cell with no finer overlapping coverage is
    /// cleared so it paints in full. Call on every load / unload / scale change;
    /// callers should skip it (and use <see cref="ClearAll"/>) when the mariner
    /// has opted to ignore scale minima.
    /// </summary>
    public static void Apply(IReadOnlyList<OverlapSuppressionCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        foreach (var cell in cells)
        {
            var finer = CollectFinerCoverages(cell, cells);
            foreach (var layer in cell.Layers)
                CoverageClip.Set(layer, finer);
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
    /// Collects the finer, overlapping coverages that clip <paramref name="cell"/>:
    /// every other cell with a strictly smaller scale denominator whose coverage
    /// envelope-and-geometry intersects this cell's coverage, paired with that
    /// finer cell's content zoom-out cutoff (the resolution past which the finer
    /// cell stops drawing, derived from its own scale denominator). Returns
    /// <see langword="null"/> when the cell has no coverage/scale or no finer cell
    /// overlaps it.
    /// </summary>
    internal static IReadOnlyList<FinerCoverage>? CollectFinerCoverages(
        OverlapSuppressionCell cell,
        IReadOnlyList<OverlapSuppressionCell> cells)
    {
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

            (finer ??= []).Add(new FinerCoverage(otherCoverage, ContentCutoffResolution(otherDenom, otherCoverage)));
        }

        return finer;
    }

    /// <summary>
    /// The EPSG:3857 resolution (metres/pixel) past which a finer cell of scale
    /// denominator <paramref name="denominator"/> stops drawing its content, so
    /// it must stop suppressing coarser cells (otherwise the coarser cell would be
    /// clipped to a blank hole with the now-hidden finer cell drawing nothing).
    /// Derived from the same true-scale denominator the renderer clamps the cell's
    /// layers to (<c>MapsuiDatasetRenderer.ApplyCellScaleWindow</c> and the
    /// per-feature out-of-scale-band cap), converted at the coverage envelope-
    /// centre latitude to undo web-mercator <c>1/cos φ</c> distortion (the same
    /// extent-centre convention <c>ApplyCellScaleWindow</c> uses) — so the cutoff
    /// tracks the finer cell's content visibility exactly, for both exchange-set
    /// and standalone-loaded cells.
    /// </summary>
    private static double ContentCutoffResolution(int denominator, Geometry coverage)
    {
        var envelope = coverage.EnvelopeInternal;
        var latitudeRadians = MapsuiDisplayListRenderer.WebMercatorYToLatitudeRadians(
            (envelope.MinY + envelope.MaxY) / 2.0);
        return MapsuiDisplayListRenderer.DenominatorToResolution(denominator, latitudeRadians);
    }
}
