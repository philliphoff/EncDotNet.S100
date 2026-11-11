using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Sampling;
using EncDotNet.S100.Datasets.Pipelines.Time;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Query;

/// <summary>Request payload for <see cref="SampleCoverageService"/>.</summary>
/// <param name="Spec">Spec of the coverage to sample: S-102, S-104, S-111, or any product whose catalog payload implements <see cref="ICoverageSampler"/>.</param>
/// <param name="Latitude">Sample latitude (decimal degrees, WGS-84).</param>
/// <param name="Longitude">Sample longitude (decimal degrees, WGS-84).</param>
/// <param name="Time">
/// Optional time selector for time-varying products (S-104, S-111). Ignored for S-102
/// (which has no time dimension). When null on a time-varying product the first time
/// step of the matched coverage is used. When supplied, the nearest time step of a
/// dataset whose range contains the time is selected; a time outside every covering
/// dataset's range is handled per <paramref name="OutOfRange"/>.
/// </param>
/// <param name="Times">
/// Optional richer temporal query. Takes precedence over <paramref name="Time"/>
/// when supplied. <c>Instant</c> behaves like <paramref name="Time"/>; <c>Range</c>
/// and <c>Series</c> populate <see cref="SampleCoverageResult.Series"/> with a
/// per-step entry. Currently honoured for gridded S-104 and S-111; ignored
/// elsewhere.
/// </param>
/// <param name="OutOfRange">
/// What to do when a single requested instant lies outside the time range of every
/// dataset covering the point: <see cref="TimeOutOfRangePolicy.Error"/> (the default)
/// returns <see cref="TimeOutOfRange"/>; <see cref="TimeOutOfRangePolicy.Nearest"/>
/// samples the nearest step and flags it in <see cref="SampleCoverageResult.TimeStatus"/>.
/// Windowed queries are unaffected.
/// </param>
public sealed record SampleCoverageRequest(
    [property: Description("Spec of the coverage to sample (S-102, S-104, or S-111).")] SpecRef Spec,
    [property: Description("Sample latitude in decimal degrees, WGS-84, range -90..+90.")] double Latitude,
    [property: Description("Sample longitude in decimal degrees, WGS-84, range -180..+180.")] double Longitude,
    [property: Description("UTC ISO-8601 time selector for time-varying products; ignored for S-102. Null selects the first time step; non-null selects the nearest step of a dataset whose range contains the time (see OutOfRange).")] DateTimeOffset? Time = null,
    [property: Description("Optional TimeQuery (instant / range / series). Takes precedence over 'Time'. Range/Series populate the result's 'Series' field with one entry per dataset step in window.")] TimeQuery? Times = null,
    [property: Description("Handling of a single instant outside every covering dataset's time range: Error (default) returns time_out_of_range; Nearest samples the nearest step and sets TimeStatus.")] TimeOutOfRangePolicy OutOfRange = TimeOutOfRangePolicy.Error);

/// <summary>
/// How <see cref="SampleCoverageService"/> treats a single requested instant
/// that falls outside the time range of every dataset covering the point.
/// </summary>
public enum TimeOutOfRangePolicy
{
    /// <summary>Return a <see cref="TimeOutOfRange"/> error (the default).</summary>
    Error,

    /// <summary>
    /// Sample the nearest available step and mark the result with
    /// <see cref="SampleTimeStatus.BeforeStart"/> or <see cref="SampleTimeStatus.AfterEnd"/>.
    /// </summary>
    Nearest,
}

