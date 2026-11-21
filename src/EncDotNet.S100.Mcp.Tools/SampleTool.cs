using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Time;
using EncDotNet.S100.Mcp.Tools.Tides;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>What <see cref="SampleTool"/> samples.</summary>
public enum SampleQuantity
{
    /// <summary>
    /// Charted depth by the route check's rule: S-102 first, else the finest
    /// S-101 (or S-401, or translated S-57) cell; with a time, also the water
    /// level and the available depth.
    /// </summary>
    Depth,

    /// <summary>Water level: S-104, else a tide prediction where the host has them on.</summary>
    WaterLevel,

    /// <summary>Surface current: S-111, else a tidal current prediction where the host has them on.</summary>
    Current,

    /// <summary>Weather model fields from a loaded GRIB2 or NetCDF grid.</summary>
    Weather,
}

/// <summary>Request payload for <see cref="SampleTool"/>.</summary>
/// <param name="Quantity">
/// What to sample. May be <c>null</c> when <paramref name="Spec"/> names the
/// product, and the quantity follows from it (S-102 is depth, S-104 water
/// level, S-111 current, GRIB2 and NetCDF weather; any other product with a
/// coverage sampler is sampled as it is).
/// </param>
/// <param name="Spec">
/// Pins one product: S-102 for depth (bathymetry only, no S-101 fallback),
/// S-104 for water level, S-111 for current, GRIB2 or NetCDF for weather.
/// <c>null</c> uses every product of the quantity.
/// </param>
/// <param name="Latitude">The point's latitude, for a point sample; with <paramref name="Longitude"/>, instead of <paramref name="Polyline"/>.</param>
/// <param name="Longitude">The point's longitude, for a point sample.</param>
/// <param name="Polyline">The polyline, for a profile; instead of a point. Its corridor width is ignored.</param>
/// <param name="SpacingMeters">
/// For a polyline, the longest distance between samples along each leg
/// (rhumb line, as <c>check_route</c> runs a polyline); <c>null</c> samples
/// the vertices only.
/// </param>
/// <param name="Time">The time to sample at; see <see cref="SampleCoverageRequest.Time"/>. For depth, the time of the water level.</param>
/// <param name="Times">A richer time selector; see <see cref="SampleCoverageRequest.Times"/>. Depth takes an instant only.</param>
/// <param name="OutOfRange">Handling of an instant outside every covering dataset's time range; see <see cref="SampleCoverageRequest.OutOfRange"/>.</param>
/// <param name="MinDepthMeters">
/// For depth at a point: the least charted depth the point needs. When it
/// fails (land, uncharted, too shallow, or nearer than
/// <paramref name="ClearanceMeters"/> to something that isn't navigable),
/// the result adds the nearest point that qualifies (#1559).
/// </param>
/// <param name="ClearanceMeters">With <paramref name="MinDepthMeters"/>, the room wanted round the point, in metres.</param>
/// <param name="SearchRadiusMeters">With <paramref name="MinDepthMeters"/>, how far to look for a navigable point, in metres.</param>
public sealed record SampleRequest(
    [property: Description("What to sample: depth, water level, current or weather; null when 'Spec' names the product.")] SampleQuantity? Quantity,
    [property: Description("Optional product to pin: S-102 (depth, bathymetry only), S-104, S-111, GRIB2 or NetCDF.")] SpecRef? Spec = null,
    [property: Description("Sample latitude in decimal degrees, WGS-84; with Longitude, instead of Polyline.")] double? Latitude = null,
    [property: Description("Sample longitude in decimal degrees, WGS-84.")] double? Longitude = null,
    [property: Description("Polyline to sample instead of a point; its corridor width is ignored.")] GeoPolyline? Polyline = null,
    [property: Description("With a polyline, the longest distance in metres between samples along each rhumb-line leg; null samples the vertices only.")] double? SpacingMeters = null,
    [property: Description("UTC ISO-8601 time: the time step for water level, current and weather, and the time of the water level for depth.")] DateTimeOffset? Time = null,
    [property: Description("Optional TimeQuery (instant / range / series); takes precedence over 'Time'. Depth takes an instant only.")] TimeQuery? Times = null,
    [property: Description("Handling of a single instant outside every covering dataset's time range: Error (default) or Nearest.")] TimeOutOfRangePolicy OutOfRange = TimeOutOfRangePolicy.Error,
    [property: Description("For depth at a point: the least charted depth the point needs; when it fails, the result adds nearestNavigable.")] double? MinDepthMeters = null,
    [property: Description("With MinDepthMeters, the room wanted round the point, in metres.")] double? ClearanceMeters = null,
    [property: Description("With MinDepthMeters, how far to look for a navigable point, in metres (default 5,000, at most 20,000; nearer water is searched on finer cells first).")] double? SearchRadiusMeters = null);

/// <summary>
/// The result of <see cref="SampleTool"/>: exactly one of its members is set,
/// by what was asked for.
/// </summary>
/// <param name="Coverage">A point sample of water level, current, weather or a pinned S-102.</param>
/// <param name="Depth">A point sample of charted depth.</param>
/// <param name="Profile">A polyline's samples.</param>
public sealed record SampleOutput(
    SampleCoverageResult? Coverage,
    ChartedDepthSampleResult? Depth,
    SampleProfileResult? Profile)
{
    /// <summary>The member that is set, for writing to the caller.</summary>
    public object Payload => (object?)Coverage ?? (object?)Depth ?? Profile
        ?? throw new InvalidOperationException("A sample output carries no result.");
}

