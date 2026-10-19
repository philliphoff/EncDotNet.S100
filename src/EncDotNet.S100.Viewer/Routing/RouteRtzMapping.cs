using System.Xml.Linq;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Routes.Rtz;
using EncDotNet.S100.Viewer.Routing.Exchange;

namespace EncDotNet.S100.Viewer.Routing;

/// <summary>
/// Projects an editable <see cref="Route"/> onto an RTZ route plan
/// (<see cref="RtzRoute"/>), which <see cref="RtzWriter"/> writes as RTZ 1.0,
/// 1.1 or 1.2, and back (#961).
/// </summary>
/// <remarks>
/// <para>
/// The mapping follows the RTZ column of the table on
/// <see cref="RoutePassage"/>. In RTZ a waypoint's <c>leg</c> and schedule
/// element describe the leg directed to it, so leg <c>i</c> of the route is
/// read from and written to waypoint <c>i + 1</c>. Cross-track limits are in
/// nautical miles in RTZ and in metres here. Values a waypoint doesn't set
/// come from the <c>defaultWaypoint</c> on import; export writes every
/// waypoint's values in full and no <c>defaultWaypoint</c>.
/// </para>
/// <para>
/// Import reads speeds and the departure from the planning schedule (the
/// first calculated schedule, else the first manual one; the calculated
/// value wins where both have one). A leg with no speed of its own takes the
/// speed its ETD and ETA imply. Draft is the deeper of
/// <c>draughtForward</c> and <c>draughtAft</c>. As in S-421, a value every leg
/// has is lifted into the passage.
/// </para>
/// <para>
/// Export writes one schedule: a manual table with each leg's speed and the
/// departure as the first ETD, and, when the route has a departure and a
/// speed on every leg, a calculated table with each waypoint's ETA (a copy
/// whose file had a calculated table always gets one, for its details). Content
/// the model can't hold (other schedules, schedule details, the first
/// waypoint's leg, masthead and notes, trim, vessel details, extensions and
/// unknown attributes) comes back from the preserved file: route-wide
/// content always, per-waypoint content while the copy has the same number
/// of waypoints.
/// </para>
/// </remarks>
internal static class RouteRtzMapping
{
    private const double MetresPerNauticalMile = 1852.0;

    private static readonly IReadOnlyDictionary<XName, string> NoExtras = new Dictionary<XName, string>();

    /// <summary>An editable route from an RTZ route.</summary>
    /// <param name="rtz">The RTZ route.</param>
    /// <param name="id">The route's id; a new id when omitted.</param>
    /// <returns>The route.</returns>
    public static Route FromRtz(RtzRoute rtz, string? id = null)
    {
        ArgumentNullException.ThrowIfNull(rtz);

        var info = rtz.Info;
        var route = new Route(id, string.IsNullOrWhiteSpace(info.RouteName) ? null : info.RouteName);
        route.Info.Author = info.RouteAuthor;
        route.Info.ValidityStart = info.ValidityPeriodStart;
        route.Info.ValidityEnd = info.ValidityPeriodStop;
        if (info.VesselName is not null || info.VesselMmsi is not null || info.VesselImo is not null)
            route.Info.Vessel = new RouteVesselInfo { Name = info.VesselName, Mmsi = info.VesselMmsi, Imo = info.VesselImo };

        for (var i = 0; i < rtz.Waypoints.Count; i++)
        {
            var source = rtz.Waypoints[i];
            var waypoint = route.AppendWaypoint(source.Position);
            waypoint.Number = source.Id;
            waypoint.Name = source.Name;
            waypoint.TurnRadiusNm = rtz.GetEffectiveRadius(i);
        }

        // Geometry first: implied speeds need each leg's distance.
        var legs = Enumerable.Range(0, route.Legs.Count).Select(i => rtz.GetEffectiveLeg(i + 1)).ToList();
        for (var i = 0; i < route.Legs.Count; i++)
        {
            var leg = route.Legs[i];
            var source = legs[i];
            leg.GeometryType = source?.GeometryType == RtzGeometryType.Orthodrome ? RouteLegGeometryType.Geodesic : RouteLegGeometryType.Loxodrome;
            if (source is null)
                continue;
            leg.SafetyContourMeters = source.SafetyContour;
            leg.SafetyDepthMeters = source.SafetyDepth;
            leg.SpeedOverGroundMinKnots = source.SpeedMin;
            leg.SpeedOverGroundMaxKnots = source.SpeedMax;
            leg.DynamicUnderKeelClearanceMeters = source.DynamicUkc;
            leg.Note = source.LegNote2;
        }

        var schedule = rtz.GetPlanningSchedule();
        var legValues = new List<IReadOnlyDictionary<RoutePassageField, double?>>();
        for (var i = 0; i < route.Legs.Count; i++)
        {
            var source = legs[i];
            legValues.Add(new Dictionary<RoutePassageField, double?>
            {
                [RoutePassageField.Draft] = source?.MaxDraught,
                [RoutePassageField.RequiredUkc] = source?.StaticUkc,
                [RoutePassageField.PortCrossTrack] = Metres(source?.PortsideXtd),
                [RoutePassageField.StarboardCrossTrack] = Metres(source?.StarboardXtd),
                [RoutePassageField.Speed] = schedule is null ? null : LegSpeed(route, rtz, schedule, i),
            });
        }
        RouteS421Mapping.ApplyLegValues(route, legValues);

        if (schedule?.ElementFor(rtz.Waypoints[0].Id) is { } first && (first.Departure ?? first.Eta) is { } departure)
            route.SetDeparture(departure);
        return route;
    }

