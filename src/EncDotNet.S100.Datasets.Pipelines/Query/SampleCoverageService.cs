using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Time;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Query;

/// <summary>Request payload for <see cref="SampleCoverageService"/>.</summary>
/// <param name="Spec">Spec of the coverage to sample. S-102, S-104, and S-111 are supported.</param>
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

/// <summary>
/// Values of <see cref="SampleCoverageResult.TimeStatus"/>: where the requested
/// instant fell relative to the sampled dataset's time range.
/// </summary>
public static class SampleTimeStatus
{
    /// <summary>The requested time is within the dataset's range (allowing one time-step interval at either end).</summary>
    public const string InRange = "in_range";

    /// <summary>The requested time precedes the dataset's first step; the first step was sampled.</summary>
    public const string BeforeStart = "before_start";

    /// <summary>The requested time follows the dataset's last step; the last step was sampled.</summary>
    public const string AfterEnd = "after_end";
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

/// <summary>Discriminated payload returned by <see cref="SampleCoverageService"/>.</summary>
public abstract record SampledValue;

/// <summary>S-102 depth sample (metres below the vertical datum, positive down).</summary>
/// <param name="DepthMeters">Depth in metres below the vertical datum, positive down (per S-102).</param>
/// <param name="UncertaintyMeters">Vertical uncertainty in metres at the resolved cell; null when the source dataset has no uncertainty layer.</param>
public sealed record DepthSample(
    [property: Description("Depth in metres below the vertical datum, positive down (per S-102).")] double DepthMeters,
    [property: Description("Vertical uncertainty in metres at the resolved cell; null when the source dataset has no uncertainty layer.")] double? UncertaintyMeters) : SampledValue;

/// <summary>
/// S-104 water level sample read from a dcf8 ("time series at fixed
/// stations") dataset — picks the nearest station to the requested
/// position and the nearest time step within that station's series.
/// </summary>
/// <param name="StationId">Reporting station identifier (S-104 <c>stationIdentification</c>).</param>
/// <param name="StationDistanceMetres">Great-circle distance from requested point to the station, metres.</param>
/// <param name="WaterLevelHeight">Water level height in metres relative to the vertical datum.</param>
/// <param name="Trend">Decoded S-104 trend (see <see cref="WaterLevelSample"/>).</param>
/// <param name="SampleTime">Actual time step (UTC) selected for this sample.</param>
/// <param name="RequestedTime">The time the caller asked for, or <c>null</c> if unspecified.</param>
/// <param name="StationLatitude">Latitude of the matched station.</param>
/// <param name="StationLongitude">Longitude of the matched station.</param>
public sealed record WaterLevelStationSample(
    [property: Description("Reporting station identifier (S-104 stationIdentification).")] string StationId,
    [property: Description("Great-circle distance from requested point to the station, in metres.")] double StationDistanceMetres,
    [property: Description("Water level height in metres relative to the vertical datum, positive up (per S-104).")] double WaterLevelHeight,
    [property: Description("S-104 decoded trend: \"decreasing\", \"increasing\", \"steady\", \"unknown\" (S-104 §10.2.2). Raw integer surfaced as a string for unrecognised values.")] string Trend,
    [property: Description("UTC instant of the time step actually selected for this sample.")] DateTime SampleTime,
    [property: Description("UTC instant the caller asked for, or null if unspecified.")] DateTimeOffset? RequestedTime,
    [property: Description("Latitude of the matched station, decimal degrees, WGS-84.")] double StationLatitude,
    [property: Description("Longitude of the matched station, decimal degrees, WGS-84.")] double StationLongitude) : SampledValue;

/// <summary>
/// S-104 water level sample at the nearest grid cell and time step.
/// </summary>
/// <param name="WaterLevelHeight">Water level height in metres relative to the vertical datum.</param>
/// <param name="Trend">
/// Decoded S-104 trend (per S-104 §10.2.2: <c>0=unknown</c>, <c>1=decreasing</c>,
/// <c>2=increasing</c>, <c>3=steady</c>). When the raw value falls outside the
/// spec-defined set the raw integer is surfaced as the string instead.
/// </param>
/// <param name="SampleTime">The actual time step (UTC) selected for this sample.</param>
/// <param name="RequestedTime">The time the caller asked for, or <c>null</c> if unspecified.</param>
/// <param name="Row">Row index of the resolved cell in the source grid (0-based).</param>
/// <param name="Column">Column index of the resolved cell in the source grid (0-based).</param>
/// <param name="CellCentreLatitude">Latitude of the resolved cell centre.</param>
/// <param name="CellCentreLongitude">Longitude of the resolved cell centre.</param>
public sealed record WaterLevelSample(
    [property: Description("Water level height in metres relative to the vertical datum, positive up (per S-104).")] double WaterLevelHeight,
    [property: Description("S-104 decoded trend: \"decreasing\", \"increasing\", \"steady\", \"unknown\" (S-104 §10.2.2). Raw integer surfaced as a string for unrecognised values.")] string Trend,
    [property: Description("UTC instant of the time step actually selected for this sample.")] DateTime SampleTime,
    [property: Description("UTC instant the caller asked for, or null if unspecified.")] DateTimeOffset? RequestedTime,
    [property: Description("Zero-based row index of the resolved cell in the source grid.")] int Row,
    [property: Description("Zero-based column index of the resolved cell in the source grid.")] int Column,
    [property: Description("Latitude of the resolved cell centre, decimal degrees, WGS-84.")] double CellCentreLatitude,
    [property: Description("Longitude of the resolved cell centre, decimal degrees, WGS-84.")] double CellCentreLongitude) : SampledValue;

/// <summary>
/// S-111 surface current sample at the nearest grid cell and time step.
/// </summary>
/// <param name="SpeedMetresPerSecond">
/// Speed in metres per second, converted from the encoded knots as
/// <c>kn × 0.514444</c>.
/// </param>
/// <param name="SpeedKnots">
/// Speed in knots — the unit S-111 encodes <c>surfaceCurrentSpeed</c> in.
/// </param>
/// <param name="DirectionDegreesTrue">Direction in degrees from true north, clockwise (0..360).</param>
/// <param name="SampleTime">The actual time step (UTC) selected for this sample.</param>
/// <param name="RequestedTime">The time the caller asked for, or <c>null</c> if unspecified.</param>
/// <param name="Row">Row index of the resolved cell in the source grid (0-based).</param>
/// <param name="Column">Column index of the resolved cell in the source grid (0-based).</param>
/// <param name="CellCentreLatitude">Latitude of the resolved cell centre.</param>
/// <param name="CellCentreLongitude">Longitude of the resolved cell centre.</param>
public sealed record SurfaceCurrentSample(
    [property: Description("Current speed in metres per second, converted from the S-111 encoded knots (kn × 0.514444).")] double SpeedMetresPerSecond,
    [property: Description("Current speed in knots, as encoded by S-111 surfaceCurrentSpeed.")] double SpeedKnots,
    [property: Description("Direction the current is flowing toward, in degrees from true north, clockwise, 0..360.")] double DirectionDegreesTrue,
    [property: Description("UTC instant of the time step actually selected for this sample.")] DateTime SampleTime,
    [property: Description("UTC instant the caller asked for, or null if unspecified.")] DateTimeOffset? RequestedTime,
    [property: Description("Zero-based row index of the resolved cell in the source grid.")] int Row,
    [property: Description("Zero-based column index of the resolved cell in the source grid.")] int Column,
    [property: Description("Latitude of the resolved cell centre, decimal degrees, WGS-84.")] double CellCentreLatitude,
    [property: Description("Longitude of the resolved cell centre, decimal degrees, WGS-84.")] double CellCentreLongitude) : SampledValue;

/// <summary>
/// S-111 surface current sample read from a dcf8 ("time series at
/// fixed stations") dataset — picks the nearest station to the
/// requested position and the nearest time step within that station's
/// series (S-111 Edition 2.0.0 §10.2.3 / §10.2.7).
/// </summary>
/// <param name="StationId">Reporting station identifier (S-111 <c>stationIdentification</c>).</param>
/// <param name="StationDistanceMetres">Great-circle distance from requested point to the station, metres.</param>
/// <param name="SpeedMetresPerSecond">Speed in metres per second, converted from the encoded knots as <c>kn × 0.514444</c>.</param>
/// <param name="SpeedKnots">Speed in knots — the unit S-111 encodes <c>surfaceCurrentSpeed</c> in.</param>
/// <param name="DirectionDegreesTrue">Direction in degrees from true north, clockwise (0..360).</param>
/// <param name="SampleTime">Actual time step (UTC) selected for this sample.</param>
/// <param name="RequestedTime">The time the caller asked for, or <c>null</c> if unspecified.</param>
/// <param name="StationLatitude">Latitude of the matched station.</param>
/// <param name="StationLongitude">Longitude of the matched station.</param>
public sealed record SurfaceCurrentStationSample(
    [property: Description("Reporting station identifier (S-111 stationIdentification).")] string StationId,
    [property: Description("Great-circle distance from requested point to the station, in metres.")] double StationDistanceMetres,
    [property: Description("Current speed in metres per second, converted from the S-111 encoded knots (kn × 0.514444).")] double SpeedMetresPerSecond,
    [property: Description("Current speed in knots, as encoded by S-111 surfaceCurrentSpeed.")] double SpeedKnots,
    [property: Description("Direction the current is flowing toward, in degrees from true north, clockwise, 0..360.")] double DirectionDegreesTrue,
    [property: Description("UTC instant of the time step actually selected for this sample.")] DateTime SampleTime,
    [property: Description("UTC instant the caller asked for, or null if unspecified.")] DateTimeOffset? RequestedTime,
    [property: Description("Latitude of the matched station, decimal degrees, WGS-84.")] double StationLatitude,
    [property: Description("Longitude of the matched station, decimal degrees, WGS-84.")] double StationLongitude) : SampledValue;

/// <summary>
/// Samples a coverage product at a single lat/lon, returning the nearest
/// grid cell's value. Supports S-102 (depth + uncertainty), S-104
/// (water level height + trend, nearest time step), and S-111
/// (surface current speed + direction, nearest time step).
/// </summary>
/// <remarks>
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
/// </remarks>
public sealed class SampleCoverageService
{
    /// <summary>Tool name used in <see cref="SpecNotSupportedForTool"/> errors.</summary>
    public const string Name = "sample_coverage";

