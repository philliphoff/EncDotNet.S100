using EncDotNet.S100.Datasets.S421.DataModel;

namespace EncDotNet.S100.Viewer.Routing;

/// <summary>
/// Projects an editable <see cref="Route"/> onto the S-421 typed model
/// (<see cref="S421Route"/>), which <see cref="Datasets.S421.S421DatasetWriter"/>
/// writes as S-421 GML, and back (#390).
/// </summary>
/// <remarks>
/// <para>
/// The mapping follows the table on <see cref="RoutePassage"/>. Passage
/// values have no route-level slot in S-421, so export writes each leg's
/// effective value (its override, else the passage value): draft as
/// <c>routeWaypointLegDraft</c>, required UKC as
/// <c>routeWaypointLegStaticUKC</c>, cross-track limits as the XTDLs, and
/// speed as each waypoint's <c>routeScheduleElementPlanSOG</c> in a manual
/// schedule whose first element carries the departure as its ETD. Squat
/// has no S-421 attribute and isn't exported.
/// </para>
/// <para>
/// Import does the reverse and lifts a value every leg shares back into
/// the passage, leaving the rest as leg overrides. Object ids are written
/// as <c>RTE</c>, <c>RTE.WPT.n</c> and <c>RTE.LEG.n</c>; the route's own
/// id is its <c>routeID</c>.
/// </para>
/// </remarks>
internal static class RouteS421Mapping
{
    private static readonly IReadOnlyDictionary<string, string> NoExtras = new Dictionary<string, string>();

    private static readonly RoutePassageField[] LegFields =
    [
        RoutePassageField.Draft,
        RoutePassageField.RequiredUkc,
        RoutePassageField.PortCrossTrack,
        RoutePassageField.StarboardCrossTrack,
    ];

    /// <summary>The S-421 projection of <paramref name="route"/>.</summary>
    /// <param name="route">The route; it needs at least two waypoints to be written.</param>
    /// <returns>The typed S-421 route.</returns>
    public static S421Route ToS421(Route route)
    {
        ArgumentNullException.ThrowIfNull(route);

        var legs = new List<S421Leg>();
        for (var i = 0; i < route.Legs.Count; i++)
        {
            var leg = route.Legs[i];
            legs.Add(new S421Leg
            {
                Id = $"RTE.LEG.{i + 1}",
                Coordinates = [route.Waypoints[i].Position, route.Waypoints[i + 1].Position],
                StarboardCrossTrackDistanceLimit = route.GetEffectiveLegValue(i, RoutePassageField.StarboardCrossTrack),
                PortCrossTrackDistanceLimit = route.GetEffectiveLegValue(i, RoutePassageField.PortCrossTrack),
                StarboardChannelLimit = leg.StarboardChannelLimitMeters,
                PortChannelLimit = leg.PortChannelLimitMeters,
                SafetyContour = leg.SafetyContourMeters,
                SafetyDepth = leg.SafetyDepthMeters,
                GeometryTypeCode = (int)leg.GeometryType,
                SpeedOverGroundMin = leg.SpeedOverGroundMinKnots,
                SpeedOverGroundMax = leg.SpeedOverGroundMaxKnots,
                SpeedThroughWaterMin = leg.SpeedThroughWaterMinKnots,
                SpeedThroughWaterMax = leg.SpeedThroughWaterMaxKnots,
                Draft = route.GetEffectiveLegValue(i, RoutePassageField.Draft),
                StaticUnderKeelClearance = route.GetEffectiveLegValue(i, RoutePassageField.RequiredUkc),
                DynamicUnderKeelClearance = leg.DynamicUnderKeelClearanceMeters,
                SafetyMargin = leg.SafetyMarginMeters,
                Note = leg.Note,
                ExtraAttributes = NoExtras,
            });
        }

        var waypoints = route.Waypoints.Select((w, i) => new S421Waypoint
        {
            Id = $"RTE.WPT.{i + 1}",
            WaypointNumber = w.Number ?? i + 1,
            Name = w.Name,
            Fixed = w.Fixed,
            TurnRadius = w.TurnRadiusNm,
            Position = w.Position,
            OutgoingLeg = i < legs.Count ? legs[i] : null,
            ExtraAttributes = NoExtras,
        }).ToList();

        return new S421Route
        {
            Id = "RTE",
            FormatVersion = "1.0",
            RouteId = route.Id,
            EditionNumber = 1,
            Info = ToS421(route.Info),
            Waypoints = waypoints,
            Legs = legs,
            ActionPoints = [],
            Schedules = Schedule(route, waypoints),
            ExtraAttributes = NoExtras,
        };
    }

