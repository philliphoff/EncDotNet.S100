using System.Globalization;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S104.Validation;

/// <summary>
/// The default <see cref="ValidationRuleSet{TModel}"/> of normative rules
/// for an S-104 <see cref="S104Dataset"/>. Rule identifiers follow the
/// convention <c>S104-R-{clause}</c> for normative rules and
/// <c>S104-PROJ-{kind}</c> for projection-diagnostic surrogates,
/// traceable back to S-104 (Edition 2.0.0) and S-100 Part 10c.
/// </summary>
/// <remarks>
/// <para>
/// This is the V-2 rule pack as defined in
/// <c>docs/design/non-gml-validation.md</c> §6.2. Rules read off the
/// strongly-typed <see cref="S104Dataset"/> produced by
/// <see cref="S104DatasetReader"/>; structural-schema failures the
/// reader cannot tolerate are thrown as
/// <see cref="EncDotNet.S100.Hdf5.S100DatasetSchemaException"/> at
/// read time (and unsupported data-coding-format selections as
/// <see cref="EncDotNet.S100.Hdf5.S100DatasetNotSupportedException"/>)
/// and (per design §5.1 and §5.3) surfaced as <c>S104-PROJ-SCHEMA</c>
/// / <c>S104-PROJ-UNSUPPORTED</c> findings by the dataset processor's
/// <c>Validate()</c> wrapper.
/// </para>
/// <para>
/// V-2 introduces the time-axis rule pattern (R-2.1 monotonicity,
/// R-2.2 cadence). V-3 (S-111) reuses these patterns against
/// <c>SurfaceCurrentCoverage</c>.
/// </para>
/// <para>
/// Tier-3 cross-dataset rules are out of scope; per-finding payload
/// conventions follow design §4.3:
/// <list type="bullet">
/// <item><description>per-coverage finding — <c>RelatedFeatureId = "{groupPath}"</c></description></item>
/// <item><description>per-time-step finding — <c>RelatedFeatureId = "{groupPath}#timePoint"</c></description></item>
/// </list>
/// </para>
/// </remarks>
public static class S104DatasetRules
{
    /// <summary>
    /// S-104 NODATA sentinel for the water-level <c>Height</c> channel
    /// (S-100 Part 10c §11 fill convention; this codebase uses
    /// <c>-9999.0f</c> as documented on <c>S104CoverageSource.FillValue</c>).
    /// </summary>
    internal const float NoDataValue = -9999.0f;

    /// <summary>
    /// Fallback <c>RelatedFeatureId</c> stem when a coverage lacks a
    /// populated <see cref="WaterLevelCoverage.GroupPath"/> (e.g.
    /// synthetic test fixtures). Matches the conventional S-104
    /// container group name.
    /// </summary>
    private const string FallbackCoveragePath = "/WaterLevel";