    /// <summary>
    /// A received, read-only route from an RTZ route read from
    /// <paramref name="filePath"/>. RTZ has no portrayal of its own, so it
    /// starts in Planning mode; RTZ gives it no route ID or edition.
    /// </summary>
    /// <param name="rtz">The RTZ route.</param>
    /// <param name="filePath">The file it was read from.</param>
    /// <returns>The received route.</returns>
    public static Route ToReceived(RtzRoute rtz, string filePath)
    {
        ArgumentNullException.ThrowIfNull(rtz);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var route = FromRtz(rtz, Guid.NewGuid().ToString("n"));
        if (string.IsNullOrWhiteSpace(route.Name))
            route.Info.Name = Path.GetFileNameWithoutExtension(filePath);
        route.MarkReceived(
            new RouteSource(RouteSourceFormats.Rtz, rtz.Version, filePath, RouteId: null, Edition: null, Status: null),
            RouteDisplayMode.Planning);
        route.Preserved = Preserve(rtz);
        return route;
    }

    /// <summary>The preserved content of an RTZ route: all of it, for writing back.</summary>
    /// <param name="rtz">The RTZ route.</param>
    /// <returns>The preserved content; its schedule count excludes the planning schedule.</returns>
    internal static RoutePreservedContent Preserve(RtzRoute rtz)
        => new(RouteSourceFormats.Rtz, rtz, rtz.Waypoints.Count, Math.Max(0, rtz.Schedules.Count - (rtz.GetPlanningSchedule() is null ? 0 : 1)));