/// <summary>Result of <see cref="SampleCoverageService"/>.</summary>
/// <param name="DatasetId">Dataset that produced the sample.</param>
/// <param name="Latitude">Latitude that was requested, echoed back (decimal degrees, WGS-84).</param>
/// <param name="Longitude">Longitude that was requested, echoed back (decimal degrees, WGS-84).</param>
/// <param name="Value">Typed sample payload; discriminated on the JSON <c>$kind</c> property.</param>
/// <param name="Series">
/// Optional per-time-step series, populated when the request's
/// <see cref="SampleCoverageRequest.Times"/> is a <c>Range</c> or <c>Series</c>
/// and the matched dataset is a supported time-varying coverage. Null for
/// single-instant queries, for S-102, and for station-series datasets (which
/// currently honour only single-instant time selection).
/// </param>
/// <param name="TimeStatus">
/// For a single-instant request on a time-varying product, where the requested
/// time fell relative to the sampled dataset's range — one of the
/// <see cref="SampleTimeStatus"/> values. <c>before_start</c> / <c>after_end</c>
/// only appear when the request opted into <see cref="TimeOutOfRangePolicy.Nearest"/>.
/// Null when no time was requested, for S-102, and for windowed queries.
/// </param>
/// <param name="Truncated">
/// For windowed queries, <c>true</c> when the requested window reaches past the
/// sampled dataset's time range so the series is shorter than requested. Null for
/// single-instant queries.
/// </param>
/// <param name="CoveredFrom">For windowed queries, the start of the requested window clipped to the dataset's range.</param>
/// <param name="CoveredTo">For windowed queries, the end of the requested window clipped to the dataset's range.</param>
public sealed record SampleCoverageResult(
    [property: Description("Identifier of the dataset that produced the sample.")] DatasetId DatasetId,
    [property: Description("Latitude that was requested, echoed back in decimal degrees, WGS-84.")] double Latitude,
    [property: Description("Longitude that was requested, echoed back in decimal degrees, WGS-84.")] double Longitude,
    [property: Description("Typed sample payload; the JSON \"$kind\" discriminator selects the variant.")] SampledValue Value,
    [property: Description("Optional per-step series; populated when the request's Times is Range/Series and the dataset is gridded S-104 or S-111.")] IReadOnlyList<TimedSampledValue>? Series = null,
    [property: Description("Single-instant requests only: \"in_range\", or \"before_start\" / \"after_end\" when outOfRange=nearest sampled the dataset's first / last step instead. Null when no time was requested.")] string? TimeStatus = null,
    [property: Description("Windowed requests only: true when the requested window extends past the dataset's time range, so the series covers only part of it.")] bool? Truncated = null,
    [property: Description("Windowed requests only: start of the requested window clipped to the dataset's time range, UTC ISO-8601.")] DateTimeOffset? CoveredFrom = null,
    [property: Description("Windowed requests only: end of the requested window clipped to the dataset's time range, UTC ISO-8601.")] DateTimeOffset? CoveredTo = null);

/// <summary>
/// A single entry in a <see cref="SampleCoverageResult.Series"/>: the
/// dataset's actual time-step instant alongside the sampled value at the
/// requested cell.
/// </summary>
/// <param name="SampleTime">UTC instant of the time step actually selected for this entry.</param>
/// <param name="RequestedTime">
/// UTC instant the caller asked for. For <c>Range</c> queries this is the
/// dataset's step instant itself (the window selected it); for <c>Series</c>
/// queries this is the enumerated series instant the step was snapped to.
/// </param>
/// <param name="Value">Typed sample payload, or <c>null</c> when the cell has no data at this step.</param>
public sealed record TimedSampledValue(
    [property: Description("UTC instant of the time step actually selected for this entry.")] DateTime SampleTime,
    [property: Description("UTC instant the caller asked for (the dataset step for Range; the enumerated instant for Series).")] DateTimeOffset RequestedTime,
    [property: Description("Typed sample payload, or null when the cell has no data at this step.")] SampledValue? Value);

