using System.Collections.Immutable;
using System.Globalization;

namespace EncDotNet.S100.Datasets.S421.DataModel;

/// <summary>
/// Strongly-typed projection of an <see cref="S421Dataset"/> route plan.
/// </summary>
/// <remarks>
/// <para>
/// This type wraps the feature-centric <see cref="S421Dataset"/> in an
/// object graph organised around the spec's domain concepts: a single
/// <see cref="S421Route"/> with resolved waypoints, legs, action points,
/// and schedules.
/// </para>
/// <para>
/// The projection is lossy by design — anything the typed model does not
/// understand is preserved on the source <see cref="S421Dataset"/> and on
/// the typed objects' <c>ExtraAttributes</c> dictionaries. Projection
/// failures surface as <see cref="S421ProjectionDiagnostic"/> entries
/// rather than exceptions, with the sole exception of a missing
/// <c>Route</c> feature.
/// </para>
/// </remarks>
public sealed class S421RoutePlan
{
    /// <summary>The dataset identifier carried by the source GML <c>Dataset</c> element.</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>The S-421 product identifier (typically <c>"S-421"</c>).</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>The route described by this dataset.</summary>
    public required S421Route Route { get; init; }

    /// <summary>The originating feature-bag dataset.</summary>
    public required S421Dataset Source { get; init; }

    /// <summary>
    /// Projects a feature-bag <see cref="S421Dataset"/> into the typed data
    /// model. Issues encountered during projection are reported via
    /// <paramref name="diagnostics"/>; the projection itself never throws
    /// for malformed input other than a missing <c>Route</c> feature.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the dataset contains no <c>Route</c> feature.
    /// </exception>
    public static S421RoutePlan From(S421Dataset dataset, out IReadOnlyList<S421ProjectionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var diags = new List<S421ProjectionDiagnostic>();

        var byId = BuildIdIndex(dataset);
        var routeFeature = dataset.Features.FirstOrDefault(f => f.FeatureType == "Route")
            ?? throw new InvalidOperationException("Dataset contains no Route feature.");

        var routeInfo = ResolveRouteInfo(routeFeature, byId, diags);
        var (waypoints, legs) = ResolveWaypointsAndLegs(routeFeature, byId, diags);
        var actionPoints = ResolveActionPoints(routeFeature, byId, diags);
        var schedules = ResolveSchedules(routeFeature, byId, diags);

        var route = new S421Route
        {
            Id = routeFeature.Id,
            FormatVersion = routeFeature.Attributes.GetValueOrDefault("routeFormatVersion"),
            RouteId = routeFeature.Attributes.GetValueOrDefault("routeID"),
            EditionNumber = ParseInt(routeFeature.Attributes.GetValueOrDefault("routeEditionNo")),
            Info = routeInfo,
            Waypoints = waypoints,
            Legs = legs,
            ActionPoints = actionPoints,
            Schedules = schedules,
            ExtraAttributes = ExcludeKnown(routeFeature.Attributes,
                "routeFormatVersion", "routeID", "routeEditionNo"),
        };

        diagnostics = diags;
        return new S421RoutePlan
        {
            DatasetIdentifier = dataset.DatasetIdentifier,
            ProductIdentifier = dataset.ProductIdentifier,
            Route = route,
            Source = dataset,
        };
    }

    // ── ID index ─────────────────────────────────────────────────