    /// <summary>The RTZ projection of <paramref name="route"/>.</summary>
    /// <param name="route">The route; it needs at least two waypoints to be written.</param>
    /// <param name="identity">How the route is identified (decision 17). RTZ has no route ID, so only the status of a kept identity is written.</param>
    /// <param name="includeCheckSettings">
    /// Whether to write the check settings (draft and required UKC on each
    /// leg, the departure). Without them a received route writes its file's
    /// values and an owned route none (decision 16).
    /// </param>
    /// <param name="preserved">The RTZ route the viewer's model came from, written back where it still fits.</param>
    /// <param name="writePreservedSchedules">Whether to write <paramref name="preserved"/>'s other schedules back.</param>
    /// <param name="version">The RTZ version the route is for.</param>
    /// <returns>The RTZ route.</returns>
    public static RtzRoute ToRtz(
        Route route,
        RouteExchangeIdentity? identity = null,
        bool includeCheckSettings = true,
        RtzRoute? preserved = null,
        bool writePreservedSchedules = false,
        string version = RtzVersions.Latest)
    {
        ArgumentNullException.ThrowIfNull(route);

        double? Value(int leg, RoutePassageField field)
        {
            if (!RoutePassageFields.IsCheckSetting(field) || includeCheckSettings)
                return route.GetEffectiveLegValue(leg, field);
            return route.IsReadOnly ? route.GetFileLegValue(leg, field) : null;
        }

        var departure = includeCheckSettings ? route.Passage.Departure : route.FileDeparture;
        var sameShape = preserved is not null && preserved.Waypoints.Count == route.Waypoints.Count;
        var ids = WaypointIds(route);

        var waypoints = new List<RtzWaypoint>();
        for (var i = 0; i < route.Waypoints.Count; i++)
        {
            var waypoint = route.Waypoints[i];
            var source = sameShape ? preserved!.Waypoints[i] : null;
            waypoints.Add(new RtzWaypoint
            {
                Id = ids[i],
                Revision = source?.Revision,
                Name = waypoint.Name,
                Radius = waypoint.TurnRadiusNm,
                Position = waypoint.Position,
                // The first waypoint's leg belongs to no leg of the route.
                Leg = i == 0 ? source?.Leg : Leg(route, i - 1, Value, sameShape ? preserved!.GetEffectiveLeg(i) : null),
                Extensions = source?.Extensions ?? [],
                ExtraAttributes = source?.ExtraAttributes ?? NoExtras,
            });
        }

        var schedules = new List<RtzSchedule>();
        var preservedPlanning = preserved?.GetPlanningSchedule();
        if (route.IsReadOnly && preserved is not null && sameShape)
        {
            // A received route keeps its own schedules; asked-for check
            // settings move the planning schedule to the local departure.
            foreach (var schedule in preserved.Schedules)
            {
                schedules.Add(includeCheckSettings && ReferenceEquals(schedule, preservedPlanning)
                    && route.LocalCheckSettings.Departure is { } local && route.FileDeparture is { } file
                    ? Shift(schedule, local - file)
                    : schedule);
            }
        }
        else
        {
            if (Schedule(route, ids, departure, preservedPlanning, sameShape) is { } planning)
                schedules.Add(planning);
            if (preserved is not null && writePreservedSchedules)
            {
                foreach (var schedule in preserved.Schedules.Where(s => !ReferenceEquals(s, preservedPlanning)))
                    schedules.Add(WithId(schedule, schedules.Select(s => s.Id).ToHashSet()));
            }
        }

        identity ??= new RouteExchangeIdentity(route.Id, 1, RouteExchangeIdentity.StatusInitial, null, null, Kept: false);
        var info = preserved?.Info;
        var vessel = route.Info.Vessel;
        return new RtzRoute
        {
            Version = version,
            Info = new RtzRouteInfo
            {
                RouteName = route.Name,
                RouteAuthor = route.Info.Author,
                RouteStatus = identity.Kept ? info?.RouteStatus : null,
                ValidityPeriodStart = route.Info.ValidityStart,
                ValidityPeriodStop = route.Info.ValidityEnd,
                VesselName = vessel?.Name,
                VesselMmsi = Digits(vessel?.Mmsi),
                VesselImo = Digits(vessel?.Imo),
                VesselVoyage = info?.VesselVoyage,
                VesselDisplacement = info?.VesselDisplacement,
                VesselCargo = info?.VesselCargo,
                VesselGm = info?.VesselGm,
                OptimizationMethod = info?.OptimizationMethod,
                VesselMaxRoll = info?.VesselMaxRoll,
                VesselMaxWave = info?.VesselMaxWave,
                VesselMaxWind = info?.VesselMaxWind,
                VesselSpeedMax = info?.VesselSpeedMax,
                VesselServiceMin = info?.VesselServiceMin,
                VesselServiceMax = info?.VesselServiceMax,
                RouteChangesHistory = info?.RouteChangesHistory,
                Extensions = info?.Extensions ?? [],
                ExtraAttributes = info?.ExtraAttributes ?? NoExtras,
            },
            Waypoints = waypoints,
            WaypointsExtensions = preserved?.WaypointsExtensions ?? [],
            Schedules = schedules,
            SchedulesExtensions = preserved?.SchedulesExtensions ?? [],
            Extensions = preserved?.Extensions ?? [],
            ExtraAttributes = preserved?.ExtraAttributes ?? NoExtras,
            NamespaceDeclarations = preserved?.NamespaceDeclarations ?? new Dictionary<string, string>(),
        };
    }

