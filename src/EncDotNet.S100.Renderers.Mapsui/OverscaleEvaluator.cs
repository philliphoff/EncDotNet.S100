using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// One loaded, drawing chart cell's contribution to overscale evaluation
/// (issue #441, S-52 / S-101 overscale indication): its display name, its
/// EPSG:3857 data-coverage footprint, and its compilation-scale denominator
/// (the finest scale the cell was compiled for, S-101 FC §3.1.1
/// <c>DataCoverage.maximumDisplayScale</c>; S-57 DSPM compilation scale).
/// </summary>
public sealed class OverscaleCellInput
{
    /// <summary>The cell's human-readable name, shown in the overscale popup.</summary>
    public required string Name { get; init; }

    /// <summary>The cell's data-coverage footprint in EPSG:3857 (Web Mercator).</summary>
    public required Geometry Coverage { get; init; }

    /// <summary>
    /// The cell's compilation-scale denominator (finest intended display scale,
    /// S-101 FC §3.1.1 <c>DataCoverage.maximumDisplayScale</c>). The view is
    /// overscaled for this cell when the current display-scale denominator is
    /// smaller (more zoomed-in) than this value.
    /// </summary>
    public required int CompilationScaleDenominator { get; init; }
}

/// <summary>
/// A single overscaled cell in the current view: the cell's name and its
/// overscale factor (how many times finer the display scale is than the cell's
/// compilation scale, e.g. <c>4.6</c> = "4.6×").
/// </summary>
/// <param name="Name">The cell's display name.</param>
/// <param name="Factor">
/// The overscale factor (<c>compilationDenominator / displayDenominator</c>),
/// always strictly greater than 1.
/// </param>
public sealed record OverscaleCell(string Name, double Factor);

/// <summary>
/// The result of evaluating overscale across the loaded cells in the current
/// view (issue #441): the overscaled cells (worst first) and the worst factor.
/// </summary>
public sealed class OverscaleReport
{
    /// <summary>An empty report (nothing overscaled).</summary>
    public static readonly OverscaleReport None = new()
    {
        OverscaledCells = [],
        CellsInViewCount = 0,
    };

    /// <summary>
    /// The overscaled cells in view, ordered by descending
    /// <see cref="OverscaleCell.Factor"/> (worst offender first). Empty when
    /// nothing in view is overscaled.
    /// </summary>
    public required IReadOnlyList<OverscaleCell> OverscaledCells { get; init; }

    /// <summary>
    /// The total number of scale-bearing cells whose coverage intersects the
    /// view (overscaled or not). Not surfaced in the current UI but retained
    /// for diagnostics / tests.
    /// </summary>
    public required int CellsInViewCount { get; init; }

    /// <summary>True when at least one cell in view is overscaled.</summary>
    public bool IsOverscaled => OverscaledCells.Count > 0;

    /// <summary>
    /// The worst (largest) overscale factor in view, or <c>0</c> when nothing is
    /// overscaled.
    /// </summary>
    public double WorstFactor => OverscaledCells.Count > 0 ? OverscaledCells[0].Factor : 0.0;
}

/// <summary>
/// Computes per-cell overscale for the current viewport (issue #441). A chart
/// cell is <em>overscaled</em> when the mariner has zoomed in past the finest
/// scale the cell was compiled for (its
/// <see cref="OverscaleCellInput.CompilationScaleDenominator"/>): the chart is
/// magnified beyond the detail it actually contains. Because compilation scale
/// is a per-cell property, a single view can hold cells overscaled by different
/// amounts (and cells not overscaled at all); this evaluator reports each
/// overscaled cell and the worst offender for the status-bar indicator.
/// </summary>
public static class OverscaleEvaluator
{
    /// <summary>
    /// Factors at or below this are treated as in-scale. A tiny margin above
    /// <c>1.0</c> avoids flagging cells sitting exactly at their compilation
    /// scale as "overscaled" due to floating-point noise.
    /// </summary>
    internal const double OverscaleThreshold = 1.0 + 1e-6;

    /// <summary>
    /// Evaluates overscale for the supplied <paramref name="cells"/> at the given
    /// EPSG:3857 <paramref name="viewport"/> extent and
    /// <paramref name="viewportResolution"/> (Web-Mercator metres per pixel).
    /// Only cells whose coverage envelope intersects the viewport are counted;
    /// each such cell's overscale factor is
    /// <c>DenominatorToResolution(compilationDenominator, φ) / viewportResolution</c>,
    /// where <c>φ</c> is the latitude of the cell's coverage-envelope centre (so
    /// the factor undoes Web-Mercator <c>1/cos φ</c> distortion, matching the
    /// status-bar scale readout and the overlap-suppression cutoffs).
    /// </summary>
    /// <param name="cells">The loaded, drawing, scale-bearing cells.</param>
    /// <param name="viewport">The current view extent in EPSG:3857.</param>
    /// <param name="viewportResolution">
    /// The current viewport resolution in Web-Mercator metres per pixel (must be
    /// positive; non-positive or non-finite yields <see cref="OverscaleReport.None"/>).
    /// </param>
    /// <returns>The overscale report (never <see langword="null"/>).</returns>
    public static OverscaleReport Evaluate(
        IReadOnlyList<OverscaleCellInput> cells,
        Envelope viewport,
        double viewportResolution)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(viewport);

        if (double.IsNaN(viewportResolution) || viewportResolution <= 0 || viewport.IsNull)
            return OverscaleReport.None;

        List<OverscaleCell>? overscaled = null;
        var inView = 0;
        foreach (var cell in cells)
        {
            if (cell.Coverage is not { IsEmpty: false } coverage || cell.CompilationScaleDenominator <= 0)
                continue;

            var envelope = coverage.EnvelopeInternal;
            if (!viewport.Intersects(envelope))
                continue;

            inView++;

            var latitudeRadians = MapsuiDisplayListRenderer.WebMercatorYToLatitudeRadians(
                (envelope.MinY + envelope.MaxY) / 2.0);
            var compilationResolution = MapsuiDisplayListRenderer.DenominatorToResolution(
                cell.CompilationScaleDenominator, latitudeRadians);
            var factor = compilationResolution / viewportResolution;
            if (factor <= OverscaleThreshold)
                continue;

            (overscaled ??= []).Add(new OverscaleCell(cell.Name, factor));
        }

        if (overscaled is null)
            return new OverscaleReport { OverscaledCells = [], CellsInViewCount = inView };

        // Worst offender first; break ties by name so the pill doesn't flicker
        // between equal-factor cells as the view pans.
        overscaled.Sort(static (a, b) =>
        {
            var byFactor = b.Factor.CompareTo(a.Factor);
            return byFactor != 0 ? byFactor : string.CompareOrdinal(a.Name, b.Name);
        });

        return new OverscaleReport { OverscaledCells = overscaled, CellsInViewCount = inView };
    }
}