/// <summary>Charted depth at a point, and with a time the water level and available depth there.</summary>
/// <param name="Latitude">The point's latitude, echoed back.</param>
/// <param name="Longitude">The point's longitude, echoed back.</param>
/// <param name="WaterMask">What the loaded charts show at the point: <c>water</c>, <c>land</c> or <c>uncharted</c>.</param>
/// <param name="Depth">The charted depth and where it came from; <c>null</c> where no source has one.</param>
/// <param name="Time">The time the water level is for; <c>null</c> when no time was given.</param>
/// <param name="WaterLevel">The water level at <paramref name="Time"/>; <c>null</c> when no time was given or there is no depth.</param>
/// <param name="AvailableDepthMeters">
/// Charted depth plus the water level, where the water level applies
/// (status <c>applied</c>); <c>null</c> otherwise.
/// </param>
/// <param name="Navigable">With a minimum depth: whether the point qualifies; <c>null</c> without one.</param>
/// <param name="NotNavigableReason">Why it doesn't: <c>land</c>, <c>uncharted</c>, <c>shoal</c>, <c>hazard</c>, <c>no_depth</c> or <c>too_close</c>.</param>
/// <param name="NearestNavigable">When it doesn't, the nearest point that does, or why there's none.</param>
public sealed record ChartedDepthSampleResult(
    [property: Description("Latitude of the sample, decimal degrees WGS-84.")] double Latitude,
    [property: Description("Longitude of the sample, decimal degrees WGS-84.")] double Longitude,
    [property: Description("What the loaded charts show at the point: \"water\", \"land\" or \"uncharted\".")] string WaterMask,
    [property: Description("The charted depth and where it came from; null where no loaded source has one.")] ChartedDepthValue? Depth,
    [property: Description("The time the water level is for (UTC); null when no time was given.")] DateTimeOffset? Time = null,
    [property: Description("The water level at the time; null when no time was given or there is no depth.")] SampleWaterLevel? WaterLevel = null,
    [property: Description("Charted depth plus the water level, in metres, where the water level applies; null otherwise.")] double? AvailableDepthMeters = null,
    [property: Description("With minDepthMeters: whether the point is navigable (deep enough, charted, no danger, and with the clearance asked for); null without minDepthMeters.")] bool? Navigable = null,
    [property: Description("Why the point isn't navigable: \"land\", \"uncharted\", \"shoal\" (shallower than minDepthMeters), \"hazard\" (a danger in the cell), \"no_depth\" (charted water with no depth) or \"too_close\" (nearer than clearanceMeters to any of those).")] string? NotNavigableReason = null,
    [property: Description("When the point isn't navigable: the nearest point that is, within searchRadiusMeters, or a 'reason' when none is.")] NearestNavigablePoint? NearestNavigable = null);

/// <summary>The nearest navigable point to a sample that isn't navigable (#1559).</summary>
/// <param name="Latitude">Its latitude, or <c>null</c> when none was found.</param>
/// <param name="Longitude">Its longitude.</param>
/// <param name="DepthMeters">The least charted depth round it, in metres.</param>
/// <param name="Depth">The charted depth at it and where it came from (the finest cell wins).</param>
/// <param name="ClearanceMeters">The room round it, in metres.</param>
/// <param name="DistanceMeters">How far it is from the point, in metres.</param>
/// <param name="BearingDegrees">Its bearing from the point, degrees true.</param>
/// <param name="Reason">When none was found, why.</param>
public sealed record NearestNavigablePoint(
    [property: Description("Latitude, decimal degrees WGS-84; null when none was found.")] double? Latitude,
    [property: Description("Longitude, decimal degrees WGS-84; null when none was found.")] double? Longitude,
    [property: Description("The least charted depth round it (its grid cell's centre and corners), in metres.")] double? DepthMeters = null,
    [property: Description("The charted depth at it and where it came from, as a depth sample reports it.")] ChartedDepthValue? Depth = null,
    [property: Description("The room round it, in metres, to the nearest land, shoal, danger or uncharted water.")] double? ClearanceMeters = null,
    [property: Description("How far it is from the point, in metres.")] double? DistanceMeters = null,
    [property: Description("Its bearing from the point, degrees true.")] double? BearingDegrees = null,
    [property: Description("When none was found, why.")] string? Reason = null);

