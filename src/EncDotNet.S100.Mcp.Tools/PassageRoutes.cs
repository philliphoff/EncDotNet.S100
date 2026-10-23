using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// A route the host can hand to <see cref="CheckRouteTool"/>, with the
/// values it has for each leg (any of which the request may override).
/// </summary>
/// <param name="RouteId">The route id.</param>
/// <param name="Name">The route name.</param>
/// <param name="Legs">The legs in order.</param>
/// <param name="DefaultDeparture">Departure to use when the request has none (the route's departure, or the view time).</param>
public sealed record PassageRouteSnapshot(
    string RouteId,
    string? Name,
    IReadOnlyList<PassageRouteLeg> Legs,
    DateTimeOffset DefaultDeparture);

/// <summary>One leg of a <see cref="PassageRouteSnapshot"/>, with the effective values the host knows.</summary>
/// <param name="Points">The leg's points (at least two).</param>
/// <param name="Geometry">Rhumb line or great circle.</param>
/// <param name="DraftMeters">Draft, or <c>null</c>.</param>
/// <param name="SpeedKnots">Speed over ground, or <c>null</c>.</param>
/// <param name="SquatMeters">Squat allowance, or <c>null</c>.</param>
/// <param name="RequiredUkcMeters">Required UKC, or <c>null</c>.</param>
/// <param name="Name">Display name, such as "Leg 6 → 7".</param>
/// <param name="PortCrossTrackMeters">The port cross-track limit in metres (a half-width: track to edge), or <c>null</c>; <c>check_route</c> sweeps the corridor.</param>
/// <param name="StarboardCrossTrackMeters">The starboard cross-track limit in metres, or <c>null</c>.</param>
public sealed record PassageRouteLeg(
    IReadOnlyList<GeoPosition> Points,
    LegGeometry Geometry,
    double? DraftMeters,
    double? SpeedKnots,
    double? SquatMeters,
    double? RequiredUkcMeters,
    string? Name,
    double? PortCrossTrackMeters = null,
    double? StarboardCrossTrackMeters = null)
{
    /// <summary>The leg as the route checks take it, with the host's values and any overrides.</summary>
    /// <param name="draftMeters">A draft that replaces the leg's, or <c>null</c> to keep it.</param>
    /// <param name="speedKnots">A speed that replaces the leg's, or <c>null</c> to keep it.</param>
    /// <param name="squatMeters">A squat allowance that replaces the leg's, or <c>null</c> to keep it.</param>
    /// <param name="requiredUkcMeters">A required UKC that replaces the leg's, or <c>null</c> to keep it.</param>
    /// <returns>The leg.</returns>
    public PassageLeg ToPassageLeg(double? draftMeters = null, double? speedKnots = null, double? squatMeters = null, double? requiredUkcMeters = null) => new()
    {
        Points = Points,
        Geometry = Geometry,
        PortLimit = PortCrossTrackMeters is { } port ? Length.FromMetres(port) : null,
        StarboardLimit = StarboardCrossTrackMeters is { } starboard ? Length.FromMetres(starboard) : null,
        Speed = (speedKnots ?? SpeedKnots) is { } knots ? Speed.FromKnots(knots) : null,
        Draft = (draftMeters ?? DraftMeters) is { } draft ? Depth.FromMetres(draft) : null,
        Squat = Depth.FromMetres(squatMeters ?? SquatMeters ?? 0),
        RequiredUkc = (requiredUkcMeters ?? RequiredUkcMeters) is { } ukc ? Depth.FromMetres(ukc) : null,
        Name = Name,
    };
}

/// <summary>Supplies host routes (such as the viewer's) to <see cref="CheckRouteTool"/>.</summary>
public interface IPassageRouteProvider
{
    /// <summary>Snapshots a route, or returns the host's error (such as route not found).</summary>
    /// <param name="routeId">The route id, or <c>null</c> for the active route.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The route, or an error.</returns>
    Task<ToolResult<PassageRouteSnapshot>> GetRouteAsync(string? routeId, CancellationToken cancellationToken);
}
