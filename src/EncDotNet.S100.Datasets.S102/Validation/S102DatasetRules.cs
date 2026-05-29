using System.Globalization;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S102.Validation;

/// <summary>
/// The default <see cref="ValidationRuleSet{TModel}"/> of normative rules
/// for an S-102 <see cref="S102Dataset"/>. Rule identifiers follow the
/// convention <c>S102-R-{clause}</c> for normative rules and
/// <c>S102-PROJ-{kind}</c> for projection-diagnostic surrogates, traceable
/// back to S-102 (Edition 3.0.0) and S-100 Part 10c.
/// </summary>
/// <remarks>
/// <para>
/// This is the V-1 rule pack as defined in
/// <c>docs/design/non-gml-validation.md</c> §6.1. Rules read off the
/// strongly-typed <see cref="S102Dataset"/> produced by
/// <see cref="S102DatasetReader"/>; structural-schema failures the
/// reader cannot tolerate are thrown as
/// <see cref="EncDotNet.S100.Hdf5.S100DatasetSchemaException"/> at read
/// time and (per design §5.1) surfaced as <c>S102-PROJ-SCHEMA</c>
/// findings by the dataset processor's <c>Validate()</c> wrapper.
/// </para>
/// <para>
/// Tier-3 cross-dataset rules (e.g. tile coherency against a sibling
/// chart) are out of scope; per-finding payload conventions follow
/// design §4.3:
/// <list type="bullet">
/// <item><description>per-coverage finding — <c>RelatedFeatureId = "{groupPath}"</c></description></item>
/// <item><description>per-cell finding — <c>RelatedFeatureId = "{groupPath}[row,col]"</c></description></item>
/// </list>
/// </para>
/// </remarks>
public static class S102DatasetRules
{
    /// <summary>S-102 NODATA sentinel (S-100 Part 10c §11; S-102 §10.x).</summary>
    internal const float NoDataValue = 1_000_000f;

    /// <summary>
    /// Hard-coded set of EPSG codes considered "known" for S-102
    /// horizontal CRS conformance (<c>S102-R-3.1</c>).
    /// Covers WGS-84 geographic (4326), NAD83 geographic (4269), and
    /// the WGS-84 UTM band ranges (north 32601–32660, south 32701–32760).
    /// </summary>
    // TODO: replace with a centralised EPSG registry once one exists in
    // the codebase (search for KnownEpsgCodes turned up no existing
    // catalogue at the time of writing). The S-102 spec permits any
    // CRS resolvable through the EPSG Geodetic Parameter Registry.
    private static bool IsKnownEpsgCode(int code) =>
        code == 4326
        || code == 4269
        || (code >= 32601 && code <= 32660)
        || (code >= 32701 && code <= 32760);