    // S-111 encodes surfaceCurrentSpeed in knots; the MCP contract also
    // reports m/s. 1 knot = 1852 m / 3600 s ≈ 0.514444 m/s.
    private const double KnotsToMetresPerSecond = 1852.0 / 3600.0;

    // Stations within this distance of the nearest one are treated as the
    // same site (e.g. one station reported by two runs), so time decides
    // between them rather than a sub-metre position difference.
    private const double CoLocatedStationMetres = 1.0;

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
        // it onto the legacy 'Time' field so the existing single-instant
        // paths apply unchanged. Range/Series dispatch to a windowed path.
        var effective = request;
        var timeParameter = "time";
        if (request.Times is TimeQuery.Instant inst)
        {
            effective = request with { Time = inst.Value, Times = null };
            timeParameter = "times";
        }

        if (effective.Times is TimeQuery.Range or TimeQuery.Series)
        {
            return Task.FromResult(effective.Spec.Name switch
            {
                "S-102" => ToolResult<SampleCoverageResult>.Err(new NotSupportedYet(
                    effective.Spec, Name, "S-102 has no time dimension; supply 'Time'/'Times' only for S-104 or S-111")),
                "S-104" => SampleS104Windowed(effective),
                "S-111" => SampleS111Windowed(effective),
                _ => ToolResult<SampleCoverageResult>.Err(
                    new SpecNotSupportedForTool(effective.Spec, Name)),
            });
        }

        return Task.FromResult(effective.Spec.Name switch
        {
            "S-102" => SampleS102(effective),
            "S-104" => SampleS104(effective, timeParameter),
            "S-111" => SampleS111(effective, timeParameter),
            _ => ToolResult<SampleCoverageResult>.Err(
                new SpecNotSupportedForTool(effective.Spec, Name)),
        });
    }

