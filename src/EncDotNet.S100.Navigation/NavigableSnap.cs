using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Navigation;

/// <summary>What a <see cref="NavigableSnap"/> looks for.</summary>
/// <param name="MinimumDepthMeters">The least charted depth a cell needs, in metres: the draft plus margin, or the safety contour.</param>
/// <param name="ClearanceMeters">
/// The room wanted between the point and anything that isn't navigable
/// (land, uncharted water, water shallower than the minimum depth, dangers),
/// in metres; 0 for none.
/// </param>
/// <param name="SearchRadiusMeters">How far to look for navigable water, in metres.</param>
/// <param name="HazardDepthMeters">Dangers at or shallower than this mark their cells; <c>null</c> uses the minimum depth.</param>
/// <param name="CellMeters">The grid's cell size in metres; <c>null</c> picks one from the radius (<see cref="NavigableSnap.CellMetersFor"/>).</param>
public sealed record NavigableSnapRequest(
    double MinimumDepthMeters,
    double ClearanceMeters = 0,
    double SearchRadiusMeters = NavigableSnap.DefaultSearchRadiusMeters,
    double? HazardDepthMeters = null,
    double? CellMeters = null);

/// <summary>What a <see cref="NavigableSnap"/> found.</summary>
public enum NavigableSnapStatus
{
    /// <summary>The point is navigable as it is.</summary>
    Navigable,

    /// <summary>The point isn't navigable; <see cref="NavigableSnapResult.Position"/> is the nearest that is.</summary>
    Moved,

    /// <summary>The point isn't navigable and nothing within the radius is.</summary>
    NotFound,
}

/// <summary>The result of a <see cref="NavigableSnap"/>.</summary>
/// <param name="Status">Whether the point was navigable, moved, or has no navigable water near.</param>
/// <param name="Reason">
/// Why the point isn't navigable: <c>land</c>, <c>uncharted</c>, <c>shoal</c>,
/// <c>hazard</c>, <c>no_depth</c> (charted water with no depth), or
/// <c>too_close</c> (navigable, but nearer than the clearance to something
/// that isn't); <c>null</c> when it is.
/// </param>
/// <param name="PointDepthMeters">The least charted depth of the point's cell, or <c>null</c>.</param>
/// <param name="Position">The navigable position: the point itself, or the centre of the nearest navigable cell; <c>null</c> when none was found.</param>
/// <param name="DepthMeters">The least charted depth of <paramref name="Position"/>'s cell, in metres.</param>
/// <param name="ClearanceMeters">The room round <paramref name="Position"/>'s cell, in metres (to the edge of the nearest cell that isn't navigable).</param>
/// <param name="DistanceMeters">How far the point moved, in metres; 0 when it didn't.</param>
/// <param name="BearingDegrees">The bearing it moved on, degrees true; 0 when it didn't.</param>
public sealed record NavigableSnapResult(
    NavigableSnapStatus Status,
    string? Reason,
    double? PointDepthMeters,
    GeoPosition? Position,
    double? DepthMeters,
    double? ClearanceMeters,
    double DistanceMeters,
    double BearingDegrees);

/// <summary>
/// The nearest navigable point (#1559): whether a point is navigable for a
/// depth and clearance, and if not, the nearest point that is. It searches a
/// <see cref="NavigabilityGrid"/>, so it agrees with the grid and with the
/// route planner built on it.
/// </summary>
/// <remarks>
/// A point moved to has a cell more room than asked for, so that a sample
/// or check there, on a grid whose cells don't line up with these, finds it
/// navigable too.
/// </remarks>
public static class NavigableSnap
{
    /// <summary>The default search radius: 1 km.</summary>
    public const double DefaultSearchRadiusMeters = 1_000;

    /// <summary>The largest search radius: 20 km.</summary>
    public const double MaxSearchRadiusMeters = 20_000;

    /// <summary>The cell size used for a radius: a hundredth of it, between 5 m and 100 m.</summary>
    /// <param name="searchRadiusMeters">The search radius in metres.</param>
    /// <returns>The cell size in metres.</returns>
    public static double CellMetersFor(double searchRadiusMeters) => Math.Clamp(Math.Round(searchRadiusMeters / 100), 5, 100);

