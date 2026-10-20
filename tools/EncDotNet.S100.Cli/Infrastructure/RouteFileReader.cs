using System.Text;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Routes.Rtz;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>A route read from an RTZ or S-421 file, as the passage tools take it.</summary>
/// <param name="Path">The file it was read from.</param>
/// <param name="Format"><c>rtz</c> or <c>s421</c>.</param>
/// <param name="Version">The RTZ version, or the S-421 route format version; <c>null</c> when the file gives none.</param>
/// <param name="RouteId">The S-421 <c>routeID</c>; <c>null</c> for RTZ, which has none.</param>
/// <param name="Name">The route's name, or <c>null</c>.</param>
/// <param name="Waypoints">The waypoint positions in order.</param>
/// <param name="Legs">The legs in order, with the file's draft, speed, required UKC and cross-track limits.</param>
/// <param name="Departure">The file's departure (its planning schedule's first ETD), or <c>null</c>.</param>
internal sealed record RouteFile(
    string Path,
    string Format,
    string? Version,
    string? RouteId,
    string? Name,
    IReadOnlyList<GeoPosition> Waypoints,
    IReadOnlyList<PassageRouteLeg> Legs,
    DateTimeOffset? Departure);

/// <summary>The file isn't a route the CLI can read.</summary>
internal sealed class RouteFileException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Reads an RTZ (IEC 61174 Annex S) or S-421 route file onto
/// <see cref="PassageRouteLeg"/>s, with the semantics SoundCharts applies
/// when it imports one (<c>RouteRtzMapping</c>, <c>RouteS421Mapping</c>).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// RTZ: leg <c>i</c> is the <c>&lt;leg&gt;</c> of waypoint <c>i + 1</c> (the
/// leg <em>to</em> it), with the default waypoint's values filled in. XTDs are
/// nautical miles in RTZ and metres here; the draft is the deeper of the
/// forward and aft draughts. Speeds and the departure come from the planning
/// schedule (the first calculated table, else the manual one): a leg's speed
/// is on the element of the waypoint it leads to, else implied by that
/// element's ETA and the previous one's ETD.
/// </description></item>
/// <item><description>
/// S-421: leg <c>i</c> is waypoint <c>i</c>'s outgoing <c>RouteWaypointLeg</c>
/// (draft, static UKC, XTDLs, geometry type). Speeds and the departure come
/// from the planning schedule variant (<see cref="S421Route.GetPlanningSchedule"/>):
/// a leg's speed is its start waypoint's planned SOG, the departure the first
/// element's ETD.
/// </description></item>
/// </list>
/// Neither format has a squat allowance.
/// </remarks>
internal static class RouteFileReader
{
    private const int SniffLength = 8192;

    /// <summary>Reads the route in <paramref name="path"/>.</summary>
    /// <param name="path">An RTZ file (any extension; recognised by its root and namespace) or an S-421 GML dataset.</param>
    /// <returns>The route.</returns>
    /// <exception cref="RouteFileException">The file is missing, isn't a route, or has fewer than two waypoints.</exception>
    public static RouteFile Read(string path)
    {
        if (!File.Exists(path))
            throw new RouteFileException($"Route file not found: {path}");

        var route = RtzVersions.Sniff(Head(path)) is { } version ? ReadRtz(path, version) : ReadS421(path);
        if (route.Waypoints.Count < 2)
            throw new RouteFileException($"The route in {path} has fewer than two waypoints.");
        return route;
    }