    private ToolResult<SampleCoverageResult> SampleS102(SampleCoverageRequest request)
    {
        var snapshot = _catalog.Datasets;

        // A projected S-102 tile's WGS-84 bounds are an axis-aligned envelope of
        // the (rotated) native grid, so a point inside the envelope may still
        // fall outside the grid after reprojection. When several S-102 datasets
        // overlap we must try each candidate whose bounds contain the point and
        // only report NoDatasetCoversPoint once every candidate fails native-grid
        // containment — otherwise the first envelope hit could mask a later tile
        // that actually covers the point.
        foreach (var dataset in snapshot)
        {
            if (dataset.Data is not S102CoverageData data) continue;
            if (!Contains(dataset.Bounds, request.Latitude, request.Longitude)) continue;

            try
            {
                // Delegate to the shared CoveragePickHelper so this MCP sampling
                // path snaps to exactly the same cell as the viewer's coverage-pick
                // and S102DatasetProcessor.SampleBaseDepth: reproject the WGS-84
                // request into the (often projected/UTM) grid CRS, floor to the
                // containing cell, and reject clicks outside the grid extent. Using
                // one helper keeps every pick path in agreement near cell edges.
                var pick = CoveragePickHelper.Sample(data.Source, _transforms, request.Latitude, request.Longitude);
                if (pick is null)
                {
                    // Inside the envelope but outside the native grid; another
                    // overlapping tile may still cover the point.
                    continue;
                }

                var noData = pick.NoDataValue;
                double? depthValue = pick.Values.TryGetValue("depth", out var depth) && depth != noData
                    ? depth
                    : null;
                double? uncertaintyValue = pick.Values.TryGetValue("uncertainty", out var uncertainty) && uncertainty != noData
                    ? uncertainty
                    : null;

                if (depthValue is null)
                {
                    return ToolResult<SampleCoverageResult>.Err(
                        new NoDataAtPoint(dataset.Id, pick.Row, pick.Col, Time: null));
                }

                return ToolResult<SampleCoverageResult>.Ok(new SampleCoverageResult(
                    dataset.Id,
                    request.Latitude,
                    request.Longitude,
                    new DepthSample(depthValue.Value, uncertaintyValue)));
            }
            catch (ObjectDisposedException)
            {
                return ToolResult<SampleCoverageResult>.Err(
                    new DatasetClosedDuringQuery(dataset.Id));
            }
            catch (Exception ex) when (ex is NotSupportedException or FormatException or OverflowException)
            {
                // The tile declares an unsupported or malformed horizontal CRS,
                // so it cannot be reprojected for sampling. Treat it as an
                // ineligible candidate and try the next overlapping dataset
                // rather than aborting the whole tool/CLI invocation.
                continue;
            }
        }

        return ToolResult<SampleCoverageResult>.Err(
            new NoDatasetCoversPoint(request.Latitude, request.Longitude));
    }

    private ToolResult<SampleCoverageResult> SampleS104(SampleCoverageRequest request, string timeParameter)
    {
        var snapshot = _catalog.Datasets;

        // First: prefer dcf2 gridded coverage whose grid contains the
        // point. This preserves existing behaviour where a colocated
        // gridded forecast wins over a sparse station file.
        var candidates = S104GridCandidates(snapshot, request, out var anyS104Gridded);
        if (candidates.Count > 0)
        {
            var (chosen, status) = ChooseForInstant(candidates, request.Time);
            if (IsOutOfRange(status) && request.OutOfRange == TimeOutOfRangePolicy.Error)
            {
                return ToolResult<SampleCoverageResult>.Err(
                    InstantOutOfRange(timeParameter, request.Time!.Value, chosen, status!, candidates));
            }

            return SampleS104Gridded(request, chosen.Dataset, chosen.Payload, status);
        }

        // No gridded match. Fall back to nearest-station across all
        // loaded dcf8 station-series datasets (no max-distance cap).
        if (snapshot.Any(d => d.Data is S104StationSeriesData))
        {
            return SampleS104StationSeries(request, timeParameter, snapshot);
        }

        return ToolResult<SampleCoverageResult>.Err(anyS104Gridded
            ? new OutOfBounds(request.Spec, request.Latitude, request.Longitude)
            : new NoDatasetCoversPoint(request.Latitude, request.Longitude));
    }

    private static List<Candidate<S104Dataset>> S104GridCandidates(
        IReadOnlyList<LoadedDataset> snapshot,
        SampleCoverageRequest request,
        out bool anyGridded)
    {
        anyGridded = false;
        var candidates = new List<Candidate<S104Dataset>>();
        foreach (var dataset in snapshot)
        {
            if (dataset.Data is not S104CoverageData s104) continue;
            anyGridded = true;
            var model = s104.Source.Dataset;
            if (model.Coverages.Count == 0) continue;
            var probe = model.Coverages[0];
            if (!CoverageContains(probe, request.Latitude, request.Longitude)) continue;
            candidates.Add(new Candidate<S104Dataset>(
                dataset,
                model,
                StepWindow.FromSteps(model.Coverages.Select(c => c.TimePoint)),
                probe.SpacingLatitudinal * probe.SpacingLongitudinal,
                S100IssueTime.Parse(model.IssueDate, model.IssueTime)));
        }
        return candidates;
    }

    private static ToolResult<SampleCoverageResult> SampleS104Gridded(
        SampleCoverageRequest request,
        LoadedDataset bestDataset,
        S104Dataset bestModel,
        string? timeStatus)
    {
        if (bestModel.DataCodingFormat != 2)
        {
            return ToolResult<SampleCoverageResult>.Err(new NotSupportedYet(
                request.Spec,
                Name,
                $"data coding format {bestModel.DataCodingFormat} is not yet supported (only dcf=2 / regular grid)"));
        }

        // Resolve the time-step. Coverages within an instance are ordered
        // by TimePoint; pick the nearest one to the requested time.
        var stepIndex = SelectTimeStep(bestModel.Coverages, request.Time);
        var step = bestModel.Coverages[stepIndex];

        var (row, col) = NearestCellInCoverage(step, request.Latitude, request.Longitude);
        var idx = row * step.NumPointsLongitudinal + col;
        try
        {
            var value = step.Values[idx];
            if (value.Height == S104CoverageSource.FillValue)
            {
                return ToolResult<SampleCoverageResult>.Err(new NoDataAtPoint(
                    bestDataset.Id, row, col, step.TimePoint));
            }

            var cellLat = step.OriginLatitude + row * step.SpacingLatitudinal;
            var cellLon = step.OriginLongitude + col * step.SpacingLongitudinal;

            return ToolResult<SampleCoverageResult>.Ok(new SampleCoverageResult(
                bestDataset.Id,
                request.Latitude,
                request.Longitude,
                new WaterLevelSample(
                    value.Height,
                    DecodeWaterLevelTrend(value.Trend),
                    DateTime.SpecifyKind(step.TimePoint, DateTimeKind.Utc),
                    request.Time,
                    row,
                    col,
                    cellLat,
                    cellLon),
                TimeStatus: timeStatus));
        }
        catch (ObjectDisposedException)
        {
            return ToolResult<SampleCoverageResult>.Err(
                new DatasetClosedDuringQuery(bestDataset.Id));
        }
    }

