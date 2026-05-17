using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S122.DataModel;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S122.Validation;

/// <summary>
/// The default <see cref="ValidationRuleSet{TModel}"/> of normative rules
/// for an S-122 <see cref="S122MarineProtectedAreaDataset"/>. Rule
/// identifiers follow the convention <c>S122-R-{clause}</c>, where
/// <c>{clause}</c> traces to the relevant section of the S-122 product
/// specification (FC 2.0.0) or — for clauses not cleanly attributable to
/// a single FC element — to a locally-assigned number in the <c>9.x</c>
/// range. The XML <c>&lt;remarks&gt;</c> on each rule cites the
/// supporting clause.
/// </summary>
/// <remarks>
/// <para>
/// This rule pack focuses on Tier-1 (schema-shape) and Tier-2
/// (spec-semantic) checks that can be evaluated against a single typed
/// dataset projection in isolation. Tier-3 cross-dataset rules (e.g.
/// cross-referencing an S-122 restricted area against an overlapping
/// S-101 routing zone) will be added in a follow-up once
/// <see cref="ValidationContext.Services"/> is wired up by the MCP
/// <c>validate_all</c> surface.
/// </para>
/// <para>
/// Rules deliberately omitted from this pack:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <em>Information-type binding resolution</em> — the typed
///     projection silently drops unresolved <c>xlink</c> targets and
///     surfaces them as <see cref="Diagnostics.ProjectionDiagnostic"/>
///     entries, not as fields on the projected feature. There is
///     nothing for a typed-model rule to observe. Mirror the deviation
///     note in the S-421 pilot.
///   </description></item>
///   <item><description>
///     <em>Dataset emptiness</em> — <see cref="S122MarineProtectedAreaDataset.From"/>
///     already throws <see cref="InvalidOperationException"/> when both
///     <see cref="S122MarineProtectedAreaDataset.Features"/> and
///     <see cref="S122MarineProtectedAreaDataset.InformationTypes"/>
///     are empty, so there is no observable empty-dataset case at the
///     typed-model layer.
///   </description></item>
/// </list>
/// </remarks>
public static class S122MarineProtectedAreaRules
{
    private const double ClosureToleranceDegrees = 1e-9;