/// <summary>A charted depth and where it came from, as the route check reads it.</summary>
/// <param name="DepthMeters">Depth below the vertical datum in metres (positive down; negative for drying heights). For an area, its minimum depth.</param>
/// <param name="Kind"><c>bathymetry</c> (S-102), <c>dredged_area</c>, <c>depth_area</c> or <c>sounding</c>.</param>
/// <param name="DatasetId">The dataset or cell that supplied the depth.</param>
/// <param name="FeatureId">For S-101, the feature's record id (for <c>describe_feature</c>); <c>null</c> for S-102.</param>
/// <param name="GridCell">For S-102, the grid cell as <c>row,col</c>; <c>null</c> for S-101.</param>
/// <param name="DepthRangeMinimumMeters">For an area, its minimum depth (DRVAL1); <c>null</c> otherwise.</param>
/// <param name="DepthRangeMaximumMeters">For an area, its maximum depth (DRVAL2) when charted; <c>null</c> otherwise.</param>
/// <param name="UncertaintyMeters">The S-102 vertical uncertainty, or <c>null</c>.</param>
/// <param name="VerticalDatum">The vertical datum as an S-100 code (MLLW is 12), or <c>null</c> when not declared.</param>
/// <param name="SoundingDistanceMeters">For a sounding, its distance from the point.</param>
/// <param name="LowConfidence"><c>true</c> for a vector depth (an area minimum or a single sounding), not a surveyed surface.</param>
public sealed record ChartedDepthValue(
    [property: Description("Depth below the vertical datum in metres, positive down (negative for drying heights). For an area, its minimum depth.")] double DepthMeters,
    [property: Description("\"bathymetry\" (S-102), \"dredged_area\", \"depth_area\" or \"sounding\" (S-101, S-401 or translated S-57).")] string Kind,
    [property: Description("The dataset or cell that supplied the depth.")] string? DatasetId,
    [property: Description("For S-101, the feature's record id, for describe_feature; null for S-102.")] string? FeatureId,
    [property: Description("For S-102, the grid cell as \"row,col\"; null for S-101.")] string? GridCell,
    [property: Description("For an area, its minimum depth (DRVAL1) in metres; null otherwise.")] double? DepthRangeMinimumMeters,
    [property: Description("For an area, its maximum depth (DRVAL2) in metres when charted; null otherwise.")] double? DepthRangeMaximumMeters,
    [property: Description("The S-102 vertical uncertainty in metres, or null.")] double? UncertaintyMeters,
    [property: Description("The vertical datum as an S-100 code (MLLW is 12), or null when not declared.")] int? VerticalDatum,
    [property: Description("For a sounding, its distance from the point in metres.")] double? SoundingDistanceMeters,
    [property: Description("True for a vector depth (an area minimum or a single sounding), which the route check treats as low confidence.")] bool LowConfidence);

/// <summary>The water level at a depth sample's time, by the route check's rule.</summary>
/// <param name="Status">
/// <c>applied</c>; <c>datum_mismatch</c> (read, not added, since its datum
/// differs from the depth's); <c>no_coverage</c>; <c>before_start</c> or
/// <c>after_end</c> (no series covers the time); or <c>no_data</c>.
/// </param>
/// <param name="HeightMeters">Height above the datum in metres (positive up), when read.</param>
/// <param name="UncertaintyMeters">The height's uncertainty, when the source carries one.</param>
/// <param name="StepTime">The time step nearest the requested time.</param>
/// <param name="DatasetId">The dataset, or <c>tide-predictions</c> for a prediction.</param>
/// <param name="SourceKind"><c>grid</c> or <c>station</c> (S-104), or <c>prediction</c>.</param>
/// <param name="VerticalDatum">The height's vertical datum as an S-100 code.</param>
/// <param name="Datum">How the depth's datum compares with the height's: <c>same</c>, <c>different</c> or <c>unknown</c>.</param>
/// <param name="Source">For a prediction, the station and the caveat to quote; <c>null</c> for S-104.</param>
public sealed record SampleWaterLevel(
    [property: Description("\"applied\"; \"datum_mismatch\" (read but not added: its datum differs from the depth's); \"no_coverage\"; \"before_start\" or \"after_end\" (no series covers the time); or \"no_data\".")] string Status,
    [property: Description("Height above the datum in metres, positive up, when read.")] double? HeightMeters = null,
    [property: Description("The height's uncertainty in metres, when the source carries one.")] double? UncertaintyMeters = null,
    [property: Description("The time step nearest the requested time (UTC).")] DateTime? StepTime = null,
    [property: Description("The dataset, or \"tide-predictions\" for a prediction.")] string? DatasetId = null,
    [property: Description("\"grid\" or \"station\" (S-104), or \"prediction\".")] string? SourceKind = null,
    [property: Description("The height's vertical datum as an S-100 code.")] int? VerticalDatum = null,
    [property: Description("How the depth's datum compares with the height's: \"same\", \"different\" or \"unknown\".")] string? Datum = null,
    [property: Description("For a prediction, the station and the caveat to quote with the value; null for S-104.")] SampleSource? Source = null);

/// <summary>Samples along a polyline.</summary>
/// <param name="Quantity">The quantity sampled, in snake case; <c>null</c> for a product sampled as it is.</param>
/// <param name="Spec">The pinned spec, or <c>null</c>.</param>
/// <param name="Samples">The samples, in order along the polyline.</param>
/// <param name="Shoalest">For depth, the shoalest sample; <c>null</c> otherwise or when no sample has a depth.</param>
public sealed record SampleProfileResult(
    [property: Description("The quantity sampled: \"depth\", \"water_level\", \"current\" or \"weather\"; null for a product sampled as it is.")] string? Quantity,
    [property: Description("The pinned spec, or null.")] SpecRef? Spec,
    [property: Description("The samples in order along the polyline. A sample with no value carries an 'error' and the rest are still usable.")] IReadOnlyList<SampleProfileEntry> Samples,
    [property: Description("For depth, the shoalest sample (lowest available depth with a time, else lowest charted depth); null otherwise.")] SampleProfileShoalest? Shoalest = null);

