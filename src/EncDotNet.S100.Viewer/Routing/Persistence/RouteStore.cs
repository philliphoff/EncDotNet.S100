using System.Text.Json;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Viewer.Routing.Persistence;

/// <summary>
/// Reads and writes the viewer's editable <see cref="RouteCollection"/> to a
/// JSON file (<c>routes.json</c>) in the viewer data directory. Translates
/// between the behaviour-bearing domain types and the flat
/// <see cref="RouteStoreDocument"/> DTO graph used on disk.
/// </summary>
/// <remarks>
/// Loading is tolerant: a missing file, an empty/whitespace file, malformed
/// JSON, or a document whose <see cref="RouteStoreDocument.SchemaVersion"/>
/// this build does not understand all leave the supplied collection
/// untouched rather than throwing, so a corrupt file never blocks viewer
/// startup. Structural anomalies within an otherwise-valid document (a leg
/// count that disagrees with the waypoint count, a waypoint with no
/// coordinates) are repaired by reconstructing legs through the
/// <see cref="Route"/> API and clamping to the available data.
/// </remarks>
internal static class RouteStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Serializes <paramref name="routes"/> to <paramref name="path"/>,
    /// creating the containing directory when needed. The write is staged
    /// through a sibling temporary file and moved into place so a crash
    /// mid-write cannot leave a half-written <c>routes.json</c>.
    /// </summary>
    /// <param name="routes">The collection to persist.</param>
    /// <param name="path">Destination file path.</param>
    /// <exception cref="ArgumentNullException"><paramref name="routes"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or whitespace.</exception>
    public static void Save(RouteCollection routes, string path)
    {
        ArgumentNullException.ThrowIfNull(routes);
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A routes file path is required.", nameof(path));

        var document = ToDocument(routes);

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(document, SerializerOptions);

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, json);
        // File.Move with overwrite is atomic on the same volume on the
        // platforms the viewer targets, so a reader never sees a partial file.
        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>
    /// Loads routes from <paramref name="path"/> into
    /// <paramref name="routes"/>, replacing whatever the collection currently
    /// holds. A missing, empty, malformed, or unrecognised-version file
    /// leaves the collection empty and reports success (<c>false</c> return
    /// only signals "nothing was loaded", never a hard failure).
    /// </summary>
    /// <param name="routes">The collection to populate.</param>
    /// <param name="path">Source file path.</param>
    /// <returns><c>true</c> when at least one route was loaded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="routes"/> is <c>null</c>.</exception>
    public static bool Load(RouteCollection routes, string path)
    {
        ArgumentNullException.ThrowIfNull(routes);

        ClearAll(routes);

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        RouteStoreDocument? document;
        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return false;
            document = JsonSerializer.Deserialize<RouteStoreDocument>(json, SerializerOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable file: start with no routes rather than
            // failing the load. The next save overwrites the bad file.
            return false;
        }

        if (document is null || document.SchemaVersion != RouteStoreDocument.CurrentSchemaVersion)
            return false;

        return Populate(routes, document);
    }

    private static RouteStoreDocument ToDocument(RouteCollection routes)
    {
        var document = new RouteStoreDocument
        {
            ActiveRouteId = routes.ActiveRoute?.Id,
        };

        foreach (var route in routes.Routes)
        {
            var routeDocument = new RouteDocument
            {
                Id = route.Id,
                Info = ToDocument(route.Info),
            };

            foreach (var waypoint in route.Waypoints)
            {
                routeDocument.Waypoints.Add(new RouteWaypointDocument
                {
                    Latitude = waypoint.Position.Latitude,
                    Longitude = waypoint.Position.Longitude,
                    Number = waypoint.Number,
                    Name = waypoint.Name,
                    Fixed = waypoint.Fixed,
                    TurnRadiusNm = waypoint.TurnRadiusNm,
                });
            }

            foreach (var leg in route.Legs)
                routeDocument.Legs.Add(ToDocument(leg));

            document.Routes.Add(routeDocument);
        }

        return document;
    }

    private static RouteInfoDocument ToDocument(RouteInfo info) => new()
    {
        Name = info.Name,
        Author = info.Author,
        Description = info.Description,
        DeparturePortId = info.DeparturePortId,
        ArrivalPortId = info.ArrivalPortId,
        ValidityStart = info.ValidityStart,
        ValidityEnd = info.ValidityEnd,
        Vessel = info.Vessel is { } v
            ? new RouteVesselInfoDocument
            {
                Name = v.Name,
                Mmsi = v.Mmsi,
                Imo = v.Imo,
                Callsign = v.Callsign,
                LengthMeters = v.LengthMeters,
                BeamMeters = v.BeamMeters,
            }
            : null,
    };

    private static RouteLegDocument ToDocument(RouteLeg leg) => new()
    {
        GeometryType = leg.GeometryType.ToString(),
        StarboardCrossTrackDistanceLimitMeters = leg.StarboardCrossTrackDistanceLimitMeters,
        PortCrossTrackDistanceLimitMeters = leg.PortCrossTrackDistanceLimitMeters,
        StarboardChannelLimitMeters = leg.StarboardChannelLimitMeters,
        PortChannelLimitMeters = leg.PortChannelLimitMeters,
        SafetyContourMeters = leg.SafetyContourMeters,
        SafetyDepthMeters = leg.SafetyDepthMeters,
        SpeedOverGroundMinKnots = leg.SpeedOverGroundMinKnots,
        SpeedOverGroundMaxKnots = leg.SpeedOverGroundMaxKnots,
        SpeedThroughWaterMinKnots = leg.SpeedThroughWaterMinKnots,
        SpeedThroughWaterMaxKnots = leg.SpeedThroughWaterMaxKnots,
        DraftMeters = leg.DraftMeters,
        StaticUnderKeelClearanceMeters = leg.StaticUnderKeelClearanceMeters,
        DynamicUnderKeelClearanceMeters = leg.DynamicUnderKeelClearanceMeters,
        SafetyMarginMeters = leg.SafetyMarginMeters,
        Note = leg.Note,
    };

    private static bool Populate(RouteCollection routes, RouteStoreDocument document)
    {
        var loadedAny = false;

        foreach (var routeDocument in document.Routes)
        {
            Route route;
            try
            {
                route = routes.CreateRoute(routeDocument.Info?.Name, routeDocument.Id);
            }
            catch (ArgumentException)
            {
                // Duplicate id in a hand-edited file: skip the offender and
                // keep loading the rest.
                continue;
            }

            ApplyInfo(route.Info, routeDocument.Info);

            foreach (var waypointDocument in routeDocument.Waypoints)
            {
                var waypoint = route.AppendWaypoint(
                    new GeoPosition(waypointDocument.Latitude, waypointDocument.Longitude));
                waypoint.Number = waypointDocument.Number;
                waypoint.Name = waypointDocument.Name;
                waypoint.Fixed = waypointDocument.Fixed;
                waypoint.TurnRadiusNm = waypointDocument.TurnRadiusNm;
            }

            // The route rebuilt its own legs as waypoints were appended; copy
            // the persisted leg attributes onto the matching segments, tolerant
            // of a document whose leg count disagrees with the waypoint count.
            var legCount = Math.Min(route.Legs.Count, routeDocument.Legs.Count);
            for (var i = 0; i < legCount; i++)
                ApplyLeg(route.Legs[i], routeDocument.Legs[i]);

            loadedAny = true;
        }

        // Restore the active route; CreateRoute left the last-added route
        // active, so only override when a different one was persisted.
        if (document.ActiveRouteId is { } activeId && routes.FindById(activeId) is { } active)
            routes.SetActiveRoute(active);

        return loadedAny;
    }

    private static void ApplyInfo(RouteInfo info, RouteInfoDocument? document)
    {
        if (document is null)
            return;

        // Name is seeded by CreateRoute; the rest start null.
        info.Name = document.Name;
        info.Author = document.Author;
        info.Description = document.Description;
        info.DeparturePortId = document.DeparturePortId;
        info.ArrivalPortId = document.ArrivalPortId;
        info.ValidityStart = document.ValidityStart;
        info.ValidityEnd = document.ValidityEnd;
        info.Vessel = document.Vessel is { } v
            ? new RouteVesselInfo
            {
                Name = v.Name,
                Mmsi = v.Mmsi,
                Imo = v.Imo,
                Callsign = v.Callsign,
                LengthMeters = v.LengthMeters,
                BeamMeters = v.BeamMeters,
            }
            : null;
    }

    private static void ApplyLeg(RouteLeg leg, RouteLegDocument document)
    {
        leg.GeometryType = Enum.TryParse<RouteLegGeometryType>(document.GeometryType, ignoreCase: true, out var geometry)
            ? geometry
            : RouteLegGeometryType.Loxodrome;
        leg.StarboardCrossTrackDistanceLimitMeters = document.StarboardCrossTrackDistanceLimitMeters;
        leg.PortCrossTrackDistanceLimitMeters = document.PortCrossTrackDistanceLimitMeters;
        leg.StarboardChannelLimitMeters = document.StarboardChannelLimitMeters;
        leg.PortChannelLimitMeters = document.PortChannelLimitMeters;
        leg.SafetyContourMeters = document.SafetyContourMeters;
        leg.SafetyDepthMeters = document.SafetyDepthMeters;
        leg.SpeedOverGroundMinKnots = document.SpeedOverGroundMinKnots;
        leg.SpeedOverGroundMaxKnots = document.SpeedOverGroundMaxKnots;
        leg.SpeedThroughWaterMinKnots = document.SpeedThroughWaterMinKnots;
        leg.SpeedThroughWaterMaxKnots = document.SpeedThroughWaterMaxKnots;
        leg.DraftMeters = document.DraftMeters;
        leg.StaticUnderKeelClearanceMeters = document.StaticUnderKeelClearanceMeters;
        leg.DynamicUnderKeelClearanceMeters = document.DynamicUnderKeelClearanceMeters;
        leg.SafetyMarginMeters = document.SafetyMarginMeters;
        leg.Note = document.Note;
    }

    private static void ClearAll(RouteCollection routes)
    {
        // Snapshot first: Remove mutates the underlying list.
        foreach (var route in new List<Route>(routes.Routes))
            routes.Remove(route);
    }
}
