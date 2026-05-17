using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S411.DataModel;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S411.Validation;

/// <summary>
/// The default <see cref="ValidationRuleSet{TModel}"/> of normative rules
/// for an S-411 <see cref="S411SeaIceInventory"/>. Rule identifiers follow
/// the convention <c>S411-R-{clause}</c>, where <c>{clause}</c> traces to
/// the relevant section of the S-411 (Sea Ice Information for Surface
/// Navigation) product specification, Edition 1.2.1.
/// </summary>
/// <remarks>
/// <para>
/// The pilot rule set focuses on Tier-1 (schema-shape) and Tier-2
/// (spec-semantic) rules that can be evaluated against a single
/// <see cref="S411SeaIceInventory"/> in isolation. Tier-3 cross-dataset
/// rules will be added in a follow-up once the MCP <c>validate_all</c>
/// surface is wired up.
/// </para>
/// <para>
/// All rules bind to the typed projection (<see cref="S411SeaIceInventory"/>
/// and <see cref="S411IceFeature"/> subclasses) rather than to either of
/// the two real-world GML shapes encountered in production
/// (JCOMM / Canadian Ice Service operational shape and IHO 1.2.1 sample
/// shape). Both shapes normalise to the same typed model, so the rules
/// are shape-agnostic.
/// </para>
/// </remarks>
public static class S411SeaIceRules
{
    // ── S411-R-3.1 — WGS-84 lat/lon bounds ──────────────────────────

