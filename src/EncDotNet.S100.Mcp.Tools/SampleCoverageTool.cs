using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Time;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using System.Collections.Immutable;
using System.ComponentModel;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>Request payload for <see cref="SampleCoverageTool"/>.</summary>
/// <param name="Spec">Spec of the coverage to sample. S-102, S-104, and S-111 are supported.</param>
/// <param name="Latitude">Sample latitude (decimal degrees, WGS-84).</param>
/// <param name="Longitude">Sample longitude (decimal degrees, WGS-84).</param>
/// <param name="Time">
/// Optional time selector for time-varying products (S-104, S-111). Ignored for S-102
/// (which has no time dimension). When null on a time-varying product the first time
/// step of the matched coverage is used. When supplied, the nearest time step is
/// selected; times outside the dataset's range clamp to the first or last step.
/// </param>
/// <param name="Times">
/// Optional richer temporal query. Takes precedence over <paramref name="Time"/>
/// when supplied. <c>Instant</c> behaves like <paramref name="Time"/>; <c>Range</c>
/// and <c>Series</c> populate <see cref="SampleCoverageResult.Series"/> with a
/// per-step entry. Currently honoured for gridded S-104 and S-111; ignored
/// elsewhere.
/// </param>
public sealed record SampleCoverageRequest(
    [property: Description("Spec of the coverage to sample (S-102, S-104, or S-111).")] SpecRef Spec,
    [property: Description("Sample latitude in decimal degrees, WGS-84, range -90..+90.")] double Latitude,
    [property: Description("Sample longitude in decimal degrees, WGS-84, range -180..+180.")] double Longitude,
    [property: Description("UTC ISO-8601 time selector for time-varying products; ignored for S-102. Null selects the first time step; non-null selects the nearest step (clamped to the dataset range).")] DateTimeOffset? Time = null,
    [property: Description("Optional TimeQuery (instant / range / series). Takes precedence over 'Time'. Range/Series populate the result's 'Series' field with one entry per dataset step in window.")] TimeQuery? Times = null);

/// <summary>Result of <see cref="SampleCoverageTool"/>.</summary>
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
public sealed record SampleCoverageResult(
    [property: Description("Identifier of the dataset that produced the sample.")] DatasetId DatasetId,
    [property: Description("Latitude that was requested, echoed back in decimal degrees, WGS-84.")] double Latitude,
    [property: Description("Longitude that was requested, echoed back in decimal degrees, WGS-84.")] double Longitude,
    [property: Description("Typed sample payload; the JSON \"$kind\" discriminator selects the variant.")] SampledValue Value,
    [property: Description("Optional per-step series; populated when the request's Times is Range/Series and the dataset is gridded S-104 or S-111.")] ImmutableArray<TimedSampledValue>? Series = null);

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

