using System;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.Viewer.Services.LazyLoading;

/// <summary>
/// Pure decision logic for viewport-driven lazy loading of exchange-set cells:
/// given a cell's geographic footprint and navigational-purpose band, decides
/// whether the cell is relevant at the current viewport and scale. Kept free
/// of Mapsui / view-model types so it is exhaustively unit-testable.
/// </summary>
/// <remarks>
/// A cell should be loaded when it BOTH intersects the visible viewport AND
/// its usage band is appropriate for the current display scale. The band
/// gate is what prevents the pathological case that motivated this feature:
/// loading thousands of large-scale (harbour / berthing) cells while zoomed
/// out over an entire region. Band-to-scale thresholds are heuristic (ECDIS
/// navigational-purpose scale ranges, widened slightly so cells appear just
/// before they are strictly needed) and centralised here for tuning.
/// </remarks>
internal static class LazyCellGate
{
    /// <summary>
    /// Coarsest scale denominator (1:N) at which a cell of each
    /// navigational-purpose band starts being loaded. A band is eligible when
    /// the current scale denominator is at or below its threshold — i.e. the
    /// view is zoomed in at least far enough for that band to be relevant.
    /// Index by band (1..6). Overview (band&#160;1) is always eligible.
    /// </summary>
    /// <remarks>
    /// Derived from the conventional ENC navigational-purpose scale bands
    /// (Overview / General / Coastal / Approach / Harbour / Berthing),
    /// widened ~2× so cells load slightly ahead of the strict boundary and the
    /// chart never shows a blank band mid-zoom. Heuristic and tunable.
    /// </remarks>
    private static readonly double[] BandMaxScaleDenominator =
    {
        double.PositiveInfinity, // index 0 unused / unknown-band fallback
        double.PositiveInfinity, // 1 Overview  — always eligible
        3_000_000,               // 2 General
        700_000,                 // 3 Coastal
        180_000,                 // 4 Approach
        45_000,                  // 5 Harbour
        10_000,                  // 6 Berthing
    };

    /// <summary>
    /// True when a cell of navigational-purpose <paramref name="band"/> is
    /// appropriate to load at the given <paramref name="scaleDenominator"/>
    /// (1:N). An unknown band (<see langword="null"/>) or an unavailable scale
    /// (non-finite or ≤ 0) fails <em>open</em> (eligible) so no cell is hidden
    /// by an unparseable name or a not-yet-laid-out viewport; the bounded load
    /// gate and LRU budget still cap the working set.
    /// </summary>
    public static bool IsBandEligible(int? band, double scaleDenominator)
    {
        if (band is not { } b || b < CellUsageBand.MinBand || b > CellUsageBand.MaxBand)
            return true;
        if (double.IsNaN(scaleDenominator) || scaleDenominator <= 0)
            return true;

        return scaleDenominator <= BandMaxScaleDenominator[b];
    }

    /// <summary>
    /// True when <paramref name="cell"/> overlaps the viewport rectangle
    /// (all values in EPSG:4326 decimal degrees). A touching edge counts as
    /// an overlap. A <see langword="null"/> footprint (container-style cells
    /// with no coverage) is treated as always intersecting so it is never
    /// culled by geography. Either box may cross the ±180° antimeridian seam
    /// (west &gt; east); such ranges are split into two non-wrapping segments
    /// (<c>[west, +180]</c> ∪ <c>[-180, east]</c>) before the longitude test
    /// so seam-crossing cells and viewports still match near the dateline.
    /// </summary>
    public static bool IntersectsViewport(
        BoundingBox? cell,
        double viewSouth,
        double viewWest,
        double viewNorth,
        double viewEast)
    {
        if (cell is null)
            return true;

        // Latitude never wraps: disjoint when one box is wholly north or
        // south of the other.
        if (cell.NorthBoundLatitude < viewSouth || cell.SouthBoundLatitude > viewNorth)
            return false;

        // Longitude can wrap the ±180° seam, so overlap is tested per
        // non-wrapping segment pair rather than with a single interval test.
        return LongitudesOverlap(
            cell.WestBoundLongitude, cell.EastBoundLongitude, viewWest, viewEast);
    }

    /// <summary>
    /// True when two longitude ranges overlap, treating a range whose
    /// <paramref name="aWest"/> exceeds its <paramref name="aEast"/> (likewise
    /// for <paramref name="bWest"/>/<paramref name="bEast"/>) as crossing the
    /// ±180° antimeridian seam. Each range is decomposed into up to two
    /// non-wrapping segments and every segment pair is tested for a touching
    /// or overlapping interval.
    /// </summary>
    private static bool LongitudesOverlap(
        double aWest, double aEast, double bWest, double bEast)
    {
        foreach (var (aLo, aHi) in SplitLongitude(aWest, aEast))
            foreach (var (bLo, bHi) in SplitLongitude(bWest, bEast))
                if (aLo <= bHi && bLo <= aHi)
                    return true;

        return false;
    }

    /// <summary>
    /// Splits a possibly seam-crossing longitude range into one or two
    /// non-wrapping <c>[low, high]</c> segments. A range with
    /// <paramref name="west"/> &gt; <paramref name="east"/> yields
    /// <c>[west, +180]</c> and <c>[-180, east]</c>.
    /// </summary>
    private static IEnumerable<(double Low, double High)> SplitLongitude(
        double west, double east)
    {
        if (west <= east)
        {
            yield return (west, east);
        }
        else
        {
            yield return (west, 180.0);
            yield return (-180.0, east);
        }
    }

    /// <summary>
    /// True when a cell with the given footprint and band should be loaded for
    /// a viewport whose bounds and scale are supplied — the conjunction of
    /// <see cref="IntersectsViewport"/> and <see cref="IsBandEligible"/>.
    /// </summary>
    public static bool ShouldBeLoaded(
        BoundingBox? cell,
        int? band,
        double scaleDenominator,
        double viewSouth,
        double viewWest,
        double viewNorth,
        double viewEast) =>
        IntersectsViewport(cell, viewSouth, viewWest, viewNorth, viewEast)
        && IsBandEligible(band, scaleDenominator);

    /// <summary>
    /// Converts an EPSG:3857 (web-mercator) resolution in metres/pixel to an
    /// approximate map-scale denominator (1:N) at the given latitude, applying
    /// the web-mercator cosine distortion correction and the OGC 0.28&#160;mm
    /// standardized pixel size (mirrors <see cref="MapScaleFormatter"/>).
    /// Returns <see cref="double.NaN"/> for a non-positive resolution.
    /// </summary>
    public static double ScaleDenominator(double mercatorResolution, double latitudeDegrees)
    {
        if (double.IsNaN(mercatorResolution) || mercatorResolution <= 0)
            return double.NaN;

        var groundMetersPerPixel =
            mercatorResolution * Math.Cos(latitudeDegrees * Math.PI / 180.0);
        if (groundMetersPerPixel <= 0 || double.IsNaN(groundMetersPerPixel))
            return double.NaN;

        return groundMetersPerPixel / MapScaleFormatter.PixelSizeMeters;
    }
}
