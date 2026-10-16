using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S421.DataModel;

namespace EncDotNet.S100.Testing.Scenarios.Writers;

/// <summary>A vessel's passage values: what the under-keel clearance check needs.</summary>
/// <param name="DraftMeters">Static draft, in metres.</param>
/// <param name="SquatMeters">Squat allowance, in metres (S-421 has no attribute for it).</param>
/// <param name="SpeedKnots">Planned speed over ground, in knots.</param>
/// <param name="RequiredUkcMeters">Required under-keel clearance, in metres.</param>
public sealed record PassageValues(double DraftMeters, double SquatMeters, double SpeedKnots, double RequiredUkcMeters);

/// <summary>An S-421 route through named places.</summary>
/// <param name="DatasetName">The file name without extension.</param>
/// <param name="RouteId">The route identifier (<c>routeID</c>).</param>
/// <param name="Name">The route name (<c>routeInfoName</c>).</param>
/// <param name="Waypoints">The scenario's place names, in route order.</param>
/// <param name="Passage">The vessel's passage values, the same on every leg.</param>
/// <param name="Departure">The planned departure from the first waypoint (UTC).</param>
public sealed record S421RouteSpec(string DatasetName, string RouteId, string Name, IReadOnlyList<string> Waypoints, PassageValues Passage, DateTime Departure)
{
    /// <summary>The file name, with the <c>.gml</c> extension.</summary>
    public string FileName => DatasetName + ".gml";
}

/// <summary>
/// Builds the typed <see cref="S421Route"/> for an <see cref="S421RouteSpec"/>,
/// which the generator writes with the public
/// <see cref="Datasets.S421.S421DatasetWriter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each leg carries the draft (<c>routeWaypointLegDraft</c>), the required
/// UKC as the static UKC (<c>routeWaypointLegStaticUKC</c>), 100 m
/// cross-track limits and a loxodrome geometry type. A manual schedule
/// gives the planned speed at each waypoint, the departure as the first
/// waypoint's ETD and each later waypoint's ETA.
/// </para>
/// <para>
/// ETAs are the along-route distance in the world's local metres divided
/// by the speed, rounded to the second: the world's own clock, which a
/// geodesic check agrees with to well under a second over a scenario.
/// </para>
/// </remarks>
public static class S421RouteBuilder
{
    /// <summary>The edition written to every generated route.</summary>
    public const int Edition = 1;

    /// <summary>The waypoints' positions, rounded to 10⁻⁷ degrees.</summary>
    /// <param name="definition">The scenario.</param>
    /// <param name="spec">The route.</param>
    /// <returns>The positions in route order.</returns>
    public static IReadOnlyList<GeoPosition> Positions(ScenarioDefinition definition, S421RouteSpec spec)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(spec);
        return spec.Waypoints.Select(name => GmlWriting.Round(definition.Place(name))).ToList();
    }

    /// <summary>The distance from departure to each waypoint, in the world's local metres.</summary>
    /// <param name="definition">The scenario.</param>
    /// <param name="spec">The route.</param>
    /// <returns>One distance per waypoint, starting at 0.</returns>
    public static IReadOnlyList<double> Distances(ScenarioDefinition definition, S421RouteSpec spec)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(spec);
        var distances = new List<double> { 0 };
        for (var i = 1; i < spec.Waypoints.Count; i++)
        {
            var (ax, ay) = definition.Places[spec.Waypoints[i - 1]];
            var (bx, by) = definition.Places[spec.Waypoints[i]];
            distances.Add(distances[^1] + Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)));
        }

        return distances;
    }

    /// <summary>The time the vessel reaches a distance along the route, at the planned speed.</summary>
    /// <param name="spec">The route.</param>
    /// <param name="distanceMeters">Metres from departure.</param>
    /// <returns>The time (UTC), rounded to the second.</returns>
    public static DateTime TimeAt(S421RouteSpec spec, double distanceMeters)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var seconds = distanceMeters / (spec.Passage.SpeedKnots * 1852.0 / 3600.0);
        return spec.Departure.AddSeconds(Math.Round(seconds));
    }

    /// <summary>Builds the typed route.</summary>
    /// <param name="definition">The scenario.</param>
    /// <param name="spec">The route.</param>
    /// <returns>The route.</returns>
    public static S421Route Build(ScenarioDefinition definition, S421RouteSpec spec)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(spec);
        var empty = new Dictionary<string, string>();
        var positions = Positions(definition, spec);
        var distances = Distances(definition, spec);

        var legs = new List<S421Leg>();
        for (var i = 1; i < positions.Count; i++)
        {
            legs.Add(new S421Leg
            {
                Id = $"RTE.LEG.{i}",
                Coordinates = [positions[i - 1], positions[i]],
                StarboardCrossTrackDistanceLimit = 100,
                PortCrossTrackDistanceLimit = 100,
                GeometryTypeCode = 1,
                Draft = spec.Passage.DraftMeters,
                StaticUnderKeelClearance = spec.Passage.RequiredUkcMeters,
                ExtraAttributes = empty,
            });
        }

        var waypoints = new List<S421Waypoint>();
        for (var i = 0; i < positions.Count; i++)
        {
            waypoints.Add(new S421Waypoint
            {
                Id = $"RTE.WPT.{i + 1}",
                WaypointNumber = i + 1,
                Name = spec.Waypoints[i],
                TurnRadius = 0.1,
                Position = positions[i],
                OutgoingLeg = i < legs.Count ? legs[i] : null,
                ExtraAttributes = empty,
            });
        }

        var elements = new List<S421ScheduleElement>();
        for (var i = 0; i < positions.Count; i++)
        {
            var last = i == positions.Count - 1;
            elements.Add(new S421ScheduleElement
            {
                Id = $"RTE.SCHED.1.MAN.{i + 1}",
                WaypointNumber = i + 1,
                PlannedSpeedOverGround = last ? null : spec.Passage.SpeedKnots,
                Etd = i == 0 ? new DateTimeOffset(spec.Departure, TimeSpan.Zero) : null,
                Eta = i == 0 ? null : new DateTimeOffset(TimeAt(spec, distances[i]), TimeSpan.Zero),
                ExtraAttributes = empty,
            });
        }

        return new S421Route
        {
            Id = "RTE",
            FormatVersion = "1.0",
            RouteId = spec.RouteId,
            EditionNumber = Edition,
            Info = new S421RouteInfo
            {
                Id = "RTE.INFO",
                Name = spec.Name,
                Author = ExchangeCatalogueBuilder.Organisation,
                Description = $"SYNTHETIC route in {definition.World.Name}, a fictitious area. Not for navigation.",
                Status = 1,
                EditionTime = new DateTimeOffset(definition.Issued, TimeSpan.Zero),
                ValidityStart = new DateTimeOffset(spec.Departure, TimeSpan.Zero),
                Vessel = new S421VesselInfo { Name = "MV SYNTHETIC (fictitious)", LengthMeters = 120, BeamMeters = 20 },
                ExtraAttributes = empty,
            },
            Waypoints = waypoints,
            Legs = legs,
            ActionPoints = [],
            Schedules =
            [
                new S421Schedule
                {
                    Id = "RTE.SCHED.1",
                    ScheduleNumber = 1,
                    Name = "Planned",
                    Variants = [new S421ScheduleVariant { Id = "RTE.SCHED.1.MAN", Kind = S421ScheduleVariantKind.Manual, Elements = elements }],
                    ExtraAttributes = empty,
                },
            ],
            ExtraAttributes = empty,
        };
    }
}