/// <summary>One sample along a polyline.</summary>
/// <param name="Index">The sample's index in <see cref="SampleProfileResult.Samples"/>.</param>
/// <param name="LegIndex">The leg the sample is on (leg <c>i</c> runs from vertex <c>i</c> to <c>i + 1</c>).</param>
/// <param name="VertexIndex">The vertex the sample is at, or <c>null</c> for a sample between vertices.</param>
/// <param name="DistanceMeters">Distance along the polyline from its first vertex, in metres.</param>
/// <param name="Latitude">The sample's latitude.</param>
/// <param name="Longitude">The sample's longitude.</param>
/// <param name="Result">For water level, current or weather, the sample; <c>null</c> when it has no value.</param>
/// <param name="Depth">For depth, the sample.</param>
/// <param name="Error">Why <paramref name="Result"/> is <c>null</c>; <c>null</c> when a value was sampled.</param>
public sealed record SampleProfileEntry(
    [property: Description("The sample's index in 'samples'.")] int Index,
    [property: Description("The leg the sample is on: leg i runs from vertex i to vertex i+1.")] int LegIndex,
    [property: Description("The vertex the sample is at, or null for a sample between vertices.")] int? VertexIndex,
    [property: Description("Distance along the polyline from its first vertex, in metres.")] double DistanceMeters,
    [property: Description("Latitude of the sample, decimal degrees WGS-84.")] double Latitude,
    [property: Description("Longitude of the sample, decimal degrees WGS-84.")] double Longitude,
    [property: Description("For water level, current or weather: the sample, as a point sample returns it; null when it has no value.")] SampleCoverageResult? Result = null,
    [property: Description("For depth: the sample, as a point sample returns it.")] ChartedDepthSampleResult? Depth = null,
    [property: Description("Why there is no value here; null when a value was sampled.")] SampleError? Error = null);

/// <summary>Why a sample along a polyline has no value.</summary>
/// <param name="Code">The error code a point sample would return, such as <c>time_out_of_range</c>, <c>no_station_within_reach</c> or <c>no_dataset_covers_point</c>.</param>
/// <param name="Message">The matching human-readable message.</param>
public sealed record SampleError(
    [property: Description("The error code a point sample here would return, e.g. \"time_out_of_range\", \"out_of_bounds\", \"no_data_at_point\", \"no_station_within_reach\".")] string Code,
    [property: Description("Human-readable message for the error; not localised.")] string Message);

/// <summary>The shoalest depth sample along a polyline.</summary>
/// <param name="Index">The sample's index.</param>
/// <param name="DepthMeters">Its charted depth.</param>
/// <param name="AvailableDepthMeters">Its available depth, when a time was given and the water level applies.</param>
public sealed record SampleProfileShoalest(
    [property: Description("The sample's index in 'samples'.")] int Index,
    [property: Description("Its charted depth in metres.")] double DepthMeters,
    [property: Description("Its available depth in metres, when a time was given and the water level applies.")] double? AvailableDepthMeters = null);