    /// <summary>
    /// <c>S411-R-3.1</c> — Every feature coordinate must lie within the
    /// valid WGS-84 ranges: latitude in [-90, +90] and longitude in
    /// [-180, +180].
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.2 — geographic coordinates for
    /// <c>EPSG:4326</c> are bounded. Applied uniformly to every typed
    /// feature regardless of geometry kind.
    /// </remarks>
    public static IValidationRule<S411SeaIceInventory> CoordinatesInWgs84Range { get; } =
        ValidationRuleBuilder.RuleFor<S411SeaIceInventory>("S411-R-3.1")
            .WithDescription("Feature coordinates must lie within the WGS-84 lat/lon ranges.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((inv, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var f in EnumerateAllFeatures(inv))
                {
                    foreach (var pos in f.Coordinates)
                    {
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
                            RuleId = "S411-R-3.1",
                            Severity = ValidationSeverity.Error,
                            Message = $"Feature '{f.Id}' ({f.NormalizedFeatureType}): {details}.",
                            Point = pos,
                            RelatedFeatureId = f.Id,
                        });
                    }
                }
                return findings;
            })
            .Build();

    // ── S411-R-3.2 — surface polygon closure ────────────────────────

    /// <summary>
    /// <c>S411-R-3.2</c> — Surface (area) features must have a closed
    /// exterior ring of at least four coordinates with the first and last
    /// coincident.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.3 (GML surface encoding) —
    /// <c>gml:LinearRing</c> must be closed (first point equals last)
    /// and must contain at least four positions to enclose a non-zero
    /// area. Closure is tested with a tight tolerance (1e-9 degrees) so
    /// floating-point round-trip does not trigger the rule.
    /// </remarks>
    public static IValidationRule<S411SeaIceInventory> SurfacePolygonClosed { get; } =
        ValidationRuleBuilder.RuleFor<S411SeaIceInventory>("S411-R-3.2")
            .WithDescription("Surface features must have a closed ring with ≥ 4 coordinates.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((inv, _) =>
            {
                const double tolerance = 1e-9;
                var findings = new List<ValidationFinding>();
                foreach (var f in EnumerateAllFeatures(inv))
                {
                    if (f.GeometryKind != S411GeometryKind.Surface) continue;

                    var coords = f.Coordinates;
                    if (coords.Length < 4)
                    {
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S411-R-3.2",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Surface feature '{f.Id}' ({f.NormalizedFeatureType}) has " +
                                $"{coords.Length} coordinate(s); a closed ring requires ≥ 4.",
                            RelatedFeatureId = f.Id,
                        });
                        continue;
                    }

                    var first = coords[0];
                    var last = coords[^1];
                    if (Math.Abs(first.Latitude - last.Latitude) > tolerance
                        || Math.Abs(first.Longitude - last.Longitude) > tolerance)
                    {
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S411-R-3.2",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Surface feature '{f.Id}' ({f.NormalizedFeatureType}) is not closed: " +
                                $"first ({first.Latitude}, {first.Longitude}) ≠ last " +
                                $"({last.Latitude}, {last.Longitude}).",
                            Point = first,
                            RelatedFeatureId = f.Id,
                        });
                    }
                }
                return findings;
            })
            .Build();

    // ── S411-R-3.3 — curve minimum vertex count ─────────────────────

    /// <summary>
    /// <c>S411-R-3.3</c> — Curve (line) features (e.g. <c>IceEdge</c>,
    /// <c>IcebergLimit</c>, <c>LineOfIceCrack</c>) must have at least two
    /// vertices.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.3 (GML curve encoding) — a
    /// <c>gml:LineString</c> requires at least two positions to describe
    /// a non-degenerate segment.
    /// </remarks>
    public static IValidationRule<S411SeaIceInventory> CurveHasMinimumVertices { get; } =
        ValidationRuleBuilder.RuleFor<S411SeaIceInventory>("S411-R-3.3")
            .WithDescription("Curve features must have at least two vertices.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((inv, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var f in EnumerateAllFeatures(inv))
                {
                    if (f.GeometryKind != S411GeometryKind.Curve) continue;
                    if (f.Coordinates.Length >= 2) continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S411-R-3.3",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"Curve feature '{f.Id}' ({f.NormalizedFeatureType}) has " +
                            $"{f.Coordinates.Length} vertex(es); a linestring requires ≥ 2.",
                        RelatedFeatureId = f.Id,
                    });
                }
                return findings;
            })
            .Build();

    // ── S411-R-4.1 — total concentration enumeration ────────────────

    /// <summary>
    /// The set of valid <c>totalConcentration</c> enumeration codes from
    /// S-411 Edition 1.2.1 Annex A (WMO sea-ice concentration in tenths
    /// plus sentinels: 1 = Ice Free, 2 = Open Water, 3 = Bergy Water,
    /// 10/20/…/90 = N/10 ice, 12/13/23/24/… = range pairs, 91 = ice of
    /// land origin, 92 = undefined / unknown, 99 = not surveyed).
    /// </summary>
    private static readonly ImmutableHashSet<int> ValidTotalConcentrationCodes = ImmutableHashSet.Create(
        1, 2, 3,
        10, 12, 13,
        20, 23, 24,
        30, 34, 35,
        40, 45, 46,
        50, 56, 57,
        60, 67, 68,
        70, 78, 79,
        80, 81, 89,
        90, 91, 92, 99);

    /// <summary>
    /// <c>S411-R-4.1</c> — When a <c>SeaIce</c> or <c>LakeIce</c> feature
    /// carries an egg-code total concentration, the value must be one of
    /// the enumerated WMO codes defined by the S-411 Feature Catalogue.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-411 Annex A <c>totalConcentration</c> (ICEACT) —
    /// an enumeration of WMO sea-ice concentration codes. The allowed
    /// codes include both single-tenth values (10, 20, … 90) and WMO
    /// range pairs (e.g. 23 = "2/10 to 3/10"), plus reserved sentinels
    /// (1 = Ice Free, 2 = Open Water, 3 = Bergy Water, 91 = ice of land
    /// origin, 92 = undefined, 99 = not surveyed). Values outside this
    /// closed set are reported as warnings because some producers extend
    /// the vocabulary informally.
    /// </remarks>
    public static IValidationRule<S411SeaIceInventory> TotalConcentrationInEnumeration { get; } =
        ValidationRuleBuilder.RuleFor<S411SeaIceInventory>("S411-R-4.1")
            .WithDescription("Egg-code total concentration must be one of the S-411 enumerated WMO codes.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((inv, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var f in inv.IceFeatures)
                {
                    var egg = f switch
                    {
                        S411SeaIce s => s.EggCode,
                        S411LakeIce l => l.EggCode,
                        _ => null,
                    };
                    if (egg?.TotalConcentration is not { } code) continue;
                    if (ValidTotalConcentrationCodes.Contains(code)) continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S411-R-4.1",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"Feature '{f.Id}' ({f.NormalizedFeatureType}) carries " +
                            $"totalConcentration={code}, which is not in the S-411 enumerated WMO code set.",
                        RelatedFeatureId = f.Id,
                    });
                }
                return findings;
            })
            .Build();

    // ── S411-R-4.2 — ice average thickness non-negative ─────────────

    /// <summary>
    /// <c>S411-R-4.2</c> — When an <see cref="S411IceThickness"/> feature
    /// carries an <c>iceAverageThickness</c> value, it must be
    /// non-negative.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-411 Annex A <c>iceAverageThickness</c> (ICETCK)
    /// — a real-valued ice-thickness measurement. Negative thickness has
    /// no physical meaning.
    /// </remarks>
    public static IValidationRule<S411SeaIceInventory> IceAverageThicknessNonNegative { get; } =
        ValidationRuleBuilder.RuleFor<S411SeaIceInventory>("S411-R-4.2")
            .WithDescription("Ice average thickness must be non-negative when present.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((inv, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var f in inv.IceFeatures.OfType<S411IceThickness>())
                {
                    if (f.IceAverageThickness is not { } thickness || thickness >= 0) continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S411-R-4.2",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"IceThickness feature '{f.Id}' has negative iceAverageThickness " +
                            $"({thickness}).",
                        RelatedFeatureId = f.Id,
                    });
                }
                return findings;
            })
            .Build();

    // ── S411-R-4.3 — snow depth non-negative ────────────────────────

    /// <summary>
    /// <c>S411-R-4.3</c> — When an egg-code <c>snowDepth</c> value is
    /// present, it must be non-negative.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-411 Annex A <c>snowDepth</c> (ICESCT) — the
    /// depth of snow cover on the ice, measured in centimetres. Negative
    /// values are nonsensical.
    /// </remarks>
    public static IValidationRule<S411SeaIceInventory> SnowDepthNonNegative { get; } =
        ValidationRuleBuilder.RuleFor<S411SeaIceInventory>("S411-R-4.3")
            .WithDescription("Egg-code snow depth must be non-negative when present.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((inv, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var f in inv.IceFeatures)
                {
                    var egg = f switch
                    {
                        S411SeaIce s => s.EggCode,
                        S411LakeIce l => l.EggCode,
                        _ => null,
                    };
                    if (egg?.SnowDepth is not { } depth || depth >= 0) continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S411-R-4.3",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"Feature '{f.Id}' ({f.NormalizedFeatureType}) has negative snowDepth " +
                            $"({depth}).",
                        RelatedFeatureId = f.Id,
                    });
                }
                return findings;
            })
            .Build();

    // ── S411-R-4.4 — iceberg size code enumeration ──────────────────

    /// <summary>
    /// Valid <c>icebergSize</c> enumeration codes from S-411 Edition 1.2.1
    /// Annex A: 1 = Growler, 2 = Bergy Bit, 3 = Small, 4 = Medium,
    /// 5 = Large, 6 = Very Large, 7 = Tabular, 8 = Pinnacle, 9 = Dome,
    /// 99 = Other.
    /// </summary>
    private static readonly ImmutableHashSet<int> ValidIcebergSizeCodes =
        ImmutableHashSet.Create(1, 2, 3, 4, 5, 6, 7, 8, 9, 99);

    /// <summary>
    /// <c>S411-R-4.4</c> — When an <see cref="S411Iceberg"/> feature
    /// carries an <c>icebergSize</c> value, it must be one of the
    /// enumerated codes defined by the S-411 Feature Catalogue.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-411 Annex A <c>icebergSize</c> (ICEBSZ) — a
    /// closed enumeration with codes 1–9 (Growler … Dome) plus 99
    /// (Other). Any other value is a producer error.
    /// </remarks>
    public static IValidationRule<S411SeaIceInventory> IcebergSizeInEnumeration { get; } =
        ValidationRuleBuilder.RuleFor<S411SeaIceInventory>("S411-R-4.4")
            .WithDescription("Iceberg size code must be one of the S-411 enumerated values.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((inv, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var f in inv.IceFeatures.OfType<S411Iceberg>())
                {
                    if (f.IcebergSizeCode is not { } code) continue;
                    if (ValidIcebergSizeCodes.Contains(code)) continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S411-R-4.4",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"Iceberg feature '{f.Id}' has icebergSize={code}, which is not in " +
                            $"the S-411 enumeration (1–9, 99).",
                        RelatedFeatureId = f.Id,
                    });
                }
                return findings;
            })
            .Build();

    // ── S411-R-5.1 — unique feature identifiers ─────────────────────

    /// <summary>
    /// <c>S411-R-5.1</c> — Every feature in the dataset must carry a
    /// unique identifier.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §5 — <c>gml:id</c> uniqueness is a
    /// GML schema requirement; duplicate IDs break xlink resolution and
    /// downstream lookups. Empty IDs are ignored (they're a separate
    /// schema-shape concern handled by the projection).
    /// </remarks>
    public static IValidationRule<S411SeaIceInventory> UniqueFeatureIdentifiers { get; } =
        ValidationRuleBuilder.RuleFor<S411SeaIceInventory>("S411-R-5.1")
            .WithDescription("Feature identifiers must be unique across the dataset.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((inv, _) =>
            {
                var seen = new Dictionary<string, S411GeometryKind>(StringComparer.Ordinal);
                var reported = new HashSet<string>(StringComparer.Ordinal);
                var findings = new List<ValidationFinding>();
                foreach (var f in EnumerateAllFeatures(inv))
                {
                    if (string.IsNullOrEmpty(f.Id)) continue;
                    if (!seen.ContainsKey(f.Id))
                    {
                        seen.Add(f.Id, f.GeometryKind);
                        continue;
                    }

                    if (!reported.Add(f.Id)) continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S411-R-5.1",
                        Severity = ValidationSeverity.Error,
                        Message = $"Duplicate feature identifier '{f.Id}'.",
                        RelatedFeatureId = f.Id,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>The canonical default rule set for S-411 sea-ice inventories.</summary>
    public static ValidationRuleSet<S411SeaIceInventory> Default { get; } = new(
        CoordinatesInWgs84Range,
        SurfacePolygonClosed,
        CurveHasMinimumVertices,
        TotalConcentrationInEnumeration,
        IceAverageThicknessNonNegative,
        SnowDepthNonNegative,
        IcebergSizeInEnumeration,
        UniqueFeatureIdentifiers);

    /// <summary>
    /// Convenience wrapper around
    /// <see cref="ValidationRuleSet{T}.Run(T, ValidationContext?)"/> using
    /// the <see cref="Default"/> rule set.
    /// </summary>
    public static ValidationReport Validate(S411SeaIceInventory inventory, ValidationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return Default.Run(inventory, context);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static IEnumerable<S411IceFeature> EnumerateAllFeatures(S411SeaIceInventory inv)
    {
        foreach (var f in inv.IceFeatures) yield return f;
        foreach (var f in inv.DataCoverages) yield return f;
        foreach (var f in inv.OtherFeatures) yield return f;
    }
}
