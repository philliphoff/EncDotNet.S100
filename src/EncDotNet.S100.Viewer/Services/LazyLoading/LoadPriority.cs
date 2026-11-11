using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Rendering.Engine.Prediction;

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
    /// The cell is in view at a known destination: a fling's resting viewport
    /// or an animation's target (map rendering slice&#160;9). It loads as soon
    /// as the gesture starts, as predicted work, and may use every load slot,
    /// like a visible cell.
    /// </summary>
    Destination = 1,

    /// <summary>
    /// The cell lies just outside the viewport, within the ahead margin, or in
    /// a view the demand planner predicts a few seconds ahead (along the pan
    /// velocity and zoom rate, or along the route). Loaded speculatively so a
    /// short pan finds it ready, like the tile renderer's predicted tiles.
    /// </summary>
    Ahead = 2,

    /// <summary>
    /// The cell is in view, but finer cells that draw at this scale cover its
    /// part of the view (#1265). Overlap suppression would hide almost all of
    /// it, so it loads speculatively after the visible and ahead cells instead
    /// of competing with them; once loaded it fills any hairline gaps between
    /// the finer cells.
    /// </summary>
    Covered = 3,

    /// <summary>
    /// The cell is in a place the view rested at recently, or the start view
    /// (map rendering slice&#160;9). Loaded at low priority, so jumping back is
    /// warm.
    /// </summary>
    History = 4,

    /// <summary>
    /// The cell has no known footprint, so it may or may not be in view. Loaded
    /// last, like the tile renderer's idle pre-warm, so it never holds back a
    /// cell that is known to be in view.
    /// </summary>
    Unbounded = 5,
}

/// <summary>
/// A view the demand planner predicts (map rendering slice&#160;9), in the
/// terms cells are ranked by: its footprint in EPSG:4326 decimal degrees and
/// the scale it would be shown at.
/// </summary>
/// <param name="South">South edge.</param>
/// <param name="West">West edge; greater than <paramref name="East"/> when the view crosses ±180°.</param>
/// <param name="North">North edge.</param>
/// <param name="East">East edge.</param>
/// <param name="CenterLatitude">The view centre's latitude.</param>
/// <param name="CenterLongitude">The view centre's longitude.</param>
/// <param name="ScaleDenominator">The scale denominator the view would be shown at.</param>
/// <param name="Resolution">The view's EPSG:3857 resolution, metres per pixel.</param>
/// <param name="Probability">How likely the view is.</param>
/// <param name="Source">The signal the prediction came from.</param>
internal sealed record PredictedWindow(
    double South,
    double West,
    double North,
    double East,
    double CenterLatitude,
    double CenterLongitude,
    double ScaleDenominator,
    double Resolution,
    double Probability,
    PredictionSource Source)
{
    /// <summary>The cell tier a cell in this window gets.</summary>
    public LoadTier Tier => Source switch
    {
        PredictionSource.Destination => LoadTier.Destination,
        PredictionSource.History => LoadTier.History,
        _ => LoadTier.Ahead,
    };

    /// <summary>Projects a predicted view to geographic terms.</summary>
    /// <param name="predicted">The predicted view.</param>
    /// <returns>The window, or <see langword="null"/> for a view with no size.</returns>
    public static PredictedWindow? From(PredictedView predicted)
    {
        var view = predicted.View;
        if (!view.HasSize || !(view.Resolution > 0))
            return null;

        var extent = view.ToExtent();
        var (west, south) = ToLonLat(extent.MinX, extent.MinY);
        var (east, north) = ToLonLat(extent.MaxX, extent.MaxY);
        var (centerLon, centerLat) = ToLonLat(view.CenterX, view.CenterY);
        return new PredictedWindow(
            south,
            NormalizeLongitude(west),
            north,
            NormalizeLongitude(east),
            centerLat,
            NormalizeLongitude(centerLon),
            LazyCellGate.ScaleDenominator(view.Resolution, centerLat),
            view.Resolution,
            predicted.Probability,
            predicted.Source);
    }

    /// <summary>The window as a <see cref="LoadWindow"/> with no ahead margin, for the coverage test.</summary>
    public LoadWindow ToLoadWindow() => new()
    {
        South = South,
        West = West,
        North = North,
        East = East,
        AheadSouth = South,
        AheadWest = West,
        AheadNorth = North,
        AheadEast = East,
        CenterLatitude = CenterLatitude,
        CenterLongitude = CenterLongitude,
        LeadLatitude = CenterLatitude,
        LeadLongitude = CenterLongitude,
    };

    // EPSG:3857 to WGS-84, as Mapsui's SphericalMercator.ToLonLat.
    private static (double Longitude, double Latitude) ToLonLat(double x, double y)
    {
        const double radius = 6378137.0;
        var longitude = x / radius * (180.0 / Math.PI);
        var latitude = (2.0 * Math.Atan(Math.Exp(y / radius)) - (Math.PI / 2.0)) * (180.0 / Math.PI);
        return (longitude, Math.Clamp(latitude, -90.0, 90.0));
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
    /// The strongest predicted window <paramref name="cell"/> falls in, as a
    /// tier and the window, for a cell the current view doesn't rank (map
    /// rendering slice&#160;9). Each window applies its own scale, so a cell
    /// that only draws at an animated zoom's target scale ranks for it.
    /// </summary>
    /// <param name="cell">The cell's footprint.</param>
    /// <param name="band">The cell's S-57 usage band, when it has one.</param>
    /// <param name="minimumDisplayScale">The cell's coarsest display scale, when it declares one.</param>
    /// <param name="windows">The predicted windows, strongest first.</param>
    /// <returns>The tier and window, or <see langword="null"/>.</returns>
    public static (LoadTier Tier, PredictedWindow Window)? ClassifyPredicted(
        BoundingBox? cell,
        int? band,
        int? minimumDisplayScale,
        IReadOnlyList<PredictedWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (cell is null)
            return null;

        (LoadTier Tier, PredictedWindow Window)? best = null;
        foreach (var window in windows)
        {
            if (best is { } b && (window.Tier > b.Tier || (window.Tier == b.Tier && window.Probability <= b.Window.Probability)))
                continue;
            if (!LazyCellGate.IsScaleEligible(band, minimumDisplayScale, window.ScaleDenominator))
                continue;
            if (!LazyCellGate.IntersectsViewport(cell, window.South, window.West, window.North, window.East))
                continue;
            best = (window.Tier, window);
        }

        return best;
    }

    /// <summary>
    /// The rank of a cell within a predicted window: its distance from the
    /// window's centre, divided by the window's probability, so the likeliest,
    /// nearest views fill first.
    /// </summary>
    /// <param name="cell">The cell's footprint.</param>
    /// <param name="window">The window.</param>
    /// <returns>An approximate distance in degrees.</returns>
    public static double RankPredicted(BoundingBox? cell, PredictedWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (cell is null)
            return 0;

        return Distance(cell, window.CenterLatitude, window.CenterLongitude) / Math.Max(window.Probability, 0.01);
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
        return Distance(cell, lat, lon);
    }

    private static double Distance(BoundingBox cell, double lat, double lon)
    {
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