    /// <summary>
    /// An editable route from an S-421 route: its waypoints, leg attributes
    /// and info, with the values every leg shares lifted into the passage.
    /// </summary>
    /// <param name="s421">The S-421 route.</param>
    /// <returns>The route; its id is the S-421 <c>routeID</c> (or a new id).</returns>
    public static Route FromS421(S421Route s421)
    {
        ArgumentNullException.ThrowIfNull(s421);

        var route = new Route(s421.RouteId, s421.Info?.Name);
        if (s421.Info is { } info)
        {
            route.Info.Author = info.Author;
            route.Info.Description = info.Description;
            route.Info.DeparturePortId = info.DeparturePortId1;
            route.Info.ArrivalPortId = info.ArrivalPortId1;
            route.Info.ValidityStart = info.ValidityStart;
            route.Info.ValidityEnd = info.ValidityEnd;
            if (info.Vessel is { } vessel)
            {
                route.Info.Vessel = new RouteVesselInfo
                {
                    Name = vessel.Name,
                    Mmsi = vessel.Mmsi,
                    Imo = vessel.Imo,
                    Callsign = vessel.Callsign,
                    LengthMeters = vessel.LengthMeters,
                    BeamMeters = vessel.BeamMeters,
                };
            }
        }

        foreach (var s421Waypoint in s421.Waypoints)
        {
            var waypoint = route.AppendWaypoint(s421Waypoint.Position);
            waypoint.Number = s421Waypoint.WaypointNumber;
            waypoint.Name = s421Waypoint.Name;
            waypoint.Fixed = s421Waypoint.Fixed;
            waypoint.TurnRadiusNm = s421Waypoint.TurnRadius;
        }

        var elements = PlanningElements(s421);
        var legValues = new List<Dictionary<RoutePassageField, double?>>();
        for (var i = 0; i < route.Legs.Count; i++)
        {
            var source = s421.Waypoints[i].OutgoingLeg;
            var leg = route.Legs[i];
            if (source is not null)
            {
                leg.GeometryType = source.GeometryTypeCode == (int)RouteLegGeometryType.Geodesic ? RouteLegGeometryType.Geodesic : RouteLegGeometryType.Loxodrome;
                leg.StarboardChannelLimitMeters = source.StarboardChannelLimit;
                leg.PortChannelLimitMeters = source.PortChannelLimit;
                leg.SafetyContourMeters = source.SafetyContour;
                leg.SafetyDepthMeters = source.SafetyDepth;
                leg.SpeedOverGroundMinKnots = source.SpeedOverGroundMin;
                leg.SpeedOverGroundMaxKnots = source.SpeedOverGroundMax;
                leg.SpeedThroughWaterMinKnots = source.SpeedThroughWaterMin;
                leg.SpeedThroughWaterMaxKnots = source.SpeedThroughWaterMax;
                leg.DynamicUnderKeelClearanceMeters = source.DynamicUnderKeelClearance;
                leg.SafetyMarginMeters = source.SafetyMargin;
                leg.Note = source.Note;
            }

            var number = s421.Waypoints[i].WaypointNumber;
            legValues.Add(new Dictionary<RoutePassageField, double?>
            {
                [RoutePassageField.Draft] = source?.Draft,
                [RoutePassageField.RequiredUkc] = source?.StaticUnderKeelClearance,
                [RoutePassageField.PortCrossTrack] = source?.PortCrossTrackDistanceLimit,
                [RoutePassageField.StarboardCrossTrack] = source?.StarboardCrossTrackDistanceLimit,
                [RoutePassageField.Speed] = elements.FirstOrDefault(e => e.WaypointNumber == number)?.PlannedSpeedOverGround,
            });
        }

        foreach (var field in LegFields.Append(RoutePassageField.Speed))
        {
            var values = legValues.Select(v => v[field]).Distinct().ToList();
            if (values.Count == 1 && values[0] is { } shared)
            {
                route.SetPassageValue(field, shared);
                continue;
            }

            for (var i = 0; i < legValues.Count; i++)
            {
                route.SetLegOverride(i, field, legValues[i][field]);
            }
        }

        if (elements.Count > 0 && elements[0].Etd is { } departure)
        {
            route.SetDeparture(departure);
        }

        return route;
    }

    private static S421RouteInfo ToS421(RouteInfo info) => new()
    {
        Id = "RTE.INFO",
        Name = info.Name,
        Author = info.Author,
        Description = info.Description,
        Status = 1,
        ValidityStart = info.ValidityStart,
        ValidityEnd = info.ValidityEnd,
        DeparturePortId1 = info.DeparturePortId,
        ArrivalPortId1 = info.ArrivalPortId,
        Vessel = info.Vessel is { } vessel
            ? new S421VesselInfo
            {
                Name = vessel.Name,
                Mmsi = vessel.Mmsi,
                Imo = vessel.Imo,
                Callsign = vessel.Callsign,
                LengthMeters = vessel.LengthMeters,
                BeamMeters = vessel.BeamMeters,
            }
            : null,
        ExtraAttributes = NoExtras,
    };

    // A manual schedule with each waypoint's planned speed (its outgoing
    // leg's effective speed) and the departure as the first ETD, when the
    // route has either.
    private static IReadOnlyList<S421Schedule> Schedule(Route route, IReadOnlyList<S421Waypoint> waypoints)
    {
        var speeds = Enumerable.Range(0, route.Legs.Count).Select(i => route.GetEffectiveLegValue(i, RoutePassageField.Speed)).ToList();
        if (route.Passage.Departure is null && speeds.All(s => s is null))
        {
            return [];
        }

        var elements = waypoints.Select((w, i) => new S421ScheduleElement
        {
            Id = $"RTE.SCHED.1.MAN.{i + 1}",
            WaypointNumber = w.WaypointNumber,
            PlannedSpeedOverGround = i < speeds.Count ? speeds[i] : null,
            Etd = i == 0 ? route.Passage.Departure : null,
            ExtraAttributes = NoExtras,
        }).ToList();
        return
        [
            new S421Schedule
            {
                Id = "RTE.SCHED.1",
                ScheduleNumber = 1,
                Name = "Planned",
                Variants = [new S421ScheduleVariant { Id = "RTE.SCHED.1.MAN", Kind = S421ScheduleVariantKind.Manual, Elements = elements }],
                ExtraAttributes = NoExtras,
            },
        ];
    }

    // The schedule elements a passage reads: the first schedule's manual
    // variant, else its first variant.
    private static IReadOnlyList<S421ScheduleElement> PlanningElements(S421Route route)
    {
        if (route.Schedules.Count == 0)
        {
            return [];
        }

        var variants = route.Schedules[0].Variants;
        return (variants.FirstOrDefault(v => v.Kind == S421ScheduleVariantKind.Manual) ?? variants.FirstOrDefault())?.Elements ?? [];
    }
}
