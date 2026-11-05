using System.Globalization;

namespace EncDotNet.S100.PerfRunner;

/// <summary>A place the settle route jumps to.</summary>
/// <param name="Name">The label in the manifest.</param>
/// <param name="Latitude">WGS-84 latitude of the view centre.</param>
/// <param name="Longitude">WGS-84 longitude of the view centre.</param>
internal readonly record struct SettleWaypoint(string Name, double Latitude, double Longitude);

/// <summary>
/// The routes of the settle harness (map rendering slice 1): the jumps
/// Seattle → Tacoma → Port Townsend at one scale, and a pan that crosses
/// cell boundaries.
/// </summary>
internal static class SettleRoute
{
    /// <summary>The scale the design's measurements use: 1:44 300.</summary>
    public const double DefaultScaleDenominator = 44_300;

    /// <summary>
    /// The jumps of <c>docs/design/map-render-architecture.md</c>: Seattle's
    /// Elliott Bay, Tacoma's Commencement Bay, then Port Townsend.
    /// </summary>
    public static IReadOnlyList<SettleWaypoint> DefaultJumps { get; } =
    [
        new("Seattle", 47.6050, -122.3550),
        new("Tacoma", 47.2750, -122.4300),
        new("Port Townsend", 48.1150, -122.7550),
    ];

    /// <summary>
    /// The default pan: west across Elliott Bay from Seattle towards Bainbridge
    /// Island, which crosses harbour and approach cell boundaries at 1:44 300.
    /// </summary>
    public static (SettleWaypoint From, SettleWaypoint To) DefaultPan { get; } =
        (new("Seattle", 47.6050, -122.3550), new("Bainbridge Island", 47.6200, -122.5200));

    /// <summary>
    /// Parses <c>Name=lat,lon;Name=lat,lon</c>. A name may be left out
    /// (<c>lat,lon</c>), in which case the stop is named by its position.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <param name="waypoints">The waypoints.</param>
    /// <returns>Whether every stop parsed and lies within WGS-84.</returns>
    public static bool TryParseWaypoints(string? value, out IReadOnlyList<SettleWaypoint> waypoints)
    {
        waypoints = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var list = new List<SettleWaypoint>();
        foreach (var stop in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = default(string);
            var position = stop;
            var equals = stop.IndexOf('=', StringComparison.Ordinal);
            if (equals >= 0)
            {
                name = stop[..equals].Trim();
                position = stop[(equals + 1)..];
            }

            var parts = position.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
                || !double.IsFinite(lat) || !double.IsFinite(lon)
                || lat is < -90 or > 90 || lon is < -180 or > 180)
            {
                return false;
            }

            list.Add(new SettleWaypoint(
                string.IsNullOrEmpty(name) ? FormattableString.Invariant($"{lat:F4},{lon:F4}") : name,
                lat,
                lon));
        }

        waypoints = list;
        return list.Count > 0;
    }

    /// <summary>
    /// The viewport centres of a pan from <paramref name="from"/> to
    /// <paramref name="to"/> in <paramref name="steps"/> equal moves, both
    /// ends included (so <paramref name="steps"/> + 1 centres).
    /// </summary>
    public static IReadOnlyList<SettleWaypoint> PanSteps(SettleWaypoint from, SettleWaypoint to, int steps)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);
        var list = new List<SettleWaypoint>(steps + 1);
        for (var i = 0; i <= steps; i++)
        {
            var f = (double)i / steps;
            list.Add(new SettleWaypoint(
                FormattableString.Invariant($"pan {i}"),
                from.Latitude + ((to.Latitude - from.Latitude) * f),
                from.Longitude + ((to.Longitude - from.Longitude) * f)));
        }

        return list;
    }
}