    /// <summary>
    /// Sample a loaded dcf8 station-series dataset (S-104 Edition 2.0.0
    /// §10.2.3 / §10.2.7). Finds the nearest station across every loaded
    /// dcf8 dataset by great-circle distance with no max-distance cap,
    /// then picks the nearest time step within that station's series.
    /// When several runs report the same station, the one whose series
    /// covers the requested time wins.
    /// </summary>
    private static ToolResult<SampleCoverageResult> SampleS104StationSeries(
        SampleCoverageRequest request,
        string timeParameter,
        IReadOnlyList<LoadedDataset> snapshot)
    {
        var candidates = new List<Candidate<WaterLevelStation>>();
        foreach (var dataset in snapshot)
        {
            if (dataset.Data is not S104StationSeriesData ss) continue;
            var run = S100IssueTime.Parse(ss.Dataset.IssueDate, ss.Dataset.IssueTime);
            foreach (var s in ss.Dataset.Stations)
            {
                candidates.Add(new Candidate<WaterLevelStation>(
                    dataset,
                    s,
                    StepWindow.FromStation(s.StartTime, s.EndTime, s.TimeRecordInterval, s.SampleTimes),
                    GreatCircleMetres(request.Latitude, request.Longitude, s.Latitude, s.Longitude),
                    run));
            }
        }

        var coLocated = CoLocated(candidates);
        if (coLocated.Count == 0)
        {
            return ToolResult<SampleCoverageResult>.Err(
                new NoDatasetCoversPoint(request.Latitude, request.Longitude));
        }

        var (chosen, status) = ChooseForInstant(coLocated, request.Time);
        if (IsOutOfRange(status) && request.OutOfRange == TimeOutOfRangePolicy.Error)
        {
            return ToolResult<SampleCoverageResult>.Err(
                InstantOutOfRange(timeParameter, request.Time!.Value, chosen, status!, coLocated));
        }

        var station = chosen.Payload;
        var idx = request.Time is { } requested ? station.NearestTimeIndex(requested.UtcDateTime) : 0;
        var sampleTime = request.Time is null ? station.StartTime : station.TimeAt(idx);

        return ToolResult<SampleCoverageResult>.Ok(new SampleCoverageResult(
            chosen.Dataset.Id,
            request.Latitude,
            request.Longitude,
            new WaterLevelStationSample(
                station.Identifier,
                chosen.Rank,
                station.Heights[idx],
                DecodeWaterLevelTrend(station.Trends[idx]),
                DateTime.SpecifyKind(sampleTime, DateTimeKind.Utc),
                request.Time,
                station.Latitude,
                station.Longitude),
            TimeStatus: status));
    }

    // Spherical-earth great-circle distance, matching the accuracy bar
    // of other catalog tools.
    private const double EarthRadiusMetres = 6_371_000.0;

    private static double GreatCircleMetres(double lat1, double lon1, double lat2, double lon2)
    {
        var phi1 = lat1 * Math.PI / 180.0;
        var phi2 = lat2 * Math.PI / 180.0;
        var dPhi = (lat2 - lat1) * Math.PI / 180.0;
        var dLambda = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dPhi / 2) * Math.Sin(dPhi / 2)
              + Math.Cos(phi1) * Math.Cos(phi2)
              * Math.Sin(dLambda / 2) * Math.Sin(dLambda / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMetres * c;
    }

    private ToolResult<SampleCoverageResult> SampleS111(SampleCoverageRequest request, string timeParameter)
    {
        var snapshot = _catalog.Datasets;

        var candidates = S111GridCandidates(snapshot, request, out var anyS111Gridded);
        if (candidates.Count > 0)
        {
            var (chosen, status) = ChooseForInstant(candidates, request.Time);
            if (IsOutOfRange(status) && request.OutOfRange == TimeOutOfRangePolicy.Error)
            {
                return ToolResult<SampleCoverageResult>.Err(
                    InstantOutOfRange(timeParameter, request.Time!.Value, chosen, status!, candidates));
            }

            return SampleS111Gridded(request, chosen.Dataset, chosen.Payload, status);
        }

        if (snapshot.Any(d => d.Data is S111StationSeriesData))
        {
            return SampleS111StationSeries(request, timeParameter, snapshot);
        }

        return ToolResult<SampleCoverageResult>.Err(anyS111Gridded
            ? new OutOfBounds(request.Spec, request.Latitude, request.Longitude)
            : new NoDatasetCoversPoint(request.Latitude, request.Longitude));
    }

    private static List<Candidate<S111Dataset>> S111GridCandidates(
        IReadOnlyList<LoadedDataset> snapshot,
        SampleCoverageRequest request,
        out bool anyGridded)
    {
        anyGridded = false;
        var candidates = new List<Candidate<S111Dataset>>();
        foreach (var dataset in snapshot)
        {
            if (dataset.Data is not S111CoverageData s111) continue;
            anyGridded = true;
            var model = s111.Source.Dataset;
            if (model.Coverages.Count == 0) continue;
            var probe = model.Coverages[0];
            if (!CoverageContains(probe, request.Latitude, request.Longitude)) continue;
            candidates.Add(new Candidate<S111Dataset>(
                dataset,
                model,
                StepWindow.FromSteps(model.Coverages.Select(c => c.TimePoint)),
                probe.SpacingLatitudinal * probe.SpacingLongitudinal,
                S100IssueTime.Parse(model.IssueDate, model.IssueTime)));
        }
        return candidates;
    }

    private static ToolResult<SampleCoverageResult> SampleS111Gridded(
        SampleCoverageRequest request,
        LoadedDataset bestDataset,
        S111Dataset bestModel,
        string? timeStatus)
    {
        if (bestModel.DataCodingFormat != 2)
        {
            return ToolResult<SampleCoverageResult>.Err(new NotSupportedYet(
                request.Spec,
                Name,
                $"data coding format {bestModel.DataCodingFormat} is not yet supported (only dcf=2 / regular grid)"));
        }

        var stepIndex = SelectTimeStep(bestModel.Coverages, request.Time);
        var step = bestModel.Coverages[stepIndex];

        var (row, col) = NearestCellInCoverage(step, request.Latitude, request.Longitude);
        var idx = row * step.NumPointsLongitudinal + col;
        try
        {
            var value = step.Values[idx];
            // S-111 §10.2.5 fill value for both speed and direction is -9999f.
            if (value.Speed == S111CoverageSource.FillValue)
            {
                return ToolResult<SampleCoverageResult>.Err(new NoDataAtPoint(
                    bestDataset.Id, row, col, step.TimePoint));
            }

            var cellLat = step.OriginLatitude + row * step.SpacingLatitudinal;
            var cellLon = step.OriginLongitude + col * step.SpacingLongitudinal;

            return ToolResult<SampleCoverageResult>.Ok(new SampleCoverageResult(
                bestDataset.Id,
                request.Latitude,
                request.Longitude,
                new SurfaceCurrentSample(
                    value.Speed * KnotsToMetresPerSecond,
                    value.Speed,
                    value.Direction,
                    DateTime.SpecifyKind(step.TimePoint, DateTimeKind.Utc),
                    request.Time,
                    row,
                    col,
                    cellLat,
                    cellLon),
                TimeStatus: timeStatus));
        }
        catch (ObjectDisposedException)
        {
            return ToolResult<SampleCoverageResult>.Err(
                new DatasetClosedDuringQuery(bestDataset.Id));
        }
    }