    private static RtzLeg Leg(Route route, int legIndex, Func<int, RoutePassageField, double?> value, RtzLeg? source)
    {
        var leg = route.Legs[legIndex];
        var draft = value(legIndex, RoutePassageField.Draft);
        // A file's trim survives while its deeper draught is still the draft.
        var keepTrim = draft is not null && source?.MaxDraught == draft;
        return new RtzLeg
        {
            StarboardXtd = NauticalMiles(value(legIndex, RoutePassageField.StarboardCrossTrack)),
            PortsideXtd = NauticalMiles(value(legIndex, RoutePassageField.PortCrossTrack)),
            SafetyContour = leg.SafetyContourMeters,
            SafetyDepth = leg.SafetyDepthMeters,
            GeometryType = leg.GeometryType == RouteLegGeometryType.Geodesic ? RtzGeometryType.Orthodrome : RtzGeometryType.Loxodrome,
            SpeedMin = leg.SpeedOverGroundMinKnots,
            SpeedMax = leg.SpeedOverGroundMaxKnots,
            DraughtForward = keepTrim ? source!.DraughtForward : draft,
            DraughtAft = keepTrim ? source!.DraughtAft : draft,
            StaticUkc = value(legIndex, RoutePassageField.RequiredUkc),
            DynamicUkc = leg.DynamicUnderKeelClearanceMeters,
            Masthead = source?.Masthead,
            LegReport = source?.LegReport,
            LegInfo = source?.LegInfo,
            LegNote1 = source?.LegNote1,
            LegNote2 = leg.Note,
            Extensions = source?.Extensions ?? [],
            ExtraAttributes = source?.ExtraAttributes ?? NoExtras,
        };
    }

    // The planning schedule: a manual table with each leg's speed (on the
    // waypoint the leg leads to) and the departure as the first ETD, plus a
    // calculated table of ETAs when every leg has a speed and there is a
    // departure. The preserved planning schedule keeps its id and name; while
    // the shape is the same its other details (windows, stays, weather,
    // fuel, notes, extensions) are written back too.
    private static RtzSchedule? Schedule(Route route, IReadOnlyList<int> ids, DateTimeOffset? departure, RtzSchedule? preserved, bool sameShape)
    {
        // Per-waypoint details only line up while the shape is the same.
        var manualSource = sameShape ? preserved?.Manual : null;
        var calculatedSource = sameShape ? preserved?.Calculated : null;
        var speeds = Enumerable.Range(0, route.Legs.Count).Select(i => route.GetEffectiveLegValue(i, RoutePassageField.Speed)).ToList();
        if (departure is null && speeds.All(s => s is null))
            return null;

        var manual = ids.Select((id, i) => Element(
            manualSource?.ElementFor(id),
            id,
            etd: i == 0 ? departure : null,
            eta: null,
            speed: i == 0 ? null : speeds[i - 1])).ToList();

        // The calculated table carries ETAs when they can be worked out, and
        // a preserved calculated table's details (weather, fuel…) either way.
        RtzScheduleTable? calculated = null;
        var times = departure is { } start && speeds.All(s => s > 0)
            ? PassageSchedule.Compute(RouteEtas.ToPassageLegs(route), start)
            : null;
        if (times is not null || calculatedSource is not null)
        {
            calculated = new RtzScheduleTable
            {
                Elements = ids.Select((id, i) =>
                {
                    var eta = i == 0 ? null : times?.Legs[i - 1].End;
                    return Element(
                        calculatedSource?.ElementFor(id),
                        id,
                        etd: i == 0 ? times?.Departure : i < ids.Count - 1 ? eta : null,
                        eta: eta,
                        speed: i == 0 ? null : speeds[i - 1]);
                }).ToList(),
                Extensions = calculatedSource?.Extensions ?? [],
            };
        }

        return new RtzSchedule
        {
            Id = preserved?.Id ?? 1,
            Name = preserved?.Name ?? "Planned",
            Manual = new RtzScheduleTable { Elements = manual, Extensions = manualSource?.Extensions ?? [] },
            Calculated = calculated,
            Extensions = preserved?.Extensions ?? [],
            ExtraAttributes = preserved?.ExtraAttributes ?? NoExtras,
        };
    }

