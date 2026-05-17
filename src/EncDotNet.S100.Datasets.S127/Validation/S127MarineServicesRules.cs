using System.Collections.Immutable;
using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S127.DataModel;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S127.Validation;

/// <summary>
/// The default <see cref="ValidationRuleSet{TModel}"/> of normative rules
/// for an S-127 <see cref="S127MarineServicesDataset"/>. Rule identifiers
/// follow the convention <c>S127-R-{clause}</c>, where <c>{clause}</c>
/// traces to the relevant section of the S-127 Edition 2.0.0 Product
/// Specification.
/// </summary>
/// <remarks>
/// <para>
/// The pilot rule set focuses on Tier-1 (schema-shape) and Tier-2
/// (spec-semantic) rules that can be evaluated against a single typed
/// <see cref="S127MarineServicesDataset"/> in isolation. Tier-3
/// cross-dataset rules (for example, validating that a
/// <c>VesselTrafficService</c>-area report point falls within charted
/// waters in a loaded S-101 dataset) will be added in a follow-up once
/// the cross-dataset validation surface is wired up — they need access
/// to sibling datasets via <see cref="ValidationContext.Services"/>.
/// </para>
/// <para>
/// Two candidates from the initial design pass were intentionally
/// dropped, mirroring the deferred-rule reasoning in
/// <c>EncDotNet.S100.Datasets.S421.Validation.S421RoutePlanRules</c>:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       <b>VTS report-point geometry</b> — <c>VesselTrafficServiceArea</c>
///       features may reference report points via <c>xlink:href</c> to
///       other features. The typed projection only resolves the
///       <c>theAuthority</c> binding today; the other xlinks remain on
///       <c>Source.FeatureReferences</c> and would need a cross-feature
///       resolution pass to validate, so this rule is deferred.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Closed enumeration validity</b> for attributes such as
///       <c>categoryOfService</c> — most S-127 category codes are
///       carried in <c>ExtraAttributes</c> as opaque strings rather
///       than being broken out as typed enums on the projection.
///       Validating a closed enumeration off such a loose surface
///       would be brittle (false positives on values the FC permits
///       but the rule has not been updated for), so this is deferred
///       pending a typed enum surface for the relevant attributes.
///     </description>
///   </item>
/// </list>
/// </remarks>
public static class S127MarineServicesRules
{
    /// <summary>
    /// <c>S127-R-12.1</c> — Every feature's geometry coordinates must
    /// lie within the WGS-84 ranges: latitude in [-90, +90] and
    /// longitude in [-180, +180].
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.2 — geographic coordinates for
    /// <c>EPSG:4326</c> are bounded; S-127 Edition 2.0.0 inherits this
    /// CRS. Container-style features without geometry (for example
    /// <see cref="S127Authority"/>) carry an empty
    /// <see cref="IS127Feature.Coordinates"/> array and therefore
    /// trivially pass this rule.
    /// </remarks>
    public static IValidationRule<S127MarineServicesDataset> WgsLatLonInRange { get; } =
        ValidationRuleBuilder.RuleFor<S127MarineServicesDataset>("S127-R-12.1")
            .WithDescription("Feature coordinates must lie within the WGS-84 lat/lon ranges.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var feature in dataset.Features)
                {
                    if (feature.Coordinates.IsDefaultOrEmpty) continue;

                    foreach (var pos in feature.Coordinates)
                    {
                        bool latOk = pos.Latitude is >= -90 and <= 90;
                        bool lonOk = pos.Longitude is >= -180 and <= 180;
                        if (latOk && lonOk) continue;

                        var details = (latOk, lonOk) switch
                        {
                            (false, true) => $"latitude {pos.Latitude.ToString(CultureInfo.InvariantCulture)} is outside [-90, +90]",
                            (true, false) => $"longitude {pos.Longitude.ToString(CultureInfo.InvariantCulture)} is outside [-180, +180]",
                            _ => $"latitude {pos.Latitude.ToString(CultureInfo.InvariantCulture)} and longitude {pos.Longitude.ToString(CultureInfo.InvariantCulture)} are both out of range",
                        };
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S127-R-12.1",
                            Severity = ValidationSeverity.Error,
                            Message = $"Feature '{feature.Id}' ({feature.FeatureType}): {details}.",
                            Point = pos,
                            RelatedFeatureId = feature.Id,
                            DatasetId = dataset.DatasetIdentifier,
                        });
                    }
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S127-R-12.2</c> — Every <see cref="S127PilotBoardingPlace"/>
    /// feature must carry a non-empty point or surface geometry.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-127 Edition 2.0.0 §12 — <c>PilotBoardingPlace</c>
    /// is a geographic feature whose <c>spatial</c> association is
    /// mandatory; a pilot boarding place without coordinates has no
    /// charted location and cannot be portrayed. Container-style
    /// features (such as <see cref="S127Authority"/>) deliberately
    /// carry no geometry and are out of scope for this rule.
    /// </remarks>
    public static IValidationRule<S127MarineServicesDataset> PilotBoardingPlaceHasGeometry { get; } =
        ValidationRuleBuilder.RuleFor<S127MarineServicesDataset>("S127-R-12.2")
            .WithDescription("PilotBoardingPlace features must carry a non-empty geometry.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var pbp in dataset.Features.OfType<S127PilotBoardingPlace>())
                {
                    if (pbp.GeometryKind != S127GeometryKind.None
                        && !pbp.Coordinates.IsDefaultOrEmpty)
                        continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S127-R-12.2",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"PilotBoardingPlace '{pbp.Id}' has no geometry " +
                            $"(GeometryKind={pbp.GeometryKind}, {pbp.Coordinates.Length} coordinate(s)).",
                        RelatedFeatureId = pbp.Id,
                        DatasetId = dataset.DatasetIdentifier,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S127-R-12.3</c> — Surface geometries must form a closed
    /// polygon: at least four vertices, with the first and last
    /// coincident.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6 — surface exterior rings are
    /// GML <c>LinearRing</c>s and must be topologically closed. The
    /// minimum-of-four-vertices rule is the canonical GML linear-ring
    /// cardinality (three distinct corners plus a repeat of the start).
    /// Closure is tested with a tight tolerance (1e-9 degrees, about
    /// 0.1 mm at the equator) so ordinary floating-point round-trip
    /// does not trigger the rule.
    /// </remarks>
    public static IValidationRule<S127MarineServicesDataset> SurfacePolygonClosure { get; } =
        ValidationRuleBuilder.RuleFor<S127MarineServicesDataset>("S127-R-12.3")
            .WithDescription("Surface geometries must have ≥4 vertices and be closed (first == last).")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                const double tolerance = 1e-9;
                var findings = new List<ValidationFinding>();
                foreach (var feature in dataset.Features)
                {
                    if (feature.GeometryKind != S127GeometryKind.Surface) continue;
                    var ring = feature.Coordinates;
                    if (ring.IsDefaultOrEmpty) continue;

                    if (ring.Length < 4)
                    {
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S127-R-12.3",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Feature '{feature.Id}' ({feature.FeatureType}) has a surface exterior ring " +
                                $"with only {ring.Length} vertex(es); a closed polygon needs at least 4.",
                            RelatedFeatureId = feature.Id,
                            DatasetId = dataset.DatasetIdentifier,
                        });
                        continue;
                    }

                    var first = ring[0];
                    var last = ring[^1];
                    if (Math.Abs(first.Latitude - last.Latitude) > tolerance
                        || Math.Abs(first.Longitude - last.Longitude) > tolerance)
                    {
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S127-R-12.3",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Feature '{feature.Id}' ({feature.FeatureType}) has an unclosed surface ring " +
                                $"(first=({first.Latitude}, {first.Longitude}), last=({last.Latitude}, {last.Longitude})).",
                            Point = first,
                            RelatedFeatureId = feature.Id,
                            DatasetId = dataset.DatasetIdentifier,
                        });
                    }
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S127-R-12.4</c> — Curve geometries must have at least two
    /// vertices.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6 — a GML curve is encoded as a
    /// <c>LineString</c> with at least two <c>pos</c> elements. A
    /// single-point "curve" has no defined direction and cannot be
    /// portrayed as a line.
    /// </remarks>
    public static IValidationRule<S127MarineServicesDataset> CurveMinimumVertices { get; } =
        ValidationRuleBuilder.RuleFor<S127MarineServicesDataset>("S127-R-12.4")
            .WithDescription("Curve geometries must have at least two vertices.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var feature in dataset.Features)
                {
                    if (feature.GeometryKind != S127GeometryKind.Curve) continue;
                    if (feature.Coordinates.Length >= 2) continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S127-R-12.4",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"Feature '{feature.Id}' ({feature.FeatureType}) is a curve with only " +
                            $"{feature.Coordinates.Length} vertex(es); a curve requires at least 2.",
                        RelatedFeatureId = feature.Id,
                        DatasetId = dataset.DatasetIdentifier,
                    });
                }
                return findings;
            })
            .Build();

    private static readonly (string Min, string Max)[] s_vesselSizeLimitPairs =
    {
        ("minimumVesselsLength",  "maximumVesselsLength"),
        ("minimumVesselsDraught", "maximumVesselsDraught"),
        ("minimumVesselsBeam",    "maximumVesselsBeam"),
    };

    /// <summary>
    /// <c>S127-R-12.5</c> — When a feature carries both a minimum and
    /// maximum vessel size limit (length / draught / beam), the
    /// minimum must not exceed the maximum.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-127 Edition 2.0.0 §12 — the vessel-dimension
    /// envelope attributes (<c>minimumVesselsLength</c> /
    /// <c>maximumVesselsLength</c>, and the corresponding draught and
    /// beam pairs) describe the range of vessels eligible for, or
    /// regulated by, a service area. A min greater than max inverts
    /// the envelope. Attributes that are absent or unparseable are
    /// skipped silently; this rule only fires on a sane-but-inverted
    /// pairing.
    /// </remarks>
    public static IValidationRule<S127MarineServicesDataset> VesselSizeLimitsMonotonic { get; } =
        ValidationRuleBuilder.RuleFor<S127MarineServicesDataset>("S127-R-12.5")
            .WithDescription("Vessel-size limit minimums must not exceed their corresponding maximums.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var feature in dataset.Features)
                {
                    var attrs = feature.Source.Attributes;
                    if (attrs.IsEmpty) continue;

                    foreach (var (minKey, maxKey) in s_vesselSizeLimitPairs)
                    {
                        if (!attrs.TryGetValue(minKey, out var minStr)
                            || !attrs.TryGetValue(maxKey, out var maxStr))
                            continue;

                        if (!double.TryParse(minStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
                            || !double.TryParse(maxStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
                            continue;

                        if (min <= max) continue;

                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S127-R-12.5",
                            Severity = ValidationSeverity.Warning,
                            Message =
                                $"Feature '{feature.Id}' ({feature.FeatureType}) has {minKey}={minStr} " +
                                $"greater than {maxKey}={maxStr}.",
                            RelatedFeatureId = feature.Id,
                            DatasetId = dataset.DatasetIdentifier,
                        });
                    }
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S127-R-12.6</c> — Service-hours availability ranges must be
    /// monotonic: when a complex attribute exposes a time-of-day pair
    /// (<c>timeOfDayStart</c> / <c>timeOfDayEnd</c>) or a date pair
    /// (<c>dateStart</c> / <c>dateEnd</c>), the start must not be
    /// strictly after the end.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-127 Edition 2.0.0 §12 — the <c>availability</c>
    /// (and related scheduling) complex attribute carries the periods
    /// during which a service is offered. An inverted range cannot be
    /// interpreted unambiguously and is almost always an authoring
    /// mistake. Values that are absent or unparseable are skipped
    /// silently; the rule only fires when both endpoints parse and the
    /// start is strictly after the end. Times are compared on
    /// <see cref="TimeSpan"/>; dates on <see cref="DateOnly"/>.
    /// </remarks>
    public static IValidationRule<S127MarineServicesDataset> ServiceHoursValidity { get; } =
        ValidationRuleBuilder.RuleFor<S127MarineServicesDataset>("S127-R-12.6")
            .WithDescription("Availability time-of-day / date ranges must have start ≤ end.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var feature in dataset.Features)
                {
                    if (feature.Source.ComplexAttributes.IsDefaultOrEmpty) continue;

                    foreach (var complex in feature.Source.ComplexAttributes)
                    {
                        var subs = complex.SubAttributes;

                        if (subs.TryGetValue("timeOfDayStart", out var todStart)
                            && subs.TryGetValue("timeOfDayEnd", out var todEnd)
                            && TryParseTime(todStart, out var t0)
                            && TryParseTime(todEnd, out var t1)
                            && t0 > t1)
                        {
                            findings.Add(new ValidationFinding
                            {
                                RuleId = "S127-R-12.6",
                                Severity = ValidationSeverity.Warning,
                                Message =
                                    $"Feature '{feature.Id}' ({feature.FeatureType}) has {complex.Code} " +
                                    $"timeOfDayStart={todStart} after timeOfDayEnd={todEnd}.",
                                RelatedFeatureId = feature.Id,
                                DatasetId = dataset.DatasetIdentifier,
                            });
                        }

                        if (subs.TryGetValue("dateStart", out var dStart)
                            && subs.TryGetValue("dateEnd", out var dEnd)
                            && TryParseDate(dStart, out var d0)
                            && TryParseDate(dEnd, out var d1)
                            && d0 > d1)
                        {
                            findings.Add(new ValidationFinding
                            {
                                RuleId = "S127-R-12.6",
                                Severity = ValidationSeverity.Warning,
                                Message =
                                    $"Feature '{feature.Id}' ({feature.FeatureType}) has {complex.Code} " +
                                    $"dateStart={dStart} after dateEnd={dEnd}.",
                                RelatedFeatureId = feature.Id,
                                DatasetId = dataset.DatasetIdentifier,
                            });
                        }
                    }
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S127-R-12.7</c> — Feature identifiers must be unique within
    /// a dataset.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6 (GML encoding) — <c>gml:id</c>
    /// is an XML ID and must be unique within the GML document. The
    /// typed projection groups duplicates during a case-insensitive
    /// pass; this rule surfaces them to callers (and protects against
    /// upstream producers that emit non-unique IDs).
    /// </remarks>
    public static IValidationRule<S127MarineServicesDataset> UniqueFeatureIds { get; } =
        ValidationRuleBuilder.RuleFor<S127MarineServicesDataset>("S127-R-12.7")
            .WithDescription("Feature identifiers must be unique within the dataset.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                if (dataset.Features.IsDefaultOrEmpty)
                    return Array.Empty<ValidationFinding>();

                var findings = new List<ValidationFinding>();
                var groups = dataset.Features
                    .Where(f => !string.IsNullOrEmpty(f.Id))
                    .GroupBy(f => f.Id, StringComparer.OrdinalIgnoreCase);

                foreach (var group in groups)
                {
                    var count = group.Count();
                    if (count <= 1) continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S127-R-12.7",
                        Severity = ValidationSeverity.Error,
                        Message = $"Feature identifier '{group.Key}' is used by {count} features.",
                        RelatedFeatureId = group.Key,
                        DatasetId = dataset.DatasetIdentifier,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S127-R-12.8</c> — <see cref="S127Authority"/> features should
    /// carry a non-empty <c>authorityName</c>.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-127 Edition 2.0.0 §12 — an <c>Authority</c>
    /// without a name has no human-readable identity and is of little
    /// use when bound from a service via the <c>theAuthority</c>
    /// xlink. Emitted as a warning (not an error) because the FC does
    /// not strictly require the attribute; the projection tolerates
    /// its absence.
    /// </remarks>
    public static IValidationRule<S127MarineServicesDataset> AuthorityNamePresent { get; } =
        ValidationRuleBuilder.RuleFor<S127MarineServicesDataset>("S127-R-12.8")
            .WithDescription("Authority features should carry a non-empty authorityName.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var authority in dataset.Authorities)
                {
                    if (!string.IsNullOrWhiteSpace(authority.AuthorityName)) continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S127-R-12.8",
                        Severity = ValidationSeverity.Warning,
                        Message = $"Authority '{authority.Id}' has no authorityName.",
                        RelatedFeatureId = authority.Id,
                        DatasetId = dataset.DatasetIdentifier,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>The canonical default rule set for S-127 marine resources and services datasets.</summary>
    public static ValidationRuleSet<S127MarineServicesDataset> Default { get; } = new(
        WgsLatLonInRange,
        PilotBoardingPlaceHasGeometry,
        SurfacePolygonClosure,
        CurveMinimumVertices,
        VesselSizeLimitsMonotonic,
        ServiceHoursValidity,
        UniqueFeatureIds,
        AuthorityNamePresent);

    /// <summary>
    /// Convenience wrapper around
    /// <see cref="ValidationRuleSet{T}.Run(T, ValidationContext?)"/>
    /// using the <see cref="Default"/> rule set.
    /// </summary>
    public static ValidationReport Validate(
        S127MarineServicesDataset dataset,
        ValidationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return Default.Run(dataset, context);
    }

    // ── Parsing helpers ──────────────────────────────────────────────

    private static bool TryParseTime(string raw, out TimeSpan value)
    {
        // S-100 schedules use ISO 8601 time literals ("HH:mm[:ss]"),
        // optionally with a trailing 'Z'.
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            value = default;
            return false;
        }

        if (trimmed.EndsWith('Z'))
            trimmed = trimmed[..^1];

        return TimeSpan.TryParseExact(trimmed, new[] { @"hh\:mm", @"hh\:mm\:ss" },
            CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseDate(string raw, out DateOnly value)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            value = default;
            return false;
        }

        // Drop any time component if a producer emits a full ISO 8601 datetime.
        var tIndex = trimmed.IndexOf('T');
        if (tIndex >= 0) trimmed = trimmed[..tIndex];

        return DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out value);
    }
}