/// <summary>Discriminated payload returned by <see cref="SampleCoverageTool"/>.</summary>
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
/// Speed in metres per second — the canonical S-111 unit (S-111 §10.2.5).
/// </param>
/// <param name="SpeedKnots">
/// Speed in knots, computed as <c>m/s × 1.94384</c> for convenience.
/// </param>
/// <param name="DirectionDegreesTrue">Direction in degrees from true north, clockwise (0..360).</param>
/// <param name="SampleTime">The actual time step (UTC) selected for this sample.</param>
/// <param name="RequestedTime">The time the caller asked for, or <c>null</c> if unspecified.</param>
/// <param name="Row">Row index of the resolved cell in the source grid (0-based).</param>
/// <param name="Column">Column index of the resolved cell in the source grid (0-based).</param>
/// <param name="CellCentreLatitude">Latitude of the resolved cell centre.</param>
/// <param name="CellCentreLongitude">Longitude of the resolved cell centre.</param>
public sealed record SurfaceCurrentSample(
    [property: Description("Current speed in metres per second — canonical S-111 unit (§10.2.5).")] double SpeedMetresPerSecond,
    [property: Description("Current speed in knots (m/s × 1.94384), provided as a convenience.")] double SpeedKnots,
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
/// <param name="SpeedMetresPerSecond">Speed in metres per second — canonical S-111 unit (§10.2.5).</param>
/// <param name="SpeedKnots">Speed in knots, computed as <c>m/s × 1.94384</c> for convenience.</param>
/// <param name="DirectionDegreesTrue">Direction in degrees from true north, clockwise (0..360).</param>
/// <param name="SampleTime">Actual time step (UTC) selected for this sample.</param>
/// <param name="RequestedTime">The time the caller asked for, or <c>null</c> if unspecified.</param>
/// <param name="StationLatitude">Latitude of the matched station.</param>
/// <param name="StationLongitude">Longitude of the matched station.</param>
public sealed record SurfaceCurrentStationSample(
    [property: Description("Reporting station identifier (S-111 stationIdentification).")] string StationId,
    [property: Description("Great-circle distance from requested point to the station, in metres.")] double StationDistanceMetres,
    [property: Description("Current speed in metres per second — canonical S-111 unit (§10.2.5).")] double SpeedMetresPerSecond,
    [property: Description("Current speed in knots (m/s × 1.94384), provided as a convenience.")] double SpeedKnots,
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
/// "Nearest cell" semantics: the cell whose centre is closest to the
/// requested point. No interpolation is performed. For time-varying
/// products the nearest time step is selected; ties round to the earlier
/// step, and times outside the dataset clamp to the first/last step.
/// </remarks>
public sealed class SampleCoverageTool
{
    /// <summary>Tool name used in <see cref="SpecNotSupportedForTool"/> errors.</summary>
    public const string Name = "sample_coverage";

    // S-111 conversion factor m/s → knots (1 m/s ≈ 1.94384 kn). The exact
    // SI definition is 1 knot = 1852 m / 3600 s, i.e. 1/0.514444 m/s.
    private const double MetresPerSecondToKnots = 1.9438444924406046;

    private readonly IDatasetCatalog _catalog;

    /// <summary>Creates a new <see cref="SampleCoverageTool"/>.</summary>
    public SampleCoverageTool(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
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
        if (request.Times is TimeQuery.Instant inst)
        {
            effective = request with { Time = inst.Value, Times = null };
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
            "S-104" => SampleS104(effective),
            "S-111" => SampleS111(effective),
            _ => ToolResult<SampleCoverageResult>.Err(
                new SpecNotSupportedForTool(effective.Spec, Name)),
        });
    }

    private ToolResult<SampleCoverageResult> SampleS102(SampleCoverageRequest request)
    {
        var snapshot = _catalog.Datasets;
        LoadedDataset? match = null;
        foreach (var dataset in snapshot)
        {
            if (dataset.Data is not S102CoverageData) continue;
            if (!Contains(dataset.Bounds, request.Latitude, request.Longitude)) continue;
            match = dataset;
            break;
        }

        if (match is null)
        {
            return ToolResult<SampleCoverageResult>.Err(
                new NoDatasetCoversPoint(request.Latitude, request.Longitude));
        }

        var source = ((S102CoverageData)match.Data).Source;

        try
        {
            var metadata = source.Metadata;
            var grid = metadata.GridMetadata;
            var (row, col) = NearestCell(grid, request.Latitude, request.Longitude);

            var region = new GridRegion(row, row + 1, col, col + 1, 1, 1);
            var sampled = source.Sample(region);

            var depth = ReadScalar(sampled, "depth");
            var uncertainty = TryReadScalar(sampled, "uncertainty");

            var noData = metadata.NoDataValue;
            double? depthValue = depth == noData ? null : depth;
            double? uncertaintyValue = uncertainty is { } u && u != noData ? u : null;

            if (depthValue is null)
            {
                return ToolResult<SampleCoverageResult>.Err(
                    new NoDataAtPoint(match.Id, row, col, Time: null));
            }

            return ToolResult<SampleCoverageResult>.Ok(new SampleCoverageResult(
                match.Id,
                request.Latitude,
                request.Longitude,
                new DepthSample(depthValue.Value, uncertaintyValue)));
        }
        catch (ObjectDisposedException)
        {
            return ToolResult<SampleCoverageResult>.Err(
                new DatasetClosedDuringQuery(match.Id));
        }
    }

    private ToolResult<SampleCoverageResult> SampleS104(SampleCoverageRequest request)
    {
        var snapshot = _catalog.Datasets;

        // First: prefer dcf2 gridded coverage whose bounds contain the
        // point. This preserves existing behaviour where a colocated
        // gridded forecast wins over a sparse station file.
        LoadedDataset? bestDataset = null;
        S104CoverageSource? bestSource = null;
        S104Dataset? bestModel = null;
        WaterLevelCoverage? bestCoverage = null;
        double bestArea = double.PositiveInfinity;
        bool anyS104Gridded = false;
        bool anyS104StationSeries = false;
        foreach (var dataset in snapshot)
        {
            switch (dataset.Data)
            {
                case S104CoverageData s104:
                {
                    anyS104Gridded = true;
                    var model = s104.Source.Dataset;
                    if (model.Coverages.Count == 0) break;
                    var probe = model.Coverages[0];
                    if (!CoverageContains(probe, request.Latitude, request.Longitude)) break;
                    var area = probe.SpacingLatitudinal * probe.SpacingLongitudinal;
                    if (area < bestArea)
                    {
                        bestArea = area;
                        bestDataset = dataset;
                        bestSource = s104.Source;
                        bestModel = model;
                        bestCoverage = probe;
                    }
                    break;
                }
                case S104StationSeriesData:
                    anyS104StationSeries = true;
                    break;
            }
        }

        if (bestDataset is not null && bestModel is not null && bestCoverage is not null && bestSource is not null)
        {
            return SampleS104Gridded(request, bestDataset, bestSource, bestModel, bestCoverage);
        }

        // No gridded match. Fall back to nearest-station across all
        // loaded dcf8 station-series datasets (no max-distance cap).
        if (anyS104StationSeries)
        {
            return SampleS104StationSeries(request, snapshot);
        }

        return ToolResult<SampleCoverageResult>.Err(anyS104Gridded
            ? new OutOfBounds(request.Spec, request.Latitude, request.Longitude)
            : new NoDatasetCoversPoint(request.Latitude, request.Longitude));
    }

    private static ToolResult<SampleCoverageResult> SampleS104Gridded(
        SampleCoverageRequest request,
        LoadedDataset bestDataset,
        S104CoverageSource bestSource,
        S104Dataset bestModel,
        WaterLevelCoverage bestCoverage)
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
                    cellLon)));
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
    /// </summary>
    private static ToolResult<SampleCoverageResult> SampleS104StationSeries(
        SampleCoverageRequest request,
        System.Collections.Immutable.ImmutableArray<LoadedDataset> snapshot)
    {
        LoadedDataset? bestDataset = null;
        WaterLevelStation? bestStation = null;
        double bestDistance = double.PositiveInfinity;

        foreach (var dataset in snapshot)
        {
            if (dataset.Data is not S104StationSeriesData ss) continue;
            foreach (var station in ss.Dataset.Stations)
            {
                var d = GreatCircleMetres(request.Latitude, request.Longitude, station.Latitude, station.Longitude);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestStation = station;
                    bestDataset = dataset;
                }
            }
        }

        if (bestDataset is null || bestStation is null)
        {
            return ToolResult<SampleCoverageResult>.Err(
                new NoDatasetCoversPoint(request.Latitude, request.Longitude));
        }

        var requestedAsUtc = request.Time?.UtcDateTime;
        DateTime sampleTime;
        int idx;
        if (requestedAsUtc is null)
        {
            idx = 0;
            sampleTime = bestStation.StartTime;
        }
        else
        {
            idx = bestStation.NearestTimeIndex(requestedAsUtc.Value);
            sampleTime = bestStation.TimeAt(idx);
        }

        return ToolResult<SampleCoverageResult>.Ok(new SampleCoverageResult(
            bestDataset.Id,
            request.Latitude,
            request.Longitude,
            new WaterLevelStationSample(
                bestStation.Identifier,
                bestDistance,
                bestStation.Heights[idx],
                DecodeWaterLevelTrend(bestStation.Trends[idx]),
                DateTime.SpecifyKind(sampleTime, DateTimeKind.Utc),
                request.Time,
                bestStation.Latitude,
                bestStation.Longitude)));
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

    private ToolResult<SampleCoverageResult> SampleS111(SampleCoverageRequest request)
    {
        var snapshot = _catalog.Datasets;

        LoadedDataset? bestDataset = null;
        S111CoverageSource? bestSource = null;
        S111Dataset? bestModel = null;
        SurfaceCurrentCoverage? bestCoverage = null;
        double bestArea = double.PositiveInfinity;
        bool anyS111Gridded = false;
        bool anyS111StationSeries = false;
        foreach (var dataset in snapshot)
        {
            switch (dataset.Data)
            {
                case S111CoverageData s111:
                {
                    anyS111Gridded = true;
                    var model = s111.Source.Dataset;
                    if (model.Coverages.Count == 0) break;
                    var probe = model.Coverages[0];
                    if (!CoverageContains(probe, request.Latitude, request.Longitude)) break;
                    var area = probe.SpacingLatitudinal * probe.SpacingLongitudinal;
                    if (area < bestArea)
                    {
                        bestArea = area;
                        bestDataset = dataset;
                        bestSource = s111.Source;
                        bestModel = model;
                        bestCoverage = probe;
                    }
                    break;
                }
                case S111StationSeriesData:
                    anyS111StationSeries = true;
                    break;
            }
        }

        if (bestDataset is not null && bestModel is not null && bestCoverage is not null && bestSource is not null)
        {
            return SampleS111Gridded(request, bestDataset, bestSource, bestModel, bestCoverage);
        }

        if (anyS111StationSeries)
        {
            return SampleS111StationSeries(request, snapshot);
        }

        return ToolResult<SampleCoverageResult>.Err(anyS111Gridded
            ? new OutOfBounds(request.Spec, request.Latitude, request.Longitude)
            : new NoDatasetCoversPoint(request.Latitude, request.Longitude));
    }

    private static ToolResult<SampleCoverageResult> SampleS111Gridded(
        SampleCoverageRequest request,
        LoadedDataset bestDataset,
        S111CoverageSource bestSource,
        S111Dataset bestModel,
        SurfaceCurrentCoverage bestCoverage)
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
                    value.Speed,
                    value.Speed * MetresPerSecondToKnots,
                    value.Direction,
                    DateTime.SpecifyKind(step.TimePoint, DateTimeKind.Utc),
                    request.Time,
                    row,
                    col,
                    cellLat,
                    cellLon)));
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
    /// </summary>
    private static ToolResult<SampleCoverageResult> SampleS111StationSeries(
        SampleCoverageRequest request,
        System.Collections.Immutable.ImmutableArray<LoadedDataset> snapshot)
    {
        LoadedDataset? bestDataset = null;
        SurfaceCurrentStation? bestStation = null;
        double bestDistance = double.PositiveInfinity;

        foreach (var dataset in snapshot)
        {
            if (dataset.Data is not S111StationSeriesData ss) continue;
            foreach (var station in ss.Dataset.Stations)
            {
                var d = GreatCircleMetres(request.Latitude, request.Longitude, station.Latitude, station.Longitude);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestStation = station;
                    bestDataset = dataset;
                }
            }
        }

        if (bestDataset is null || bestStation is null)
        {
            return ToolResult<SampleCoverageResult>.Err(
                new NoDatasetCoversPoint(request.Latitude, request.Longitude));
        }

        var requestedAsUtc = request.Time?.UtcDateTime;
        DateTime sampleTime;
        int idx;
        if (requestedAsUtc is null)
        {
            idx = 0;
            sampleTime = bestStation.StartTime;
        }
        else
        {
            idx = bestStation.NearestTimeIndex(requestedAsUtc.Value);
            sampleTime = bestStation.TimeAt(idx);
        }

        var speed = bestStation.SpeedsMetresPerSecond[idx];
        return ToolResult<SampleCoverageResult>.Ok(new SampleCoverageResult(
            bestDataset.Id,
            request.Latitude,
            request.Longitude,
            new SurfaceCurrentStationSample(
                bestStation.Identifier,
                bestDistance,
                speed,
                speed * MetresPerSecondToKnots,
                bestStation.DirectionsDegreesTrue[idx],
                DateTime.SpecifyKind(sampleTime, DateTimeKind.Utc),
                request.Time,
                bestStation.Latitude,
                bestStation.Longitude)));
    }

    /// <summary>
    /// Windowed S-104 sampling: dispatched when <see cref="SampleCoverageRequest.Times"/>
    /// is <see cref="TimeQuery.Range"/> or <see cref="TimeQuery.Series"/>. Only the
    /// gridded (dcf=2) path is currently honoured — station-series datasets fall back
    /// to the existing single-instant behaviour.
    /// </summary>
    private ToolResult<SampleCoverageResult> SampleS104Windowed(SampleCoverageRequest request)
    {
        var snapshot = _catalog.Datasets;
        LoadedDataset? bestDataset = null;
        S104Dataset? bestModel = null;
        WaterLevelCoverage? bestCoverage = null;
        double bestArea = double.PositiveInfinity;
        bool anyS104Gridded = false;
        foreach (var dataset in snapshot)
        {
            if (dataset.Data is not S104CoverageData s104) continue;
            anyS104Gridded = true;
            var model = s104.Source.Dataset;
            if (model.Coverages.Count == 0) continue;
            var probe = model.Coverages[0];
            if (!CoverageContains(probe, request.Latitude, request.Longitude)) continue;
            var area = probe.SpacingLatitudinal * probe.SpacingLongitudinal;
            if (area < bestArea)
            {
                bestArea = area;
                bestDataset = dataset;
                bestModel = model;
                bestCoverage = probe;
            }
        }

        if (bestDataset is null || bestModel is null || bestCoverage is null)
        {
            return ToolResult<SampleCoverageResult>.Err(anyS104Gridded
                ? new NotSupportedYet(request.Spec, Name, "windowed time queries are only supported for gridded S-104 (dcf=2); no in-bounds gridded dataset was found")
                : new NoDatasetCoversPoint(request.Latitude, request.Longitude));
        }

        if (bestModel.DataCodingFormat != 2)
        {
            return ToolResult<SampleCoverageResult>.Err(new NotSupportedYet(
                request.Spec, Name,
                $"data coding format {bestModel.DataCodingFormat} is not yet supported (only dcf=2 / regular grid)"));
        }

        var instants = ResolveStepIndices(bestModel.Coverages, request.Times!, out var firstStep, out var lastStep);
        if (instants.Length == 0)
        {
            var (from, to) = request.Times!.GetWindow();
            return ToolResult<SampleCoverageResult>.Err(new TimeOutOfRange(
                "times", from, to,
                firstStep is null ? null : new DateTimeOffset(DateTime.SpecifyKind(firstStep.Value, DateTimeKind.Utc)),
                lastStep is null ? null : new DateTimeOffset(DateTime.SpecifyKind(lastStep.Value, DateTimeKind.Utc))));
        }

        try
        {
            var (row, col) = NearestCellInCoverage(bestCoverage, request.Latitude, request.Longitude);
            var cellLat = bestCoverage.OriginLatitude + row * bestCoverage.SpacingLatitudinal;
            var cellLon = bestCoverage.OriginLongitude + col * bestCoverage.SpacingLongitudinal;

            var series = ImmutableArray.CreateBuilder<TimedSampledValue>(instants.Length);
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
                series.MoveToImmutable()));
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
        var snapshot = _catalog.Datasets;
        LoadedDataset? bestDataset = null;
        S111Dataset? bestModel = null;
        SurfaceCurrentCoverage? bestCoverage = null;
        double bestArea = double.PositiveInfinity;
        bool anyS111Gridded = false;
        foreach (var dataset in snapshot)
        {
            if (dataset.Data is not S111CoverageData s111) continue;
            anyS111Gridded = true;
            var model = s111.Source.Dataset;
            if (model.Coverages.Count == 0) continue;
            var probe = model.Coverages[0];
            if (!CoverageContains(probe, request.Latitude, request.Longitude)) continue;
            var area = probe.SpacingLatitudinal * probe.SpacingLongitudinal;
            if (area < bestArea)
            {
                bestArea = area;
                bestDataset = dataset;
                bestModel = model;
                bestCoverage = probe;
            }
        }

        if (bestDataset is null || bestModel is null || bestCoverage is null)
        {
            return ToolResult<SampleCoverageResult>.Err(anyS111Gridded
                ? new NotSupportedYet(request.Spec, Name, "windowed time queries are only supported for gridded S-111 (dcf=2); no in-bounds gridded dataset was found")
                : new NoDatasetCoversPoint(request.Latitude, request.Longitude));
        }

        if (bestModel.DataCodingFormat != 2)
        {
            return ToolResult<SampleCoverageResult>.Err(new NotSupportedYet(
                request.Spec, Name,
                $"data coding format {bestModel.DataCodingFormat} is not yet supported (only dcf=2 / regular grid)"));
        }

        var instants = ResolveStepIndices(bestModel.Coverages, request.Times!, out var firstStep, out var lastStep);
        if (instants.Length == 0)
        {
            var (from, to) = request.Times!.GetWindow();
            return ToolResult<SampleCoverageResult>.Err(new TimeOutOfRange(
                "times", from, to,
                firstStep is null ? null : new DateTimeOffset(DateTime.SpecifyKind(firstStep.Value, DateTimeKind.Utc)),
                lastStep is null ? null : new DateTimeOffset(DateTime.SpecifyKind(lastStep.Value, DateTimeKind.Utc))));
        }

        try
        {
            var (row, col) = NearestCellInCoverage(bestCoverage, request.Latitude, request.Longitude);
            var cellLat = bestCoverage.OriginLatitude + row * bestCoverage.SpacingLatitudinal;
            var cellLon = bestCoverage.OriginLongitude + col * bestCoverage.SpacingLongitudinal;

            var series = ImmutableArray.CreateBuilder<TimedSampledValue>(instants.Length);
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
                        value.Speed,
                        value.Speed * MetresPerSecondToKnots,
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
                series.MoveToImmutable()));
        }
        catch (ObjectDisposedException)
        {
            return ToolResult<SampleCoverageResult>.Err(
                new DatasetClosedDuringQuery(bestDataset.Id));
        }
    }

    /// <summary>
    /// Resolves the time-step indices for a windowed <see cref="TimeQuery"/>:
    /// <list type="bullet">
    /// <item><description><see cref="TimeQuery.Range"/>: every dataset step whose
    /// <c>TimePoint</c> falls within the window, with the step's own time used as
    /// the requested-time echo.</description></item>
    /// <item><description><see cref="TimeQuery.Series"/>: one entry per enumerated
    /// instant, snapped to the nearest dataset step; consecutive duplicates are
    /// suppressed but the agent's requested instants are preserved.</description></item>
    /// </list>
    /// Returns an empty array when no overlap exists.
    /// </summary>
    private static ImmutableArray<(DateTimeOffset RequestedTime, int StepIndex)> ResolveStepIndices<TCoverage>(
        IReadOnlyList<TCoverage> coverages,
        TimeQuery query,
        out DateTime? firstStep,
        out DateTime? lastStep)
        where TCoverage : class
    {
        firstStep = coverages.Count == 0 ? null : GetTimePoint(coverages[0]);
        lastStep = coverages.Count == 0 ? null : GetTimePoint(coverages[coverages.Count - 1]);
        if (coverages.Count == 0)
        {
            return ImmutableArray<(DateTimeOffset, int)>.Empty;
        }

        switch (query)
        {
            case TimeQuery.Range r:
            {
                var fromUtc = r.From.UtcDateTime;
                var toUtc = r.To.UtcDateTime;
                var builder = ImmutableArray.CreateBuilder<(DateTimeOffset, int)>();
                for (int i = 0; i < coverages.Count; i++)
                {
                    var tp = GetTimePoint(coverages[i]);
                    if (tp >= fromUtc && tp <= toUtc)
                    {
                        builder.Add((new DateTimeOffset(DateTime.SpecifyKind(tp, DateTimeKind.Utc)), i));
                    }
                }
                return builder.ToImmutable();
            }
            case TimeQuery.Series s:
            {
                var enumerated = s.Enumerate();
                var builder = ImmutableArray.CreateBuilder<(DateTimeOffset, int)>(enumerated.Length);
                int? lastIndex = null;
                foreach (var instant in enumerated)
                {
                    var idx = SelectTimeStep(coverages, instant);
                    if (lastIndex == idx)
                    {
                        // Preserve the requested-time echo but skip duplicate dataset rows.
                        builder.Add((instant, idx));
                        continue;
                    }
                    builder.Add((instant, idx));
                    lastIndex = idx;
                }
                return builder.ToImmutable();
            }
            default:
                return ImmutableArray<(DateTimeOffset, int)>.Empty;
        }
    }

    /// <summary>
    /// Selects the index of the time step whose <c>TimePoint</c> is closest
    /// to <paramref name="requested"/>. Returns 0 when <paramref name="requested"/>
    /// is <c>null</c>. Times outside the dataset's range clamp to the first
    /// or last step (per S-100 Part 10c §10.2.1.1: time-step indices are in
    /// <c>[0, numberOfTimes - 1]</c>).
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

    private static (int Row, int Col) NearestCell(GridMetadata grid, double lat, double lon)
    {
        var row = (int)Math.Round((lat - grid.OriginLatitude) / grid.SpacingLatitudinal);
        var col = (int)Math.Round((lon - grid.OriginLongitude) / grid.SpacingLongitudinal);
        row = Math.Clamp(row, 0, grid.NumRows - 1);
        col = Math.Clamp(col, 0, grid.NumColumns - 1);
        return (row, col);
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

    private static float ReadScalar(SampledCoverage sampled, string field)
    {
        var data = sampled.GetField(field);
        return data[0, 0];
    }

    private static float? TryReadScalar(SampledCoverage sampled, string field)
    {
        if (!sampled.Values.TryGetValue(field, out var data))
        {
            return null;
        }
        return data[0];
    }
}