    /// <summary>
    /// <c>S104-R-1.1</c> — Every <see cref="WaterLevelCoverage"/>'s
    /// <see cref="WaterLevelCoverage.Values"/> array length equals
    /// <see cref="WaterLevelCoverage.NumPointsLatitudinal"/> ×
    /// <see cref="WaterLevelCoverage.NumPointsLongitudinal"/>.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10c §10.2.1.2 (gridded coverage
    /// shape contract) and S-104 Edition 2.0.0 §10.2 / §12
    /// (<c>WaterLevel</c> feature). Implements the
    /// <c>s104-water-level</c> skill review-checklist item
    /// "values array shape matches numPointsLat × numPointsLon".
    /// </remarks>
    public static IValidationRule<S104Dataset> CoverageValuesLengthMatchesShape { get; } =
        ValidationRuleBuilder.RuleFor<S104Dataset>("S104-R-1.1")
            .WithDescription("Each WaterLevelCoverage's Values length must equal NumPointsLatitudinal × NumPointsLongitudinal.")
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
                        RuleId = "S104-R-1.1",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"WaterLevelCoverage at '{CoveragePath(c)}' (timePoint {FmtTime(c.TimePoint)}) has Values length {c.Values.LongLength} but " +
                            $"expected NumPointsLatitudinal ({c.NumPointsLatitudinal}) × NumPointsLongitudinal ({c.NumPointsLongitudinal}) = {expected}.",
                        RelatedFeatureId = CoveragePath(c),
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S104-R-1.2</c> — <see cref="S104Dataset.DataCodingFormat"/>
    /// is in the supported set <c>{2, 3}</c> (regular grid /
    /// ungeorectified grid). The reader currently rejects other formats
    /// at parse time (raising
    /// <see cref="EncDotNet.S100.Hdf5.S100DatasetNotSupportedException"/>);
    /// this rule documents intent so future reader changes do not
    /// silently widen the supported set.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10c §10.2.1 (data coding format
    /// enumeration); S-104 Edition 2.0.0 §10.2 (<c>dataCodingFormat</c>
    /// root attribute on the <c>WaterLevel</c> group). Implements the
    /// <c>s104-water-level</c> skill review-checklist item
    /// "data coding format is the supported gridded set".
    /// </remarks>
    public static IValidationRule<S104Dataset> DataCodingFormatIsSupported { get; } =
        ValidationRuleBuilder.RuleFor<S104Dataset>("S104-R-1.2")
            .WithDescription("DataCodingFormat must be in the supported gridded set {2, 3}.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                int dcf = dataset.DataCodingFormat;
                if (dcf == 2 || dcf == 3)
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = "S104-R-1.2",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"DataCodingFormat {dcf} is not in the supported S-104 gridded set " +
                            "{2 (regular grid), 3 (ungeorectified grid)}.",
                    },
                };
            })
            .Build();

    /// <summary>
    /// <c>S104-R-2.1</c> — <see cref="S104Dataset.Coverages"/> ordered
    /// by <see cref="WaterLevelCoverage.TimePoint"/> are strictly
    /// increasing. Emits a single finding at the first violation;
    /// later violations are usually cascade noise and are suppressed
    /// per <c>docs/design/non-gml-validation.md</c> §7.2.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-104 Edition 2.0.0 §10.2.5 (<c>timePoint</c>
    /// attribute on each <c>Group_NNN</c>); S-100 Part 10c §10.2.6
    /// (time-series group sequence). Implements the
    /// <c>s104-water-level</c> skill review-checklist item
    /// "time-step groups are monotonic in time".
    /// </remarks>
    public static IValidationRule<S104Dataset> TimePointsStrictlyIncreasing { get; } =
        ValidationRuleBuilder.RuleFor<S104Dataset>("S104-R-2.1")
            .WithDescription("Coverage TimePoint sequence must be strictly increasing.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                if (dataset.Coverages.Count < 2)
                    return Array.Empty<ValidationFinding>();

                for (var i = 1; i < dataset.Coverages.Count; i++)
                {
                    var prev = dataset.Coverages[i - 1];
                    var curr = dataset.Coverages[i];
                    if (curr.TimePoint > prev.TimePoint)
                        continue;

                    return new[]
                    {
                        new ValidationFinding
                        {
                            RuleId = "S104-R-2.1",
                            Severity = ValidationSeverity.Warning,
                            Message =
                                $"Coverage TimePoint sequence is not strictly increasing at index {i}: " +
                                $"previous = {FmtTime(prev.TimePoint)}, current = {FmtTime(curr.TimePoint)}. " +
                                "Only the first violation is reported (later out-of-order steps are usually cascade noise).",
                            RelatedFeatureId = $"{CoveragePath(curr)}#timePoint",
                        },
                    };
                }

                return Array.Empty<ValidationFinding>();
            })
            .Build();

    /// <summary>
    /// <c>S104-R-2.2</c> — Successive <see cref="WaterLevelCoverage.TimePoint"/>
    /// deltas vary by no more than ±10% of the median delta. Skipped
    /// when <c>Coverages.Count &lt; 3</c> (a single delta has no
    /// comparison). One finding per offending gap.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-104 Edition 2.0.0 §10.2.5 / §12.3
    /// (<c>timeRecordInterval</c>) — operational consumers assume
    /// near-uniform cadence. Implements the <c>s104-water-level</c>
    /// skill review-checklist item "time-step cadence is uniform
    /// within tolerance". The rule deliberately uses the median
    /// (not the mean) delta as the reference so a single missing
    /// step does not skew the tolerance for the rest of the series.
    /// </remarks>
    public static IValidationRule<S104Dataset> TimeStepCadenceWithinTolerance { get; } =
        ValidationRuleBuilder.RuleFor<S104Dataset>("S104-R-2.2")
            .WithDescription("Successive TimePoint deltas must vary by no more than ±10% of the median delta.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                if (dataset.Coverages.Count < 3)
                    return Array.Empty<ValidationFinding>();

                var deltas = new TimeSpan[dataset.Coverages.Count - 1];
                for (var i = 1; i < dataset.Coverages.Count; i++)
                {
                    var delta = dataset.Coverages[i].TimePoint - dataset.Coverages[i - 1].TimePoint;
                    if (delta <= TimeSpan.Zero)
                    {
                        // Monotonicity is R-2.1's job; skip cadence on non-positive deltas
                        // to avoid double-reporting the same anomaly.
                        return Array.Empty<ValidationFinding>();
                    }
                    deltas[i - 1] = delta;
                }

                var sorted = (TimeSpan[])deltas.Clone();
                Array.Sort(sorted);
                var median = sorted[sorted.Length / 2];
                if (median <= TimeSpan.Zero)
                    return Array.Empty<ValidationFinding>();

                double medianTicks = median.Ticks;
                double tolerance = 0.10;

                var findings = new List<ValidationFinding>();
                for (var i = 0; i < deltas.Length; i++)
                {
                    double ratio = deltas[i].Ticks / medianTicks;
                    if (ratio >= 1.0 - tolerance && ratio <= 1.0 + tolerance)
                        continue;

                    var offending = dataset.Coverages[i + 1];
                    var prev = dataset.Coverages[i];
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S104-R-2.2",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"TimePoint cadence at index {i + 1} is {FmtDelta(deltas[i])} " +
                            $"({Fmt(ratio * 100.0)}% of median {FmtDelta(median)}); " +
                            $"outside the ±10% tolerance (previous = {FmtTime(prev.TimePoint)}, current = {FmtTime(offending.TimePoint)}).",
                        RelatedFeatureId = $"{CoveragePath(offending)}#timePoint",
                    });
                }

                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S104-R-3.1</c> — <see cref="S104Dataset.MethodWaterLevelProduct"/>
    /// is set (non-null, non-empty) when the dataset contains more than
    /// one coverage (i.e. is itself a time series, where the method
    /// provenance is operationally important).
    /// </summary>
    /// <remarks>
    /// Spec reference: S-104 Edition 2.0.0 §12 / §10.2.3
    /// (<c>methodWaterLevelProduct</c> attribute on the
    /// <c>WaterLevel</c> container; recommended for forecast and
    /// hindcast products). Implements the <c>s104-water-level</c>
    /// skill review-checklist item "provenance attributes populated
    /// on multi-step datasets".
    /// </remarks>
    public static IValidationRule<S104Dataset> MethodSetForTimeSeries { get; } =
        ValidationRuleBuilder.RuleFor<S104Dataset>("S104-R-3.1")
            .WithDescription("MethodWaterLevelProduct should be set on multi-step datasets.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                if (dataset.Coverages.Count <= 1)
                    return Array.Empty<ValidationFinding>();
                if (!string.IsNullOrWhiteSpace(dataset.MethodWaterLevelProduct))
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = "S104-R-3.1",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"MethodWaterLevelProduct is not set on a multi-step dataset " +
                            $"({dataset.Coverages.Count} coverages); operational consumers expect this " +
                            "provenance attribute on forecast or hindcast time series.",
                    },
                };
            })
            .Build();

    /// <summary>
    /// <c>S104-R-4.1</c> — Non-NODATA water-level <c>Height</c> values
    /// across each coverage lie within the plausible range [-15, 15]
    /// metres. NODATA cells (finite values equal to
    /// <see cref="NoDataValue"/>, plus <see cref="float.NaN"/> and
    /// ±<see cref="float.PositiveInfinity"/>) are excluded from the
    /// range check.
    /// </summary>
    /// <remarks>
    /// Plausibility heuristic: tides, surges, and routine
    /// meteorological forcing rarely exceed ±15 m even in extreme
    /// estuaries; catches common producer mistakes such as a centimetre
    /// scaling factor left applied or a missing datum subtraction.
    /// One finding per offending coverage with the count of out-of-range
    /// cells in the message; emitting one finding per cell on a
    /// broken tile would drown the report in cascade noise (design §6.2).
    /// Spec reference: S-104 Edition 2.0.0 §12 (<c>waterLevelHeight</c>
    /// units). Implements the <c>s104-water-level</c> skill
    /// review-checklist item "water-level values within physically
    /// plausible range".
    /// </remarks>
    public static IValidationRule<S104Dataset> WaterLevelValuesInPlausibleRange { get; } =
        ValidationRuleBuilder.RuleFor<S104Dataset>("S104-R-4.1")
            .WithDescription("Non-NODATA water-level values must lie in [-15, 15] metres.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                const float min = -15f;
                const float max = 15f;
                var findings = new List<ValidationFinding>();
                for (var i = 0; i < dataset.Coverages.Count; i++)
                {
                    var c = dataset.Coverages[i];
                    long offending = 0;
                    float worst = 0f;
                    for (var idx = 0; idx < c.Values.Length; idx++)
                    {
                        float h = c.Values[idx].Height;
                        if (h == NoDataValue)
                            continue;
                        if (float.IsNaN(h) || float.IsInfinity(h))
                            continue;
                        if (h >= min && h <= max)
                            continue;

                        offending++;
                        if (Math.Abs(h) > Math.Abs(worst))
                            worst = h;
                    }
                    if (offending == 0)
                        continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S104-R-4.1",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"WaterLevelCoverage at '{CoveragePath(c)}' (timePoint {FmtTime(c.TimePoint)}) has {offending} non-NODATA " +
                            $"value(s) outside the plausible range [-15, 15] m (worst observed: {Fmt(worst)} m).",
                        RelatedFeatureId = CoveragePath(c),
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S104-R-4.2</c> — Each coverage's
    /// <see cref="WaterLevelCoverage.OriginLatitude"/> is in [-90, 90]
    /// and <see cref="WaterLevelCoverage.OriginLongitude"/> is in
    /// [-180, 180], and the extent
    /// (<c>origin + (numPoints - 1) × spacing</c>) stays in range
    /// without wrapping the antimeridian or crossing the pole.
    /// </summary>
    /// <remarks>
    /// Folds the V-1 S102-R-4.1 (origin range) and S102-R-4.2 (extent
    /// range) into a single rule because the S-104 time-series shape
    /// already inflates the per-coverage rule count. Per-coverage
    /// finding; populates <see cref="ValidationFinding.BoundingBox"/>
    /// to the offending tile extent (clamped to ordered edges; values
    /// may themselves be out of range). Spec reference: S-100 Part 10c
    /// §10.2.1.2 (grid georeferencing). Implements the
    /// <c>s104-water-level</c> skill review-checklist items
    /// "georeferencing attributes within WGS-84 range" and "tile extent
    /// stays within WGS-84 ranges".
    /// </remarks>
    public static IValidationRule<S104Dataset> CoverageGeoreferencingInRange { get; } =
        ValidationRuleBuilder.RuleFor<S104Dataset>("S104-R-4.2")
            .WithDescription("Coverage origin and extent must stay in WGS-84 range and not wrap the antimeridian or cross the pole.")
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

                    double latEnd = c.OriginLatitude;
                    double lonEnd = c.OriginLongitude;
                    if (c.NumPointsLatitudinal > 0 && c.NumPointsLongitudinal > 0)
                    {
                        latEnd = c.OriginLatitude + (c.NumPointsLatitudinal - 1) * c.SpacingLatitudinal;
                        lonEnd = c.OriginLongitude + (c.NumPointsLongitudinal - 1) * c.SpacingLongitudinal;
                        if (latEnd > 90 || latEnd < -90)
                            problems.Add($"latitude end {Fmt(latEnd)} outside [-90, 90]");
                        if (lonEnd > 180 || lonEnd < -180)
                            problems.Add($"longitude end {Fmt(lonEnd)} outside [-180, 180] (antimeridian-spanning tiles are out of scope for V-2)");
                    }

                    if (problems.Count == 0)
                        continue;

                    double south = Math.Min(c.OriginLatitude, latEnd);
                    double north = Math.Max(c.OriginLatitude, latEnd);
                    double west = Math.Min(c.OriginLongitude, lonEnd);
                    double east = Math.Max(c.OriginLongitude, lonEnd);

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S104-R-4.2",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"WaterLevelCoverage at '{CoveragePath(c)}' (timePoint {FmtTime(c.TimePoint)}) has out-of-range " +
                            "georeferencing: " + string.Join("; ", problems) + ".",
                        RelatedFeatureId = CoveragePath(c),
                        BoundingBox = new BoundingBox(south, west, north, east),
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S104-PROJ-SCHEMA</c> — defensive projection-diagnostic
    /// surrogate (design §5.1, §5.3).
    /// </summary>
    /// <remarks>
    /// The rule body is intentionally a no-op: by the time the rule
    /// pack runs the dataset is already constructed, so any
    /// <see cref="EncDotNet.S100.Hdf5.S100DatasetSchemaException"/>
    /// has already thrown out of the reader. The rule id is reserved
    /// in this rule set so that <c>S104DatasetProcessor.Validate()</c>
    /// can emit a single-finding report carrying the exception's
    /// <c>GroupPath</c>, <c>AttributeOrDataset</c>, and
    /// <c>SpecReference</c> under this same rule id when the
    /// defensive try/catch around the rule-pack call fires.
    /// </remarks>
    public static IValidationRule<S104Dataset> ProjectionSchemaSurrogate { get; } =
        ValidationRuleBuilder.RuleFor<S104Dataset>("S104-PROJ-SCHEMA")
            .WithDescription("Defensive surrogate: an S100DatasetSchemaException was caught during validation.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((_, _) => Array.Empty<ValidationFinding>())
            .Build();

    /// <summary>
    /// <c>S104-PROJ-UNSUPPORTED</c> — projection-diagnostic surrogate
    /// for an <see cref="EncDotNet.S100.Hdf5.S100DatasetNotSupportedException"/>
    /// (design §5.3). In practice this fires when the underlying
    /// HDF5 dataset uses a data coding format the reader has not
    /// implemented (e.g. dcf 8 station-series, which lacks an
    /// <see cref="S104Dataset"/> view), or when a future reader
    /// change widens the rejected set.
    /// </summary>
    /// <remarks>
    /// The rule body is a no-op (analogous to <see cref="ProjectionSchemaSurrogate"/>);
    /// <c>S104DatasetProcessor.Validate()</c> emits findings under
    /// this rule id directly when it detects the dcf8 variant or
    /// when its defensive try/catch traps the exception.
    /// </remarks>
    public static IValidationRule<S104Dataset> ProjectionUnsupportedSurrogate { get; } =
        ValidationRuleBuilder.RuleFor<S104Dataset>("S104-PROJ-UNSUPPORTED")
            .WithDescription("Defensive surrogate: an S100DatasetNotSupportedException was caught during validation.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((_, _) => Array.Empty<ValidationFinding>())
            .Build();

    /// <summary>The canonical default rule set for S-104 water-level datasets.</summary>
    public static ValidationRuleSet<S104Dataset> Default { get; } = new(
        CoverageValuesLengthMatchesShape,
        DataCodingFormatIsSupported,
        TimePointsStrictlyIncreasing,
        TimeStepCadenceWithinTolerance,
        MethodSetForTimeSeries,
        WaterLevelValuesInPlausibleRange,
        CoverageGeoreferencingInRange,
        ProjectionSchemaSurrogate,
        ProjectionUnsupportedSurrogate);

    /// <summary>
    /// Convenience wrapper around <see cref="ValidationRuleSet{T}.Run(T, ValidationContext?)"/>
    /// using the <see cref="Default"/> rule set.
    /// </summary>
    public static ValidationReport Validate(S104Dataset dataset, ValidationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return Default.Run(dataset, context);
    }

    private static string CoveragePath(WaterLevelCoverage coverage)
        => coverage.GroupPath ?? FallbackCoveragePath;

    private static string Fmt(double value)
        => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string FmtTime(DateTime value)
        => value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string FmtDelta(TimeSpan value)
        => value.ToString("c", CultureInfo.InvariantCulture);
}
