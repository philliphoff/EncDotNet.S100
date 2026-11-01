using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.Viewer.Services.LazyLoading;

/// <summary>
/// How urgently a deferred cell should load, best first. The tiers mirror the
/// tile renderer's queue (<c>S100VectorTileRenderer</c>): visible work first,
/// then the work a pan is about to need, then idle-time work.
/// </summary>
internal enum LoadTier
{
    /// <summary>The cell's footprint overlaps the viewport.</summary>
    Visible = 0,

    /// <summary>
    /// The cell lies just outside the viewport, within the ahead margin. Loaded
    /// speculatively so a short pan finds it ready, like the tile renderer's
    /// predicted tiles.
    /// </summary>
    Ahead = 1,

    /// <summary>
    /// The cell has no known footprint, so it may or may not be in view. Loaded
    /// last, like the tile renderer's idle pre-warm, so it never holds back a
    /// cell that is known to be in view.
    /// </summary>
    Unbounded = 2,
}

/// <summary>
/// The geographic windows one evaluation ranks cells against: the viewport,
/// the wider ahead window around it, and the point that ahead cells are ranked
/// by. All values are EPSG:4326 decimal degrees.
/// </summary>
internal sealed record LoadWindow
{
    /// <summary>The viewport's south edge.</summary>
    public required double South { get; init; }

    /// <summary>The viewport's west edge.</summary>
    public required double West { get; init; }

    /// <summary>The viewport's north edge.</summary>
    public required double North { get; init; }

    /// <summary>The viewport's east edge.</summary>
    public required double East { get; init; }

    /// <summary>The ahead window's south edge.</summary>
    public required double AheadSouth { get; init; }

    /// <summary>The ahead window's west edge; greater than its east edge when it crosses ±180°.</summary>
    public required double AheadWest { get; init; }

    /// <summary>The ahead window's north edge.</summary>
    public required double AheadNorth { get; init; }

    /// <summary>The ahead window's east edge.</summary>
    public required double AheadEast { get; init; }

    /// <summary>The viewport centre's latitude.</summary>
    public required double CenterLatitude { get; init; }

    /// <summary>The viewport centre's longitude.</summary>
    public required double CenterLongitude { get; init; }

    /// <summary>
    /// The latitude ahead cells are ranked by: the viewport centre moved on by
    /// the last pan, so cells in the direction of travel come first.
    /// </summary>
    public required double LeadLatitude { get; init; }

    /// <summary>The longitude ahead cells are ranked by.</summary>
    public required double LeadLongitude { get; init; }

    /// <summary>
    /// Builds the windows for <paramref name="current"/>. The ahead window grows
    /// the viewport by <paramref name="aheadMarginFraction"/> of its span on each
    /// side. When <paramref name="previous"/> is supplied, the lead point is the
    /// current centre moved on by the change from the previous centre.
    /// </summary>
    /// <param name="current">The viewport being evaluated.</param>
    /// <param name="previous">The previously evaluated viewport, if any.</param>
    /// <param name="aheadMarginFraction">The ahead margin as a fraction of the viewport span.</param>
    /// <returns>The windows.</returns>
    public static LoadWindow From(
        MapViewportSnapshot current,
        MapViewportSnapshot? previous,
        double aheadMarginFraction)
    {
        ArgumentNullException.ThrowIfNull(current);

        var centerLat = (current.MinLatitude + current.MaxLatitude) / 2.0;
        var centerLon = NormalizeLongitude((current.MinLongitude + current.MaxLongitude) / 2.0);
        var leadLat = centerLat;
        var leadLon = centerLon;
        if (previous is not null)
        {
            var previousLat = (previous.MinLatitude + previous.MaxLatitude) / 2.0;
            var previousLon = NormalizeLongitude((previous.MinLongitude + previous.MaxLongitude) / 2.0);
            leadLat = Math.Clamp(centerLat + (centerLat - previousLat), -90.0, 90.0);
            leadLon = NormalizeLongitude(centerLon + NormalizeLongitude(centerLon - previousLon));
        }

        var margin = Math.Max(0.0, aheadMarginFraction);
        var latMargin = (current.MaxLatitude - current.MinLatitude) * margin;
        var lonSpan = current.MaxLongitude - current.MinLongitude;
        if (lonSpan < 0)
            lonSpan += 360.0;
        var lonMargin = lonSpan * margin;

        double aheadWest;
        double aheadEast;
        if (lonSpan + 2 * lonMargin >= 360.0)
        {
            aheadWest = -180.0;
            aheadEast = 180.0;
        }
        else
        {
            // Normalised edges may cross ±180° (west > east), which the
            // intersection test splits into two ranges.
            aheadWest = NormalizeLongitude(current.MinLongitude - lonMargin);
            aheadEast = NormalizeLongitude(current.MaxLongitude + lonMargin);
        }

        return new LoadWindow
        {
            South = current.MinLatitude,
            West = current.MinLongitude,
            North = current.MaxLatitude,
            East = current.MaxLongitude,
            AheadSouth = Math.Max(-90.0, current.MinLatitude - latMargin),
            AheadWest = aheadWest,
            AheadNorth = Math.Min(90.0, current.MaxLatitude + latMargin),
            AheadEast = aheadEast,
            CenterLatitude = centerLat,
            CenterLongitude = centerLon,
            LeadLatitude = leadLat,
            LeadLongitude = leadLon,
        };
    }