    /// <summary>
    /// Sample a loaded dcf8 S-111 station-series dataset (S-111 Edition
    /// 2.0.0 §10.2.3 / §10.2.7). Finds the nearest station across every
    /// loaded dcf8 dataset by great-circle distance with no max-distance
    /// cap, then picks the nearest time step within that station's series.
    /// When several runs report the same station, the one whose series
    /// covers the requested time wins.
    /// </summary>
    private static ToolResult<SampleCoverageResult> SampleS111StationSeries(
        SampleCoverageRequest request,
        string timeParameter,
        IReadOnlyList<LoadedDataset> snapshot)
    {
        var candidates = new List<Candidate<SurfaceCurrentStation>>();
        foreach (var dataset in snapshot)
        {
            if (dataset.Data is not S111StationSeriesData ss) continue;
            var run = S100IssueTime.Parse(ss.Dataset.IssueDate, ss.Dataset.IssueTime);
            foreach (var s in ss.Dataset.Stations)
            {
                candidates.Add(new Candidate<SurfaceCurrentStation>(
                    dataset,
                    s,
                    StepWindow.FromStation(s.StartTime, s.EndTime, s.TimeRecordInterval, s.SampleTimes),
                    GreatCircleMetres(request.Latitude, request.Longitude, s.Latitude, s.Longitude),
                    run));
            }
        }

        var coLocated = CoLocated(candidates);
        if (coLocated.Count == 0)
        {
            return ToolResult<SampleCoverageResult>.Err(
                new NoDatasetCoversPoint(request.Latitude, request.Longitude));
        }

        var (chosen, status) = ChooseForInstant(coLocated, request.Time);
        if (IsOutOfRange(status) && request.OutOfRange == TimeOutOfRangePolicy.Error)
        {
            return ToolResult<SampleCoverageResult>.Err(
                InstantOutOfRange(timeParameter, request.Time!.Value, chosen, status!, coLocated));
        }

        var station = chosen.Payload;
        var idx = request.Time is { } requested ? station.NearestTimeIndex(requested.UtcDateTime) : 0;
        var sampleTime = request.Time is null ? station.StartTime : station.TimeAt(idx);

        var speed = station.SpeedsKnots[idx];
        return ToolResult<SampleCoverageResult>.Ok(new SampleCoverageResult(
            chosen.Dataset.Id,
            request.Latitude,
            request.Longitude,
            new SurfaceCurrentStationSample(
                station.Identifier,
                chosen.Rank,
                speed * KnotsToMetresPerSecond,
                speed,
                station.DirectionsDegreesTrue[idx],
                DateTime.SpecifyKind(sampleTime, DateTimeKind.Utc),
                request.Time,
                station.Latitude,
                station.Longitude),
            TimeStatus: status));
    }

    /// <summary>
    /// Windowed S-104 sampling: dispatched when <see cref="SampleCoverageRequest.Times"/>
    /// is <see cref="TimeQuery.Range"/> or <see cref="TimeQuery.Series"/>. Only the
    /// gridded (dcf=2) path is currently honoured — station-series datasets fall back
    /// to the existing single-instant behaviour.
    /// </summary>
    private ToolResult<SampleCoverageResult> SampleS104Windowed(SampleCoverageRequest request)
    {
        var candidates = S104GridCandidates(_catalog.Datasets, request, out var anyS104Gridded);
        if (candidates.Count == 0)
        {
            return ToolResult<SampleCoverageResult>.Err(anyS104Gridded
                ? new NotSupportedYet(request.Spec, Name, "windowed time queries are only supported for gridded S-104 (dcf=2); no in-bounds gridded dataset was found")
                : new NoDatasetCoversPoint(request.Latitude, request.Longitude));
        }

        if (!TryChooseForWindow(candidates, m => m.Coverages, request.Times!, out var chosen, out var fit, out var error))
        {
            return ToolResult<SampleCoverageResult>.Err(error!);
        }

        var bestDataset = chosen!.Dataset;
        var bestModel = chosen.Payload;
        if (bestModel.DataCodingFormat != 2)
        {
            return ToolResult<SampleCoverageResult>.Err(new NotSupportedYet(
                request.Spec, Name,
                $"data coding format {bestModel.DataCodingFormat} is not yet supported (only dcf=2 / regular grid)"));
        }

        var instants = fit!.Entries;
        var bestCoverage = bestModel.Coverages[0];
        try
        {
            var (row, col) = NearestCellInCoverage(bestCoverage, request.Latitude, request.Longitude);
            var cellLat = bestCoverage.OriginLatitude + row * bestCoverage.SpacingLatitudinal;
            var cellLon = bestCoverage.OriginLongitude + col * bestCoverage.SpacingLongitudinal;

            var series = new List<TimedSampledValue>(instants.Count);
            SampledValue? firstValue = null;
            DateTime firstSampleTime = default;
            foreach (var (requestedInstant, stepIndex) in instants)
            {
                var step = bestModel.Coverages[stepIndex];
                var idx = row * step.NumPointsLongitudinal + col;
                var value = step.Values[idx];
                SampledValue? sampled;
                if (value.Height == S104CoverageSource.FillValue)
                {
                    sampled = null;
                }
                else
                {
                    sampled = new WaterLevelSample(
                        value.Height,
                        DecodeWaterLevelTrend(value.Trend),
                        DateTime.SpecifyKind(step.TimePoint, DateTimeKind.Utc),
                        requestedInstant,
                        row,
                        col,
                        cellLat,
                        cellLon);
                }
                var stepTimeUtc = DateTime.SpecifyKind(step.TimePoint, DateTimeKind.Utc);
                series.Add(new TimedSampledValue(stepTimeUtc, requestedInstant, sampled));
                if (firstValue is null && sampled is not null)
                {
                    firstValue = sampled;
                    firstSampleTime = stepTimeUtc;
                }
            }

            // Fall back to a placeholder Value if every step had NoData,
            // since SampleCoverageResult.Value is non-nullable.
            firstValue ??= new WaterLevelSample(
                double.NaN, "unknown", firstSampleTime == default
                    ? DateTime.SpecifyKind(bestModel.Coverages[instants[0].StepIndex].TimePoint, DateTimeKind.Utc)
                    : firstSampleTime,
                instants[0].RequestedTime, 0, 0, cellLat, cellLon);

            return ToolResult<SampleCoverageResult>.Ok(new SampleCoverageResult(
                bestDataset.Id,
                request.Latitude,
                request.Longitude,
                firstValue,
                series,
                Truncated: fit.Truncated,
                CoveredFrom: fit.CoveredFrom,
                CoveredTo: fit.CoveredTo));
        }
        catch (ObjectDisposedException)
        {
            return ToolResult<SampleCoverageResult>.Err(
                new DatasetClosedDuringQuery(bestDataset.Id));
        }
    }