/// <summary>
/// The <c>sample</c> MCP tool: one sampler by quantity (charted depth, water
/// level, current, weather) at a point or along a polyline.
/// </summary>
/// <remarks>
/// <para>
/// Depth uses the route check's own sources (<see cref="RouteCheckSourceCache"/>):
/// S-102 first, else the finest S-101, S-401 or translated S-57 cell's
/// dredged area, depth area or nearest sounding, with the water mask where
/// there is no depth. With a time, the water level is read by the same rule
/// as the clearance check (<see cref="WaterLevelSnapshot.Resolve(IReadOnlyList{IWaterLevelSeries}, DateTime, int?)"/>),
/// so a sample agrees with <c>check_route</c> at the same point and time.
/// </para>
/// <para>
/// Water level, current and weather go through <see cref="SampleCoverageService"/>,
/// with tide and tidal current predictions where no S-104 or S-111 covers the
/// point and time and the host has predictions on.
/// </para>
/// </remarks>
public sealed class SampleTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = SampleCoverageService.Name;

    /// <summary>The most samples one polyline request returns.</summary>
    public const int MaxProfileSamples = 2000;

    private static readonly string[] WeatherSpecs = [SpecName.Grib2, SpecName.NetCdf];

    private readonly IDatasetCatalog _catalog;
    private readonly RouteCheckSourceCache _sources;
    private readonly SampleCoverageService _service;
    private readonly PredictedWaterLevelSampler _predictions;
    private readonly PredictedCurrentSampler _currentPredictions;

    /// <summary>Creates the tool.</summary>
    /// <param name="catalog">The catalog of loaded datasets to sample from.</param>
    /// <param name="transforms">Reprojects the request point into a coverage's native CRS.</param>
    /// <param name="sources">
    /// The host's route-check sources: the charted depth, water mask and water
    /// level a depth sample reads, and the tide and current predictions
    /// (<see cref="RouteCheckSourceCache.TidePredictions"/>) that fill S-104
    /// and S-111 gaps.
    /// </param>
    /// <param name="time">The clock, for a predicted sample with no time.</param>
    public SampleTool(IDatasetCatalog catalog, ICrsTransformFactory transforms, RouteCheckSourceCache sources, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(transforms);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(time);
        _catalog = catalog;
        _sources = sources;
        _service = new SampleCoverageService(catalog, transforms);
        _predictions = new PredictedWaterLevelSampler(catalog, sources, time);
        _currentPredictions = new PredictedCurrentSampler(catalog, sources, time);
    }

    /// <summary>Executes the tool.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the sampling.</param>
    /// <returns>The sample or profile, or the error.</returns>
    public async Task<ToolResult<SampleOutput>> InvokeAsync(SampleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (Validate(request) is { } invalid)
        {
            return ToolResult<SampleOutput>.Err(invalid);
        }

        var quantity = request.Quantity ?? QuantityOf(request.Spec!.Value);
        if (request.Polyline is { } polyline)
        {
            var profile = await ProfileAsync(request, quantity, polyline, cancellationToken).ConfigureAwait(false);
            return profile.TryGetValue(out var value)
                ? ToolResult<SampleOutput>.Ok(new SampleOutput(null, null, value))
                : ToolResult<SampleOutput>.Err(Error(profile));
        }

        var latitude = request.Latitude!.Value;
        var longitude = request.Longitude!.Value;
        if (quantity == SampleQuantity.Depth && !IsS102(request.Spec))
        {
            var charted = SampleDepth(request, latitude, longitude);
            if (request.MinDepthMeters is not null)
            {
                charted = await Task.Run(() => WithNavigability(charted, request, cancellationToken), cancellationToken).ConfigureAwait(false);
            }

            return ToolResult<SampleOutput>.Ok(new SampleOutput(null, charted, null));
        }

        var coverage = await SampleCoverageAsync(request, quantity, latitude, longitude, cancellationToken).ConfigureAwait(false);
        return coverage.TryGetValue(out var sample)
            ? ToolResult<SampleOutput>.Ok(new SampleOutput(sample, null, null))
            : ToolResult<SampleOutput>.Err(Error(coverage));
    }

    /// <summary>
    /// Samples a product's coverage at a point: the shared
    /// <see cref="SampleCoverageService"/>, then for S-104 a tide prediction,
    /// or for S-111 a tidal current prediction, when no dataset covers the
    /// point and time.
    /// </summary>
    /// <param name="request">The coverage request.</param>
    /// <param name="cancellationToken">Cancels the sampling.</param>
    /// <returns>The sample, or the error.</returns>
    public async Task<ToolResult<SampleCoverageResult>> SampleCoverageAsync(
        SampleCoverageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await _service.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.TryGetError(out var error)
            || error is not (NoDatasetCoversPoint or OutOfBounds or NoDataAtPoint or TimeOutOfRange))
        {
            return result;
        }

        var predicted = request.Spec.Name switch
        {
            "S-104" => _predictions.Sample(request),
            "S-111" => _currentPredictions.Sample(request),
            _ => null,
        };

        // Out of every station's reach says more than "nothing covers the
        // point" (#1627), but less than a dataset that covers it at another
        // time or has no data in this cell.
        return predicted is null
            || (predicted.TryGetError(out var miss) && miss is NoStationWithinReach && error is not (NoDatasetCoversPoint or OutOfBounds))
            ? result
            : predicted;
    }

    private static ToolError? Validate(SampleRequest request)
    {
        if (request.Quantity is null && request.Spec is null)
        {
            return new InvalidArgument("quantity", "give a quantity (depth, water_level, current or weather), or a spec");
        }

        var hasPoint = request.Latitude is not null || request.Longitude is not null;
        if (hasPoint == (request.Polyline is not null))
        {
            return new InvalidArgument("latitude", "give either latitude and longitude, or a polyline");
        }

        if (hasPoint && (request.Latitude is null || request.Longitude is null))
        {
            return new InvalidArgument(request.Latitude is null ? "latitude" : "longitude", "a point needs both latitude and longitude");
        }

        if (request.Polyline is { } polyline && GeoQueryValidator.Validate(new GeoQuery.Polyline(polyline)) is { } geometry)
        {
            return geometry;
        }

        if (request.SpacingMeters is { } spacing && (!double.IsFinite(spacing) || spacing <= 0))
        {
            return new InvalidArgument("spacingMeters", "must be a positive number of metres");
        }

        if (request.SpacingMeters is not null && request.Polyline is null)
        {
            return new InvalidArgument("spacingMeters", "applies to a polyline only");
        }

        if (request.Spec is { } spec && request.Quantity is { } quantity && !Fits(quantity, spec))
        {
            return new InvalidArgument("spec", $"{spec.Name} doesn't carry {Snake(quantity)}");
        }

        var quantityOrSpec = request.Quantity ?? QuantityOf(request.Spec!.Value);
        if (ValidateNavigability(request, quantityOrSpec) is { } navigability)
        {
            return navigability;
        }

        if (quantityOrSpec == SampleQuantity.Depth && !IsS102(request.Spec)
            && request.Times is TimeQuery.Range or TimeQuery.Series)
        {
            return new InvalidArgument("times", "depth takes a single time; give 'time' or an instant");
        }

        return null;
    }

    private static InvalidArgument? ValidateNavigability(SampleRequest request, SampleQuantity quantity)
    {
        if (request.MinDepthMeters is null)
        {
            return request.ClearanceMeters is not null
                ? new InvalidArgument("clearanceMeters", "give minDepthMeters with it")
                : request.SearchRadiusMeters is not null
                    ? new InvalidArgument("searchRadiusMeters", "give minDepthMeters with it")
                    : null;
        }

        if (quantity != SampleQuantity.Depth || IsS102(request.Spec) || request.Polyline is not null)
        {
            return new InvalidArgument("minDepthMeters", "applies to quantity depth at a point, without an S-102 spec");
        }

        if (!double.IsFinite(request.MinDepthMeters.Value))
        {
            return new InvalidArgument("minDepthMeters", "must be a number of metres");
        }

        if (request.ClearanceMeters is { } clearance && (!double.IsFinite(clearance) || clearance < 0))
        {
            return new InvalidArgument("clearanceMeters", "must be zero or more metres");
        }

        if (request.SearchRadiusMeters is { } radius && (!double.IsFinite(radius) || radius <= 0 || radius > NavigableSnap.MaxSearchRadiusMeters))
        {
            return new InvalidArgument("searchRadiusMeters", $"must be more than 0 and at most {NavigableSnap.MaxSearchRadiusMeters:0} m");
        }

        return null;
    }

    private ChartedDepthSampleResult WithNavigability(ChartedDepthSampleResult sample, SampleRequest request, CancellationToken cancellationToken)
    {
        var sources = _sources.Get(_catalog.Datasets);
        var radius = request.SearchRadiusMeters ?? NavigableSnap.DefaultSearchRadiusMeters;
        var snap = NavigableSnap.Snap(
            sources,
            new GeoPosition(sample.Latitude, sample.Longitude),
            new NavigableSnapRequest(request.MinDepthMeters!.Value, request.ClearanceMeters ?? 0, radius),
            cancellationToken);
        return sample with
        {
            Navigable = snap.Status == NavigableSnapStatus.Navigable,
            NotNavigableReason = snap.Reason,
            NearestNavigable = Nearest(snap, sources, radius),
        };
    }

    /// <summary>A snap's nearest navigable point as sample and plan_route report it; <c>null</c> when the point didn't move.</summary>
    internal static NearestNavigablePoint? Nearest(NavigableSnapResult snap, RouteCheckSources sources, double radiusMeters)
    {
        switch (snap.Status)
        {
            case NavigableSnapStatus.Moved when snap.Position is { } to:
                var at = sources.Depth?.Sample(to);
                return new NearestNavigablePoint(
                    Math.Round(to.Latitude, 7),
                    Math.Round(to.Longitude, 7),
                    snap.DepthMeters,
                    at is null ? null : ToValue(at),
                    snap.ClearanceMeters,
                    snap.DistanceMeters,
                    snap.BearingDegrees);
            case NavigableSnapStatus.NotFound:
                return new NearestNavigablePoint(
                    null,
                    null,
                    Reason: FormattableString.Invariant($"Nothing within {radiusMeters:0} m is charted water at least the minimum depth with the clearance asked for. Widen searchRadiusMeters (at most {NavigableSnap.MaxSearchRadiusMeters:0}), or lower the depth or clearance."));
            default:
                return null;
        }
    }

    // A spec with no quantity of its own is sampled as it is, under the
    // quantity it is asked for (its own sampler decides what it returns).
    private static SampleQuantity QuantityOf(SpecRef spec) => spec.Name switch
    {
        "S-102" => SampleQuantity.Depth,
        "S-104" => SampleQuantity.WaterLevel,
        "S-111" => SampleQuantity.Current,
        SpecName.Grib2 or SpecName.NetCdf => SampleQuantity.Weather,
        _ => SampleQuantity.Weather,
    };

    private static bool Fits(SampleQuantity quantity, SpecRef spec) => spec.Name switch
    {
        "S-102" => quantity == SampleQuantity.Depth,
        "S-104" => quantity == SampleQuantity.WaterLevel,
        "S-111" => quantity == SampleQuantity.Current,
        SpecName.Grib2 or SpecName.NetCdf => quantity == SampleQuantity.Weather,
        _ => quantity != SampleQuantity.Depth,
    };

    private static bool IsS102(SpecRef? spec) => spec is { Name: "S-102" };

    /// <summary>The quantity's name as the tool takes and writes it.</summary>
    /// <param name="quantity">The quantity.</param>
    /// <returns><c>depth</c>, <c>water_level</c>, <c>current</c> or <c>weather</c>.</returns>
    public static string Snake(SampleQuantity quantity) => quantity switch
    {
        SampleQuantity.Depth => "depth",
        SampleQuantity.WaterLevel => "water_level",
        SampleQuantity.Current => "current",
        _ => "weather",
    };

    /// <summary>Reads a quantity's name as the tool takes it.</summary>
    /// <param name="value">The name, such as <c>water_level</c>; case and separators don't matter.</param>
    /// <returns>The quantity, or <c>null</c> for an empty value.</returns>
    /// <exception cref="ArgumentException">The name isn't a quantity.</exception>
    public static SampleQuantity? ParseQuantity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Replace("-", "_", StringComparison.Ordinal).Replace(" ", "_", StringComparison.Ordinal).ToLowerInvariant() switch
        {
            "depth" => SampleQuantity.Depth,
            "water_level" or "waterlevel" => SampleQuantity.WaterLevel,
            "current" => SampleQuantity.Current,
            "weather" => SampleQuantity.Weather,
            _ => throw new ArgumentException(
                $"quantity must be \"depth\", \"water_level\", \"current\" or \"weather\" (got \"{value}\").", "quantity"),
        };
    }

    // The specs a coverage quantity reads, in the order tried.
    private static IReadOnlyList<SpecRef> SpecsFor(SampleRequest request, SampleQuantity quantity) =>
        request.Spec is { } spec
            ? [spec]
            : quantity switch
            {
                SampleQuantity.WaterLevel => [new SpecRef("S-104", default)],
                SampleQuantity.Current => [new SpecRef("S-111", default)],
                SampleQuantity.Depth => [new SpecRef("S-102", default)],
                _ => WeatherSpecs.Select(name => new SpecRef(name, default)).ToArray(),
            };

    private async Task<ToolResult<SampleCoverageResult>> SampleCoverageAsync(
        SampleRequest request,
        SampleQuantity quantity,
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
    {
        var specs = SpecsFor(request, quantity);
        ToolResult<SampleCoverageResult>? best = null;
        foreach (var spec in specs)
        {
            var result = await SampleCoverageAsync(
                new SampleCoverageRequest(spec, latitude, longitude, request.Time, request.Times, request.OutOfRange),
                cancellationToken).ConfigureAwait(false);
            if (result.TryGetValue(out _))
            {
                return result;
            }

            best = Better(best, result);
        }

        // With several specs, none of them loaded reads as nothing covering the point.
        return best is null || (specs.Count > 1 && best.TryGetError(out var error) && error is SpecNotSupportedForTool)
            ? ToolResult<SampleCoverageResult>.Err(new NoDatasetCoversPoint(latitude, longitude))
            : best;
    }

    // Of several specs' misses, the most telling: a time or data problem over
    // "nothing covers", and that over a spec that isn't loaded.
    private static ToolResult<SampleCoverageResult> Better(ToolResult<SampleCoverageResult>? current, ToolResult<SampleCoverageResult> next)
    {
        next.TryGetError(out var nextError);
        if (current is null || !current.TryGetError(out var currentError))
        {
            return next;
        }

        return Rank(nextError) > Rank(currentError) ? next : current;

        static int Rank(ToolError? error) => error switch
        {
            TimeOutOfRange => 3,
            NoDataAtPoint or OutOfBounds or NoStationWithinReach => 2,
            NoDatasetCoversPoint => 1,
            _ => 0,
        };
    }

    private ChartedDepthSampleResult SampleDepth(SampleRequest request, double latitude, double longitude)
    {
        var sources = _sources.Get(_catalog.Datasets);
        var time = request.Times is TimeQuery.Instant instant ? instant.Value : request.Time;
        return SampleDepth(sources, new GeoPosition(latitude, longitude), time);
    }

    private ChartedDepthSampleResult SampleDepth(RouteCheckSources sources, GeoPosition position, DateTimeOffset? time)
    {
        var mask = sources.WaterMask?.Classify(position) ?? WaterMaskClass.Uncharted;
        var depth = sources.Depth?.Sample(position);
        var value = depth is null ? null : ToValue(depth);
        if (time is not { } at)
        {
            return new ChartedDepthSampleResult(position.Latitude, position.Longitude, Snake(mask), value);
        }

        if (depth is null)
        {
            return new ChartedDepthSampleResult(position.Latitude, position.Longitude, Snake(mask), null, at.ToUniversalTime());
        }

        var series = sources.WaterLevel?.Locate(position) ?? [];
        var level = WaterLevelSnapshot.Resolve(series, at.UtcDateTime, depth.VerticalDatum);
        var waterLevel = ToWaterLevel(level);
        double? available = level is { Status: WaterLevelStatus.Applied, Sample: { } applied }
            ? Math.Round(depth.DepthMeters + applied.HeightMeters, 3)
            : null;
        return new ChartedDepthSampleResult(position.Latitude, position.Longitude, Snake(mask), value, at.ToUniversalTime(), waterLevel, available);
    }

    internal static ChartedDepthValue ToValue(ChartedDepth depth)
    {
        var isArea = depth.Kind is ChartedDepthKind.DepthArea or ChartedDepthKind.DredgedArea;
        var isGrid = depth.Kind == ChartedDepthKind.Bathymetry;
        return new ChartedDepthValue(
            depth.DepthMeters,
            depth.Kind switch
            {
                ChartedDepthKind.Bathymetry => "bathymetry",
                ChartedDepthKind.DredgedArea => "dredged_area",
                ChartedDepthKind.DepthArea => "depth_area",
                _ => "sounding",
            },
            depth.DatasetId,
            isGrid ? null : depth.Reference,
            isGrid ? depth.Reference : null,
            isArea ? depth.DepthMeters : null,
            isArea ? depth.DepthRangeMaximumMeters : null,
            depth.UncertaintyMeters,
            depth.VerticalDatum,
            depth.SoundingDistanceMeters is { } d ? Math.Round(d, 1) : null,
            depth.IsLowConfidence);
    }

    private SampleWaterLevel ToWaterLevel(WaterLevelAtPoint level)
    {
        var status = level.Status switch
        {
            WaterLevelStatus.Applied => "applied",
            WaterLevelStatus.DatumMismatch => "datum_mismatch",
            WaterLevelStatus.BeforeStart => "before_start",
            WaterLevelStatus.AfterEnd => "after_end",
            WaterLevelStatus.NoData => "no_data",
            _ => "no_coverage",
        };

        if (level.Source is not { } info)
        {
            return new SampleWaterLevel(status);
        }

        var sample = level.Sample;
        return new SampleWaterLevel(
            status,
            sample?.HeightMeters is { } h ? Math.Round(h, 3) : null,
            sample?.UncertaintyMeters,
            sample?.StepTime,
            info.DatasetId,
            info.Kind switch
            {
                WaterLevelSourceKind.Grid => "grid",
                WaterLevelSourceKind.Station => "station",
                _ => "prediction",
            },
            info.VerticalDatum,
            level.Datum switch
            {
                DatumAgreement.Same => "same",
                DatumAgreement.Different => "different",
                DatumAgreement.Unknown => "unknown",
                _ => null,
            },
            info.Kind == WaterLevelSourceKind.Prediction ? PredictionSource(info) : null);
    }

    private SampleSource PredictionSource(WaterLevelSourceInfo info) => new(
        "prediction",
        new SampleSourceStation(
            info.StationId ?? info.DatasetId,
            info.StationName,
            "tide",
            info.ReferenceStationId is null ? "harmonic" : "subordinate",
            info.ReferenceStationId,
            info.DistanceMeters is { } d ? Math.Round(d) : null),
        info.VerticalDatum,
        TideToolText.Caveat,
        _sources.TidePredictions?.Retrieved?.ToUniversalTime());

    private async Task<ToolResult<SampleProfileResult>> ProfileAsync(
        SampleRequest request,
        SampleQuantity quantity,
        GeoPolyline polyline,
        CancellationToken cancellationToken)
    {
        var (count, length) = Measure(polyline, request.SpacingMeters);
        if (count > MaxProfileSamples)
        {
            var spacing = Math.Ceiling(length / Math.Max(MaxProfileSamples - polyline.Vertices.Count, 1));
            return ToolResult<SampleProfileResult>.Err(new InvalidArgument(
                "spacingMeters",
                $"the polyline gives {count} samples at this spacing, more than {MaxProfileSamples}; use a spacing of at least {spacing.ToString(System.Globalization.CultureInfo.InvariantCulture)} m"));
        }

        var points = Densify(polyline, request.SpacingMeters);

        var entries = new List<SampleProfileEntry>(points.Count);
        var name = request.Quantity is null && request.Spec is { } pinned && !IsKnown(pinned) ? null : Snake(quantity);
        if (quantity == SampleQuantity.Depth && !IsS102(request.Spec))
        {
            var sources = _sources.Get(_catalog.Datasets);
            var time = request.Times is TimeQuery.Instant instant ? instant.Value : request.Time;
            SampleProfileShoalest? shoalest = null;
            foreach (var point in points)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var depth = SampleDepth(sources, point.Position, time);
                entries.Add(Entry(entries.Count, point, null, depth, null));
                if (depth.Depth is { } d)
                {
                    var key = depth.AvailableDepthMeters ?? d.DepthMeters;
                    var current = shoalest is null ? double.PositiveInfinity : shoalest.AvailableDepthMeters ?? shoalest.DepthMeters;
                    if (key < current)
                    {
                        shoalest = new SampleProfileShoalest(entries.Count - 1, d.DepthMeters, depth.AvailableDepthMeters);
                    }
                }
            }

            return ToolResult<SampleProfileResult>.Ok(new SampleProfileResult(name, request.Spec, entries, shoalest));
        }

        TimeOutOfRange? firstTimeError = null;
        var anyValue = false;
        foreach (var point in points)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inner = await SampleCoverageAsync(request, quantity, point.Position.Latitude, point.Position.Longitude, cancellationToken)
                .ConfigureAwait(false);
            if (inner.TryGetError(out var error))
            {
                // A request-level error (a spec no sampler reads) fails the
                // whole call; a miss at one point leaves the rest usable.
                if (error is SpecNotSupportedForTool)
                {
                    return ToolResult<SampleProfileResult>.Err(error);
                }

                firstTimeError ??= error as TimeOutOfRange;
                entries.Add(Entry(entries.Count, point, null, null, new SampleError(error.Code, error.Message)));
                continue;
            }

            inner.TryGetValue(out var value);
            anyValue = true;
            entries.Add(Entry(entries.Count, point, value, null, null));
        }

        if (!anyValue && firstTimeError is not null)
        {
            return ToolResult<SampleProfileResult>.Err(firstTimeError);
        }

        return ToolResult<SampleProfileResult>.Ok(new SampleProfileResult(name, request.Spec, entries));
    }

    private static bool IsKnown(SpecRef spec) =>
        spec.Name is "S-102" or "S-104" or "S-111" or SpecName.Grib2 or SpecName.NetCdf;

    private static SampleProfileEntry Entry(int index, ProfilePoint point, SampleCoverageResult? result, ChartedDepthSampleResult? depth, SampleError? error) =>
        new(index, point.Leg, point.Vertex, Math.Round(point.Distance, 1), point.Position.Latitude, point.Position.Longitude, result, depth, error);

    private readonly record struct ProfilePoint(GeoPosition Position, int Leg, int? Vertex, double Distance);

    // How many samples a spacing gives, and the polyline's length.
    private static (int Count, double Length) Measure(GeoPolyline polyline, double? spacing)
    {
        var vertices = polyline.Vertices;
        var count = vertices.Count;
        var length = 0.0;
        for (var i = 0; i + 1 < vertices.Count; i++)
        {
            var leg = PassageGeodesy.RhumbDistanceMeters(
                new GeoPosition(vertices[i].Latitude, vertices[i].Longitude),
                new GeoPosition(vertices[i + 1].Latitude, vertices[i + 1].Longitude));
            length += leg;
            if (spacing is { } step && leg > step)
            {
                count += (int)Math.Min(Math.Ceiling(leg / step) - 1, int.MaxValue / 4);
            }
        }

        return (count, length);
    }

    // The vertices, and with a spacing the points between them along each
    // rhumb-line leg, so no stretch is longer than the spacing.
    private static List<ProfilePoint> Densify(GeoPolyline polyline, double? spacing)
    {
        var vertices = polyline.Vertices;
        var points = new List<ProfilePoint>();
        var along = 0.0;
        for (var i = 0; i < vertices.Count; i++)
        {
            var here = new GeoPosition(vertices[i].Latitude, vertices[i].Longitude);
            points.Add(new ProfilePoint(here, Math.Min(i, Math.Max(vertices.Count - 2, 0)), i, along));
            if (i == vertices.Count - 1)
            {
                break;
            }

            var next = new GeoPosition(vertices[i + 1].Latitude, vertices[i + 1].Longitude);
            var length = PassageGeodesy.RhumbDistanceMeters(here, next);
            if (spacing is { } step && length > step)
            {
                var segments = (int)Math.Ceiling(length / step);
                for (var k = 1; k < segments; k++)
                {
                    var fraction = (double)k / segments;
                    points.Add(new ProfilePoint(PassageGeodesy.RhumbInterpolate(here, next, fraction), i, null, along + (length * fraction)));
                }
            }

            along += length;
        }

        return points;
    }

    private static string Snake(WaterMaskClass mask) => mask switch
    {
        WaterMaskClass.Water => "water",
        WaterMaskClass.Land => "land",
        _ => "uncharted",
    };

    private static ToolError Error<T>(ToolResult<T> result)
    {
        result.TryGetError(out var error);
        return error!;
    }
}