    private static Dictionary<string, object> BuildIdIndex(S421Dataset dataset)
    {
        var index = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in dataset.Features)
            if (!string.IsNullOrEmpty(f.Id)) index[f.Id] = f;
        foreach (var i in dataset.InformationTypes)
            if (!string.IsNullOrEmpty(i.Id)) index[i.Id] = i;
        return index;
    }

    private static string NormaliseHref(string href) =>
        href.StartsWith('#') ? href[1..] : href;

    private static T? Resolve<T>(Dictionary<string, object> byId, string href, string role,
        List<S421ProjectionDiagnostic> diags, string? relatedId = null) where T : class
    {
        var key = NormaliseHref(href);
        if (byId.TryGetValue(key, out var obj) && obj is T typed) return typed;

        diags.Add(new S421ProjectionDiagnostic
        {
            Severity = S421DiagnosticSeverity.Warning,
            Message = $"Unresolved {role} reference '{href}'.",
            RelatedId = relatedId,
        });
        return null;
    }

    /// <summary>
    /// Resolves a container reference whose target may live under either
    /// <c>&lt;member&gt;</c> (feature) or <c>&lt;imember&gt;</c>
    /// (information type) in different IEC sample fixtures, returning the
    /// target's xlink references and id. Container objects (Route,
    /// RouteWaypoints, RouteSchedules, RouteActionPoints) carry no
    /// attribute payload of their own that the typed model uses, so a
    /// uniform reference list is sufficient.
    /// </summary>
    private static (string Id, ImmutableArray<S421Reference> References)? ResolveContainer(
        Dictionary<string, object> byId, string href, string role,
        List<S421ProjectionDiagnostic> diags, string? relatedId = null)
    {
        var key = NormaliseHref(href);
        if (byId.TryGetValue(key, out var obj))
        {
            return obj switch
            {
                S421Feature f => (f.Id, f.References),
                S421InformationType i => (i.Id, i.References),
                _ => null,
            };
        }

        diags.Add(new S421ProjectionDiagnostic
        {
            Severity = S421DiagnosticSeverity.Warning,
            Message = $"Unresolved {role} reference '{href}'.",
            RelatedId = relatedId,
        });
        return null;
    }

    // ── RouteInfo ────────────────────────────────────────────────

    private static S421RouteInfo? ResolveRouteInfo(S421Feature route,
        Dictionary<string, object> byId, List<S421ProjectionDiagnostic> diags)
    {
        var infoRef = route.References.FirstOrDefault(r => r.Role == "routeInfo");
        if (infoRef is null) return null;

        var infoType = Resolve<S421InformationType>(byId, infoRef.Href, "routeInfo", diags, route.Id);
        if (infoType is null) return null;

        var a = infoType.Attributes;
        return new S421RouteInfo
        {
            Id = infoType.Id,
            Name = a.GetValueOrDefault("routeInfoName"),
            Author = a.GetValueOrDefault("routeInfoAuthor"),
            Description = a.GetValueOrDefault("routeInfoDescription"),
            Status = ParseInt(a.GetValueOrDefault("routeInfoStatus")),
            EditionTime = ParseDateTime(a.GetValueOrDefault("routeInfoEditionTime"), infoType.Id, "routeInfoEditionTime", diags),
            ValidityStart = ParseDateTime(a.GetValueOrDefault("routeInfoValidityStart"), infoType.Id, "routeInfoValidityStart", diags),
            ValidityEnd = ParseDateTime(a.GetValueOrDefault("routeInfoValidityEnd"), infoType.Id, "routeInfoValidityEnd", diags),
            DeparturePortId1 = a.GetValueOrDefault("routeInfoDeparturePortID1"),
            DeparturePortId2 = a.GetValueOrDefault("routeInfoDeparturePortID2"),
            DeparturePortCall = a.GetValueOrDefault("routeInfoDeparturePortCall"),
            ArrivalPortId1 = a.GetValueOrDefault("routeInfoArrivalPortID1"),
            ArrivalPortId2 = a.GetValueOrDefault("routeInfoArrivalPortID2"),
            ArrivalPortCall = a.GetValueOrDefault("routeInfoArrivalPortCall"),
            PreviousRouteReference = a.GetValueOrDefault("routeInfoReferencePrevRoute"),
            NextRouteReference = a.GetValueOrDefault("routeInfoReferenceNextRoute"),
            Vessel = BuildVessel(a),
            ExtraAttributes = ExcludeKnown(a,
                "routeInfoName", "routeInfoAuthor", "routeInfoDescription", "routeInfoStatus",
                "routeInfoEditionTime", "routeInfoValidityStart", "routeInfoValidityEnd",
                "routeInfoDeparturePortID1", "routeInfoDeparturePortID2", "routeInfoDeparturePortCall",
                "routeInfoArrivalPortID1", "routeInfoArrivalPortID2", "routeInfoArrivalPortCall",
                "routeInfoReferencePrevRoute", "routeInfoReferenceNextRoute",
                "routeInfoVesselType", "routeInfoVesselName", "routeInfoVesselMMSI",
                "routeInfoVesselCallsign", "routeInfoVesselIMO", "routeInfoVesselVoyage",
                "routeInfoVesselHeight", "routeInfoVesselLength", "routeInfoVesselBeam"),
        };
    }

    private static S421VesselInfo? BuildVessel(ImmutableDictionary<string, string> a)
    {
        bool any =
            a.ContainsKey("routeInfoVesselType") || a.ContainsKey("routeInfoVesselName") ||
            a.ContainsKey("routeInfoVesselMMSI") || a.ContainsKey("routeInfoVesselCallsign") ||
            a.ContainsKey("routeInfoVesselIMO") || a.ContainsKey("routeInfoVesselVoyage") ||
            a.ContainsKey("routeInfoVesselHeight") || a.ContainsKey("routeInfoVesselLength") ||
            a.ContainsKey("routeInfoVesselBeam");
        if (!any) return null;

        return new S421VesselInfo
        {
            VesselType = ParseInt(a.GetValueOrDefault("routeInfoVesselType")),
            Name = a.GetValueOrDefault("routeInfoVesselName"),
            Mmsi = a.GetValueOrDefault("routeInfoVesselMMSI"),
            Callsign = a.GetValueOrDefault("routeInfoVesselCallsign"),
            Imo = a.GetValueOrDefault("routeInfoVesselIMO"),
            VoyageId = a.GetValueOrDefault("routeInfoVesselVoyage"),
            HeightMeters = ParseDouble(a.GetValueOrDefault("routeInfoVesselHeight")),
            LengthMeters = ParseDouble(a.GetValueOrDefault("routeInfoVesselLength")),
            BeamMeters = ParseDouble(a.GetValueOrDefault("routeInfoVesselBeam")),
        };
    }

    // ── Waypoints + legs ─────────────────────────────────────────

    private static (ImmutableArray<S421Waypoint>, ImmutableArray<S421Leg>) ResolveWaypointsAndLegs(
        S421Feature route, Dictionary<string, object> byId, List<S421ProjectionDiagnostic> diags)
    {
        var wptsRef = route.References.FirstOrDefault(r => r.Role == "routeWaypoints");
        if (wptsRef is null)
            return (ImmutableArray<S421Waypoint>.Empty, ImmutableArray<S421Leg>.Empty);

        var container = ResolveContainer(byId, wptsRef.Href, "routeWaypoints", diags, route.Id);
        if (container is null)
            return (ImmutableArray<S421Waypoint>.Empty, ImmutableArray<S421Leg>.Empty);

        var legs = ImmutableArray.CreateBuilder<S421Leg>();
        var seenLegs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var waypoints = ImmutableArray.CreateBuilder<S421Waypoint>();
        foreach (var wptRef in container.Value.References.Where(r => r.Role == "routeWaypoint"))
        {
            var wptFeature = Resolve<S421Feature>(byId, wptRef.Href, "routeWaypoint", diags, container.Value.Id);
            if (wptFeature is null) continue;

            S421Leg? leg = null;
            var legRef = wptFeature.References.FirstOrDefault(r => r.Role == "routeWaypointLeg");
            if (legRef is not null)
            {
                var legFeature = Resolve<S421Feature>(byId, legRef.Href, "routeWaypointLeg", diags, wptFeature.Id);
                if (legFeature is not null)
                {
                    leg = ProjectLeg(legFeature);
                    if (seenLegs.Add(legFeature.Id))
                        legs.Add(leg);
                }
            }

            waypoints.Add(ProjectWaypoint(wptFeature, leg, diags));
        }

        return (waypoints.ToImmutable(), legs.ToImmutable());
    }

    private static S421Waypoint ProjectWaypoint(S421Feature f, S421Leg? leg, List<S421ProjectionDiagnostic> diags)
    {
        GeoPosition position = default;
        if (!f.Points.IsDefaultOrEmpty)
        {
            var (lat, lon) = f.Points[0];
            position = new GeoPosition(lat, lon);
        }
        else
        {
            diags.Add(new S421ProjectionDiagnostic
            {
                Severity = S421DiagnosticSeverity.Warning,
                Message = "RouteWaypoint has no point geometry.",
                RelatedId = f.Id,
            });
        }

        return new S421Waypoint
        {
            Id = f.Id,
            WaypointNumber = ParseInt(f.Attributes.GetValueOrDefault("routeWaypointID")),
            Name = f.Attributes.GetValueOrDefault("routeWaypointName"),
            ExternalReferenceId = f.Attributes.GetValueOrDefault("routeWaypointExternalReferenceID"),
            Fixed = ParseBool(f.Attributes.GetValueOrDefault("routeWaypointFixed")),
            TurnRadius = ParseDouble(f.Attributes.GetValueOrDefault("routeWaypointTurnRadius")),
            Position = position,
            OutgoingLeg = leg,
            ExtraAttributes = ExcludeKnown(f.Attributes,
                "routeWaypointID", "routeWaypointName", "routeWaypointExternalReferenceID",
                "routeWaypointFixed", "routeWaypointTurnRadius"),
        };
    }

    private static S421Leg ProjectLeg(S421Feature f)
    {
        var coords = ImmutableArray.CreateBuilder<GeoPosition>();
        foreach (var curve in f.Curves)
            foreach (var (lat, lon) in curve)
                coords.Add(new GeoPosition(lat, lon));

        var a = f.Attributes;
        return new S421Leg
        {
            Id = f.Id,
            Coordinates = coords.ToImmutable(),
            StarboardCrossTrackDistanceLimit = ParseDouble(a.GetValueOrDefault("routeWaypointLegStarboardXTDL")),
            PortCrossTrackDistanceLimit = ParseDouble(a.GetValueOrDefault("routeWaypointLegPortXTDL")),
            StarboardChannelLimit = ParseDouble(a.GetValueOrDefault("routeWaypointLegStarboardCL")),
            PortChannelLimit = ParseDouble(a.GetValueOrDefault("routeWaypointLegPortCL")),
            SafetyContour = ParseDouble(a.GetValueOrDefault("routeWaypointLegSafetyContour")),
            SafetyDepth = ParseDouble(a.GetValueOrDefault("routeWaypointLegSafetyDepth")),
            GeometryTypeCode = ParseInt(a.GetValueOrDefault("routeWaypointLegGeometryType")),
            SpeedOverGroundMin = ParseDouble(a.GetValueOrDefault("routeWaypointLegSOGMin")),
            SpeedOverGroundMax = ParseDouble(a.GetValueOrDefault("routeWaypointLegSOGMax")),
            SpeedThroughWaterMin = ParseDouble(a.GetValueOrDefault("routeWaypointLegSTWMin")),
            SpeedThroughWaterMax = ParseDouble(a.GetValueOrDefault("routeWaypointLegSTWMax")),
            Draft = ParseDouble(a.GetValueOrDefault("routeWaypointLegDraft")),
            StaticUnderKeelClearance = ParseDouble(a.GetValueOrDefault("routeWaypointLegStaticUKC")),
            DynamicUnderKeelClearance = ParseDouble(a.GetValueOrDefault("routeWaypointLegDynamicUKC")),
            SafetyMargin = ParseDouble(a.GetValueOrDefault("routeWaypointLegSafetyMargin")),
            Note = a.GetValueOrDefault("routeWaypointLegNote"),
            ExtraAttributes = ExcludeKnown(a,
                "routeWaypointLegStarboardXTDL", "routeWaypointLegPortXTDL",
                "routeWaypointLegStarboardCL", "routeWaypointLegPortCL",
                "routeWaypointLegSafetyContour", "routeWaypointLegSafetyDepth",
                "routeWaypointLegGeometryType",
                "routeWaypointLegSOGMin", "routeWaypointLegSOGMax",
                "routeWaypointLegSTWMin", "routeWaypointLegSTWMax",
                "routeWaypointLegDraft", "routeWaypointLegStaticUKC",
                "routeWaypointLegDynamicUKC", "routeWaypointLegSafetyMargin",
                "routeWaypointLegNote"),
        };
    }

    // ── Action points ────────────────────────────────────────────

    private static ImmutableArray<S421ActionPoint> ResolveActionPoints(S421Feature route,
        Dictionary<string, object> byId, List<S421ProjectionDiagnostic> diags)
    {
        var apRef = route.References.FirstOrDefault(r => r.Role == "routeActionPoints");
        if (apRef is null) return ImmutableArray<S421ActionPoint>.Empty;

        var container = ResolveContainer(byId, apRef.Href, "routeActionPoints", diags, route.Id);
        if (container is null) return ImmutableArray<S421ActionPoint>.Empty;

        var result = ImmutableArray.CreateBuilder<S421ActionPoint>();
        foreach (var apFeatureRef in container.Value.References.Where(r => r.Role == "routeActionPoint"))
        {
            var ap = Resolve<S421Feature>(byId, apFeatureRef.Href, "routeActionPoint", diags, container.Value.Id);
            if (ap is null) continue;
            result.Add(ProjectActionPoint(ap));
        }
        return result.ToImmutable();
    }

    private static S421ActionPoint ProjectActionPoint(S421Feature f)
    {
        S421ActionPointGeometryKind kind;
        ImmutableArray<GeoPosition> coords;
        switch (f.GeometryType)
        {
            case S421GeometryType.Point:
                kind = S421ActionPointGeometryKind.Point;
                coords = f.Points.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.Points.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray();
                break;
            case S421GeometryType.Curve:
                kind = S421ActionPointGeometryKind.Curve;
                coords = f.Curves.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.Curves.SelectMany(c => c).Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray();
                break;
            case S421GeometryType.Surface:
                kind = S421ActionPointGeometryKind.Surface;
                coords = f.ExteriorRing.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.ExteriorRing.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray();
                break;
            default:
                kind = S421ActionPointGeometryKind.Point;
                coords = ImmutableArray<GeoPosition>.Empty;
                break;
        }

        var a = f.Attributes;
        // Tolerate both spellings for the description attribute.
        var description = a.GetValueOrDefault("routeActionPointRequiredActionDescription")
            ?? a.GetValueOrDefault("routeActionPointRequredActionDescription");

        return new S421ActionPoint
        {
            Id = f.Id,
            ActionPointNumber = ParseInt(a.GetValueOrDefault("routeActionPointID")),
            Name = a.GetValueOrDefault("routeActionPointName"),
            RadiusNauticalMiles = ParseDouble(a.GetValueOrDefault("routeActionPointRadius")),
            TimeToActMinutes = ParseDouble(a.GetValueOrDefault("routeActionPointTimeToAct")),
            RequiredAction = ParseInt(a.GetValueOrDefault("routeActionPointRequiredAction")),
            RequiredActionDescription = description,
            GeometryKind = kind,
            Coordinates = coords,
            ExtraAttributes = ExcludeKnown(a,
                "routeActionPointID", "routeActionPointName", "routeActionPointRadius",
                "routeActionPointTimeToAct", "routeActionPointRequiredAction",
                "routeActionPointRequiredActionDescription",
                "routeActionPointRequredActionDescription"),
        };
    }

    // ── Schedules ────────────────────────────────────────────────

    private static ImmutableArray<S421Schedule> ResolveSchedules(S421Feature route,
        Dictionary<string, object> byId, List<S421ProjectionDiagnostic> diags)
    {
        var schRef = route.References.FirstOrDefault(r => r.Role == "routeSchedules");
        if (schRef is null) return ImmutableArray<S421Schedule>.Empty;

        var container = ResolveContainer(byId, schRef.Href, "routeSchedules", diags, route.Id);
        if (container is null) return ImmutableArray<S421Schedule>.Empty;

        var result = ImmutableArray.CreateBuilder<S421Schedule>();
        foreach (var entry in container.Value.References.Where(r => r.Role == "routeSchedule"))
        {
            var sched = Resolve<S421InformationType>(byId, entry.Href, "routeSchedule", diags, container.Value.Id);
            if (sched is null) continue;
            result.Add(ProjectSchedule(sched, byId, diags));
        }
        return result.ToImmutable();
    }

    private static S421Schedule ProjectSchedule(S421InformationType sched,
        Dictionary<string, object> byId, List<S421ProjectionDiagnostic> diags)
    {
        var variants = ImmutableArray.CreateBuilder<S421ScheduleVariant>();
        AddVariant(sched, "routeScheduleManual", S421ScheduleVariantKind.Manual, byId, diags, variants);
        AddVariant(sched, "routeScheduleCalculated", S421ScheduleVariantKind.Calculated, byId, diags, variants);
        AddVariant(sched, "routeScheduleRecommended", S421ScheduleVariantKind.Recommended, byId, diags, variants);

        var a = sched.Attributes;
        return new S421Schedule
        {
            Id = sched.Id,
            ScheduleNumber = ParseInt(a.GetValueOrDefault("routeScheduleID")),
            Name = a.GetValueOrDefault("routeScheduleName"),
            Variants = variants.ToImmutable(),
            ExtraAttributes = ExcludeKnown(a, "routeScheduleID", "routeScheduleName"),
        };
    }

    private static void AddVariant(S421InformationType schedule, string role, S421ScheduleVariantKind kind,
        Dictionary<string, object> byId, List<S421ProjectionDiagnostic> diags,
        ImmutableArray<S421ScheduleVariant>.Builder output)
    {
        var reference = schedule.References.FirstOrDefault(r => r.Role == role);
        if (reference is null) return;

        var variant = Resolve<S421InformationType>(byId, reference.Href, role, diags, schedule.Id);
        if (variant is null) return;

        var elements = ImmutableArray.CreateBuilder<S421ScheduleElement>();
        foreach (var elemRef in variant.References.Where(r => r.Role == "routeScheduleElement"))
        {
            var elem = Resolve<S421InformationType>(byId, elemRef.Href, "routeScheduleElement", diags, variant.Id);
            if (elem is null) continue;
            elements.Add(ProjectScheduleElement(elem, diags));
        }

        output.Add(new S421ScheduleVariant
        {
            Id = variant.Id,
            Kind = kind,
            Elements = elements.ToImmutable(),
        });
    }

    private static S421ScheduleElement ProjectScheduleElement(S421InformationType e, List<S421ProjectionDiagnostic> diags)
    {
        var a = e.Attributes;
        return new S421ScheduleElement
        {
            Id = e.Id,
            WaypointNumber = ParseInt(a.GetValueOrDefault("routeWaypointId")),
            PlannedSpeedOverGround = ParseDouble(a.GetValueOrDefault("routeScheduleElementPlanSOG")),
            Etd = ParseDateTime(a.GetValueOrDefault("routeScheduleElementETD"), e.Id, "routeScheduleElementETD", diags),
            Eta = ParseDateTime(a.GetValueOrDefault("routeScheduleElementETA"), e.Id, "routeScheduleElementETA", diags),
            EtdWindowBeforeMinutes = ParseInt(a.GetValueOrDefault("routeScheduleElementETDWindowBefore")),
            EtdWindowAfterMinutes = ParseInt(a.GetValueOrDefault("routeScheduleElementETDWindowAfter")),
            EtaWindowBeforeMinutes = ParseInt(a.GetValueOrDefault("routeScheduleElementETAWindowBefore")),
            EtaWindowAfterMinutes = ParseInt(a.GetValueOrDefault("routeScheduleElementETAWindowAfter")),
            Note = a.GetValueOrDefault("routeScheduleElementNote"),
            ExtraAttributes = ExcludeKnown(a,
                "routeWaypointId",
                "routeScheduleElementPlanSOG",
                "routeScheduleElementETD", "routeScheduleElementETA",
                "routeScheduleElementETDWindowBefore", "routeScheduleElementETDWindowAfter",
                "routeScheduleElementETAWindowBefore", "routeScheduleElementETAWindowAfter",
                "routeScheduleElementNote"),
        };
    }

    // ── Primitive parsing ────────────────────────────────────────

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : null;

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : null;

    private static bool? ParseBool(string? value) => value switch
    {
        null => null,
        "1" or "true" or "True" or "TRUE" => true,
        "0" or "false" or "False" or "FALSE" => false,
        _ => null,
    };

    private static DateTimeOffset? ParseDateTime(string? value, string relatedId, string code,
        List<S421ProjectionDiagnostic> diags)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var r))
            return r;

        diags.Add(new S421ProjectionDiagnostic
        {
            Severity = S421DiagnosticSeverity.Warning,
            Message = $"Could not parse '{code}' value '{value}' as a date/time.",
            RelatedId = relatedId,
        });
        return null;
    }

    private static ImmutableDictionary<string, string> ExcludeKnown(
        ImmutableDictionary<string, string> source, params string[] knownKeys)
    {
        var known = new HashSet<string>(knownKeys, StringComparer.OrdinalIgnoreCase);
        var b = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var (k, v) in source)
            if (!known.Contains(k)) b[k] = v;
        return b.ToImmutable();
    }
}