    private static RtzScheduleElement Element(RtzScheduleElement? source, int waypointId, DateTimeOffset? etd, DateTimeOffset? eta, double? speed) => new()
    {
        WaypointId = waypointId,
        Etd = etd,
        Eta = eta,
        Speed = speed,
        EtdWindowBefore = source?.EtdWindowBefore,
        EtdWindowAfter = source?.EtdWindowAfter,
        EtaWindowBefore = source?.EtaWindowBefore,
        EtaWindowAfter = source?.EtaWindowAfter,
        Stay = source?.Stay,
        SpeedWindow = source?.SpeedWindow,
        WindSpeed = source?.WindSpeed,
        WindDirection = source?.WindDirection,
        CurrentSpeed = source?.CurrentSpeed,
        CurrentDirection = source?.CurrentDirection,
        WindLoss = source?.WindLoss,
        WaveLoss = source?.WaveLoss,
        TotalLoss = source?.TotalLoss,
        Rpm = source?.Rpm,
        Pitch = source?.Pitch,
        Fuel = source?.Fuel,
        RelFuelSave = source?.RelFuelSave,
        AbsFuelSave = source?.AbsFuelSave,
        Note = source?.Note,
        Extensions = source?.Extensions ?? [],
        ExtraAttributes = source?.ExtraAttributes ?? NoExtras,
    };

    // A schedule with every ETD and ETA moved by delta.
    private static RtzSchedule Shift(RtzSchedule schedule, TimeSpan delta)
    {
        RtzScheduleTable? ShiftTable(RtzScheduleTable? table) => table is null ? null : new RtzScheduleTable
        {
            Elements = table.Elements.Select(e => Element(e, e.WaypointId, e.Etd + delta, e.Eta + delta, e.Speed)).ToList(),
            Extensions = table.Extensions,
        };
        return new RtzSchedule
        {
            Id = schedule.Id,
            Name = schedule.Name,
            Manual = ShiftTable(schedule.Manual),
            Calculated = ShiftTable(schedule.Calculated),
            Extensions = schedule.Extensions,
            ExtraAttributes = schedule.ExtraAttributes,
        };
    }

    // Preserved schedules keep their id unless one is already taken.
    private static RtzSchedule WithId(RtzSchedule schedule, HashSet<int> taken)
    {
        if (!taken.Contains(schedule.Id))
            return schedule;
        var id = taken.Max() + 1;
        return new RtzSchedule
        {
            Id = id,
            Name = schedule.Name,
            Manual = schedule.Manual,
            Calculated = schedule.Calculated,
            Extensions = schedule.Extensions,
            ExtraAttributes = schedule.ExtraAttributes,
        };
    }

    // Waypoint numbers as RTZ ids when they're unique and non-negative,
    // else 1…n.
    private static List<int> WaypointIds(Route route)
    {
        var numbers = route.Waypoints.Select((w, i) => w.Number ?? i + 1).ToList();
        return numbers.All(n => n >= 0) && numbers.Distinct().Count() == numbers.Count
            ? numbers
            : Enumerable.Range(1, numbers.Count).ToList();
    }

    // Leg i's speed: the element of the waypoint it leads to, else the speed
    // the leg's ETD and ETA imply.
    private static double? LegSpeed(Route route, RtzRoute rtz, RtzSchedule schedule, int legIndex)
    {
        var to = schedule.ElementFor(rtz.Waypoints[legIndex + 1].Id);
        if (to?.Speed is { } speed)
            return speed;
        if (schedule.ElementFor(rtz.Waypoints[legIndex].Id)?.Departure is not { } leave || to?.Eta is not { } arrive || arrive <= leave)
            return null;
        return Math.Round(route.ComputeLegMetrics(legIndex).DistanceNm / (arrive - leave).TotalHours, 2);
    }

    private static double? Metres(double? nauticalMiles) => nauticalMiles * MetresPerNauticalMile;

    // Rounded so a value read from RTZ goes back as written.
    private static double? NauticalMiles(double? metres) => metres is { } m ? Math.Round(m / MetresPerNauticalMile, 9) : null;

    /// <summary>An MMSI or IMO number as RTZ writes it (digits only), or <c>null</c> when it isn't one.</summary>
    internal static string? Digits(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is { Length: > 0 } && trimmed.All(char.IsAsciiDigit) ? trimmed : null;
    }
}