/// <summary>
/// Samples a coverage product at a single lat/lon, returning the nearest
/// grid cell's (or station's) value. Any product whose catalog payload is an
/// <see cref="ICoverageSampler"/> is supported: S-102 (depth + uncertainty),
/// S-104 (water level height + trend, nearest time step) and S-111 (surface
/// current speed + direction, nearest time step) today.
/// </summary>
/// <remarks>
/// <para>
/// The service picks the catalog's datasets of the requested spec that carry
/// a sampler and hands them to <see cref="CoverageSampling"/>, which chooses
/// the dataset and site and applies the time handling; it then maps the
/// outcome onto <see cref="SampleCoverageResult"/> and the tool errors. It
/// knows no product: a new one is sampled as soon as its datasets reach the
/// catalog with a sampler (a <see cref="Catalog.ProductDatasetData"/> that
/// implements <see cref="ICoverageSampler"/>, for example).
/// </para>
/// <para>
/// "Nearest cell" semantics: the cell whose centre is closest to the
/// requested point. No interpolation is performed. For time-varying
/// products the nearest time step is selected; ties round to the earlier
/// step.
/// </para>
/// <para>
/// Dataset selection is time-aware: of the datasets covering the point,
/// those whose time range contains the requested time are preferred, and
/// the finest grid (or nearest station) among them wins; equally fine
/// candidates prefer the newest run. A request within one time-step
/// interval of either end of a dataset's range counts as in range.
/// </para>
/// <para>
/// A single instant outside every candidate's range returns
/// <see cref="TimeOutOfRange"/> unless the request opts into
/// <see cref="TimeOutOfRangePolicy.Nearest"/>, in which case the nearest
/// step is returned and <see cref="SampleCoverageResult.TimeStatus"/> says
/// which end was used. Windowed queries return the covered part of the
/// window, flagged <see cref="SampleCoverageResult.Truncated"/> when the
/// data does not reach across the whole window, and error only when there
/// is no overlap at all (issue #789).
/// </para>
/// <para>
/// A spec is reported as <see cref="SpecNotSupportedForTool"/> when no loaded
/// dataset of it has a sampler and it is not one of the products this
/// assembly samples (so a request for S-102 with nothing loaded is still
/// <see cref="NoDatasetCoversPoint"/>).
/// </para>
/// </remarks>
public sealed class SampleCoverageService
{
    /// <summary>Tool name used in <see cref="SpecNotSupportedForTool"/> errors.</summary>
    public const string Name = "sample_coverage";

    private readonly IDatasetCatalog _catalog;
    private readonly ICrsTransformFactory _transforms;

    /// <summary>Creates a new <see cref="SampleCoverageService"/>.</summary>
    /// <param name="catalog">The catalog of loaded datasets to sample from.</param>
    /// <param name="transforms">
    /// Factory used to reproject the WGS-84 request point into a coverage's
    /// native CRS before grid indexing — required for correct sampling of
    /// projected S-102 tiles (e.g. UTM zone 31N), whose grid georeferencing
    /// is native metres rather than degrees.
    /// </param>
    public SampleCoverageService(IDatasetCatalog catalog, ICrsTransformFactory transforms)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(transforms);
        _catalog = catalog;
        _transforms = transforms;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<SampleCoverageResult>> InvokeAsync(
        SampleCoverageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Normalise the temporal selector: if Times is an Instant, lower
        // it onto the legacy 'Time' field so the single-instant path applies
        // unchanged. Range/Series dispatch to the windowed path.
        var effective = request;
        var timeParameter = "time";
        if (request.Times is TimeQuery.Instant inst)
        {
            effective = request with { Time = inst.Value, Times = null };
            timeParameter = "times";
        }

        var samplers = Samplers(effective.Spec);
        if (samplers.Count == 0 && !BuiltInCoverageSamplers.Specs.Contains(effective.Spec.Name))
        {
            return Task.FromResult(ToolResult<SampleCoverageResult>.Err(
                new SpecNotSupportedForTool(effective.Spec, Name)));
        }

        var query = new CoverageSampleQuery(effective.Latitude, effective.Longitude, _transforms);
        if (effective.Times is TimeQuery.Range or TimeQuery.Series)
        {
            return Task.FromResult(ToResult(
                effective,
                "times",
                CoverageSampling.SampleWindow(samplers, query, ToWindow(effective.Times))));
        }

        return Task.FromResult(ToResult(
            effective,
            timeParameter,
            CoverageSampling.SampleAt(
                samplers,
                query,
                effective.Time,
                effective.OutOfRange == TimeOutOfRangePolicy.Nearest)));
    }