    /// <summary>Builds a grid round a point and snaps the point on it.</summary>
    /// <param name="sources">The route-check sources: water mask, charted depth and charts.</param>
    /// <param name="position">The point.</param>
    /// <param name="request">The depth, clearance and radius.</param>
    /// <param name="cancellationToken">Cancels the grid build.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The radius isn't positive or exceeds <see cref="MaxSearchRadiusMeters"/>, or the clearance is negative.</exception>
    public static NavigableSnapResult Snap(
        RouteCheckSources sources,
        GeoPosition position,
        NavigableSnapRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(request);
        if (!(request.SearchRadiusMeters > 0) || request.SearchRadiusMeters > MaxSearchRadiusMeters)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.SearchRadiusMeters, $"The search radius must be more than 0 and at most {MaxSearchRadiusMeters} m.");
        }

        if (!(request.ClearanceMeters >= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.ClearanceMeters, "The clearance must be zero or more.");
        }

        var cell = request.CellMeters ?? CellMetersFor(request.SearchRadiusMeters);

        // The radius, plus the clearance so a cell at the edge sees what's round it.
        var half = request.SearchRadiusMeters + request.ClearanceMeters + (2 * cell);
        var grid = NavigabilityGrid.Build(
            sources,
            new NavigabilityGridRequest(Box(position, half), cell, request.MinimumDepthMeters, request.HazardDepthMeters),
            cancellationToken);
        return Snap(grid, position, request.ClearanceMeters, request.SearchRadiusMeters);
    }

    /// <summary>Snaps a point on a grid already built.</summary>
    /// <param name="grid">The grid.</param>
    /// <param name="position">The point.</param>
    /// <param name="clearanceMeters">The room wanted, in metres.</param>
    /// <param name="searchRadiusMeters">How far to look, in metres.</param>
    /// <param name="accept">Further test a cell it moves to must pass, or <c>null</c>.</param>
    /// <returns>The result.</returns>
    public static NavigableSnapResult Snap(
        NavigabilityGrid grid,
        GeoPosition position,
        double clearanceMeters,
        double searchRadiusMeters,
        Func<NavigabilityCell, bool>? accept = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        string reason;
        double? pointDepth = null;
        if (grid.CellAt(position) is { } here)
        {
            pointDepth = Round(grid.LeastDepthAt(here.Row, here.Column));
            if (grid.IsNavigable(here.Row, here.Column, clearanceMeters))
            {
                return new NavigableSnapResult(
                    NavigableSnapStatus.Navigable, null, pointDepth, position, pointDepth, Room(grid, here), 0, 0);
            }

            reason = grid.ClassAt(here.Row, here.Column) switch
            {
                NavigabilityCellClass.Navigable => "too_close",
                NavigabilityCellClass.Land => "land",
                NavigabilityCellClass.Shoal => "shoal",
                NavigabilityCellClass.Hazard => "hazard",
                NavigabilityCellClass.NoDepth => "no_depth",
                _ => "uncharted",
            };
        }
        else
        {
            reason = "uncharted";
        }

        // One more cell of room than asked for, so the cells round the one it
        // moves to are navigable too: a check on another grid, whose cells
        // don't line up with these, still finds the point navigable.
        if (grid.NearestNavigable(position, clearanceMeters + grid.CellMeters, searchRadiusMeters, accept) is not var (cell, distance))
        {
            return new NavigableSnapResult(NavigableSnapStatus.NotFound, reason, pointDepth, null, null, null, 0, 0);
        }

        var to = grid.CenterOf(cell);
        return new NavigableSnapResult(
            NavigableSnapStatus.Moved,
            reason,
            pointDepth,
            to,
            Round(grid.LeastDepthAt(cell.Row, cell.Column)),
            Room(grid, cell),
            Math.Round(distance, 1),
            Math.Round(InitialBearingDegrees(position, to), 1));
    }

    /// <summary>The initial great-circle bearing from one position to another, degrees true in [0, 360).</summary>
    /// <param name="from">The start.</param>
    /// <param name="to">The end.</param>
    /// <returns>The bearing.</returns>
    public static double InitialBearingDegrees(GeoPosition from, GeoPosition to)
    {
        var phi1 = from.Latitude * Math.PI / 180;
        var phi2 = to.Latitude * Math.PI / 180;
        var dLambda = (to.Longitude - from.Longitude) * Math.PI / 180;
        var y = Math.Sin(dLambda) * Math.Cos(phi2);
        var x = (Math.Cos(phi1) * Math.Sin(phi2)) - (Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(dLambda));
        var degrees = Math.Atan2(y, x) * 180 / Math.PI;
        return (degrees + 360) % 360;
    }

    /// <summary>A box of a half-width round a position.</summary>
    /// <param name="position">The centre.</param>
    /// <param name="halfMeters">The half-width in metres.</param>
    /// <returns>The box.</returns>
    public static BoundingBox Box(GeoPosition position, double halfMeters)
    {
        var dLat = halfMeters / PassageGeodesy.MetersPerDegreeLatitude;
        var dLon = dLat / Math.Max(0.01, Math.Cos(position.Latitude * Math.PI / 180));
        return new BoundingBox(
            Math.Max(-90, position.Latitude - dLat),
            Wrap(position.Longitude - dLon),
            Math.Min(90, position.Latitude + dLat),
            Wrap(position.Longitude + dLon));
    }

    private static double Room(NavigabilityGrid grid, NavigabilityCell cell) =>
        Math.Round(Math.Max(0, grid.ClearanceMeters(cell.Row, cell.Column) - (grid.CellMeters / 2)), 1);

    private static double? Round(double? depth) => depth is { } d ? Math.Round(d, 2) : null;

    private static double Wrap(double longitude) => ((longitude + 540) % 360) - 180;
}