    /// <summary>
    /// Windowed S-111 sampling — mirror of <see cref="SampleS104Windowed"/>.
    /// </summary>
    private ToolResult<SampleCoverageResult> SampleS111Windowed(SampleCoverageRequest request)
    {
        var candidates = S111GridCandidates(_catalog.Datasets, request, out var anyS111Gridded);
        if (candidates.Count == 0)
        {
            return ToolResult<SampleCoverageResult>.Err(anyS111Gridded
                ? new NotSupportedYet(request.Spec, Name, "windowed time queries are only supported for gridded S-111 (dcf=2); no in-bounds gridded dataset was found")
                : new NoDatasetCoversPoint(request.Latitude, request.Longitude));
        }

        if (!TryChooseForWindow(candidates, m => m.Coverages, request.Times!, out var chosen, out var fit, out var error))
        {
            return ToolResult<SampleCoverageResult>.Err(error!);
        }

        var bestDataset = chosen!.Dataset;
        var bestModel = chosen.Payload;
        if (bestModel.DataCodingFormat != 2)
        {
            return ToolResult<SampleCoverageResult>.Err(new NotSupportedYet(
                request.Spec, Name,
                $"data coding format {bestModel.DataCodingFormat} is not yet supported (only dcf=2 / regular grid)"));
        }

        var instants = fit!.Entries;
        var bestCoverage = bestModel.Coverages[0];
        try
        {
            var (row, col) = NearestCellInCoverage(bestCoverage, request.Latitude, request.Longitude);
            var cellLat = bestCoverage.OriginLatitude + row * bestCoverage.SpacingLatitudinal;
            var cellLon = bestCoverage.OriginLongitude + col * bestCoverage.SpacingLongitudinal;

            var series = new List<TimedSampledValue>(instants.Count);
            SampledValue? firstValue = null;
            DateTime firstSampleTime = default;
            foreach (var (requestedInstant, stepIndex) in instants)
            {
                var step = bestModel.Coverages[stepIndex];
                var idx = row * step.NumPointsLongitudinal + col;
                var value = step.Values[idx];
                SampledValue? sampled;
                if (value.Speed == S111CoverageSource.FillValue)
                {
                    sampled = null;
                }
                else
                {
                    sampled = new SurfaceCurrentSample(
                        value.Speed * KnotsToMetresPerSecond,
                        value.Speed,
                        value.Direction,
                        DateTime.SpecifyKind(step.TimePoint, DateTimeKind.Utc),
                        requestedInstant,
                        row,
                        col,
                        cellLat,
                        cellLon);
                }
                var stepTimeUtc = DateTime.SpecifyKind(step.TimePoint, DateTimeKind.Utc);
                series.Add(new TimedSampledValue(stepTimeUtc, requestedInstant, sampled));
                if (firstValue is null && sampled is not null)
                {
                    firstValue = sampled;
                    firstSampleTime = stepTimeUtc;
                }
            }

            firstValue ??= new SurfaceCurrentSample(
                double.NaN, double.NaN, double.NaN,
                firstSampleTime == default
                    ? DateTime.SpecifyKind(bestModel.Coverages[instants[0].StepIndex].TimePoint, DateTimeKind.Utc)
                    : firstSampleTime,
                instants[0].RequestedTime, 0, 0, cellLat, cellLon);

            return ToolResult<SampleCoverageResult>.Ok(new SampleCoverageResult(
                bestDataset.Id,
                request.Latitude,
                request.Longitude,
                firstValue,
                series,
                Truncated: fit.Truncated,
                CoveredFrom: fit.CoveredFrom,
                CoveredTo: fit.CoveredTo));
        }
        catch (ObjectDisposedException)
        {
            return ToolResult<SampleCoverageResult>.Err(
                new DatasetClosedDuringQuery(bestDataset.Id));
        }
    }

    /// <summary>
    /// A dataset (or station within one) that could answer the request,
    /// with the time span its samples cover. <paramref name="Rank"/> orders
    /// candidates that all cover the requested time: grid cell area for
    /// gridded coverages (finest wins), distance in metres for stations.
    /// </summary>
    private sealed record Candidate<T>(LoadedDataset Dataset, T Payload, StepWindow Window, double Rank, DateTime? Run);

    /// <summary>
    /// The first and last sample times of a candidate, with the tolerance
    /// (one time-step interval) within which a request beyond either end
    /// still counts as in range.
    /// </summary>
    private readonly record struct StepWindow(DateTime First, DateTime Last, TimeSpan Tolerance)
    {
        public bool Covers(DateTime t) => t >= First - Tolerance && t <= Last + Tolerance;

        public TimeSpan DistanceTo(DateTime t) =>
            t < First ? First - t : t > Last ? t - Last : TimeSpan.Zero;

        public static StepWindow FromSteps(IEnumerable<DateTime> steps)
        {
            var sorted = steps.Order().ToArray();
            return new StepWindow(sorted[0], sorted[^1], SmallestGap(sorted));
        }

        public static StepWindow FromStation(DateTime start, DateTime end, TimeSpan interval, IReadOnlyList<DateTime> sampleTimes)
        {
            if (sampleTimes.Count > 0)
            {
                var sorted = sampleTimes.Order().ToArray();
                return new StepWindow(sorted[0], sorted[^1], interval > TimeSpan.Zero ? interval : SmallestGap(sorted));
            }
            return new StepWindow(start, end, interval > TimeSpan.Zero ? interval : TimeSpan.Zero);
        }

        private static TimeSpan SmallestGap(DateTime[] sorted)
        {
            var gap = TimeSpan.Zero;
            for (int i = 1; i < sorted.Length; i++)
            {
                var d = sorted[i] - sorted[i - 1];
                if (d > TimeSpan.Zero && (gap == TimeSpan.Zero || d < gap)) gap = d;
            }
            return gap;
        }
    }

    /// <summary>The steps a windowed query resolved to on one candidate, and how much of the window they cover.</summary>
    private sealed record WindowFit(
        IReadOnlyList<(DateTimeOffset RequestedTime, int StepIndex)> Entries,
        bool Truncated,
        DateTimeOffset? CoveredFrom,
        DateTimeOffset? CoveredTo);