    private static string Head(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[SniffLength];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static RouteFile ReadRtz(string path, string version)
    {
        if (!RtzVersions.IsSupported(version))
            throw new RouteFileException($"RTZ {version} isn't supported (supported: {string.Join(", ", RtzVersions.Supported)}).");

        RtzRoute rtz;
        try
        {
            rtz = RtzReader.Read(path);
        }
        catch (Exception ex) when (ex is FormatException or System.Xml.XmlException or IOException)
        {
            throw new RouteFileException($"Not a readable RTZ route: {ex.Message}", ex);
        }

        var waypoints = rtz.Waypoints.Select(w => w.Position).ToList();
        var schedule = rtz.GetPlanningSchedule();
        var legs = new List<PassageRouteLeg>();
        for (var i = 0; i + 1 < waypoints.Count; i++)
        {
            var leg = rtz.GetEffectiveLeg(i + 1);
            var geometry = leg?.GeometryType == RtzGeometryType.Orthodrome ? LegGeometry.GreatCircle : LegGeometry.Rhumb;
            legs.Add(new PassageRouteLeg(
                [waypoints[i], waypoints[i + 1]],
                geometry,
                leg?.MaxDraught,
                schedule is null ? null : RtzLegSpeed(rtz, schedule, i, geometry),
                SquatMeters: null,
                leg?.StaticUkc,
                LegName(i),
                Metres(leg?.PortsideXtd),
                Metres(leg?.StarboardXtd)));
        }

        var departure = schedule?.ElementFor(rtz.Waypoints[0].Id) is { } first ? first.Departure ?? first.Eta : null;
        return new RouteFile(
            path,
            "rtz",
            rtz.Version,
            RouteId: null,
            string.IsNullOrWhiteSpace(rtz.Info.RouteName) ? null : rtz.Info.RouteName,
            waypoints,
            legs,
            departure);
    }

    private static double? RtzLegSpeed(RtzRoute rtz, RtzSchedule schedule, int legIndex, LegGeometry geometry)
    {
        var to = schedule.ElementFor(rtz.Waypoints[legIndex + 1].Id);
        if (to?.Speed is { } speed)
            return speed;
        if (schedule.ElementFor(rtz.Waypoints[legIndex].Id)?.Departure is not { } leave || to?.Eta is not { } arrive || arrive <= leave)
            return null;
        var distanceNm = PassageGeodesy.DistanceMeters(rtz.Waypoints[legIndex].Position, rtz.Waypoints[legIndex + 1].Position, geometry)
            / PassageGeodesy.MetersPerNauticalMile;
        return Math.Round(distanceNm / (arrive - leave).TotalHours, 2);
    }

    private static RouteFile ReadS421(string path)
    {
        S421Route s421;
        try
        {
            s421 = S421RoutePlan.From(S421Dataset.Open(path), out _).Route;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or System.Xml.XmlException or FormatException)
        {
            throw new RouteFileException($"Not an RTZ or S-421 route: {ex.Message}", ex);
        }

        var elements = s421.GetPlanningSchedule()?.Variant.Elements ?? [];
        var waypoints = s421.Waypoints.Select(w => w.Position).ToList();
        var legs = new List<PassageRouteLeg>();
        for (var i = 0; i + 1 < waypoints.Count; i++)
        {
            var leg = s421.Waypoints[i].OutgoingLeg;
            var number = s421.Waypoints[i].WaypointNumber;
            var speed = elements.FirstOrDefault(e => e.WaypointNumber == number)?.PlannedSpeedOverGround;
            legs.Add(new PassageRouteLeg(
                [waypoints[i], waypoints[i + 1]],
                leg?.GeometryTypeCode == GeodesicGeometryType ? LegGeometry.GreatCircle : LegGeometry.Rhumb,
                leg?.Draft,
                speed > 0 ? speed : null,
                SquatMeters: null,
                leg?.StaticUnderKeelClearance,
                LegName(i),
                leg?.PortCrossTrackDistanceLimit,
                leg?.StarboardCrossTrackDistanceLimit));
        }

        return new RouteFile(
            path,
            "s421",
            s421.FormatVersion,
            s421.RouteId,
            s421.Info?.Name,
            waypoints,
            legs,
            elements.Count > 0 ? elements[0].Etd : null);
    }

    // S-421 routeWaypointLegGeometryType: 1 = loxodrome, 2 = geodesic.
    private const int GeodesicGeometryType = 2;

    private static string LegName(int index) => $"Leg {index + 1} → {index + 2}";

    private static double? Metres(double? nauticalMiles) => nauticalMiles * PassageGeodesy.MetersPerNauticalMile;
}
