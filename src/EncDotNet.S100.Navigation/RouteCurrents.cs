using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation;

/// <summary>Where a current comes from.</summary>
public enum RouteCurrentOrigin
{
    /// <summary>An S-111 surface current dataset.</summary>
    S111,

    /// <summary>A harmonic prediction at a current station: the astronomical tidal current only.</summary>
    Prediction,
}

/// <summary>A current at one place and time.</summary>
/// <param name="SpeedKnots">The speed, in knots.</param>
/// <param name="SetDegrees">The direction it sets toward, in degrees true.</param>
public readonly record struct RouteCurrent(double SpeedKnots, double SetDegrees)
{
    /// <summary>
    /// The part of the current along a course, in knots: positive with the
    /// course (fair), negative against it (foul).
    /// </summary>
    /// <param name="courseDegrees">The course, in degrees true.</param>
    /// <returns>The along-course component.</returns>
    public double AlongKnots(double courseDegrees) =>
        SpeedKnots * Math.Cos((SetDegrees - courseDegrees) * Math.PI / 180);
}

/// <summary>What a <see cref="IRouteCurrentSeries"/> reads.</summary>
/// <param name="Id">The dataset or station id.</param>
/// <param name="Name">A name to show, or <c>null</c>.</param>
/// <param name="Origin">S-111 or a station prediction.</param>
/// <param name="DistanceMeters">For a station, how far it is from the place it was located for, or <c>null</c>.</param>
public sealed record RouteCurrentSourceInfo(string Id, string? Name, RouteCurrentOrigin Origin, double? DistanceMeters);

/// <summary>The current at one place over time.</summary>
public interface IRouteCurrentSeries
{
    /// <summary>What the series reads.</summary>
    RouteCurrentSourceInfo Source { get; }

    /// <summary>The current at a time, or <c>null</c> where the series has no value then.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The current, or <c>null</c>.</returns>
    RouteCurrent? At(DateTimeOffset time);

    /// <summary>
    /// The current at a time with what it was read from, which can differ
    /// from <see cref="Source"/> when the series falls back to another
    /// source at some times (such as a prediction past an S-111 forecast).
    /// </summary>
    /// <param name="time">The time.</param>
    /// <returns>The reading, or <c>null</c>.</returns>
    RouteCurrentReading? Read(DateTimeOffset time) => At(time) is { } current ? new RouteCurrentReading(current, Source) : null;
}

/// <summary>A current read at a time, and what it was read from.</summary>
/// <param name="Current">The current.</param>
/// <param name="Source">The dataset or station it was read from.</param>
public sealed record RouteCurrentReading(RouteCurrent Current, RouteCurrentSourceInfo Source);

/// <summary>A current station: a series at a fixed place that also knows its slack waters.</summary>
public interface IRouteCurrentStation : IRouteCurrentSeries
{
    /// <summary>The station's position.</summary>
    GeoPosition Position { get; }

    /// <summary>The slack waters between two times, in time order.</summary>
    /// <param name="from">The start.</param>
    /// <param name="to">The end; not before <paramref name="from"/>.</param>
    /// <returns>The slack times.</returns>
    IReadOnlyList<DateTimeOffset> SlacksBetween(DateTimeOffset from, DateTimeOffset to);
}

/// <summary>
/// Supplies the currents a <see cref="TimingCheck"/> reads along a route
/// (#1033, #1561). <c>EncDotNet.S100.Datasets.Pipelines</c> implements it
/// over current-station predictions.
/// </summary>
/// <remarks>Implementations must be safe to call from several threads at once.</remarks>
public interface IRouteCurrentSource
{
    /// <summary>The current series at a position, or <c>null</c> where nothing covers it.</summary>
    /// <param name="position">The position.</param>
    /// <returns>The series, or <c>null</c>.</returns>
    IRouteCurrentSeries? Locate(GeoPosition position);

    /// <summary>A current station by its id or name, or <c>null</c> when none (or more than one) matches.</summary>
    /// <param name="idOrName">The id, or the name (exact, else a single partial match).</param>
    /// <returns>The station, or <c>null</c>.</returns>
    IRouteCurrentStation? FindStation(string idOrName);
}
