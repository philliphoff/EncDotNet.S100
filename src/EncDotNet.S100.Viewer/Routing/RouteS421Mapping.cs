using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Viewer.Routing.Exchange;

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
/// Import does the reverse: when every leg has a value, the value most
/// legs share (the first leg's on a tie) is lifted back into the passage,
/// and the rest stay leg overrides. Object ids are written
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
    /// <param name="identity">How the route is identified (decision 17); its own id, edition 1 and status Initial when omitted.</param>
    /// <param name="includeCheckSettings">
    /// Whether to write the check settings (draft and required UKC on each
    /// leg, the departure). Without them a received route writes its file's
    /// values and an owned route none (decision 16).
    /// </param>
    /// <param name="preserved">
    /// The S-421 route the viewer's model came from (a received route's file,
    /// or a copy's source): its action points, route-info extras, extension
    /// attributes and, when <paramref name="writePreservedSchedules"/>, its
    /// further schedules are written back.
    /// </param>
    /// <param name="writePreservedSchedules">Whether to write <paramref name="preserved"/>'s schedules back.</param>
    /// <returns>The typed S-421 route.</returns>
    public static S421Route ToS421(
        Route route,
        RouteExchangeIdentity? identity = null,
        bool includeCheckSettings = true,
        S421Route? preserved = null,
        bool writePreservedSchedules = false)
    {
        ArgumentNullException.ThrowIfNull(route);

        // A check setting comes from the route (its effective value) when
        // asked for; otherwise from a received route's file, or nowhere.
        double? Value(int leg, RoutePassageField field)
        {
            if (!RoutePassageFields.IsCheckSetting(field) || includeCheckSettings)
                return route.GetEffectiveLegValue(leg, field);
            return route.IsReadOnly ? route.GetFileLegValue(leg, field) : null;
        }

        var departure = includeCheckSettings ? route.Passage.Departure : route.FileDeparture;
        var sameShape = preserved is not null && preserved.Waypoints.Count == route.Waypoints.Count;

        var legs = new List<S421Leg>();
        for (var i = 0; i < route.Legs.Count; i++)
        {
            var leg = route.Legs[i];
            var source = sameShape ? preserved!.Waypoints[i].OutgoingLeg ?? (preserved.Legs.Count > i ? preserved.Legs[i] : null) : null;
            legs.Add(new S421Leg
            {
                Id = $"RTE.LEG.{i + 1}",
                Coordinates = [route.Waypoints[i].Position, route.Waypoints[i + 1].Position],
                StarboardCrossTrackDistanceLimit = Value(i, RoutePassageField.StarboardCrossTrack),
                PortCrossTrackDistanceLimit = Value(i, RoutePassageField.PortCrossTrack),
                StarboardChannelLimit = leg.StarboardChannelLimitMeters,
                PortChannelLimit = leg.PortChannelLimitMeters,
                SafetyContour = leg.SafetyContourMeters,
                SafetyDepth = leg.SafetyDepthMeters,
                GeometryTypeCode = (int)leg.GeometryType,
                SpeedOverGroundMin = leg.SpeedOverGroundMinKnots,
                SpeedOverGroundMax = leg.SpeedOverGroundMaxKnots,
                SpeedThroughWaterMin = leg.SpeedThroughWaterMinKnots,
                SpeedThroughWaterMax = leg.SpeedThroughWaterMaxKnots,
                Draft = Value(i, RoutePassageField.Draft),
                StaticUnderKeelClearance = Value(i, RoutePassageField.RequiredUkc),
                DynamicUnderKeelClearance = leg.DynamicUnderKeelClearanceMeters,
                SafetyMargin = leg.SafetyMarginMeters,
                Note = leg.Note,
                ExtraAttributes = source?.ExtraAttributes ?? NoExtras,
            });
        }

        var waypoints = route.Waypoints.Select((w, i) => new S421Waypoint
        {
            Id = $"RTE.WPT.{i + 1}",
            WaypointNumber = w.Number ?? i + 1,
            Name = w.Name,
            ExternalReferenceId = sameShape ? preserved!.Waypoints[i].ExternalReferenceId : null,
            Fixed = w.Fixed,
            TurnRadius = w.TurnRadiusNm,
            Position = w.Position,
            OutgoingLeg = i < legs.Count ? legs[i] : null,
            ExtraAttributes = sameShape ? preserved!.Waypoints[i].ExtraAttributes : NoExtras,
        }).ToList();

        var ids = new HashSet<string>(StringComparer.Ordinal) { "RTE", "RTE.INFO" };
        ids.UnionWith(waypoints.Select(w => w.Id));
        ids.UnionWith(legs.Select(l => l.Id));

        var schedules = new List<S421Schedule>();
        if (route.IsReadOnly && preserved is not null && sameShape)
        {
            // A received route keeps its own schedules; asked-for check
            // settings replace the planning schedule's departure.
            schedules.AddRange(preserved.Schedules.Select(ReidSchedule));
            if (includeCheckSettings && route.LocalCheckSettings.Departure is { } local && Planning(preserved) is { } planning)
                schedules = WithDeparture(schedules, planning.Index, planning.Variant.Kind, local);
        }
        else
        {
            schedules.AddRange(Schedule(route, waypoints, departure, Value));
            ids.UnionWith(schedules.SelectMany(ScheduleIds));
            if (preserved is not null && writePreservedSchedules)
            {
                var used = Planning(preserved)?.Index;
                schedules.AddRange(preserved.Schedules.Where((_, i) => i != used).Select(ReidSchedule));
            }
        }

        var actionPoints = preserved?.ActionPoints.Select(ReidActionPoint).ToList() ?? [];

        identity ??= new RouteExchangeIdentity(route.Id, 1, RouteExchangeIdentity.StatusInitial, null, null, Kept: false);
        return new S421Route
        {
            Id = "RTE",
            FormatVersion = "1.0",
            RouteId = identity.RouteId,
            EditionNumber = identity.Edition,
            Info = ToS421(route.Info, identity, preserved?.Info),
            Waypoints = waypoints,
            Legs = legs,
            ActionPoints = actionPoints,
            Schedules = schedules,
            ExtraAttributes = preserved?.ExtraAttributes ?? NoExtras,
        };

        // Preserved objects keep their ids unless one is already taken.
        string Id(string id) => ids.Add(id) ? id : Id("P." + id);

        S421ActionPoint ReidActionPoint(S421ActionPoint a) => new()
        {
            Id = Id(a.Id),
            ActionPointNumber = a.ActionPointNumber,
            Name = a.Name,
            RadiusNauticalMiles = a.RadiusNauticalMiles,
            TimeToActMinutes = a.TimeToActMinutes,
            RequiredAction = a.RequiredAction,
            RequiredActionDescription = a.RequiredActionDescription,
            GeometryKind = a.GeometryKind,
            Coordinates = a.Coordinates,
            ExtraAttributes = a.ExtraAttributes,
        };

        S421Schedule ReidSchedule(S421Schedule schedule) => new()
        {
            Id = Id(schedule.Id),
            ScheduleNumber = schedule.ScheduleNumber,
            Name = schedule.Name,
            ExtraAttributes = schedule.ExtraAttributes,
            Variants = schedule.Variants.Select(v => new S421ScheduleVariant
            {
                Id = Id(v.Id),
                Kind = v.Kind,
                Elements = v.Elements.Select(e => CopyElement(e, Id(e.Id), e.Etd)).ToList(),
            }).ToList(),
        };
    }

    private static IEnumerable<string> ScheduleIds(S421Schedule schedule)
        => schedule.Variants.SelectMany(v => v.Elements.Select(e => e.Id).Append(v.Id)).Append(schedule.Id);

    // The schedules with the planning variant's first ETD set to departure.
    private static List<S421Schedule> WithDeparture(List<S421Schedule> schedules, int index, S421ScheduleVariantKind kind, DateTimeOffset departure)
    {
        var first = schedules[index];
        var planning = first.Variants.FirstOrDefault(v => v.Kind == kind);
        var variants = first.Variants.Select(v => !ReferenceEquals(v, planning) || v.Elements.Count == 0
            ? v
            : new S421ScheduleVariant
            {
                Id = v.Id,
                Kind = v.Kind,
                Elements = v.Elements.Select((e, i) => i == 0 ? CopyElement(e, e.Id, departure) : e).ToList(),
            }).ToList();
        schedules[index] = new S421Schedule
        {
            Id = first.Id,
            ScheduleNumber = first.ScheduleNumber,
            Name = first.Name,
            Variants = variants,
            ExtraAttributes = first.ExtraAttributes,
        };
        return schedules;
    }

    private static S421ScheduleElement CopyElement(S421ScheduleElement e, string id, DateTimeOffset? etd) => new()
    {
        Id = id,
        WaypointNumber = e.WaypointNumber,
        PlannedSpeedOverGround = e.PlannedSpeedOverGround,
        Etd = etd,
        Eta = e.Eta,
        EtdWindowBeforeMinutes = e.EtdWindowBeforeMinutes,
        EtdWindowAfterMinutes = e.EtdWindowAfterMinutes,
        EtaWindowBeforeMinutes = e.EtaWindowBeforeMinutes,
        EtaWindowAfterMinutes = e.EtaWindowAfterMinutes,
        Note = e.Note,
        ExtraAttributes = e.ExtraAttributes,
    };

    /// <summary>
    /// An editable route from an S-421 route: its waypoints, leg attributes
    /// and info, with the values every leg shares lifted into the passage.
    /// </summary>
    /// <param name="s421">The S-421 route.</param>
    /// <param name="id">The route's id; the S-421 <c>routeID</c> (or a new id) when omitted.</param>
    /// <returns>The route.</returns>
    public static Route FromS421(S421Route s421, string? id = null)
    {
        ArgumentNullException.ThrowIfNull(s421);

        var route = new Route(id ?? s421.RouteId, s421.Info?.Name);
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

        ApplyLegValues(route, legValues);

        if (elements.Count > 0 && elements[0].Etd is { } departure)
        {
            route.SetDeparture(departure);
        }

        return route;
    }

    /// <summary>
    /// Sets a file's per-leg passage values on <paramref name="route"/>: when
    /// every leg has a value for a field, the one most legs share (the first
    /// leg's on a tie) becomes the passage value and the rest stay leg
    /// overrides; <see cref="Route.SetLegOverride"/> drops an override equal
    /// to it. Shared with the RTZ mapping.
    /// </summary>
    /// <param name="route">The route, with its legs in place.</param>
    /// <param name="legValues">Each leg's values, in leg order.</param>
    internal static void ApplyLegValues(Route route, IReadOnlyList<IReadOnlyDictionary<RoutePassageField, double?>> legValues)
    {
        foreach (var field in LegFields.Append(RoutePassageField.Speed))
        {
            var values = legValues.Select(v => v.GetValueOrDefault(field)).ToList();
            if (values.Count > 0 && values.All(v => v is not null))
            {
                var shared = values
                    .Select((v, i) => (Value: v!.Value, Index: i))
                    .GroupBy(v => v.Value)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Min(v => v.Index))
                    .First().Key;
                route.SetPassageValue(field, shared);
            }

            for (var i = 0; i < legValues.Count; i++)
            {
                route.SetLegOverride(i, field, legValues[i].GetValueOrDefault(field));
            }
        }
    }

    /// <summary>
    /// A received, read-only route from an S-421 route read from
    /// <paramref name="filePath"/> (#960). It starts in Native mode, has a
    /// new id, and keeps the file's identity as its <see cref="Route.Source"/>.
    /// </summary>
    /// <param name="s421">The S-421 route.</param>
    /// <param name="filePath">The file it was read from.</param>
    /// <returns>The received route.</returns>
    public static Route ToReceived(S421Route s421, string filePath)
    {
        ArgumentNullException.ThrowIfNull(s421);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var route = FromS421(s421, Guid.NewGuid().ToString("n"));
        if (string.IsNullOrWhiteSpace(route.Name))
            route.Info.Name = s421.RouteId ?? Path.GetFileNameWithoutExtension(filePath);
        route.MarkReceived(
            new RouteSource(RouteSourceFormats.S421, s421.FormatVersion, filePath, s421.RouteId, s421.EditionNumber, s421.Info?.Status),
            RouteDisplayMode.Native);
        route.Preserved = Preserve(s421);
        return route;
    }

    /// <summary>The preserved content of an S-421 route: all of it, for writing back.</summary>
    /// <param name="s421">The S-421 route.</param>
    /// <returns>The preserved content; its schedule count excludes the schedule the passage reads.</returns>
    internal static RoutePreservedContent Preserve(S421Route s421)
        => new(RouteSourceFormats.S421, s421, s421.Waypoints.Count, Math.Max(0, s421.Schedules.Count - (Planning(s421) is null ? 0 : 1)));

    private static S421RouteInfo ToS421(RouteInfo info, RouteExchangeIdentity identity, S421RouteInfo? preserved) => new()
    {
        Id = "RTE.INFO",
        Name = info.Name,
        Author = info.Author,
        Description = info.Description,
        Status = identity.Status,
        EditionTime = identity.Kept ? preserved?.EditionTime : null,
        ValidityStart = info.ValidityStart,
        ValidityEnd = info.ValidityEnd,
        DeparturePortId1 = info.DeparturePortId,
        DeparturePortId2 = preserved?.DeparturePortId2,
        DeparturePortCall = preserved?.DeparturePortCall,
        ArrivalPortId1 = info.ArrivalPortId,
        ArrivalPortId2 = preserved?.ArrivalPortId2,
        ArrivalPortCall = preserved?.ArrivalPortCall,
        PreviousRouteReference = identity.Kept ? preserved?.PreviousRouteReference : identity.PreviousRouteReference,
        NextRouteReference = identity.Kept ? preserved?.NextRouteReference : null,
        Vessel = info.Vessel is { } vessel
            ? new S421VesselInfo
            {
                VesselType = preserved?.Vessel?.VesselType,
                Name = vessel.Name,
                Mmsi = vessel.Mmsi,
                Imo = vessel.Imo,
                Callsign = vessel.Callsign,
                VoyageId = preserved?.Vessel?.VoyageId,
                HeightMeters = preserved?.Vessel?.HeightMeters,
                LengthMeters = vessel.LengthMeters,
                BeamMeters = vessel.BeamMeters,
            }
            : null,
        ExtraAttributes = preserved?.ExtraAttributes ?? NoExtras,
    };

    // A manual schedule with each waypoint's planned speed (its outgoing
    // leg's effective speed) and the departure as the first ETD, when the
    // route has either.
    private static IReadOnlyList<S421Schedule> Schedule(
        Route route, IReadOnlyList<S421Waypoint> waypoints, DateTimeOffset? departure, Func<int, RoutePassageField, double?> value)
    {
        var speeds = Enumerable.Range(0, route.Legs.Count).Select(i => value(i, RoutePassageField.Speed)).ToList();
        if (departure is null && speeds.All(s => s is null))
        {
            return [];
        }

        var elements = waypoints.Select((w, i) => new S421ScheduleElement
        {
            Id = $"RTE.SCHED.1.MAN.{i + 1}",
            WaypointNumber = w.WaypointNumber,
            PlannedSpeedOverGround = i < speeds.Count ? speeds[i] : null,
            Etd = i == 0 ? departure : null,
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

    // The schedule elements a passage reads.
    private static IReadOnlyList<S421ScheduleElement> PlanningElements(S421Route route)
        => Planning(route)?.Variant.Elements ?? [];

    /// <summary>
    /// The schedule variant a passage reads its speeds and departure from
    /// (#391 concern 3): the first calculated schedule, else the first manual
    /// one, else the first schedule's first variant; with its schedule's index.
    /// </summary>
    internal static (int Index, S421ScheduleVariant Variant)? Planning(S421Route route)
        => route.GetPlanningSchedule();
}