    private static double NormalizeLongitude(double longitude)
    {
        var normalized = (longitude + 180.0) % 360.0;
        if (normalized < 0)
            normalized += 360.0;
        return normalized - 180.0;
    }
}

/// <summary>
/// Pure ranking rules for deferred cells: which <see cref="LoadTier"/> a cell
/// falls in for a viewport, and its rank within that tier.
/// </summary>
internal static class LoadPrioritizer
{
    /// <summary>
    /// The tier <paramref name="cell"/> falls in, or <see langword="null"/>
    /// when it should not load for this viewport: outside the ahead window, or
    /// not eligible at the current scale (<see cref="LazyCellGate.IsScaleEligible"/>).
    /// </summary>
    /// <param name="cell">The cell's footprint, or <see langword="null"/> when unknown.</param>
    /// <param name="band">The cell's S-57 usage band, when it has one.</param>
    /// <param name="minimumDisplayScale">The cell's coarsest display scale, when it declares one.</param>
    /// <param name="scaleDenominator">The viewport's scale denominator.</param>
    /// <param name="window">The windows to rank against.</param>
    /// <returns>The tier, or <see langword="null"/>.</returns>
    public static LoadTier? Classify(
        BoundingBox? cell,
        int? band,
        int? minimumDisplayScale,
        double scaleDenominator,
        LoadWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!LazyCellGate.IsScaleEligible(band, minimumDisplayScale, scaleDenominator))
            return null;
        if (cell is null)
            return LoadTier.Unbounded;
        if (LazyCellGate.IntersectsViewport(cell, window.South, window.West, window.North, window.East))
            return LoadTier.Visible;
        if (LazyCellGate.IntersectsViewport(
                cell, window.AheadSouth, window.AheadWest, window.AheadNorth, window.AheadEast))
        {
            return LoadTier.Ahead;
        }
        return null;
    }

    /// <summary>
    /// The rank of a cell within its tier; lower loads first. Visible cells
    /// rank by distance from the viewport centre, so the middle of the screen
    /// fills first; ahead cells rank by distance from the lead point, so the
    /// direction of travel fills first. Unbounded cells all rank equal.
    /// </summary>
    /// <param name="cell">The cell's footprint, or <see langword="null"/> when unknown.</param>
    /// <param name="tier">The tier the cell fell in.</param>
    /// <param name="window">The windows it was ranked against.</param>
    /// <returns>An approximate distance in degrees.</returns>
    public static double Rank(BoundingBox? cell, LoadTier tier, LoadWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (cell is null || tier == LoadTier.Unbounded)
            return 0;

        var (lat, lon) = tier == LoadTier.Ahead
            ? (window.LeadLatitude, window.LeadLongitude)
            : (window.CenterLatitude, window.CenterLongitude);

        var cellLat = (cell.SouthBoundLatitude + cell.NorthBoundLatitude) / 2.0;
        var cellEast = cell.EastBoundLongitude < cell.WestBoundLongitude
            ? cell.EastBoundLongitude + 360.0
            : cell.EastBoundLongitude;
        var cellLon = (cell.WestBoundLongitude + cellEast) / 2.0;

        var dLat = cellLat - lat;
        var dLon = Math.Abs(cellLon - lon) % 360.0;
        if (dLon > 180.0)
            dLon = 360.0 - dLon;
        dLon *= Math.Cos(lat * Math.PI / 180.0);
        return Math.Sqrt(dLat * dLat + dLon * dLon);
    }
}
