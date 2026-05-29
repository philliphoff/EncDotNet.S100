using System.Globalization;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S111.Validation;

/// <summary>
/// The default <see cref="ValidationRuleSet{TModel}"/> of normative rules
/// for an S-111 <see cref="S111Dataset"/>. Rule identifiers follow the
/// convention <c>S111-R-{clause}</c> for normative rules and
/// <c>S111-PROJ-{kind}</c> for projection-diagnostic surrogates,
/// traceable back to S-111 (Edition 2.0.0) and S-100 Part 10c.
/// </summary>
/// <remarks>
/// <para>
/// This is the V-3 rule pack as defined in
/// <c>docs/design/non-gml-validation.md</c> §6.3. Rules read off the
/// strongly-typed <see cref="S111Dataset"/> produced by
/// <see cref="S111DatasetReader"/>; structural-schema failures the
/// reader cannot tolerate are thrown as
/// <see cref="EncDotNet.S100.Hdf5.S100DatasetSchemaException"/> at
/// read time (and unsupported data-coding-format selections — e.g.
/// dcf 3 ungeorectified grid or dcf 8 time series at fixed stations,
/// which are projected onto <see cref="S111StationSeriesDataset"/>
/// instead of <see cref="S111Dataset"/> — as
/// <see cref="EncDotNet.S100.Hdf5.S100DatasetNotSupportedException"/>)
/// and (per design §5.1 and §5.3) surfaced as <c>S111-PROJ-SCHEMA</c>
/// / <c>S111-PROJ-UNSUPPORTED</c> findings by the dataset processor's
/// <c>Validate()</c> wrapper.
/// </para>
/// <para>
/// V-3 mirrors V-2 (S-104) almost exactly: the time-axis rule pattern
/// from V-2's <c>S104-R-2.1</c> / <c>S104-R-2.2</c> is folded into a
/// single <c>S111-R-2.1</c> (monotonicity-then-cadence) per design §6.3.
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
public static class S111SurfaceCurrentRules
{
    /// <summary>
    /// S-111 NODATA sentinel for the surface-current value channels
    /// (S-100 Part 10c §11 fill convention; this codebase uses
    /// <c>-9999.0f</c> as documented on
    /// <see cref="S111CoverageSource.FillValue"/>). Applies to both
    /// <see cref="SurfaceCurrentValue.Speed"/> and
    /// <see cref="SurfaceCurrentValue.Direction"/>.
    /// </summary>
    internal const float NoDataValue = -9999.0f;

    /// <summary>
    /// Conversion factor from knots to metres per second
    /// (1 international nautical mile per hour = 1852 / 3600 m/s).
    /// S-111 stores <see cref="SurfaceCurrentValue.Speed"/> in knots
    /// (S-111 Edition 2.0.0 §12; <c>surfaceCurrentSpeed</c> compound
    /// member), but the plausibility bound in design §6.3 is expressed
    /// in m/s, so the rule converts on the fly.
    /// </summary>
    internal const float KnotsToMetresPerSecond = 0.5144444f;

    /// <summary>
    /// Fallback <c>RelatedFeatureId</c> stem when a coverage lacks a
    /// populated <see cref="SurfaceCurrentCoverage.GroupPath"/> (e.g.
    /// synthetic test fixtures). Matches the conventional S-111
    /// container group name.
    /// </summary>
    private const string FallbackCoveragePath = "/SurfaceCurrent";