    /// <summary>
    /// <c>S122-R-3.1</c> — Every feature that declares a non-<see cref="S122GeometryKind.None"/>
    /// geometry kind must carry at least one coordinate.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-122 FC 2.0.0 § Feature Types — a feature whose
    /// geometry primitive is <em>Point</em>, <em>Curve</em>, or
    /// <em>Surface</em> must have coordinates conveying that geometry.
    /// Geometry-less features (<c>GeometryKind == None</c>) are tolerated
    /// for container-style projections that delegate their boundary via
    /// an <c>xlink</c> association (see the project-level S-122
    /// instructions, "Renderers must tolerate geometry-less features").
    /// </remarks>
    public static IValidationRule<S122MarineProtectedAreaDataset> GeometryPresentWhenKindSet { get; } =
        ValidationRuleBuilder.RuleFor<S122MarineProtectedAreaDataset>("S122-R-3.1")
            .WithDescription(
                "Features that declare a Point/Curve/Surface geometry kind must carry at least one coordinate.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var feature in dataset.Features)
                {
                    if (feature.GeometryKind == S122GeometryKind.None)
                        continue;
                    if (!feature.Coordinates.IsDefaultOrEmpty)
                        continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S122-R-3.1",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"Feature '{feature.Id}' ({feature.FeatureType}) declares geometry kind " +
                            $"'{feature.GeometryKind}' but carries no coordinates.",
                        RelatedFeatureId = feature.Id,
                        DatasetId = dataset.DatasetIdentifier,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S122-R-4.1</c> — Every coordinate on every feature must lie
    /// within the valid WGS-84 ranges: latitude in <c>[-90, +90]</c>
    /// and longitude in <c>[-180, +180]</c>.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.2 — geographic coordinates for
    /// <c>EPSG:4326</c> are bounded. The S-122 application schema
    /// inherits this constraint via its dependency on the S-100 GML
    /// profile. The S-122 instructions also remind authors that
    /// coordinate ordering in <c>&lt;gml:pos&gt;</c> is lat-lon for
    /// EPSG:4326.
    /// </remarks>
    public static IValidationRule<S122MarineProtectedAreaDataset> CoordinatesWithinWgs84Range { get; } =
        ValidationRuleBuilder.RuleFor<S122MarineProtectedAreaDataset>("S122-R-4.1")
            .WithDescription("All feature coordinates must lie within the WGS-84 lat/lon ranges.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var feature in dataset.Features)
                {
                    if (feature.Coordinates.IsDefaultOrEmpty)
                        continue;

                    for (int i = 0; i < feature.Coordinates.Length; i++)
                    {
                        var pos = feature.Coordinates[i];
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
                            RuleId = "S122-R-4.1",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Feature '{feature.Id}' ({feature.FeatureType}) coordinate #{i}: {details}.",
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
    /// <c>S122-R-5.1</c> — Surface features must have a closed exterior
    /// ring with at least four coordinates (three distinct plus the
    /// closing point) and the first / last positions must coincide
    /// within a small tolerance.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.2.4 / OGC GML 3.2 §10.5 —
    /// <c>gml:LinearRing</c> is closed by definition: the first and
    /// last positions are identical. Surface features therefore require
    /// at least four coordinates (a degenerate triangle plus closure).
    /// The tolerance (<c>1e-9</c> degrees, about 0.1 mm at the equator)
    /// matches the tolerance used by the S-421 pilot's coincident-
    /// waypoint rule so that ordinary floating-point round-trip does
    /// not falsely trigger this rule.
    /// </remarks>
    public static IValidationRule<S122MarineProtectedAreaDataset> SurfaceRingClosure { get; } =
        ValidationRuleBuilder.RuleFor<S122MarineProtectedAreaDataset>("S122-R-5.1")
            .WithDescription(
                "Surface features must have a closed exterior ring (>= 4 coordinates, first == last).")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var feature in dataset.Features)
                {
                    if (feature.GeometryKind != S122GeometryKind.Surface)
                        continue;
                    if (feature.Coordinates.IsDefaultOrEmpty)
                        continue;

                    var ring = feature.Coordinates;
                    if (ring.Length < 4)
                    {
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S122-R-5.1",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Feature '{feature.Id}' ({feature.FeatureType}) surface exterior ring " +
                                $"has {ring.Length} coordinate(s); a closed ring requires at least 4.",
                            RelatedFeatureId = feature.Id,
                            DatasetId = dataset.DatasetIdentifier,
                        });
                        continue;
                    }

                    var first = ring[0];
                    var last = ring[ring.Length - 1];
                    if (Math.Abs(first.Latitude - last.Latitude) > ClosureToleranceDegrees
                        || Math.Abs(first.Longitude - last.Longitude) > ClosureToleranceDegrees)
                    {
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S122-R-5.1",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Feature '{feature.Id}' ({feature.FeatureType}) surface exterior ring " +
                                $"is not closed: first = ({first.Latitude}, {first.Longitude}), " +
                                $"last = ({last.Latitude}, {last.Longitude}).",
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
    /// <c>S122-R-6.1</c> — No two features within the dataset may share
    /// the same identifier (<c>gml:id</c>).
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.4 / OGC GML 3.2 §7.2.4.2 —
    /// <c>gml:id</c> is of XSD type <c>ID</c>, which requires uniqueness
    /// per XML document. Duplicate feature identifiers also break the
    /// projection's <c>xlink</c> resolver, since references can no
    /// longer be disambiguated.
    /// </remarks>
    public static IValidationRule<S122MarineProtectedAreaDataset> UniqueFeatureIds { get; } =
        ValidationRuleBuilder.RuleFor<S122MarineProtectedAreaDataset>("S122-R-6.1")
            .WithDescription("Feature identifiers (gml:id) must be unique within the dataset.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var feature in dataset.Features)
                {
                    if (string.IsNullOrEmpty(feature.Id))
                        continue;
                    if (seen.Add(feature.Id))
                        continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S122-R-6.1",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"Feature identifier '{feature.Id}' ({feature.FeatureType}) is not unique " +
                            "within the dataset.",
                        RelatedFeatureId = feature.Id,
                        DatasetId = dataset.DatasetIdentifier,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S122-R-6.2</c> — No two information types within the dataset
    /// may share the same identifier (<c>gml:id</c>).
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.4 / OGC GML 3.2 §7.2.4.2 —
    /// <c>gml:id</c> uniqueness is per XML document, spanning both
    /// features and information types. This rule covers the
    /// information-type subset; <see cref="UniqueFeatureIds"/> covers
    /// the feature subset. (We split the rule along that boundary so
    /// per-finding <see cref="ValidationFinding.RelatedFeatureId"/>
    /// remains meaningful — feature vs. info type IDs land in
    /// different result lanes in viewer / MCP consumers.)
    /// </remarks>
    public static IValidationRule<S122MarineProtectedAreaDataset> UniqueInformationTypeIds { get; } =
        ValidationRuleBuilder.RuleFor<S122MarineProtectedAreaDataset>("S122-R-6.2")
            .WithDescription("Information-type identifiers (gml:id) must be unique within the dataset.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var info in dataset.InformationTypes)
                {
                    if (string.IsNullOrEmpty(info.Id))
                        continue;
                    if (seen.Add(info.Id))
                        continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S122-R-6.2",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"Information-type identifier '{info.Id}' ({info.TypeCode}) is not unique " +
                            "within the dataset.",
                        RelatedFeatureId = info.Id,
                        DatasetId = dataset.DatasetIdentifier,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S122-R-7.1</c> — When a feature carries a
    /// <c>scaleMinimum</c> attribute, the value must be a positive
    /// display-scale denominator (≥ 1).
    /// </summary>
    /// <remarks>
    /// Spec reference: S-122 FC 2.0.0 §scaleMinimum (inherited from
    /// the FC-abstract <c>FeatureType</c>). The attribute is a display
    /// scale denominator (the "S" in 1:S), so zero or negative values
    /// have no cartographic meaning. Surfaced as a <em>warning</em>
    /// rather than an error because some producer pipelines emit
    /// <c>0</c> as a sentinel for "no scale limit" — non-conformant
    /// but observed in the wild.
    /// </remarks>
    public static IValidationRule<S122MarineProtectedAreaDataset> ScaleMinimumPositive { get; } =
        ValidationRuleBuilder.RuleFor<S122MarineProtectedAreaDataset>("S122-R-7.1")
            .WithDescription("When present, scaleMinimum must be a positive display-scale denominator (>= 1).")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var feature in dataset.Features)
                {
                    if (feature is not S122FeatureBase fb || fb.ScaleMinimum is not { } scaleMin)
                        continue;
                    if (scaleMin >= 1)
                        continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S122-R-7.1",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"Feature '{feature.Id}' ({feature.FeatureType}) has non-positive scaleMinimum " +
                            $"({scaleMin}); a display-scale denominator must be >= 1.",
                        RelatedFeatureId = feature.Id,
                        DatasetId = dataset.DatasetIdentifier,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S122-R-9.1</c> — When present, the dataset
    /// <see cref="S122MarineProtectedAreaDataset.ProductIdentifier"/>
    /// must identify the S-122 product specification (i.e. start with
    /// <c>"S-122"</c>, case-insensitive).
    /// </summary>
    /// <remarks>
    /// Local clause numbering (9.x) — there is no single S-122 FC
    /// clause for product-identifier conformance. The check derives
    /// from the S-122 instructions file: "the S-100
    /// <c>&lt;productIdentifier&gt;</c> element is the source of
    /// truth" for distinguishing S-122 from incorrectly-namespaced
    /// trial datasets (the official 2.0.0 sample is mis-labelled with
    /// the S-123 namespace prefix). A dataset whose product identifier
    /// does not name S-122 should not be consumed as an S-122 dataset.
    /// </remarks>
    public static IValidationRule<S122MarineProtectedAreaDataset> ProductIdentifierIsS122 { get; } =
        ValidationRuleBuilder.RuleFor<S122MarineProtectedAreaDataset>("S122-R-9.1")
            .WithDescription("When present, productIdentifier must identify S-122 (start with 'S-122').")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                var pid = dataset.ProductIdentifier;
                if (string.IsNullOrEmpty(pid))
                    return Array.Empty<ValidationFinding>();
                if (pid.StartsWith("S-122", StringComparison.OrdinalIgnoreCase))
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = "S122-R-9.1",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"Dataset productIdentifier '{pid}' does not name S-122; " +
                            "this dataset may have been opened against the wrong product specification.",
                        DatasetId = dataset.DatasetIdentifier,
                    },
                };
            })
            .Build();

    /// <summary>The canonical default rule set for S-122 datasets.</summary>
    public static ValidationRuleSet<S122MarineProtectedAreaDataset> Default { get; } = new(
        GeometryPresentWhenKindSet,
        CoordinatesWithinWgs84Range,
        SurfaceRingClosure,
        UniqueFeatureIds,
        UniqueInformationTypeIds,
        ScaleMinimumPositive,
        ProductIdentifierIsS122);

    /// <summary>
    /// Convenience wrapper around <see cref="ValidationRuleSet{T}.Run(T, ValidationContext?)"/>
    /// using the <see cref="Default"/> rule set.
    /// </summary>
    public static ValidationReport Validate(
        S122MarineProtectedAreaDataset dataset,
        ValidationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return Default.Run(dataset, context);
    }
}
