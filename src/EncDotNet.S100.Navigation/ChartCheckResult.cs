using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation;

/// <summary>The corridor the check swept along one leg.</summary>
/// <param name="LegIndex">The leg's index in the route.</param>
/// <param name="StartDistanceMeters">Distance from departure to the leg's start.</param>
/// <param name="EndDistanceMeters">Distance from departure to the leg's end.</param>
/// <param name="PortHalfWidthMeters">How far the corridor reaches to port of the track.</param>
/// <param name="StarboardHalfWidthMeters">How far the corridor reaches to starboard of the track.</param>
/// <param name="Ring">The corridor's closed ring, in WGS-84: port side forward, then starboard side back.</param>
public sealed record RouteCheckCorridor(
    int LegIndex,
    double StartDistanceMeters,
    double EndDistanceMeters,
    double PortHalfWidthMeters,
    double StarboardHalfWidthMeters,
    IReadOnlyList<GeoPosition> Ring);

/// <summary>
/// What <see cref="ChartCheck"/> found besides its findings: the corridor it
/// swept along each leg.
/// </summary>
/// <param name="Corridors">The corridor of each leg, in route order.</param>
public sealed record ChartCheckResult(IReadOnlyList<RouteCheckCorridor> Corridors)
{
    /// <summary>The route's length in metres.</summary>
    public double LengthMeters => Corridors.Count == 0 ? 0 : Corridors[^1].EndDistanceMeters;
}