    /// <summary>
    /// <c>S111-R-1.1</c> — Every <see cref="SurfaceCurrentCoverage"/>'s
    /// <see cref="SurfaceCurrentCoverage.Values"/> array length equals
    /// <see cref="SurfaceCurrentCoverage.NumPointsLatitudinal"/> ×
    /// <see cref="SurfaceCurrentCoverage.NumPointsLongitudinal"/>.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10c §10.2.1.2 (gridded coverage
    /// shape contract) and S-111 Edition 2.0.0 §12 (<c>SurfaceCurrent</c>
    /// feature). Implements the <c>s111-surface-currents</c> skill
    /// review-checklist item "HDF5 layout under
    /// <c>/SurfaceCurrent/SurfaceCurrent.NN/Group_NNN/values</c>
    /// matches spec".
    /// </remarks>
    public static IValidationRule<S111Dataset> CoverageValuesLengthMatchesShape { get; } =
        ValidationRuleBuilder.RuleFor<S111Dataset>("S111-R-1.1")
            .WithDescription("Each SurfaceCurrentCoverage's Values length must equal NumPointsLatitudinal × NumPointsLongitudinal.")
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
                        RuleId = "S111-R-1.1",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"SurfaceCurrentCoverage at '{CoveragePath(c)}' (timePoint {FmtTime(c.TimePoint)}) has Values length {c.Values.LongLength} but " +
                            $"expected NumPointsLatitudinal ({c.NumPointsLatitudinal}) × NumPointsLongitudinal ({c.NumPointsLongitudinal}) = {expected}.",
                        RelatedFeatureId = CoveragePath(c),
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S111-R-2.1</c> — <see cref="S111Dataset.Coverages"/> ordered
    /// by <see cref="SurfaceCurrentCoverage.TimePoint"/> are strictly
    /// increasing <em>and</em> their successive deltas vary by no more
    /// than ±10% of the median delta. Per design §6.3 the S-111
    /// time-axis check folds the V-2 monotonicity (<c>S104-R-2.1</c>)
    /// and cadence (<c>S104-R-2.2</c>) into a single rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Walks the time-point sequence in two passes:
    /// </para>
    /// <list type="number">
    /// <item><description>Monotonicity — emits a single finding at the
    /// first non-strictly-increasing step and returns immediately;
    /// later violations are usually cascade noise (design §7.2).</description></item>
    /// <item><description>Cadence — only entered when the sequence
    /// is monotonic. Skipped when <c>Coverages.Count &lt; 3</c> (a
    /// single delta has no comparison). The median (not the mean)
    /// delta is the reference so a single missing step does not skew
    /// the tolerance for the rest of the series.</description></item>
    /// </list>
    /// <para>
    /// Spec reference: S-111 Edition 2.0.0 §12 (per-<c>Group_NNN</c>
    /// <c>timePoint</c> attribute) and §10 (<c>timeRecordInterval</c>);
    /// S-100 Part 10c §10.2.6 (time-series group sequence). Implements
    /// the <c>s111-surface-currents</c> skill review-checklist item
    /// "times derived from <c>dateTimeOfFirstRecord</c> +
    /// <c>timeRecordInterval</c>".
    /// </para>
    /// </remarks>
    public static IValidationRule<S111Dataset> TimePointMonotonicityAndCadence { get; } =
        ValidationRuleBuilder.RuleFor<S111Dataset>("S111-R-2.1")
            .WithDescription("Coverage TimePoint sequence must be strictly increasing with cadence within ±10% of the median delta.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                if (dataset.Coverages.Count < 2)
                    return Array.Empty<ValidationFinding>();

                // Pass 1: monotonicity. Early-return on the first violation
                // to suppress cascade noise per design §7.2.
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
                            RuleId = "S111-R-2.1",
                            Severity = ValidationSeverity.Warning,
                            Message =
                                $"Coverage TimePoint sequence is not strictly increasing at index {i}: " +
                                $"previous = {FmtTime(prev.TimePoint)}, current = {FmtTime(curr.TimePoint)}. " +
                                "Only the first violation is reported (later out-of-order steps are usually cascade noise); " +
                                "cadence is not evaluated until monotonicity holds.",
                            RelatedFeatureId = $"{CoveragePath(curr)}#timePoint",
                        },
                    };
                }

                // Pass 2: cadence. Requires ≥ 3 coverages (≥ 2 deltas) for
                // a meaningful comparison.
                if (dataset.Coverages.Count < 3)
                    return Array.Empty<ValidationFinding>();

                var deltas = new TimeSpan[dataset.Coverages.Count - 1];
                for (var i = 1; i < dataset.Coverages.Count; i++)
                {
                    var delta = dataset.Coverages[i].TimePoint - dataset.Coverages[i - 1].TimePoint;
                    if (delta <= TimeSpan.Zero)
                    {
                        // Defensive — monotonicity pass above should have caught this.
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
                const double tolerance = 0.10;

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
                        RuleId = "S111-R-2.1",
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
    /// <c>S111-R-3.1</c> — <see cref="S111Dataset.SurfaceCurrentDepth"/>,
    /// <em>when present</em>, lies in the plausible range [0, 1500]
    /// metres below the surface. Skipped entirely when the attribute is
    /// absent / null (it is optional on the <c>/SurfaceCurrent</c>
    /// container).
    /// </summary>
    /// <remarks>
    /// Spec reference: S-111 Edition 2.0.0 §10 / §12
    /// (<c>surfaceCurrentDepth</c> root attribute on the
    /// <c>SurfaceCurrent</c> container). The 1500 m upper bound is a
    /// plausibility heuristic (currents reported as "surface" are
    /// typically tens of metres deep at most; operational depths beyond
    /// 1500 m almost certainly indicate a unit error). Implements the
    /// "root-attribute completeness" conditional pattern in design §7.5.
    /// </remarks>
    public static IValidationRule<S111Dataset> SurfaceCurrentDepthInRange { get; } =
        ValidationRuleBuilder.RuleFor<S111Dataset>("S111-R-3.1")
            .WithDescription("SurfaceCurrentDepth, when present, must lie in [0, 1500] metres.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                if (dataset.SurfaceCurrentDepth is not float depth)
                    return Array.Empty<ValidationFinding>();
                if (float.IsNaN(depth) || float.IsInfinity(depth))
                {
                    return new[]
                    {
                        new ValidationFinding
                        {
                            RuleId = "S111-R-3.1",
                            Severity = ValidationSeverity.Warning,
                            Message = $"SurfaceCurrentDepth is non-finite ({depth}); expected a value in [0, 1500] metres.",
                            RelatedFeatureId = FallbackCoveragePath,
                        },
                    };
                }
                if (depth >= 0f && depth <= 1500f)
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = "S111-R-3.1",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"SurfaceCurrentDepth {Fmt(depth)} m is outside the plausible range [0, 1500] m; " +
                            "verify the producer is reporting metres below the surface.",
                        RelatedFeatureId = FallbackCoveragePath,
                    },
                };
            })
            .Build();

    /// <summary>
    /// Documented members of the <see cref="S111Dataset.TypeOfCurrentData"/>
    /// enumeration (S-111 Edition 2.0.0 §10 / §12; S-100 GI Registry
    /// <c>typeOfCurrentData</c> enumerated domain). Used by
    /// <see cref="TypeOfCurrentDataInEnumeratedSet"/>.
    /// </summary>
    /// <remarks>
    /// The members are:
    /// <list type="number">
    /// <item><description>1 — History</description></item>
    /// <item><description>2 — Real-time</description></item>
    /// <item><description>3 — Astronomical prediction</description></item>
    /// <item><description>4 — Analysis or hybrid method</description></item>
    /// <item><description>5 — Hydrodynamic model hindcast</description></item>
    /// <item><description>6 — Hydrodynamic model forecast</description></item>
    /// </list>
    /// </remarks>
    internal static readonly int[] ValidTypeOfCurrentData = { 1, 2, 3, 4, 5, 6 };

    /// <summary>
    /// <c>S111-R-3.2</c> — <see cref="S111Dataset.TypeOfCurrentData"/>,
    /// <em>when present</em>, is a member of the S-111 enumerated set
    /// (see <see cref="ValidTypeOfCurrentData"/>). Skipped entirely
    /// when the attribute is absent / null.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-111 Edition 2.0.0 §10 / §12 (root attribute
    /// <c>typeOfCurrentData</c> on the <c>/SurfaceCurrent</c>
    /// container); enumeration values published in the S-100 GI
    /// Registry. Implements the <c>s111-surface-currents</c> skill
    /// review-checklist item "root attributes include
    /// <c>typeOfCurrentData</c>".
    /// </remarks>
    public static IValidationRule<S111Dataset> TypeOfCurrentDataInEnumeratedSet { get; } =
        ValidationRuleBuilder.RuleFor<S111Dataset>("S111-R-3.2")
            .WithDescription("TypeOfCurrentData, when present, must be a member of the S-111 enumerated set {1..6}.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                if (dataset.TypeOfCurrentData is not int t)
                    return Array.Empty<ValidationFinding>();
                if (Array.IndexOf(ValidTypeOfCurrentData, t) >= 0)
                    return Array.Empty<ValidationFinding>();

                return new[]
                {
                    new ValidationFinding
                    {
                        RuleId = "S111-R-3.2",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"TypeOfCurrentData {t} is not in the S-111 enumerated set " +
                            "{1=History, 2=Real-time, 3=Astronomical prediction, 4=Analysis or hybrid, " +
                            "5=Hydrodynamic model hindcast, 6=Hydrodynamic model forecast}.",
                        RelatedFeatureId = FallbackCoveragePath,
                    },
                };
            })
            .Build();

    /// <summary>
    /// <c>S111-R-4.1</c> — Non-NODATA surface current speeds across each
    /// coverage lie within the plausible range [0, 15] metres per second.
    /// NODATA cells (finite values equal to <see cref="NoDataValue"/>,
    /// plus <see cref="float.NaN"/> and ±<see cref="float.PositiveInfinity"/>)
    /// are excluded from the range check.
    /// </summary>
    /// <remarks>
    /// <para>
    /// S-111 stores <see cref="SurfaceCurrentValue.Speed"/> in
    /// <strong>knots</strong> (S-111 Edition 2.0.0 §12; compound member
    /// <c>surfaceCurrentSpeed</c>). The plausibility bound in design
    /// §6.3 is expressed in m/s; this rule converts each value via
    /// <see cref="KnotsToMetresPerSecond"/> before comparing. 15 m/s
    /// (~29.2 knots) comfortably accommodates strong tidal currents
    /// and the fastest western-boundary currents while catching the
    /// common producer mistake of reporting cm/s where m/s was assumed
    /// (a 100x scale error) or vice versa.
    /// </para>
    /// <para>
    /// One finding per offending coverage with the count of out-of-range
    /// cells in the message; emitting one finding per cell on a broken
    /// tile would drown the report in cascade noise (design §6.3 / §7.2).
    /// </para>
    /// <para>
    /// Implements the <c>s111-surface-currents</c> skill known pitfall
    /// "Speed units vary by producer".
    /// </para>
    /// </remarks>
    public static IValidationRule<S111Dataset> SurfaceCurrentSpeedsInPlausibleRange { get; } =
        ValidationRuleBuilder.RuleFor<S111Dataset>("S111-R-4.1")
            .WithDescription("Non-NODATA surface current speeds must lie in [0, 15] m/s after knots→m/s conversion.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                const float minMps = 0f;
                const float maxMps = 15f;
                var findings = new List<ValidationFinding>();
                for (var i = 0; i < dataset.Coverages.Count; i++)
                {
                    var c = dataset.Coverages[i];
                    long offending = 0;
                    float worstMps = 0f;
                    float worstKnots = 0f;
                    for (var idx = 0; idx < c.Values.Length; idx++)
                    {
                        float knots = c.Values[idx].Speed;
                        if (knots == NoDataValue)
                            continue;
                        if (float.IsNaN(knots) || float.IsInfinity(knots))
                            continue;

                        float mps = knots * KnotsToMetresPerSecond;
                        if (mps >= minMps && mps <= maxMps)
                            continue;

                        offending++;
                        if (Math.Abs(mps - 7.5f) > Math.Abs(worstMps - 7.5f))
                        {
                            worstMps = mps;
                            worstKnots = knots;
                        }
                    }
                    if (offending == 0)
                        continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S111-R-4.1",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"SurfaceCurrentCoverage at '{CoveragePath(c)}' (timePoint {FmtTime(c.TimePoint)}) has {offending} non-NODATA " +
                            $"speed(s) outside the plausible range [0, 15] m/s (worst observed: {Fmt(worstKnots)} knots = {Fmt(worstMps)} m/s).",
                        RelatedFeatureId = CoveragePath(c),
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S111-R-4.2</c> — Non-NODATA surface current directions across
    /// each coverage lie within the <em>half-open</em> range
    /// <c>[0, 360)</c> degrees true. Note the upper bound is exclusive:
    /// <c>360.0</c> is invalid (per spec convention it must wrap to
    /// <c>0</c>). NODATA cells (<see cref="NoDataValue"/>,
    /// <see cref="float.NaN"/>, ±infinity) are skipped.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-111 Edition 2.0.0 §12; <c>surfaceCurrentDirection</c>
    /// compound member, degrees true, "going to" oceanographic
    /// convention (also documented on
    /// <see cref="SurfaceCurrentValue.Direction"/>). Implements the
    /// <c>s111-surface-currents</c> skill review-checklist item
    /// "<c>surfaceCurrentDirection</c> (float32, degrees true, 0–360)"
    /// and the known pitfall "Wrap-around at 360° must be handled".
    /// </remarks>
    public static IValidationRule<S111Dataset> SurfaceCurrentDirectionsInRange { get; } =
        ValidationRuleBuilder.RuleFor<S111Dataset>("S111-R-4.2")
            .WithDescription("Non-NODATA surface current directions must lie in [0, 360) degrees (upper bound exclusive).")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                for (var i = 0; i < dataset.Coverages.Count; i++)
                {
                    var c = dataset.Coverages[i];
                    long offending = 0;
                    float worst = 0f;
                    for (var idx = 0; idx < c.Values.Length; idx++)
                    {
                        float dir = c.Values[idx].Direction;
                        if (dir == NoDataValue)
                            continue;
                        if (float.IsNaN(dir) || float.IsInfinity(dir))
                            continue;
                        // Half-open [0, 360) — 360.0 itself is invalid and
                        // should wrap to 0 per the spec convention.
                        if (dir >= 0f && dir < 360f)
                            continue;

                        offending++;
                        if (Math.Abs(dir - 180f) > Math.Abs(worst - 180f))
                            worst = dir;
                    }
                    if (offending == 0)
                        continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S111-R-4.2",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"SurfaceCurrentCoverage at '{CoveragePath(c)}' (timePoint {FmtTime(c.TimePoint)}) has {offending} non-NODATA " +
                            $"direction(s) outside the half-open range [0, 360) degrees (worst observed: {Fmt(worst)}°); " +
                            "360° should wrap to 0°.",
                        RelatedFeatureId = CoveragePath(c),
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S111-PROJ-SCHEMA</c> — defensive projection-diagnostic
    /// surrogate (design §5.1, §5.3).
    /// </summary>
    /// <remarks>
    /// The rule body is intentionally a no-op: by the time the rule
    /// pack runs the dataset is already constructed, so any
    /// <see cref="EncDotNet.S100.Hdf5.S100DatasetSchemaException"/>
    /// has already thrown out of the reader. The rule id is reserved
    /// in this rule set so that <c>S111DatasetProcessor.Validate()</c>
    /// can emit a single-finding report carrying the exception's
    /// <c>GroupPath</c>, <c>AttributeOrDataset</c>, and
    /// <c>SpecReference</c> under this same rule id when the
    /// defensive try/catch around the rule-pack call fires.
    /// </remarks>
    public static IValidationRule<S111Dataset> ProjectionSchemaSurrogate { get; } =
        ValidationRuleBuilder.RuleFor<S111Dataset>("S111-PROJ-SCHEMA")
            .WithDescription("Defensive surrogate: an S100DatasetSchemaException was caught during validation.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((_, _) => Array.Empty<ValidationFinding>())
            .Build();

    /// <summary>
    /// <c>S111-PROJ-UNSUPPORTED</c> — projection-diagnostic surrogate
    /// for an <see cref="EncDotNet.S100.Hdf5.S100DatasetNotSupportedException"/>
    /// (design §5.3). In practice this fires when the underlying
    /// HDF5 dataset uses a data coding format the gridded
    /// <see cref="S111Dataset"/> view does not cover — currently dcf 3
    /// (ungeorectified grid) and dcf 8 (time series at fixed stations),
    /// both of which the reader projects onto
    /// <see cref="S111StationSeriesDataset"/> instead — or when a
    /// future reader change rejects an additional format.
    /// </summary>
    /// <remarks>
    /// The rule body is a no-op (analogous to <see cref="ProjectionSchemaSurrogate"/>);
    /// <c>S111DatasetProcessor.Validate()</c> emits findings under
    /// this rule id directly when it observes the station-series
    /// variant or when its defensive try/catch traps the exception.
    /// </remarks>
    public static IValidationRule<S111Dataset> ProjectionUnsupportedSurrogate { get; } =
        ValidationRuleBuilder.RuleFor<S111Dataset>("S111-PROJ-UNSUPPORTED")
            .WithDescription("Defensive surrogate: an S100DatasetNotSupportedException was caught during validation.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((_, _) => Array.Empty<ValidationFinding>())
            .Build();

    /// <summary>The canonical default rule set for S-111 surface-current datasets.</summary>
    public static ValidationRuleSet<S111Dataset> Default { get; } = new(
        CoverageValuesLengthMatchesShape,
        TimePointMonotonicityAndCadence,
        SurfaceCurrentDepthInRange,
        TypeOfCurrentDataInEnumeratedSet,
        SurfaceCurrentSpeedsInPlausibleRange,
        SurfaceCurrentDirectionsInRange,
        ProjectionSchemaSurrogate,
        ProjectionUnsupportedSurrogate);

    /// <summary>
    /// Convenience wrapper around <see cref="ValidationRuleSet{T}.Run(T, ValidationContext?)"/>
    /// using the <see cref="Default"/> rule set.
    /// </summary>
    public static ValidationReport Validate(S111Dataset dataset, ValidationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return Default.Run(dataset, context);
    }

    private static string CoveragePath(SurfaceCurrentCoverage coverage)
        => coverage.GroupPath ?? FallbackCoveragePath;

    private static string Fmt(double value)
        => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string FmtTime(DateTime value)
        => value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string FmtDelta(TimeSpan value)
        => value.ToString("c", CultureInfo.InvariantCulture);
}
