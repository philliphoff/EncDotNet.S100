using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Gml;

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
/// failures surface as <see cref="ProjectionDiagnostic"/> entries
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
    public static S421RoutePlan From(S421Dataset dataset, out IReadOnlyList<ProjectionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        var ctx = new ProjectionContext(BuildXlinkResolver(dataset));

        var routeFeature = dataset.Features.FirstOrDefault(f => f.FeatureType == "Route")
            ?? throw new InvalidOperationException("Dataset contains no Route feature.");

        var routeInfo = ResolveRouteInfo(routeFeature, ctx);
        var (waypoints, legs) = ResolveWaypointsAndLegs(routeFeature, ctx);
        var actionPoints = ResolveActionPoints(routeFeature, ctx);
        var schedules = ResolveSchedules(routeFeature, ctx);

        var route = new S421Route
        {
            Id = routeFeature.Id,
            FormatVersion = routeFeature.Attributes.GetValueOrDefault("routeFormatVersion"),
            RouteId = routeFeature.Attributes.GetValueOrDefault("routeID"),
            EditionNumber = AttributeParser.TryParseInt(
                routeFeature.Attributes.GetValueOrDefault("routeEditionNo"), ctx, routeFeature.Id, "routeEditionNo"),
            Info = routeInfo,
            Waypoints = waypoints,
            Legs = legs,
            ActionPoints = actionPoints,
            Schedules = schedules,
            ExtraAttributes = ExtraAttributes.ExcludeKnown(routeFeature.Attributes,
                "routeFormatVersion", "routeID", "routeEditionNo"),
        };

        diagnostics = ctx.ToImmutableDiagnostics();
        return new S421RoutePlan
        {
            DatasetIdentifier = dataset.DatasetIdentifier,
            ProductIdentifier = dataset.ProductIdentifier,
            Route = route,
            Source = dataset,
        };
    }

    // ── XLink index ──────────────────────────────────────────────

    private static XlinkResolver BuildXlinkResolver(S421Dataset dataset)
    {
        IEnumerable<KeyValuePair<string, object>> All()
        {
            foreach (var f in dataset.Features)
                yield return new KeyValuePair<string, object>(f.Id, f);
            foreach (var i in dataset.InformationTypes)
                yield return new KeyValuePair<string, object>(i.Id, i);
        }
        return XlinkResolver.Build(All());
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
    private static (string Id, ImmutableArray<GmlReference> References)? ResolveContainer(
        string href, string role, ProjectionContext ctx, string? relatedId = null)
    {
        var obj = ctx.Xlinks.ResolveAny(href, role, ctx, relatedId);
        return obj switch
        {
            S421Feature f => (f.Id, f.References),
            S421InformationType i => (i.Id, i.References),
            _ => null,
        };
    }

    // ── RouteInfo ────────────────────────────────────────────────

    private static S421RouteInfo? ResolveRouteInfo(S421Feature route, ProjectionContext ctx)
    {
        var infoRef = route.References.FirstOrDefault(r => r.Role == "routeInfo");
        if (infoRef is null) return null;

        var infoType = ctx.Xlinks.Resolve<S421InformationType>(infoRef.Href, "routeInfo", ctx, route.Id);
        if (infoType is null) return null;

        var a = infoType.Attributes;
        return new S421RouteInfo
        {
            Id = infoType.Id,
            Name = a.GetValueOrDefault("routeInfoName"),
            Author = a.GetValueOrDefault("routeInfoAuthor"),
            Description = a.GetValueOrDefault("routeInfoDescription"),
            Status = AttributeParser.TryParseInt(a.GetValueOrDefault("routeInfoStatus"), ctx, infoType.Id, "routeInfoStatus"),
            EditionTime = AttributeParser.TryParseDateTimeOffset(a.GetValueOrDefault("routeInfoEditionTime"), ctx, infoType.Id, "routeInfoEditionTime"),
            ValidityStart = AttributeParser.TryParseDateTimeOffset(a.GetValueOrDefault("routeInfoValidityStart"), ctx, infoType.Id, "routeInfoValidityStart"),
            ValidityEnd = AttributeParser.TryParseDateTimeOffset(a.GetValueOrDefault("routeInfoValidityEnd"), ctx, infoType.Id, "routeInfoValidityEnd"),
            DeparturePortId1 = a.GetValueOrDefault("routeInfoDeparturePortID1"),
            DeparturePortId2 = a.GetValueOrDefault("routeInfoDeparturePortID2"),
            DeparturePortCall = a.GetValueOrDefault("routeInfoDeparturePortCall"),
            ArrivalPortId1 = a.GetValueOrDefault("routeInfoArrivalPortID1"),
            ArrivalPortId2 = a.GetValueOrDefault("routeInfoArrivalPortID2"),
            ArrivalPortCall = a.GetValueOrDefault("routeInfoArrivalPortCall"),
            PreviousRouteReference = a.GetValueOrDefault("routeInfoReferencePrevRoute"),
            NextRouteReference = a.GetValueOrDefault("routeInfoReferenceNextRoute"),
            Vessel = BuildVessel(a, ctx, infoType.Id),
            ExtraAttributes = ExtraAttributes.ExcludeKnown(a,
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

    private static S421VesselInfo? BuildVessel(ImmutableDictionary<string, string> a, ProjectionContext ctx, string relatedId)
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
            VesselType = AttributeParser.TryParseInt(a.GetValueOrDefault("routeInfoVesselType"), ctx, relatedId, "routeInfoVesselType"),
            Name = a.GetValueOrDefault("routeInfoVesselName"),
            Mmsi = a.GetValueOrDefault("routeInfoVesselMMSI"),
            Callsign = a.GetValueOrDefault("routeInfoVesselCallsign"),
            Imo = a.GetValueOrDefault("routeInfoVesselIMO"),
            VoyageId = a.GetValueOrDefault("routeInfoVesselVoyage"),
            HeightMeters = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeInfoVesselHeight"), ctx, relatedId, "routeInfoVesselHeight"),
            LengthMeters = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeInfoVesselLength"), ctx, relatedId, "routeInfoVesselLength"),
            BeamMeters = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeInfoVesselBeam"), ctx, relatedId, "routeInfoVesselBeam"),
        };
    }

    // ── Waypoints + legs ─────────────────────────────────────────

    private static (ImmutableArray<S421Waypoint>, ImmutableArray<S421Leg>) ResolveWaypointsAndLegs(
        S421Feature route, ProjectionContext ctx)
    {
        var wptsRef = route.References.FirstOrDefault(r => r.Role == "routeWaypoints");
        if (wptsRef is null)
            return (ImmutableArray<S421Waypoint>.Empty, ImmutableArray<S421Leg>.Empty);

        var container = ResolveContainer(wptsRef.Href, "routeWaypoints", ctx, route.Id);
        if (container is null)
            return (ImmutableArray<S421Waypoint>.Empty, ImmutableArray<S421Leg>.Empty);

        var legs = ImmutableArray.CreateBuilder<S421Leg>();
        var seenLegs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var waypoints = ImmutableArray.CreateBuilder<S421Waypoint>();
        foreach (var wptRef in container.Value.References.Where(r => r.Role == "routeWaypoint"))
        {
            var wptFeature = ctx.Xlinks.Resolve<S421Feature>(wptRef.Href, "routeWaypoint", ctx, container.Value.Id);
            if (wptFeature is null) continue;

            S421Leg? leg = null;
            var legRef = wptFeature.References.FirstOrDefault(r => r.Role == "routeWaypointLeg");
            if (legRef is not null)
            {
                var legFeature = ctx.Xlinks.Resolve<S421Feature>(legRef.Href, "routeWaypointLeg", ctx, wptFeature.Id);
                if (legFeature is not null)
                {
                    leg = ProjectLeg(legFeature, ctx);
                    if (seenLegs.Add(legFeature.Id))
                        legs.Add(leg);
                }
            }

            waypoints.Add(ProjectWaypoint(wptFeature, leg, ctx));
        }

        return (waypoints.ToImmutable(), legs.ToImmutable());
    }

    private static S421Waypoint ProjectWaypoint(S421Feature f, S421Leg? leg, ProjectionContext ctx)
    {
        GeoPosition position = default;
        if (!f.Points.IsDefaultOrEmpty)
        {
            var (lat, lon) = f.Points[0];
            position = new GeoPosition(lat, lon);
        }
        else
        {
            ctx.Report(new ProjectionDiagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Message = "RouteWaypoint has no point geometry.",
                Code = "feature.geometry.missing",
                RelatedId = f.Id,
            });
        }

        return new S421Waypoint
        {
            Id = f.Id,
            WaypointNumber = AttributeParser.TryParseInt(f.Attributes.GetValueOrDefault("routeWaypointID"), ctx, f.Id, "routeWaypointID"),
            Name = f.Attributes.GetValueOrDefault("routeWaypointName"),
            ExternalReferenceId = f.Attributes.GetValueOrDefault("routeWaypointExternalReferenceID"),
            Fixed = AttributeParser.TryParseBool(f.Attributes.GetValueOrDefault("routeWaypointFixed"), ctx, f.Id, "routeWaypointFixed"),
            TurnRadius = AttributeParser.TryParseDouble(f.Attributes.GetValueOrDefault("routeWaypointTurnRadius"), ctx, f.Id, "routeWaypointTurnRadius"),
            Position = position,
            OutgoingLeg = leg,
            ExtraAttributes = ExtraAttributes.ExcludeKnown(f.Attributes,
                "routeWaypointID", "routeWaypointName", "routeWaypointExternalReferenceID",
                "routeWaypointFixed", "routeWaypointTurnRadius"),
        };
    }

    private static S421Leg ProjectLeg(S421Feature f, ProjectionContext ctx)
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
            StarboardCrossTrackDistanceLimit = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeWaypointLegStarboardXTDL"), ctx, f.Id, "routeWaypointLegStarboardXTDL"),
            PortCrossTrackDistanceLimit = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeWaypointLegPortXTDL"), ctx, f.Id, "routeWaypointLegPortXTDL"),
            StarboardChannelLimit = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeWaypointLegStarboardCL"), ctx, f.Id, "routeWaypointLegStarboardCL"),
            PortChannelLimit = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeWaypointLegPortCL"), ctx, f.Id, "routeWaypointLegPortCL"),
            SafetyContour = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeWaypointLegSafetyContour"), ctx, f.Id, "routeWaypointLegSafetyContour"),
            SafetyDepth = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeWaypointLegSafetyDepth"), ctx, f.Id, "routeWaypointLegSafetyDepth"),
            GeometryTypeCode = AttributeParser.TryParseInt(a.GetValueOrDefault("routeWaypointLegGeometryType"), ctx, f.Id, "routeWaypointLegGeometryType"),
            SpeedOverGroundMin = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeWaypointLegSOGMin"), ctx, f.Id, "routeWaypointLegSOGMin"),
            SpeedOverGroundMax = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeWaypointLegSOGMax"), ctx, f.Id, "routeWaypointLegSOGMax"),
            SpeedThroughWaterMin = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeWaypointLegSTWMin"), ctx, f.Id, "routeWaypointLegSTWMin"),
            SpeedThroughWaterMax = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeWaypointLegSTWMax"), ctx, f.Id, "routeWaypointLegSTWMax"),
            Draft = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeWaypointLegDraft"), ctx, f.Id, "routeWaypointLegDraft"),
            StaticUnderKeelClearance = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeWaypointLegStaticUKC"), ctx, f.Id, "routeWaypointLegStaticUKC"),
            DynamicUnderKeelClearance = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeWaypointLegDynamicUKC"), ctx, f.Id, "routeWaypointLegDynamicUKC"),
            SafetyMargin = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeWaypointLegSafetyMargin"), ctx, f.Id, "routeWaypointLegSafetyMargin"),
            Note = a.GetValueOrDefault("routeWaypointLegNote"),
            ExtraAttributes = ExtraAttributes.ExcludeKnown(a,
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

    private static ImmutableArray<S421ActionPoint> ResolveActionPoints(S421Feature route, ProjectionContext ctx)
    {
        var apRef = route.References.FirstOrDefault(r => r.Role == "routeActionPoints");
        if (apRef is null) return ImmutableArray<S421ActionPoint>.Empty;

        var container = ResolveContainer(apRef.Href, "routeActionPoints", ctx, route.Id);
        if (container is null) return ImmutableArray<S421ActionPoint>.Empty;

        var result = ImmutableArray.CreateBuilder<S421ActionPoint>();
        foreach (var apFeatureRef in container.Value.References.Where(r => r.Role == "routeActionPoint"))
        {
            var ap = ctx.Xlinks.Resolve<S421Feature>(apFeatureRef.Href, "routeActionPoint", ctx, container.Value.Id);
            if (ap is null) continue;
            result.Add(ProjectActionPoint(ap, ctx));
        }
        return result.ToImmutable();
    }

    private static S421ActionPoint ProjectActionPoint(S421Feature f, ProjectionContext ctx)
    {
        S421ActionPointGeometryKind kind;
        ImmutableArray<GeoPosition> coords;
        switch (f.GeometryType)
        {
            case GmlGeometryType.Point:
                kind = S421ActionPointGeometryKind.Point;
                coords = f.Points.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.Points.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray();
                break;
            case GmlGeometryType.Curve:
                kind = S421ActionPointGeometryKind.Curve;
                coords = f.Curves.IsDefaultOrEmpty
                    ? ImmutableArray<GeoPosition>.Empty
                    : f.Curves.SelectMany(c => c).Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToImmutableArray();
                break;
            case GmlGeometryType.Surface:
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
            ActionPointNumber = AttributeParser.TryParseInt(a.GetValueOrDefault("routeActionPointID"), ctx, f.Id, "routeActionPointID"),
            Name = a.GetValueOrDefault("routeActionPointName"),
            RadiusNauticalMiles = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeActionPointRadius"), ctx, f.Id, "routeActionPointRadius"),
            TimeToActMinutes = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeActionPointTimeToAct"), ctx, f.Id, "routeActionPointTimeToAct"),
            RequiredAction = AttributeParser.TryParseInt(a.GetValueOrDefault("routeActionPointRequiredAction"), ctx, f.Id, "routeActionPointRequiredAction"),
            RequiredActionDescription = description,
            GeometryKind = kind,
            Coordinates = coords,
            ExtraAttributes = ExtraAttributes.ExcludeKnown(a,
                "routeActionPointID", "routeActionPointName", "routeActionPointRadius",
                "routeActionPointTimeToAct", "routeActionPointRequiredAction",
                "routeActionPointRequiredActionDescription",
                "routeActionPointRequredActionDescription"),
        };
    }

    // ── Schedules ────────────────────────────────────────────────

    private static ImmutableArray<S421Schedule> ResolveSchedules(S421Feature route, ProjectionContext ctx)
    {
        var schRef = route.References.FirstOrDefault(r => r.Role == "routeSchedules");
        if (schRef is null) return ImmutableArray<S421Schedule>.Empty;

        var container = ResolveContainer(schRef.Href, "routeSchedules", ctx, route.Id);
        if (container is null) return ImmutableArray<S421Schedule>.Empty;

        var result = ImmutableArray.CreateBuilder<S421Schedule>();
        foreach (var entry in container.Value.References.Where(r => r.Role == "routeSchedule"))
        {
            var sched = ctx.Xlinks.Resolve<S421InformationType>(entry.Href, "routeSchedule", ctx, container.Value.Id);
            if (sched is null) continue;
            result.Add(ProjectSchedule(sched, ctx));
        }
        return result.ToImmutable();
    }

    private static S421Schedule ProjectSchedule(S421InformationType sched, ProjectionContext ctx)
    {
        var variants = ImmutableArray.CreateBuilder<S421ScheduleVariant>();
        AddVariant(sched, "routeScheduleManual", S421ScheduleVariantKind.Manual, ctx, variants);
        AddVariant(sched, "routeScheduleCalculated", S421ScheduleVariantKind.Calculated, ctx, variants);
        AddVariant(sched, "routeScheduleRecommended", S421ScheduleVariantKind.Recommended, ctx, variants);

        var a = sched.Attributes;
        return new S421Schedule
        {
            Id = sched.Id,
            ScheduleNumber = AttributeParser.TryParseInt(a.GetValueOrDefault("routeScheduleID"), ctx, sched.Id, "routeScheduleID"),
            Name = a.GetValueOrDefault("routeScheduleName"),
            Variants = variants.ToImmutable(),
            ExtraAttributes = ExtraAttributes.ExcludeKnown(a, "routeScheduleID", "routeScheduleName"),
        };
    }

    private static void AddVariant(S421InformationType schedule, string role, S421ScheduleVariantKind kind,
        ProjectionContext ctx, ImmutableArray<S421ScheduleVariant>.Builder output)
    {
        var reference = schedule.References.FirstOrDefault(r => r.Role == role);
        if (reference is null) return;

        var variant = ctx.Xlinks.Resolve<S421InformationType>(reference.Href, role, ctx, schedule.Id);
        if (variant is null) return;

        var elements = ImmutableArray.CreateBuilder<S421ScheduleElement>();
        foreach (var elemRef in variant.References.Where(r => r.Role == "routeScheduleElement"))
        {
            var elem = ctx.Xlinks.Resolve<S421InformationType>(elemRef.Href, "routeScheduleElement", ctx, variant.Id);
            if (elem is null) continue;
            elements.Add(ProjectScheduleElement(elem, ctx));
        }

        output.Add(new S421ScheduleVariant
        {
            Id = variant.Id,
            Kind = kind,
            Elements = elements.ToImmutable(),
        });
    }

    private static S421ScheduleElement ProjectScheduleElement(S421InformationType e, ProjectionContext ctx)
    {
        var a = e.Attributes;
        return new S421ScheduleElement
        {
            Id = e.Id,
            WaypointNumber = AttributeParser.TryParseInt(a.GetValueOrDefault("routeWaypointId"), ctx, e.Id, "routeWaypointId"),
            PlannedSpeedOverGround = AttributeParser.TryParseDouble(a.GetValueOrDefault("routeScheduleElementPlanSOG"), ctx, e.Id, "routeScheduleElementPlanSOG"),
            Etd = AttributeParser.TryParseDateTimeOffset(a.GetValueOrDefault("routeScheduleElementETD"), ctx, e.Id, "routeScheduleElementETD"),
            Eta = AttributeParser.TryParseDateTimeOffset(a.GetValueOrDefault("routeScheduleElementETA"), ctx, e.Id, "routeScheduleElementETA"),
            EtdWindowBeforeMinutes = AttributeParser.TryParseInt(a.GetValueOrDefault("routeScheduleElementETDWindowBefore"), ctx, e.Id, "routeScheduleElementETDWindowBefore"),
            EtdWindowAfterMinutes = AttributeParser.TryParseInt(a.GetValueOrDefault("routeScheduleElementETDWindowAfter"), ctx, e.Id, "routeScheduleElementETDWindowAfter"),
            EtaWindowBeforeMinutes = AttributeParser.TryParseInt(a.GetValueOrDefault("routeScheduleElementETAWindowBefore"), ctx, e.Id, "routeScheduleElementETAWindowBefore"),
            EtaWindowAfterMinutes = AttributeParser.TryParseInt(a.GetValueOrDefault("routeScheduleElementETAWindowAfter"), ctx, e.Id, "routeScheduleElementETAWindowAfter"),
            Note = a.GetValueOrDefault("routeScheduleElementNote"),
            ExtraAttributes = ExtraAttributes.ExcludeKnown(a,
                "routeWaypointId",
                "routeScheduleElementPlanSOG",
                "routeScheduleElementETD", "routeScheduleElementETA",
                "routeScheduleElementETDWindowBefore", "routeScheduleElementETDWindowAfter",
                "routeScheduleElementETAWindowBefore", "routeScheduleElementETAWindowAfter",
                "routeScheduleElementNote"),
        };
    }
}
