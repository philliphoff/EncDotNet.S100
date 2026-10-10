using EncDotNet.S100.Rendering.Scene;
using Mapsui.Layers;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// One loaded chart cell's contribution to cross-cell scale-band overlap
/// suppression (issue #438 Phase 2): its base-chart layers plus the
/// renderer-neutral ranking inputs of <see cref="CoverageOverlapCell"/> (its
/// coverage from <see cref="MapsuiDatasetResult.CoverageGeometry"/>, its ranking
/// denominator and its zoom-out cutoff denominator). The Mapsui renderer clamps
/// the cell's layers to the cutoff denominator
/// (<c>MapsuiDatasetRenderer.ApplyCellScaleWindow</c> / the per-feature
/// out-of-scale-band cap), so the suppressor's cutoff tracks the cell's content
/// visibility (see <see cref="OverlapSuppression.CollectFinerCoverages"/>).
/// </summary>
public sealed class OverlapSuppressionCell : CoverageOverlapCell
{
    /// <summary>The cell's layers whose drawing is clipped when a finer cell overlaps.</summary>
    public required IReadOnlyList<ILayer> Layers { get; init; }
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
            // One array shared by all of the cell's layers, so the prepared
            // hidden-region geometries (CoverageClip.GetHiddenCoverage) are
            // built once per cell rather than once per layer.
            FinerCoverage[]? finer = CollectFinerCoverages(cell, cells) is { } list ? [.. list] : null;
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
    /// Collects the finer, overlapping coverages that clip <paramref name="cell"/>
    /// (see <see cref="CoverageOverlap.CollectFinerCoverages"/>), each paired with
    /// that finer cell's content zoom-out cutoff as a resolution: the resolution
    /// past which the finer cell stops drawing, derived from its cutoff
    /// denominator (see <see cref="CoverageOverlapCell.CutoffScaleDenominator"/>).
    /// Returns <see langword="null"/> when the cell has no coverage/scale or no
    /// finer cell overlaps it.
    /// </summary>
    internal static IReadOnlyList<FinerCoverage>? CollectFinerCoverages(
        OverlapSuppressionCell cell,
        IReadOnlyList<OverlapSuppressionCell> cells) =>
        CoverageOverlap.CollectFinerCoverages(
            cell,
            cells,
            static other => ContentCutoffResolution(CoverageOverlap.CutoffScaleDenominator(other)!.Value, other.Coverage!));

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