    /// <summary>
    /// <c>S102-R-1.1</c> — Every <see cref="BathymetryCoverage"/>'s
    /// <see cref="BathymetryCoverage.Values"/> array length equals
    /// <see cref="BathymetryCoverage.NumPointsLatitudinal"/> ×
    /// <see cref="BathymetryCoverage.NumPointsLongitudinal"/>.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10c §10.2.1.2 (gridded coverage
    /// shape contract) and S-102 Edition 3.0.0 §12.6
    /// (<c>BathymetryCoverage</c>). Implements the
    /// <c>s102-bathymetry</c> skill review checklist item
    /// "values array shape matches numPointsLat × numPointsLon".
    /// </remarks>
    public static IValidationRule<S102Dataset> CoverageValuesLengthMatchesShape { get; } =
        ValidationRuleBuilder.RuleFor<S102Dataset>("S102-R-1.1")
            .WithDescription("Each BathymetryCoverage's Values length must equal NumPointsLatitudinal × NumPointsLongitudinal.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                for (var i = 0; i < dataset.Coverages.Count; i++)
                {
                    var c = dataset.Coverages[i];
                    long expected = (long)c.NumPointsLatitudinal * c.NumPointsLongitudinal;
                    if (c.Values.LongLength == expected)
                        continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S102-R-1.1",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"BathymetryCoverage at '{CoveragePath(c, i)}' has Values length {c.Values.LongLength} but " +
                            $"expected NumPointsLatitudinal ({c.NumPointsLatitudinal}) × NumPointsLongitudinal ({c.NumPointsLongitudinal}) = {expected}.",
                        RelatedFeatureId = CoveragePath(c, i),
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S102-R-2.1</c> — The NODATA fill value in a coverage's depth
    /// channel is exactly <c>1_000_000f</c>. Any candidate-NODATA
    /// pattern other than that sentinel (<see cref="float.NaN"/>,
    /// <see cref="float.NegativeInfinity"/>,
    /// <see cref="float.PositiveInfinity"/>) is flagged.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10c §11 NODATA convention, S-102
    /// Edition 3.0.0 §10 (mandatory fill = <c>1_000_000f</c>).
    /// Implements <c>s102-bathymetry</c> skill review-checklist
    /// pitfall "NODATA must be exactly 1_000_000f". Conservative
    /// per design §6.1: only NaN / ±Infinity are flagged; finite
    /// values (even implausibly large) are NOT treated as
    /// candidate-NODATA because they are addressed by
    /// <see cref="DepthValuesInPlausibleRange"/> (<c>S102-R-5.1</c>).
    /// </remarks>
    public static IValidationRule<S102Dataset> NoDataFillValueIsCanonical { get; } =
        ValidationRuleBuilder.RuleFor<S102Dataset>("S102-R-2.1")
            .WithDescription("NODATA fill values must be exactly 1_000_000f (S-100 Part 10c §11).")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                for (var i = 0; i < dataset.Coverages.Count; i++)
                {
                    var c = dataset.Coverages[i];
                    if (c.NumPointsLongitudinal <= 0)
                        continue;

                    for (var idx = 0; idx < c.Values.Length; idx++)
                    {
                        float depth = c.Values[idx].Depth;
                        if (depth == NoDataValue)
                            continue;
                        if (!float.IsNaN(depth) && !float.IsInfinity(depth))
                            continue;

                        int row = idx / c.NumPointsLongitudinal;
                        int col = idx % c.NumPointsLongitudinal;
                        string token = float.IsNaN(depth) ? "NaN"
                            : float.IsNegativeInfinity(depth) ? "-Infinity"
                            : "+Infinity";

                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S102-R-2.1",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"BathymetryCoverage at '{CoveragePath(c, i)}' has non-canonical NODATA at [{row},{col}]: " +
                                $"Depth = {token}; expected the S-102 sentinel 1_000_000f.",
                            RelatedFeatureId = $"{CoveragePath(c, i)}[{row},{col}]",
                        });
                    }
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S102-R-3.1</c> — When <see cref="S102Dataset.HorizontalCRS"/>
    /// is set, it resolves to a known EPSG code.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-102 Edition 3.0.0 §10.2 root attribute
    /// <c>horizontalCRS</c>. Severity is <em>Warning</em> per design
    /// §6.1 — an unknown EPSG code typically reflects a producer
    /// mistake but does not render the dataset unusable; consumers
    /// may still be able to resolve the CRS through extended
    /// catalogues. See <see cref="IsKnownEpsgCode"/> for the
    /// recognised set.
    /// </remarks>
    public static IValidationRule<S102Dataset> HorizontalCrsIsKnownEpsg { get; } =
        ValidationRuleBuilder.RuleFor<S102Dataset>("S102-R-3.1")
            .WithDescription("HorizontalCRS, when set, must resolve to a known EPSG code.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                if (dataset.HorizontalCRS is not int crs)
                    return Array.Empty<ValidationFinding>();
                if (IsKnownEpsgCode(crs))
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = "S102-R-3.1",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"HorizontalCRS EPSG:{crs} is not in the recognised S-102 V-1 EPSG set " +
                            "(WGS-84 geographic, NAD83 geographic, or WGS-84 UTM zones 1–60 N/S).",
                    },
                };
            })
            .Build();

    /// <summary>
    /// <c>S102-R-3.2</c> — When <see cref="S102Dataset.IssueDate"/> is
    /// set, it parses as an ISO 8601 date or date-time.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-102 Edition 3.0.0 §10.2 root attribute
    /// <c>issueDate</c>. Implements the <c>s102-bathymetry</c>
    /// skill review-checklist item "root metadata parse cleanly".
    /// </remarks>
    public static IValidationRule<S102Dataset> IssueDateIsIso8601 { get; } =
        ValidationRuleBuilder.RuleFor<S102Dataset>("S102-R-3.2")
            .WithDescription("IssueDate, when set, must parse as an ISO 8601 date or date-time.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                var raw = dataset.IssueDate;
                if (string.IsNullOrWhiteSpace(raw))
                    return Array.Empty<ValidationFinding>();

                if (DateTimeOffset.TryParse(
                        raw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTimeOffset _))
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = "S102-R-3.2",
                        Severity = ValidationSeverity.Warning,
                        Message = $"IssueDate '{raw}' is not a recognisable ISO 8601 date or date-time.",
                    },
                };
            })
            .Build();

    /// <summary>
    /// <c>S102-R-4.1</c> — Each coverage's
    /// <see cref="BathymetryCoverage.OriginLatitude"/> is in [-90, 90]
    /// and <see cref="BathymetryCoverage.OriginLongitude"/> is in
    /// [-180, 180].
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10c §10.2.1.2 grid georeferencing.
    /// Implements <c>s102-bathymetry</c> skill review-checklist item
    /// "georeferencing attributes within WGS-84 range".
    /// </remarks>
    public static IValidationRule<S102Dataset> CoverageOriginInWgs84Range { get; } =
        ValidationRuleBuilder.RuleFor<S102Dataset>("S102-R-4.1")
            .WithDescription("Each BathymetryCoverage origin lat/lon must be within WGS-84 ranges.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                for (var i = 0; i < dataset.Coverages.Count; i++)
                {
                    var c = dataset.Coverages[i];
                    var problems = new List<string>();
                    if (c.OriginLatitude < -90 || c.OriginLatitude > 90)
                        problems.Add($"OriginLatitude {Fmt(c.OriginLatitude)} outside [-90, 90]");
                    if (c.OriginLongitude < -180 || c.OriginLongitude > 180)
                        problems.Add($"OriginLongitude {Fmt(c.OriginLongitude)} outside [-180, 180]");
                    if (problems.Count == 0)
                        continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S102-R-4.1",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"BathymetryCoverage at '{CoveragePath(c, i)}' has out-of-range origin: " +
                            string.Join("; ", problems) + ".",
                        RelatedFeatureId = CoveragePath(c, i),
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S102-R-4.2</c> — Each coverage's extent
    /// (<c>origin + (numPoints - 1) × spacing</c>) does not wrap the
    /// antimeridian (longitude end &gt; 180) or cross the pole
    /// (latitude end &gt; 90). Antimeridian-spanning datasets are out
    /// of scope for V-1.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10c §10.2.1.2; implements the
    /// <c>s102-bathymetry</c> skill review-checklist item "tile
    /// extent stays within WGS-84 ranges". When the rule fires, the
    /// finding carries a <see cref="ValidationFinding.BoundingBox"/>
    /// approximating the offending tile extent (clamped to ordered
    /// edges only; values may themselves be out of range).
    /// </remarks>
    public static IValidationRule<S102Dataset> CoverageExtentDoesNotWrap { get; } =
        ValidationRuleBuilder.RuleFor<S102Dataset>("S102-R-4.2")
            .WithDescription("Coverage extent must not wrap the antimeridian or cross the pole.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                for (var i = 0; i < dataset.Coverages.Count; i++)
                {
                    var c = dataset.Coverages[i];
                    if (c.NumPointsLatitudinal <= 0 || c.NumPointsLongitudinal <= 0)
                        continue;

                    double latEnd = c.OriginLatitude + (c.NumPointsLatitudinal - 1) * c.SpacingLatitudinal;
                    double lonEnd = c.OriginLongitude + (c.NumPointsLongitudinal - 1) * c.SpacingLongitudinal;

                    var problems = new List<string>();
                    if (latEnd > 90 || latEnd < -90)
                        problems.Add($"latitude end {Fmt(latEnd)} outside [-90, 90]");
                    if (lonEnd > 180 || lonEnd < -180)
                        problems.Add($"longitude end {Fmt(lonEnd)} outside [-180, 180] (antimeridian-spanning tiles are out of scope for V-1)");
                    if (problems.Count == 0)
                        continue;

                    double south = Math.Min(c.OriginLatitude, latEnd);
                    double north = Math.Max(c.OriginLatitude, latEnd);
                    double west = Math.Min(c.OriginLongitude, lonEnd);
                    double east = Math.Max(c.OriginLongitude, lonEnd);

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S102-R-4.2",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"BathymetryCoverage at '{CoveragePath(c, i)}' has out-of-range extent: " +
                            string.Join("; ", problems) + ".",
                        RelatedFeatureId = CoveragePath(c, i),
                        BoundingBox = new BoundingBox(south, west, north, east),
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S102-R-5.1</c> — Non-NODATA depth values across each
    /// coverage lie within the plausible bathymetric range
    /// [-50, 12 000] metres.
    /// </summary>
    /// <remarks>
    /// Plausibility heuristic: continental shelves through the
    /// deepest trenches; catches common producer mistakes such as
    /// "depth recorded in centimetres" or "UTM-northing accidentally
    /// stored as depth". Per design §6.1, the rule emits one finding
    /// per coverage with the count of offending cells in the
    /// message — emitting one finding per cell on a broken tile
    /// would drown the report in cascade noise.
    /// </remarks>
    public static IValidationRule<S102Dataset> DepthValuesInPlausibleRange { get; } =
        ValidationRuleBuilder.RuleFor<S102Dataset>("S102-R-5.1")
            .WithDescription("Non-NODATA depth values must lie in [-50, 12000] metres.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                const float min = -50f;
                const float max = 12_000f;
                var findings = new List<ValidationFinding>();
                for (var i = 0; i < dataset.Coverages.Count; i++)
                {
                    var c = dataset.Coverages[i];
                    long offending = 0;
                    float worst = 0f;
                    for (var idx = 0; idx < c.Values.Length; idx++)
                    {
                        float depth = c.Values[idx].Depth;
                        if (depth == NoDataValue)
                            continue;
                        if (float.IsNaN(depth) || float.IsInfinity(depth))
                            continue;
                        if (depth >= min && depth <= max)
                            continue;

                        offending++;
                        if (Math.Abs(depth) > Math.Abs(worst))
                            worst = depth;
                    }
                    if (offending == 0)
                        continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S102-R-5.1",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"BathymetryCoverage at '{CoveragePath(c, i)}' has {offending} non-NODATA depth value(s) outside " +
                            $"the plausible range [-50, 12000] m (worst observed: {Fmt(worst)} m).",
                        RelatedFeatureId = CoveragePath(c, i),
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S102-PROJ-SCHEMA</c> — defensive projection-diagnostic
    /// surrogate (design §5.1, §5.3).
    /// </summary>
    /// <remarks>
    /// The rule body is intentionally a no-op: by the time the rule
    /// pack runs the dataset is already constructed, so any
    /// <see cref="EncDotNet.S100.Hdf5.S100DatasetSchemaException"/>
    /// has already thrown out of the reader. The rule id is reserved
    /// in this rule set so that <c>S102DatasetProcessor.Validate()</c>
    /// can emit a single-finding report carrying the exception's
    /// <c>GroupPath</c>, <c>AttributeOrDataset</c>, and
    /// <c>SpecReference</c> under this same rule id when the
    /// defensive try/catch around the rule-pack call fires.
    /// </remarks>
    public static IValidationRule<S102Dataset> ProjectionSchemaSurrogate { get; } =
        ValidationRuleBuilder.RuleFor<S102Dataset>("S102-PROJ-SCHEMA")
            .WithDescription("Defensive surrogate: an S100DatasetSchemaException was caught during validation.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((_, _) => Array.Empty<ValidationFinding>())
            .Build();

    /// <summary>The canonical default rule set for S-102 bathymetry datasets.</summary>
    public static ValidationRuleSet<S102Dataset> Default { get; } = new(
        CoverageValuesLengthMatchesShape,
        NoDataFillValueIsCanonical,
        HorizontalCrsIsKnownEpsg,
        IssueDateIsIso8601,
        CoverageOriginInWgs84Range,
        CoverageExtentDoesNotWrap,
        DepthValuesInPlausibleRange,
        ProjectionSchemaSurrogate);

    /// <summary>
    /// Convenience wrapper around <see cref="ValidationRuleSet{T}.Run(T, ValidationContext?)"/>
    /// using the <see cref="Default"/> rule set.
    /// </summary>
    public static ValidationReport Validate(S102Dataset dataset, ValidationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return Default.Run(dataset, context);
    }

    private static string CoveragePath(BathymetryCoverage coverage, int index)
        => coverage.GroupPath ?? $"/BathymetryCoverage/BathymetryCoverage.{index + 1:D2}";

    private static string Fmt(double value)
        => value.ToString("0.######", CultureInfo.InvariantCulture);
}
