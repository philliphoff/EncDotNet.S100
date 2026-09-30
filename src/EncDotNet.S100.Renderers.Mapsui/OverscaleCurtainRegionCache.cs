using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A memoising <see cref="OverscaleCurtain.ComputeRegions"/> for a caller that
/// recomputes the curtain on every zoom step over a mostly unchanged set of
/// cells (issue #691).
/// </summary>
/// <remarks>
/// <para>
/// A cell's curtain region (its coverage minus every strictly-finer overlapping
/// coverage) and its compilation resolution depend only on the cells, never on
/// the viewport. Only the overscale factor, and so which cells get a region,
/// depends on the resolution. This cache keeps the resolution-independent part
/// per cell. A call with the same cells as the previous one re-evaluates the
/// factors (cheap) and reuses every region it has already computed, instead of
/// running the NetTopologySuite <c>Intersects</c> / <c>Difference</c> overlay for
/// every overscaled cell against every finer cell again.
/// </para>
/// <para>
/// "The same cells" means the same count, and at each position a cell with the
/// same <see cref="OverscaleCellInput.Coverage"/> instance, compilation-scale
/// denominator and name. Anything else discards the cache, so the result is
/// always what <see cref="OverscaleCurtain.ComputeRegions"/> returns for the
/// given cells and resolution. Regions are still computed lazily, the first
/// time a cell is overscaled, so a cell that never is costs nothing.
/// </para>
/// <para>
/// Not thread-safe: use one instance per caller (the Viewer calls it on the UI
/// thread).
/// </para>
/// </remarks>
public sealed class OverscaleCurtainRegionCache
{
    private Slot[] _slots = [];

    /// <summary>
    /// Computes the overscale-curtain regions for <paramref name="cells"/> at
    /// <paramref name="viewportResolution"/>, identical to
    /// <see cref="OverscaleCurtain.ComputeRegions"/>, reusing per-cell regions
    /// computed by earlier calls over the same cells.
    /// </summary>
    /// <param name="cells">The loaded, drawing, scale-bearing cells.</param>
    /// <param name="viewportResolution">
    /// The current viewport resolution in Web-Mercator metres per pixel (must be
    /// positive and finite; otherwise an empty list is returned).
    /// </param>
    /// <returns>
    /// The curtain regions (never <see langword="null"/>), ordered by descending
    /// overscale factor.
    /// </returns>
    public IReadOnlyList<OverscaleRegion> ComputeRegions(
        IReadOnlyList<OverscaleCellInput> cells,
        double viewportResolution)
    {
        ArgumentNullException.ThrowIfNull(cells);

        if (double.IsNaN(viewportResolution) || viewportResolution <= 0)
            return [];

        if (!Matches(cells))
            Reset(cells);

        List<OverscaleRegion>? regions = null;
        for (var i = 0; i < _slots.Length; i++)
        {
            ref var slot = ref _slots[i];
            if (!slot.Usable)
                continue;

            var factor = slot.CompilationResolution / viewportResolution;
            if (factor <= OverscaleEvaluator.OverscaleThreshold)
                continue;

            if (!slot.RegionComputed)
            {
                var region = OverscaleCurtain.SubtractFinerCoverages(cells[i], slot.Coverage!, cells);
                slot.Region = region is { IsEmpty: false } ? region : null;
                slot.RegionComputed = true;
            }

            if (slot.Region is not null)
                (regions ??= []).Add(new OverscaleRegion(slot.Name, factor, slot.Region));
        }

        return OverscaleCurtain.Sorted(regions);
    }

    private bool Matches(IReadOnlyList<OverscaleCellInput> cells)
    {
        if (cells.Count != _slots.Length)
            return false;

        for (var i = 0; i < _slots.Length; i++)
        {
            var cell = cells[i];
            ref readonly var slot = ref _slots[i];
            if (!ReferenceEquals(cell.Coverage, slot.Coverage)
                || cell.CompilationScaleDenominator != slot.Denominator
                || !string.Equals(cell.Name, slot.Name, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void Reset(IReadOnlyList<OverscaleCellInput> cells)
    {
        var slots = new Slot[cells.Count];
        for (var i = 0; i < slots.Length; i++)
        {
            var cell = cells[i];
            slots[i] = new Slot
            {
                Name = cell.Name,
                Coverage = cell.Coverage,
                Denominator = cell.CompilationScaleDenominator,
                Usable = OverscaleCurtain.TryGetCompilationResolution(
                    cell, out _, out var compilationResolution),
                CompilationResolution = compilationResolution,
            };
        }

        _slots = slots;
    }

    private struct Slot
    {
        public string Name;
        public Geometry? Coverage;
        public int Denominator;
        public bool Usable;
        public double CompilationResolution;
        public bool RegionComputed;
        public Geometry? Region;
    }
}
