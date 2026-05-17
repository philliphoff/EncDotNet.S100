using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S131.DataModel;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S131.Validation;

/// <summary>
/// The default <see cref="ValidationRuleSet{TModel}"/> of normative rules
/// for an S-131 <see cref="S131HarbourInfrastructureDataset"/>. Rule
/// identifiers follow the convention <c>S131-R-{clause}</c>, where
/// <c>{clause}</c> traces to the relevant section of the S-131
/// Product Specification (FC Edition 1.0.0 / PC Edition 2.0.0) or the
/// S-100 framework (Part 10b §6.2 for coordinate encoding).
/// </summary>
/// <remarks>
/// <para>
/// The pilot rule set focuses on Tier-1 (schema-shape) and Tier-2
/// (spec-semantic) rules that can be evaluated against a single
/// <see cref="S131HarbourInfrastructureDataset"/> in isolation. Tier-3
/// cross-dataset rules (e.g. cross-checking <c>Berth</c> depths against a
/// loaded S-102 bathymetric surface) are out of scope here — they would
/// reach sibling datasets via <see cref="ValidationContext.Services"/>.
/// </para>
/// <para>
/// S-131 has a few characteristics worth flagging up-front:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     Container-style information types (e.g. <c>Authority</c>) never
///     carry geometry — these are modelled as <see cref="IS131InformationType"/>
///     and are excluded from geometry-presence rules by construction.
///     </description>
///   </item>
///   <item>
///     <description>
///     A handful of <c>HarbourPhysicalInfrastructure</c> features
///     (e.g. <see cref="S131HarbourInfrastructureKind.HarbourFacility"/>)
///     are container-style in practice — they aggregate child features
///     through xlinks and may legitimately have no geometry. These
///     kinds are exempted from <see cref="HarbourInfrastructureGeometryPresent"/>.
///     </description>
///   </item>
/// </list>
/// </remarks>
public static class S131HarbourInfrastructureRules
{
    // ── Helpers ────────────────────────────────────────────────────────

    private static IEnumerable<GeoPosition> EnumerateCoordinates(S131Geometry geometry)
    {
        if (geometry.IsEmpty)
            yield break;

        foreach (var p in geometry.Points)
            yield return p;

        foreach (var curve in geometry.Curves)
            foreach (var p in curve)
                yield return p;

        foreach (var p in geometry.ExteriorRing)
            yield return p;

        foreach (var ring in geometry.InteriorRings)
            foreach (var p in ring)
                yield return p;
    }

    private static bool IsValidWgs84(GeoPosition p) =>
        p.Latitude is >= -90 and <= 90 && p.Longitude is >= -180 and <= 180;

    /// <summary>
    /// Harbour-physical-infrastructure kinds that, per the S-131 FC
    /// (§B.2), are container-style and may legitimately appear without
    /// geometry — they aggregate child features through xlinks. These
    /// are exempted from the geometry-presence rule.
    /// </summary>
    private static readonly ImmutableHashSet<S131HarbourInfrastructureKind> ContainerHarbourKinds =
        ImmutableHashSet.Create(
            S131HarbourInfrastructureKind.HarbourFacility);

    // ── S131-R-1.1 — harbour-infrastructure geometry present ───────────

