using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S421.Validation;

/// <summary>
/// The default <see cref="ValidationRuleSet{TModel}"/> of normative rules
/// for an S-421 <see cref="S421RoutePlan"/>. Rule identifiers follow the
/// convention <c>S421-R-{clause}</c>, where <c>{clause}</c> traces to the
/// relevant section of the S-421 specification (IEC 63173-2).
/// </summary>
/// <remarks>
/// <para>
/// The pilot rule set focuses on Tier-1 (schema-shape) and Tier-2
/// (spec-semantic) rules that can be evaluated against a single
/// <see cref="S421RoutePlan"/> in isolation. Tier-3 cross-dataset rules
/// (e.g. comparing route waypoints to charted depths in a loaded S-102
/// coverage) will be added in a follow-up once the MCP <c>validate_all</c>
/// surface is wired up — they need access to sibling datasets via
/// <see cref="ValidationContext.Services"/>.
/// </para>
/// </remarks>
public static class S421RoutePlanRules
{
    /// <summary>
    /// <c>S421-R-3.1</c> — A route must contain at least two waypoints.
    /// </summary>
    /// <remarks>
    /// A route with fewer than two waypoints cannot describe a passage:
    /// at minimum a route requires an origin and a destination. Spec
    /// reference: S-421 Annex A "Route" / "RouteWaypoint" (FC) — a
    /// <c>Route</c> aggregates <c>RouteWaypoint</c> features via the
    /// <c>routeWaypoint</c> association role.
    /// </remarks>
    public static IValidationRule<S421RoutePlan> MinimumWaypointCount { get; } =
        ValidationRuleBuilder.RuleFor<S421RoutePlan>("S421-R-3.1")
            .WithDescription("A route must contain at least two waypoints.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((plan, _) =>
            {
                var count = plan.Route.Waypoints.Length;
                if (count >= 2)
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = "S421-R-3.1",
                        Severity = ValidationSeverity.Error,
                        Message = $"Route contains {count} waypoint(s); a route must have at least two.",
                        RelatedFeatureId = plan.Route.Id,
                    },
                };
            })
            .Build();

    /// <summary>
    /// <c>S421-R-3.2</c> — Consecutive waypoints must not be coincident
    /// (i.e. no zero-length leg).
    /// </summary>
    /// <remarks>
    /// Two consecutive waypoints sharing the same position produce a
    /// zero-length leg, which has no defined heading and cannot be sailed.
    /// Coincidence is tested with a tight tolerance (1e-9 degrees, about
    /// 0.1 mm at the equator) so that ordinary floating-point round-trip
    /// does not trigger the rule.
    /// </remarks>
    public static IValidationRule<S421RoutePlan> NoCoincidentConsecutiveWaypoints { get; } =
        ValidationRuleBuilder.RuleFor<S421RoutePlan>("S421-R-3.2")
            .WithDescription("Consecutive waypoints must not be coincident (zero-length leg).")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((plan, _) =>
            {
                const double tolerance = 1e-9;
                var waypoints = plan.Route.Waypoints;
                if (waypoints.Length < 2)
                    return Array.Empty<ValidationFinding>();

                var findings = new List<ValidationFinding>();
                for (int i = 1; i < waypoints.Length; i++)
                {
                    var a = waypoints[i - 1].Position;
                    var b = waypoints[i].Position;
                    if (Math.Abs(a.Latitude - b.Latitude) <= tolerance
                        && Math.Abs(a.Longitude - b.Longitude) <= tolerance)
                    {
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S421-R-3.2",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Waypoints '{waypoints[i - 1].Id}' and '{waypoints[i].Id}' are coincident " +
                                $"(zero-length leg at ({a.Latitude}, {a.Longitude})).",
                            Point = a,
                            RelatedFeatureId = waypoints[i].Id,
                        });
                    }
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S421-R-4.1</c> — Every waypoint's position must fall within the
    /// valid WGS-84 ranges: latitude in [-90, +90] and longitude in
    /// [-180, +180].
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.2 — geographic coordinates for
    /// <c>EPSG:4326</c> are bounded.
    /// </remarks>
    public static IValidationRule<S421RoutePlan> WaypointLatLonInRange { get; } =
        ValidationRuleBuilder.RuleFor<S421RoutePlan>("S421-R-4.1")
            .WithDescription("Waypoint positions must lie within the WGS-84 lat/lon ranges.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((plan, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var wpt in plan.Route.Waypoints)
                {
                    var pos = wpt.Position;
                    bool latOk = pos.Latitude is >= -90 and <= 90;
                    bool lonOk = pos.Longitude is >= -180 and <= 180;
                    if (latOk && lonOk) continue;

                    var details = (latOk, lonOk) switch
                    {
                        (false, true) => $"latitude {pos.Latitude} is outside [-90, +90]",
                        (true, false) => $"longitude {pos.Longitude} is outside [-180, +180]",
                        _ => $"latitude {pos.Latitude} and longitude {pos.Longitude} are both out of range",
                    };
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S421-R-4.1",
                        Severity = ValidationSeverity.Error,
                        Message = $"Waypoint '{wpt.Id}': {details}.",
                        Point = pos,
                        RelatedFeatureId = wpt.Id,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S421-R-5.1</c> — When present, planned speed-over-ground values
    /// on a leg (<c>routeWaypointLegSOGMin</c> /
    /// <c>routeWaypointLegSOGMax</c>) must be non-negative and the
    /// minimum must not exceed the maximum.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-421 Annex A "RouteWaypointLeg" (FC) —
    /// <c>routeWaypointLegSOGMin</c> / <c>routeWaypointLegSOGMax</c> are
    /// expressed in knots and represent the planned speed envelope.
    /// </remarks>
    public static IValidationRule<S421RoutePlan> LegSpeedOverGroundSane { get; } =
        ValidationRuleBuilder.RuleFor<S421RoutePlan>("S421-R-5.1")
            .WithDescription("Leg planned speed-over-ground must be non-negative and min ≤ max.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((plan, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var leg in plan.Route.Legs)
                {
                    if (leg.SpeedOverGroundMin is { } min && min < 0)
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S421-R-5.1",
                            Severity = ValidationSeverity.Warning,
                            Message = $"Leg '{leg.Id}' has negative planned SOG minimum ({min} knots).",
                            RelatedFeatureId = leg.Id,
                        });

                    if (leg.SpeedOverGroundMax is { } max && max < 0)
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S421-R-5.1",
                            Severity = ValidationSeverity.Warning,
                            Message = $"Leg '{leg.Id}' has negative planned SOG maximum ({max} knots).",
                            RelatedFeatureId = leg.Id,
                        });

                    if (leg.SpeedOverGroundMin is { } sogMin
                        && leg.SpeedOverGroundMax is { } sogMax
                        && sogMin > sogMax)
                    {
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S421-R-5.1",
                            Severity = ValidationSeverity.Warning,
                            Message =
                                $"Leg '{leg.Id}' has planned SOG minimum ({sogMin} knots) " +
                                $"greater than maximum ({sogMax} knots).",
                            RelatedFeatureId = leg.Id,
                        });
                    }
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S421-R-6.1</c> — When present, the route edition number must be
    /// strictly positive.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-421 Annex A "Route" (FC) — <c>routeEditionNo</c>
    /// is the author-assigned edition counter. The S-100 lifecycle treats
    /// edition 1 as the first publishable edition; values of 0 or below
    /// are not meaningful.
    /// </remarks>
    public static IValidationRule<S421RoutePlan> RouteEditionNumberPositive { get; } =
        ValidationRuleBuilder.RuleFor<S421RoutePlan>("S421-R-6.1")
            .WithDescription("When present, route edition number must be ≥ 1.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((plan, _) =>
            {
                if (plan.Route.EditionNumber is not { } edition || edition >= 1)
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = "S421-R-6.1",
                        Severity = ValidationSeverity.Error,
                        Message = $"Route edition number must be ≥ 1, found {edition}.",
                        RelatedFeatureId = plan.Route.Id,
                    },
                };
            })
            .Build();

    /// <summary>
    /// <c>S421-R-7.1</c> — Every <c>RouteActionPoint</c> must have
    /// non-empty geometry coordinates.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-421 Annex A "RouteActionPoint" (FC) — an action
    /// point's geometry (point / curve / surface) anchors when along the
    /// route the action applies; an action point with no coordinates has
    /// no spatial meaning.
    /// </remarks>
    public static IValidationRule<S421RoutePlan> ActionPointGeometryPopulated { get; } =
        ValidationRuleBuilder.RuleFor<S421RoutePlan>("S421-R-7.1")
            .WithDescription("RouteActionPoint geometry must have at least one coordinate.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((plan, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var ap in plan.Route.ActionPoints)
                {
                    if (!ap.Coordinates.IsDefaultOrEmpty) continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S421-R-7.1",
                        Severity = ValidationSeverity.Warning,
                        Message = $"Action point '{ap.Id}' has no geometry coordinates ({ap.GeometryKind}).",
                        RelatedFeatureId = ap.Id,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>The canonical default rule set for S-421 route plans.</summary>
    public static ValidationRuleSet<S421RoutePlan> Default { get; } = new(
        MinimumWaypointCount,
        NoCoincidentConsecutiveWaypoints,
        WaypointLatLonInRange,
        LegSpeedOverGroundSane,
        RouteEditionNumberPositive,
        ActionPointGeometryPopulated);

    /// <summary>
    /// Convenience wrapper around <see cref="ValidationRuleSet{T}.Run(T, ValidationContext?)"/>
    /// using the <see cref="Default"/> rule set.
    /// </summary>
    public static ValidationReport Validate(S421RoutePlan plan, ValidationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Default.Run(plan, context);
    }
}