    private static Candidate<T> Best<T>(IEnumerable<Candidate<T>> candidates) =>
        candidates
            .OrderBy(c => c.Rank)
            .ThenByDescending(c => c.Run ?? DateTime.MinValue)
            .ThenByDescending(c => c.Window.Last)
            .First();

    private static bool IsOutOfRange(string? status) =>
        status is SampleTimeStatus.BeforeStart or SampleTimeStatus.AfterEnd;

    /// <summary>
    /// Picks the candidate for a single-instant request. With no requested
    /// time the best-ranked candidate wins (and the status is null). Otherwise
    /// candidates whose range covers the time are preferred; when none do,
    /// the candidate whose range ends nearest the requested time is returned
    /// with a <c>before_start</c> / <c>after_end</c> status.
    /// </summary>
    private static (Candidate<T> Chosen, string? Status) ChooseForInstant<T>(
        IReadOnlyList<Candidate<T>> candidates,
        DateTimeOffset? requested)
    {
        if (requested is null) return (Best(candidates), null);
        var t = requested.Value.UtcDateTime;

        var covering = candidates.Where(c => c.Window.Covers(t)).ToList();
        if (covering.Count > 0) return (Best(covering), SampleTimeStatus.InRange);

        var nearest = candidates
            .OrderBy(c => c.Window.DistanceTo(t))
            .ThenBy(c => c.Rank)
            .ThenByDescending(c => c.Run ?? DateTime.MinValue)
            .First();
        return (nearest, t < nearest.Window.First ? SampleTimeStatus.BeforeStart : SampleTimeStatus.AfterEnd);
    }

    /// <summary>
    /// Picks the candidate for a windowed request: one whose data spans the
    /// whole window beats one that covers only part of it. Among those that
    /// span it the best-ranked wins; among partial ones, the one covering the
    /// most of the window. Fails with <see cref="TimeOutOfRange"/> when no
    /// candidate has a single step in the window.
    /// </summary>
    private static bool TryChooseForWindow<T, TCoverage>(
        IReadOnlyList<Candidate<T>> candidates,
        Func<T, IReadOnlyList<TCoverage>> coverages,
        TimeQuery query,
        out Candidate<T>? chosen,
        out WindowFit? fit,
        out TimeOutOfRange? error)
        where TCoverage : class
    {
        var fits = candidates
            .Select(c => (Candidate: c, Fit: ResolveWindow(coverages(c.Payload), c.Window, query)))
            .Where(x => x.Fit.Entries.Count > 0)
            .ToList();

        if (fits.Count == 0)
        {
            var (from, to) = query.GetWindow();
            var nearest = candidates
                .OrderBy(c => WindowDistance(c.Window, from.UtcDateTime, to.UtcDateTime))
                .ThenBy(c => c.Rank)
                .First();
            chosen = null;
            fit = null;
            error = new TimeOutOfRange(
                "times",
                RequestedTime: null,
                from,
                to,
                nearest.Dataset.Id,
                Utc(nearest.Window.First),
                Utc(nearest.Window.Last),
                Utc(to.UtcDateTime < nearest.Window.First ? nearest.Window.First : nearest.Window.Last),
                nearest.Run is { } run ? Utc(run) : null,
                Ranges(candidates));
            return false;
        }

        var whole = fits.Where(x => !x.Fit.Truncated).ToList();
        if (whole.Count > 0)
        {
            var best = Best(whole.Select(x => x.Candidate));
            chosen = best;
            fit = whole.First(x => ReferenceEquals(x.Candidate, best)).Fit;
        }
        else
        {
            // No candidate spans the window: take the one covering most of
            // it (e.g. the older of two runs when the window reaches back
            // before the newer run), then the best-ranked.
            var (candidate, partial) = fits
                .OrderByDescending(x => x.Fit.CoveredTo - x.Fit.CoveredFrom)
                .ThenBy(x => x.Candidate.Rank)
                .ThenByDescending(x => x.Candidate.Run ?? DateTime.MinValue)
                .First();
            chosen = candidate;
            fit = partial;
        }
        error = null;
        return true;
    }

    private static TimeSpan WindowDistance(StepWindow w, DateTime from, DateTime to) =>
        to < w.First ? w.First - to : from > w.Last ? from - w.Last : TimeSpan.Zero;

    private static TimeOutOfRange InstantOutOfRange<T>(
        string parameter,
        DateTimeOffset requested,
        Candidate<T> nearest,
        string status,
        IReadOnlyList<Candidate<T>> candidates) =>
        new(
            parameter,
            requested,
            requested,
            requested,
            nearest.Dataset.Id,
            Utc(nearest.Window.First),
            Utc(nearest.Window.Last),
            Utc(status == SampleTimeStatus.BeforeStart ? nearest.Window.First : nearest.Window.Last),
            nearest.Run is { } run ? Utc(run) : null,
            Ranges(candidates));

    private static IReadOnlyList<DatasetTimeRange> Ranges<T>(IReadOnlyList<Candidate<T>> candidates) =>
        candidates
            .Select(c => new DatasetTimeRange(
                c.Dataset.Id,
                Utc(c.Window.First),
                Utc(c.Window.Last),
                c.Run is { } run ? Utc(run) : null))
            .Distinct()
            .ToList();

    private static DateTimeOffset Utc(DateTime t) => new(DateTime.SpecifyKind(t, DateTimeKind.Utc));

    /// <summary>
    /// The nearest station and any others at the same site (within
    /// <see cref="CoLocatedStationMetres"/>), so that two runs reporting one
    /// station are chosen between by time rather than by position noise.
    /// </summary>
    private static List<Candidate<T>> CoLocated<T>(List<Candidate<T>> stations)
    {
        if (stations.Count == 0) return stations;
        var nearest = stations.Min(c => c.Rank);
        return stations.Where(c => c.Rank <= nearest + CoLocatedStationMetres).ToList();
    }