    /// <summary>
    /// <c>S131-R-1.1</c> — Every non-container
    /// <c>HarbourPhysicalInfrastructure</c> feature must carry a
    /// non-empty geometry.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-131 FC Edition 1.0.0 §B.2 — features such as
    /// <c>Bollard</c>, <c>Dolphin</c>, <c>DryDock</c>, <c>MooringBuoy</c>,
    /// <c>LockBasin</c>, etc. describe fixed physical installations and
    /// require a spatial anchor. Container-style kinds (currently only
    /// <c>HarbourFacility</c>) are exempted because they aggregate
    /// constituent features by xlink and may have no geometry of their
    /// own.
    /// </remarks>
    public static IValidationRule<S131HarbourInfrastructureDataset> HarbourInfrastructureGeometryPresent { get; } =
        ValidationRuleBuilder.RuleFor<S131HarbourInfrastructureDataset>("S131-R-1.1")
            .WithDescription("HarbourPhysicalInfrastructure features must have non-empty geometry.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var f in dataset.HarbourInfrastructure)
                {
                    if (ContainerHarbourKinds.Contains(f.Kind)) continue;
                    if (!f.Geometry.IsEmpty) continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S131-R-1.1",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"Harbour infrastructure feature '{f.Id}' ({f.FeatureType}) has no geometry.",
                        RelatedFeatureId = f.Id,
                    });
                }
                return findings;
            })
            .Build();

    // ── S131-R-1.2 — layout-feature geometry present ───────────────────

    /// <summary>
    /// <c>S131-R-1.2</c> — Every <c>Layout</c> feature (berths,
    /// terminals, anchorage areas, harbour basins, etc.) must carry a
    /// non-empty geometry.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-131 FC Edition 1.0.0 §B.2 — every
    /// <c>Layout</c>-derived feature is intrinsically spatial; a berth
    /// without coordinates cannot be drawn or routed to. Unlike the
    /// <c>HarbourPhysicalInfrastructure</c> branch there are no
    /// container exceptions among layout features.
    /// </remarks>
    public static IValidationRule<S131HarbourInfrastructureDataset> LayoutFeatureGeometryPresent { get; } =
        ValidationRuleBuilder.RuleFor<S131HarbourInfrastructureDataset>("S131-R-1.2")
            .WithDescription("Layout features must have non-empty geometry.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var f in dataset.LayoutFeatures)
                {
                    if (!f.Geometry.IsEmpty) continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S131-R-1.2",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"Layout feature '{f.Id}' ({f.FeatureType}) has no geometry.",
                        RelatedFeatureId = f.Id,
                    });
                }
                return findings;
            })
            .Build();

    // ── S131-R-2.1 — available berthing length non-negative ────────────

    /// <summary>
    /// <c>S131-R-2.1</c> — When the <c>availableBerthingLength</c>
    /// attribute is present on a feature, its value must be a valid
    /// non-negative number expressed in metres.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-131 FC Edition 1.0.0 — the
    /// <c>availableBerthingLength</c> simple attribute is bound to
    /// <c>Berth</c>, <c>BerthPosition</c>, and related layout features;
    /// the FC declares the quantity specification as <c>length</c>
    /// with the SI base unit (metres), so negative or non-numeric
    /// values are nonsensical.
    /// </remarks>
    public static IValidationRule<S131HarbourInfrastructureDataset> AvailableBerthingLengthNonNegative { get; } =
        ValidationRuleBuilder.RuleFor<S131HarbourInfrastructureDataset>("S131-R-2.1")
            .WithDescription("availableBerthingLength must be a non-negative numeric value.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                const string code = "availableBerthingLength";
                var findings = new List<ValidationFinding>();

                foreach (var f in dataset.Features)
                {
                    if (!f.Source.Attributes.TryGetValue(code, out var raw)) continue;
                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    if (!double.TryParse(
                            raw,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var value))
                    {
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S131-R-2.1",
                            Severity = ValidationSeverity.Warning,
                            Message =
                                $"Feature '{f.Id}' ({f.FeatureType}) has non-numeric " +
                                $"{code} value '{raw}'.",
                            RelatedFeatureId = f.Id,
                        });
                        continue;
                    }

                    if (value < 0)
                    {
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S131-R-2.1",
                            Severity = ValidationSeverity.Warning,
                            Message =
                                $"Feature '{f.Id}' ({f.FeatureType}) has negative " +
                                $"{code} value ({value} m).",
                            RelatedFeatureId = f.Id,
                        });
                    }
                }
                return findings;
            })
            .Build();

    // ── S131-R-3.1 — WGS-84 lat/lon ranges ─────────────────────────────

    /// <summary>
    /// <c>S131-R-3.1</c> — Every coordinate on every feature geometry
    /// (point, curve, exterior ring, interior ring) must lie within
    /// the valid WGS-84 ranges: latitude in [-90, +90] and longitude
    /// in [-180, +180].
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.2 — geographic coordinates for
    /// <c>EPSG:4326</c> are bounded. S-131 uses the S-100 GML 5.0
    /// profile with lat/lon ordering for <c>gml:pos</c> and
    /// <c>gml:posList</c>.
    /// </remarks>
    public static IValidationRule<S131HarbourInfrastructureDataset> CoordinatesInWgs84Range { get; } =
        ValidationRuleBuilder.RuleFor<S131HarbourInfrastructureDataset>("S131-R-3.1")
            .WithDescription("All coordinates must lie within the WGS-84 lat/lon ranges.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var f in dataset.Features)
                {
                    foreach (var p in EnumerateCoordinates(f.Geometry))
                    {
                        if (IsValidWgs84(p)) continue;

                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S131-R-3.1",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Feature '{f.Id}' ({f.FeatureType}) has coordinate " +
                                $"({p.Latitude}, {p.Longitude}) outside WGS-84 bounds.",
                            Point = p,
                            RelatedFeatureId = f.Id,
                        });
                    }
                }
                return findings;
            })
            .Build();

    // ── S131-R-3.2 — surface ring closure ──────────────────────────────

    /// <summary>
    /// <c>S131-R-3.2</c> — Every surface feature's exterior and
    /// interior rings must be closed (first vertex equals last) and
    /// contain at least four vertices.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b (GML profile) — surface rings are
    /// encoded as <c>gml:LinearRing</c>, which by the OGC GML 3.2 rules
    /// must be closed and contain at least four positions (three
    /// distinct vertices plus the closing repeat).
    /// </remarks>
    public static IValidationRule<S131HarbourInfrastructureDataset> SurfaceRingsClosed { get; } =
        ValidationRuleBuilder.RuleFor<S131HarbourInfrastructureDataset>("S131-R-3.2")
            .WithDescription("Surface rings must have ≥ 4 vertices and be closed (first = last).")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                const double tolerance = 1e-9;
                var findings = new List<ValidationFinding>();

                foreach (var f in dataset.Features)
                {
                    if (f.Geometry.GeometryType != S131GeometryType.Surface) continue;

                    CheckRing(f, "exterior ring", f.Geometry.ExteriorRing, findings, tolerance);
                    for (int i = 0; i < f.Geometry.InteriorRings.Length; i++)
                    {
                        CheckRing(f, $"interior ring [{i}]", f.Geometry.InteriorRings[i], findings, tolerance);
                    }
                }
                return findings;

                static void CheckRing(
                    IS131Feature f,
                    string label,
                    ImmutableArray<GeoPosition> ring,
                    List<ValidationFinding> sink,
                    double tolerance)
                {
                    if (ring.IsDefaultOrEmpty) return;

                    if (ring.Length < 4)
                    {
                        sink.Add(new ValidationFinding
                        {
                            RuleId = "S131-R-3.2",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Feature '{f.Id}' ({f.FeatureType}) {label} has only " +
                                $"{ring.Length} vertex/vertices; a closed ring requires at least 4.",
                            RelatedFeatureId = f.Id,
                        });
                        return;
                    }

                    var first = ring[0];
                    var last = ring[^1];
                    if (Math.Abs(first.Latitude - last.Latitude) > tolerance
                        || Math.Abs(first.Longitude - last.Longitude) > tolerance)
                    {
                        sink.Add(new ValidationFinding
                        {
                            RuleId = "S131-R-3.2",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Feature '{f.Id}' ({f.FeatureType}) {label} is not closed " +
                                $"(first=({first.Latitude}, {first.Longitude}), " +
                                $"last=({last.Latitude}, {last.Longitude})).",
                            Point = first,
                            RelatedFeatureId = f.Id,
                        });
                    }
                }
            })
            .Build();

    // ── S131-R-4.1 — unique feature / info-type identifiers ────────────

    /// <summary>
    /// <c>S131-R-4.1</c> — No two features (and no two information
    /// types) in a dataset may share the same <c>gml:id</c>.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §7 (GML identifier model) —
    /// <c>gml:id</c> values must be unique within the document. The
    /// typed-projection layer also emits an <c>s131.id.duplicate</c>
    /// diagnostic for this condition; the rule re-surfaces it as an
    /// addressable Error finding in the validation report.
    /// </remarks>
    public static IValidationRule<S131HarbourInfrastructureDataset> UniqueFeatureIds { get; } =
        ValidationRuleBuilder.RuleFor<S131HarbourInfrastructureDataset>("S131-R-4.1")
            .WithDescription("Feature and information-type gml:id values must be unique within the dataset.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();

                ReportDuplicates(
                    dataset.Features.Select(f => (f.Id, Kind: "feature")),
                    findings);
                ReportDuplicates(
                    dataset.InformationTypes.Select(i => (i.Id, Kind: "information type")),
                    findings);

                return findings;

                static void ReportDuplicates(
                    IEnumerable<(string Id, string Kind)> items,
                    List<ValidationFinding> sink)
                {
                    var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (id, kind) in items)
                    {
                        if (string.IsNullOrEmpty(id)) continue;
                        if (seen.TryGetValue(id, out var n))
                        {
                            seen[id] = n + 1;
                            sink.Add(new ValidationFinding
                            {
                                RuleId = "S131-R-4.1",
                                Severity = ValidationSeverity.Error,
                                Message = $"Duplicate {kind} gml:id '{id}'.",
                                RelatedFeatureId = id,
                            });
                        }
                        else
                        {
                            seen[id] = 1;
                        }
                    }
                }
            })
            .Build();

    // ── S131-R-5.1 — xlink resolution sanity ──────────────────────────

    /// <summary>
    /// <c>S131-R-5.1</c> — Every <c>xlink:href</c> reference recorded
    /// in <see cref="IS131Feature.ResolvedReferences"/> or
    /// <see cref="IS131InformationType.ResolvedReferences"/> must
    /// resolve to a typed peer in the same dataset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Spec reference: S-131 FC Edition 1.0.0 §B.3 — feature/info-type
    /// associations are encoded as <c>xlink:href</c> attributes on
    /// role-bearing child elements; the target must exist within the
    /// dataset's id space.
    /// </para>
    /// <para>
    /// <b>Deviation from S-421 R-7.x:</b> S-421 only carries
    /// feature-to-feature xlinks (waypoints, legs, action points) which
    /// can be reasoned about purely from the typed
    /// <c>S421RoutePlan</c> graph. S-131 also carries information-type
    /// bindings (e.g. <c>Authority</c> → <c>ContactDetails</c>) that
    /// are surfaced through <see cref="IS131InformationType.ResolvedReferences"/>,
    /// so this rule checks both axes.
    /// </para>
    /// </remarks>
    public static IValidationRule<S131HarbourInfrastructureDataset> ResolvedReferencesNotNull { get; } =
        ValidationRuleBuilder.RuleFor<S131HarbourInfrastructureDataset>("S131-R-5.1")
            .WithDescription("All xlink:href references must resolve to a peer in the dataset.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();

                foreach (var f in dataset.Features)
                {
                    foreach (var r in f.ResolvedReferences)
                    {
                        if (r.Target is not null) continue;

                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S131-R-5.1",
                            Severity = ValidationSeverity.Warning,
                            Message =
                                $"Feature '{f.Id}' ({f.FeatureType}) has unresolved " +
                                $"{r.Role} xlink to '{r.TargetRef}'.",
                            RelatedFeatureId = f.Id,
                        });
                    }
                }

                foreach (var i in dataset.InformationTypes)
                {
                    foreach (var r in i.ResolvedReferences)
                    {
                        if (r.Target is not null) continue;

                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S131-R-5.1",
                            Severity = ValidationSeverity.Warning,
                            Message =
                                $"Information type '{i.Id}' ({i.TypeCode}) has unresolved " +
                                $"{r.Role} xlink to '{r.TargetRef}'.",
                            RelatedFeatureId = i.Id,
                        });
                    }
                }

                return findings;
            })
            .Build();

    // ── S131-R-6.1 — feature/info-type code recognised ─────────────────

    /// <summary>
    /// <c>S131-R-6.1</c> — Every feature and information type in the
    /// dataset must use a code declared in the S-131 Feature Catalogue
    /// (Edition 1.0.0). Unknown codes — i.e. projections that surface
    /// as <see cref="S131OtherFeature"/> or
    /// <see cref="S131OtherInformationType"/> — are surfaced as Warnings.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-131 FC Edition 1.0.0 §B.1, §B.2 — the FC
    /// closes over the legal feature- and information-type code lists.
    /// A warning (rather than an error) is used so that forward-compat
    /// extensions in future FC editions remain consumable while still
    /// flagging unrecognised codes for follow-up.
    /// </remarks>
    public static IValidationRule<S131HarbourInfrastructureDataset> FeatureCodeRecognised { get; } =
        ValidationRuleBuilder.RuleFor<S131HarbourInfrastructureDataset>("S131-R-6.1")
            .WithDescription("Feature and information-type codes must be declared in the S-131 FC.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();

                foreach (var f in dataset.OtherFeatures)
                {
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S131-R-6.1",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"Feature '{f.Id}' uses unrecognised feature type '{f.FeatureType}'.",
                        RelatedFeatureId = f.Id,
                    });
                }

                foreach (var i in dataset.InformationTypes.OfType<S131OtherInformationType>())
                {
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S131-R-6.1",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"Information type '{i.Id}' uses unrecognised type code '{i.TypeCode}'.",
                        RelatedFeatureId = i.Id,
                    });
                }

                return findings;
            })
            .Build();

    // ── Default rule set ──────────────────────────────────────────────

    /// <summary>The canonical default rule set for S-131 harbour-infrastructure datasets.</summary>
    public static ValidationRuleSet<S131HarbourInfrastructureDataset> Default { get; } = new(
        HarbourInfrastructureGeometryPresent,
        LayoutFeatureGeometryPresent,
        AvailableBerthingLengthNonNegative,
        CoordinatesInWgs84Range,
        SurfaceRingsClosed,
        UniqueFeatureIds,
        ResolvedReferencesNotNull,
        FeatureCodeRecognised);

    /// <summary>
    /// Convenience wrapper around <see cref="ValidationRuleSet{T}.Run(T, ValidationContext?)"/>
    /// using the <see cref="Default"/> rule set.
    /// </summary>
    public static ValidationReport Validate(
        S131HarbourInfrastructureDataset dataset,
        ValidationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return Default.Run(dataset, context);
    }
}
