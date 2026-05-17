using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S129.DataModel;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S129.Validation;

/// <summary>
/// The default <see cref="ValidationRuleSet{TModel}"/> of normative rules
/// for an S-129 <see cref="S129UnderKeelClearancePlan"/>. Rule identifiers
/// follow the convention <c>S129-R-{clause}</c>, where <c>{clause}</c>
/// traces to the relevant section of the S-129 Edition 2.0.0 specification.
/// </summary>
/// <remarks>
/// <para>
/// The pilot rule set focuses on Tier-1 (schema-shape) and Tier-2
/// (spec-semantic) rules that can be evaluated against a single
/// <see cref="S129UnderKeelClearancePlan"/> in isolation. Tier-3
/// cross-dataset rules — for example comparing a control-point UKC
/// margin against the charted depth at the same position in a loaded
/// S-102 bathymetric coverage — will be added in a follow-up once the
/// MCP <c>validate_all</c> surface is wired up. They need access to
/// sibling datasets via <see cref="ValidationContext.Services"/>.
/// </para>
/// </remarks>
public static class S129UkcRules
{
    /// <summary>
    /// <c>S129-R-1.1</c> — When both bounds of the plan's
    /// <c>fixedTimeRange</c> are present, the start must not be after
    /// the end.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-129 Edition 2.0.0 §<c>fixedTimeRange</c> —
    /// the plan applies during the interval <c>[timeStart, timeEnd]</c>;
    /// an interval whose start is after its end is empty and cannot
    /// describe a valid plan window.
    /// </remarks>
    public static IValidationRule<S129UnderKeelClearancePlan> PlanValidityPeriod { get; } =
        ValidationRuleBuilder.RuleFor<S129UnderKeelClearancePlan>("S129-R-1.1")
            .WithDescription("Plan fixedTimeRange start must not be after end.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((plan, _) =>
            {
                var range = plan.Plan?.FixedTimeRange;
                if (range?.Start is not { } start || range.End is not { } end)
                    return Array.Empty<ValidationFinding>();
                if (start <= end)
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = "S129-R-1.1",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"Plan fixedTimeRange is inverted: start ({start:O}) " +
                            $"is after end ({end:O}).",
                        RelatedFeatureId = plan.Plan?.Id,
                    },
                };
            })
            .Build();

    /// <summary>
    /// <c>S129-R-2.1</c> — Control points carrying an
    /// <c>expectedPassingTime</c> must be strictly monotonically
    /// increasing in time (no duplicates, no backwards steps).
    /// </summary>
    /// <remarks>
    /// Spec reference: S-129 Edition 2.0.0 §<c>expectedPassingTime</c>
    /// — control points form an ordered time series along the route.
    /// Two control points expected to be passed at the same instant,
    /// or expected to be passed in an order that contradicts the
    /// route sequence, are not navigationally meaningful. The typed
    /// projection already sorts by time; this rule reports duplicates
    /// that survive that sort.
    /// </remarks>
    public static IValidationRule<S129UnderKeelClearancePlan> ControlPointTimeMonotonic { get; } =
        ValidationRuleBuilder.RuleFor<S129UnderKeelClearancePlan>("S129-R-2.1")
            .WithDescription("Control-point expectedPassingTime must be strictly increasing.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((plan, _) =>
            {
                var findings = new List<ValidationFinding>();
                DateTimeOffset? previous = null;
                string? previousId = null;
                foreach (var cp in plan.ControlPoints)
                {
                    if (cp.ExpectedPassingTime is not { } current)
                        continue;
                    if (previous is { } prev && current <= prev)
                    {
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S129-R-2.1",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Control point '{cp.Id}' expectedPassingTime ({current:O}) " +
                                $"is not strictly after preceding control point " +
                                $"'{previousId}' ({prev:O}).",
                            Point = cp.Position,
                            RelatedFeatureId = cp.Id,
                        });
                    }
                    previous = current;
                    previousId = cp.Id;
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S129-R-3.1</c> — Every coordinate on every S-129 feature
    /// (control-point positions, plan-area rings, non-navigable and
    /// almost-non-navigable area rings) must fall within the valid
    /// WGS-84 ranges: latitude in [-90, +90], longitude in [-180, +180].
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.2 — geographic coordinates for
    /// <c>EPSG:4326</c> are bounded.
    /// </remarks>
    public static IValidationRule<S129UnderKeelClearancePlan> CoordinatesInWgs84Range { get; } =
        ValidationRuleBuilder.RuleFor<S129UnderKeelClearancePlan>("S129-R-3.1")
            .WithDescription("All feature coordinates must lie within the WGS-84 lat/lon ranges.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((plan, _) =>
            {
                var findings = new List<ValidationFinding>();

                foreach (var cp in plan.ControlPoints)
                {
                    if (cp.Position is { } pos && !InRange(pos))
                        findings.Add(OutOfRange("control point", cp.Id, pos));
                }

                if (plan.PlanArea is { } pa)
                    CheckSurface("plan area", pa.Id, pa.Coordinates, pa.InteriorRings, findings);

                foreach (var area in plan.NonNavigableAreas)
                    CheckSurface("non-navigable area", area.Id, area.Coordinates, area.InteriorRings, findings);

                foreach (var area in plan.AlmostNonNavigableAreas)
                    CheckSurface("almost-non-navigable area", area.Id, area.Coordinates, area.InteriorRings, findings);

                return findings;

                static bool InRange(GeoPosition p) =>
                    p.Latitude is >= -90 and <= 90 && p.Longitude is >= -180 and <= 180;

                static ValidationFinding OutOfRange(string kind, string id, GeoPosition p) => new()
                {
                    RuleId = "S129-R-3.1",
                    Severity = ValidationSeverity.Error,
                    Message =
                        $"{kind} '{id}' has coordinate out of WGS-84 range: " +
                        $"({p.Latitude}, {p.Longitude}).",
                    Point = p,
                    RelatedFeatureId = id,
                };

                static void CheckSurface(
                    string kind,
                    string id,
                    ImmutableArray<GeoPosition> exterior,
                    ImmutableArray<ImmutableArray<GeoPosition>> holes,
                    List<ValidationFinding> findings)
                {
                    if (!exterior.IsDefaultOrEmpty)
                    {
                        foreach (var p in exterior)
                            if (!InRange(p)) findings.Add(OutOfRange(kind, id, p));
                    }
                    if (!holes.IsDefaultOrEmpty)
                    {
                        foreach (var ring in holes)
                            if (!ring.IsDefaultOrEmpty)
                                foreach (var p in ring)
                                    if (!InRange(p)) findings.Add(OutOfRange(kind, id, p));
                    }
                }
            })
            .Build();

    /// <summary>
    /// <c>S129-R-3.2</c> — When the dataset carries a plan-area feature,
    /// its surface geometry must be populated with at least three
    /// distinct coordinates (the minimum to enclose a non-degenerate
    /// area).
    /// </summary>
    /// <remarks>
    /// Spec reference: S-129 Edition 2.0.0 <c>UnderKeelClearancePlanArea</c>
    /// — the plan area describes the spatial extent over which the UKC
    /// plan is valid. A degenerate ring (fewer than three vertices)
    /// cannot bound any area and provides no useful spatial extent.
    /// </remarks>
    public static IValidationRule<S129UnderKeelClearancePlan> PlanAreaGeometryPopulated { get; } =
        ValidationRuleBuilder.RuleFor<S129UnderKeelClearancePlan>("S129-R-3.2")
            .WithDescription("UnderKeelClearancePlanArea must have at least three coordinates.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((plan, _) =>
            {
                if (plan.PlanArea is not { } pa)
                    return Array.Empty<ValidationFinding>();

                var ring = pa.Coordinates;
                int distinct = ring.IsDefaultOrEmpty
                    ? 0
                    : ring.Distinct().Count();

                if (distinct >= 3)
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = "S129-R-3.2",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"Plan area '{pa.Id}' has degenerate geometry: " +
                            $"{distinct} distinct vertex(es) on the exterior ring " +
                            "(at least 3 required).",
                        RelatedFeatureId = pa.Id,
                    },
                };
            })
            .Build();

    /// <summary>
    /// <c>S129-R-4.1</c> — When present, the plan's <c>maximumDraught</c>
    /// must be strictly positive.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-129 Edition 2.0.0 §<c>maximumDraught</c> — the
    /// vessel draught used as input to the UKC calculation, expressed in
    /// metres. Zero or negative draught is not physically meaningful and
    /// would invalidate every UKC margin in the plan.
    /// </remarks>
    public static IValidationRule<S129UnderKeelClearancePlan> MaximumDraughtPositive { get; } =
        ValidationRuleBuilder.RuleFor<S129UnderKeelClearancePlan>("S129-R-4.1")
            .WithDescription("Plan maximumDraught, when present, must be > 0.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((plan, _) =>
            {
                if (plan.Plan?.MaximumDraught is not { } draught)
                    return Array.Empty<ValidationFinding>();
                if (draught > 0 && !double.IsInfinity(draught) && !double.IsNaN(draught))
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = "S129-R-4.1",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"Plan maximumDraught must be a positive finite value, found {draught}.",
                        RelatedFeatureId = plan.Plan?.Id,
                    },
                };
            })
            .Build();

    /// <summary>
    /// <c>S129-R-5.1</c> — Numeric control-point measurements
    /// (<c>distanceAboveUKCLimit</c> and <c>expectedPassingSpeed</c>),
    /// when present, must be finite (no <c>NaN</c>, no
    /// <c>±Infinity</c>).
    /// </summary>
    /// <remarks>
    /// Spec reference: S-129 Edition 2.0.0 §<c>distanceAboveUKCLimit</c>
    /// and §<c>expectedPassingSpeed</c> — these are real-valued
    /// measurements (metres and knots respectively). A non-finite value
    /// indicates the producer's calculation failed or the value was
    /// corrupted in transit; either way it cannot be used to drive a
    /// navigational decision.
    /// </remarks>
    public static IValidationRule<S129UnderKeelClearancePlan> ControlPointMeasurementsFinite { get; } =
        ValidationRuleBuilder.RuleFor<S129UnderKeelClearancePlan>("S129-R-5.1")
            .WithDescription("Control-point UKC and speed measurements must be finite.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((plan, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var cp in plan.ControlPoints)
                {
                    if (cp.DistanceAboveUkcLimit is { } d && !double.IsFinite(d))
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S129-R-5.1",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Control point '{cp.Id}' distanceAboveUKCLimit is non-finite ({d}).",
                            Point = cp.Position,
                            RelatedFeatureId = cp.Id,
                        });

                    if (cp.ExpectedPassingSpeed is { } s && !double.IsFinite(s))
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S129-R-5.1",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Control point '{cp.Id}' expectedPassingSpeed is non-finite ({s}).",
                            Point = cp.Position,
                            RelatedFeatureId = cp.Id,
                        });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S129-R-5.2</c> — Every control point should carry a point
    /// position; a control point without geometry cannot be plotted on
    /// the chart.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-129 Edition 2.0.0 <c>UnderKeelClearanceControlPoint</c>
    /// — the feature class is geometry-bearing (point primitive) by
    /// definition. The typed projection tolerates absent geometry to
    /// avoid throwing on malformed input; this rule surfaces it as a
    /// finding instead.
    /// </remarks>
    public static IValidationRule<S129UnderKeelClearancePlan> ControlPointHasPosition { get; } =
        ValidationRuleBuilder.RuleFor<S129UnderKeelClearancePlan>("S129-R-5.2")
            .WithDescription("Each control point must have a point position.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((plan, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var cp in plan.ControlPoints)
                {
                    if (cp.Position is null)
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S129-R-5.2",
                            Severity = ValidationSeverity.Warning,
                            Message = $"Control point '{cp.Id}' has no point geometry.",
                            RelatedFeatureId = cp.Id,
                        });
                }
                return findings;
            })
            .Build();

    /// <summary>The canonical default rule set for S-129 UKC plans.</summary>
    public static ValidationRuleSet<S129UnderKeelClearancePlan> Default { get; } = new(
        PlanValidityPeriod,
        ControlPointTimeMonotonic,
        CoordinatesInWgs84Range,
        PlanAreaGeometryPopulated,
        MaximumDraughtPositive,
        ControlPointMeasurementsFinite,
        ControlPointHasPosition);

    /// <summary>
    /// Convenience wrapper around <see cref="ValidationRuleSet{T}.Run(T, ValidationContext?)"/>
    /// using the <see cref="Default"/> rule set.
    /// </summary>
    public static ValidationReport Validate(
        S129UnderKeelClearancePlan plan,
        ValidationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Default.Run(plan, context);
    }
}