    /// <summary>
    /// Resolves the time-step indices for a windowed <see cref="TimeQuery"/>:
    /// <list type="bullet">
    /// <item><description><see cref="TimeQuery.Range"/>: every dataset step whose
    /// <c>TimePoint</c> falls within the window, with the step's own time used as
    /// the requested-time echo.</description></item>
    /// <item><description><see cref="TimeQuery.Series"/>: one entry per enumerated
    /// instant within the dataset's range (allowing one step of tolerance at
    /// either end), snapped to the nearest dataset step; instants beyond the
    /// range are dropped rather than clamped.</description></item>
    /// </list>
    /// The fit is <c>Truncated</c> when the window reaches past the dataset's
    /// range (beyond the tolerance) on either side; <c>CoveredFrom</c> /
    /// <c>CoveredTo</c> are the window clipped to the dataset's range.
    /// </summary>
    private static WindowFit ResolveWindow<TCoverage>(
        IReadOnlyList<TCoverage> coverages,
        StepWindow window,
        TimeQuery query)
        where TCoverage : class
    {
        var (from, to) = query.GetWindow();
        var fromUtc = from.UtcDateTime;
        var toUtc = to.UtcDateTime;
        var builder = new List<(DateTimeOffset, int)>();

        switch (query)
        {
            case TimeQuery.Range:
                for (int i = 0; i < coverages.Count; i++)
                {
                    var tp = GetTimePoint(coverages[i]);
                    if (tp >= fromUtc && tp <= toUtc)
                    {
                        builder.Add((Utc(tp), i));
                    }
                }
                break;
            case TimeQuery.Series s:
                foreach (var instant in s.Enumerate())
                {
                    if (!window.Covers(instant.UtcDateTime)) continue;
                    // Consecutive instants may snap to the same step; each keeps
                    // its own requested-time echo.
                    builder.Add((instant, SelectTimeStep(coverages, instant)));
                }
                break;
        }

        var truncated = fromUtc < window.First - window.Tolerance || toUtc > window.Last + window.Tolerance;
        var coveredFrom = fromUtc > window.First ? fromUtc : window.First;
        var coveredTo = toUtc < window.Last ? toUtc : window.Last;
        return builder.Count == 0
            ? new WindowFit(builder, truncated, null, null)
            : new WindowFit(builder, truncated, Utc(coveredFrom), Utc(coveredTo));
    }

    /// <summary>
    /// Selects the index of the time step whose <c>TimePoint</c> is closest
    /// to <paramref name="requested"/>. Returns 0 when <paramref name="requested"/>
    /// is <c>null</c>. Times outside the dataset's range clamp to the first
    /// or last step (per S-100 Part 10c §10.2.1.1: time-step indices are in
    /// <c>[0, numberOfTimes - 1]</c>); callers decide beforehand whether an
    /// out-of-range request may be sampled at all.
    /// </summary>
    internal static int SelectTimeStep<TCoverage>(
        IReadOnlyList<TCoverage> coverages,
        DateTimeOffset? requested)
        where TCoverage : class
    {
        if (requested is null || coverages.Count == 1) return 0;
        var target = requested.Value.UtcDateTime;

        // Clamp before the first / after the last step explicitly, so
        // out-of-range inputs don't accidentally tie to the middle.
        var first = GetTimePoint(coverages[0]);
        var last = GetTimePoint(coverages[coverages.Count - 1]);
        var ascending = last >= first;
        var earliest = ascending ? first : last;
        var latest = ascending ? last : first;
        if (target <= earliest) return ascending ? 0 : coverages.Count - 1;
        if (target >= latest) return ascending ? coverages.Count - 1 : 0;

        int best = 0;
        var bestDiff = TimeSpan.MaxValue;
        for (int i = 0; i < coverages.Count; i++)
        {
            var diff = (GetTimePoint(coverages[i]) - target).Duration();
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = i;
            }
        }
        return best;
    }

    private static DateTime GetTimePoint(object coverage) => coverage switch
    {
        WaterLevelCoverage wl => wl.TimePoint,
        SurfaceCurrentCoverage sc => sc.TimePoint,
        _ => throw new ArgumentException($"Unsupported coverage type {coverage.GetType()}"),
    };

    private static bool Contains(BoundingBox b, double lat, double lon) =>
        lat >= b.SouthLatitude
        && lat <= b.NorthLatitude
        && lon >= b.WestLongitude
        && lon <= b.EastLongitude;

    private static bool CoverageContains(WaterLevelCoverage cov, double lat, double lon) =>
        CoverageContains(
            cov.OriginLatitude, cov.OriginLongitude,
            cov.SpacingLatitudinal, cov.SpacingLongitudinal,
            cov.NumPointsLatitudinal, cov.NumPointsLongitudinal,
            lat, lon);

    private static bool CoverageContains(SurfaceCurrentCoverage cov, double lat, double lon) =>
        CoverageContains(
            cov.OriginLatitude, cov.OriginLongitude,
            cov.SpacingLatitudinal, cov.SpacingLongitudinal,
            cov.NumPointsLatitudinal, cov.NumPointsLongitudinal,
            lat, lon);

    private static bool CoverageContains(
        double originLat, double originLon,
        double spacingLat, double spacingLon,
        int numLat, int numLon,
        double lat, double lon)
    {
        var minLat = originLat;
        var maxLat = originLat + (numLat - 1) * spacingLat;
        var minLon = originLon;
        var maxLon = originLon + (numLon - 1) * spacingLon;
        if (spacingLat < 0) (minLat, maxLat) = (maxLat, minLat);
        if (spacingLon < 0) (minLon, maxLon) = (maxLon, minLon);
        return lat >= minLat && lat <= maxLat && lon >= minLon && lon <= maxLon;
    }

    private static (int Row, int Col) NearestCellInCoverage(WaterLevelCoverage cov, double lat, double lon)
    {
        var row = (int)Math.Round((lat - cov.OriginLatitude) / cov.SpacingLatitudinal);
        var col = (int)Math.Round((lon - cov.OriginLongitude) / cov.SpacingLongitudinal);
        row = Math.Clamp(row, 0, cov.NumPointsLatitudinal - 1);
        col = Math.Clamp(col, 0, cov.NumPointsLongitudinal - 1);
        return (row, col);
    }

    private static (int Row, int Col) NearestCellInCoverage(SurfaceCurrentCoverage cov, double lat, double lon)
    {
        var row = (int)Math.Round((lat - cov.OriginLatitude) / cov.SpacingLatitudinal);
        var col = (int)Math.Round((lon - cov.OriginLongitude) / cov.SpacingLongitudinal);
        row = Math.Clamp(row, 0, cov.NumPointsLatitudinal - 1);
        col = Math.Clamp(col, 0, cov.NumPointsLongitudinal - 1);
        return (row, col);
    }


    /// <summary>
    /// Decodes the S-104 waterLevelTrend enumeration (S-104 Edition 2.0.0
    /// §10.2.2 Table 10-3). Raw values outside the spec-defined set are
    /// returned as their integer string so callers can still surface the
    /// raw payload.
    /// </summary>
    private static string DecodeWaterLevelTrend(byte trend) => trend switch
    {
        0 => "unknown",
        1 => "decreasing",
        2 => "increasing",
        3 => "steady",
        _ => trend.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