    /// <summary>The loaded datasets of <paramref name="spec"/> that can be sampled, in catalog order.</summary>
    private List<CoverageSamplerEntry<LoadedDataset>> Samplers(SpecRef spec) =>
        _catalog.Datasets
            .Where(d => string.Equals(d.Spec.Name, spec.Name, StringComparison.Ordinal))
            .Select(d => d.Data is ICoverageSampler sampler
                ? new CoverageSamplerEntry<LoadedDataset>(d, sampler, d.Bounds)
                : null)
            .OfType<CoverageSamplerEntry<LoadedDataset>>()
            .ToList();

    private static CoverageTimeWindow ToWindow(TimeQuery query)
    {
        var (from, to) = query.GetWindow();
        return query is TimeQuery.Series series
            ? new CoverageTimeWindow(from, to, Defer(series))
            : new CoverageTimeWindow(from, to);
    }

    // Enumerates the series only when a candidate needs it, so a series over
    // the cap fails where it always has: once a dataset covers the point.
    private static IEnumerable<DateTimeOffset> Defer(TimeQuery.Series series)
    {
        foreach (var instant in series.Enumerate())
        {
            yield return instant;
        }
    }

    private static ToolResult<SampleCoverageResult> ToResult(
        SampleCoverageRequest request,
        string timeParameter,
        CoverageSampleOutcome<LoadedDataset> outcome) => outcome switch
        {
            CoverageSampleOutcome<LoadedDataset>.Sampled s => ToolResult<SampleCoverageResult>.Ok(new SampleCoverageResult(
                s.Source.Id,
                request.Latitude,
                request.Longitude,
                s.Value,
                s.Series?.Select(e => new TimedSampledValue(e.SampleTime, e.RequestedTime, e.Value)).ToList(),
                s.TimeStatus,
                s.Truncated,
                s.CoveredFrom,
                s.CoveredTo)),
            CoverageSampleOutcome<LoadedDataset>.OutsideGrid => ToolResult<SampleCoverageResult>.Err(
                new OutOfBounds(request.Spec, request.Latitude, request.Longitude)),
            CoverageSampleOutcome<LoadedDataset>.NoData n => ToolResult<SampleCoverageResult>.Err(
                new NoDataAtPoint(n.Source.Id, n.Row, n.Column, n.Time)),
            CoverageSampleOutcome<LoadedDataset>.NotSupported ns => ToolResult<SampleCoverageResult>.Err(
                new NotSupportedYet(request.Spec, Name, ns.Reason)),
            CoverageSampleOutcome<LoadedDataset>.SourceClosed c => ToolResult<SampleCoverageResult>.Err(
                new DatasetClosedDuringQuery(c.Source.Id)),
            CoverageSampleOutcome<LoadedDataset>.TimeOutOfRange t => ToolResult<SampleCoverageResult>.Err(
                new TimeOutOfRange(
                    timeParameter,
                    t.RequestedTime,
                    t.WindowStart,
                    t.WindowEnd,
                    t.Nearest.Id,
                    t.ValidFrom,
                    t.ValidTo,
                    t.NearestStep,
                    t.Run,
                    t.Candidates
                        .Select(c => new DatasetTimeRange(c.Source.Id, c.ValidFrom, c.ValidTo, c.Run))
                        .Distinct()
                        .ToList())),
            _ => ToolResult<SampleCoverageResult>.Err(
                new NoDatasetCoversPoint(request.Latitude, request.Longitude)),
        };
}
