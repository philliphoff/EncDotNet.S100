using System.ComponentModel;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>Request payload for <see cref="GetPassageResultTool"/>.</summary>
/// <param name="RouteId">A host route to check; omit (with no polyline) for the active route. Hosts without routes need a polyline.</param>
/// <param name="Polyline">A passage given directly: each pair of consecutive vertices is a leg. Takes precedence over <paramref name="RouteId"/>.</param>
/// <param name="DraftMeters">Draft in metres. Required with a polyline; overrides every leg's draft for a route.</param>
/// <param name="SpeedKnots">Speed over ground in knots; overrides every leg's speed. Without a speed there are no ETAs and every sample uses the departure time.</param>
/// <param name="SquatMeters">A fixed squat allowance in metres; overrides every leg's.</param>
/// <param name="RequiredUkcMeters">Required under-keel clearance in metres; overrides every leg's. Unset checks against 0 m.</param>
/// <param name="Departure">Departure time; defaults to the host's view time for a route, or now.</param>
/// <param name="GreatCircle">For a polyline, run legs as great circles instead of rhumb lines.</param>
/// <param name="SampleSpacingMeters">The longest stretch one reported sample stands for; default 50 m.</param>
/// <param name="IncludeSamples">Include per-sample values.</param>
/// <param name="MaxSamples">The most samples to return when <paramref name="IncludeSamples"/> is set.</param>
/// <param name="WindowFrom">With <paramref name="WindowTo"/>, sweep departures over this range to find the departure window.</param>
/// <param name="WindowTo">The last departure of the sweep.</param>
/// <param name="WindowStepMinutes">Minutes between swept departures.</param>
/// <param name="IncludeWindow">Without <paramref name="WindowFrom"/> and <paramref name="WindowTo"/>, sweep departures across the S-104 range that covers the passage.</param>
public sealed record GetPassageResultRequest(
    [property: Description("Id of a host route to check (viewer only). Omit, with no polyline, for the active route.")] string? RouteId = null,
    [property: Description("A passage given directly: each pair of consecutive vertices is a leg. Takes precedence over routeId.")] GeoPolyline? Polyline = null,
    [property: Description("Draft in metres. Required with a polyline; for a route it overrides every leg's draft.")] double? DraftMeters = null,
    [property: Description("Speed over ground in knots; overrides every leg's speed. Without one there are no ETAs and every sample uses the departure time. S-111 currents are not applied.")] double? SpeedKnots = null,
    [property: Description("Fixed squat allowance in metres; overrides every leg's. Default 0.")] double? SquatMeters = null,
    [property: Description("Required under-keel clearance in metres; overrides every leg's. Unset checks against 0 m and sets noMarginSet.")] double? RequiredUkcMeters = null,
    [property: Description("Departure time (ISO-8601). Defaults to the viewer's view time for a route, or the current time.")] DateTimeOffset? Departure = null,
    [property: Description("For a polyline, run legs as great circles instead of rhumb lines.")] bool GreatCircle = false,
    [property: Description("Longest stretch one reported sample stands for, in metres (default 50). Each sample keeps the shoalest depth of its stretch.")] double? SampleSpacingMeters = null,
    [property: Description("Include per-sample values in 'samples'.")] bool IncludeSamples = false,
    [property: Description("Most samples to return when includeSamples is set (default 500); extra samples are thinned, keeping the tightest of each run.")] int MaxSamples = 500,
    [property: Description("With windowTo, sweep departures from this time to find the departure window.")] DateTimeOffset? WindowFrom = null,
    [property: Description("Last departure of the sweep (inclusive).")] DateTimeOffset? WindowTo = null,
    [property: Description("Minutes between swept departures (default 5).")] int WindowStepMinutes = 5,
    [property: Description("Sweep departures across the whole S-104 time range that covers the passage, as SoundCharts' departure band does: from the first whole step after the data starts to where it ends. Ignored when windowFrom and windowTo are set. Over a long forecast the step widens to keep the sweep under 2000 departures.")] bool IncludeWindow = false);

/// <summary>Result of <see cref="GetPassageResultTool"/>.</summary>
public sealed record GetPassageResultResult(
    [property: Description("Id of the checked route, or null for a polyline.")] string? RouteId,
    [property: Description("Name of the checked route, or null.")] string? RouteName,
    [property: Description("Departure time, UTC.")] DateTimeOffset Departure,
    [property: Description("Arrival time, UTC; null when a leg has no speed.")] DateTimeOffset? Arrival,
    [property: Description("Verdict code: water_level_ends_before_arrival, below_setting, incomplete_data, meets_settings_tight or meets_settings (in that priority).")] string Verdict,
    [property: Description("The verdict in the UI's words, e.g. \"Below your UKC setting in 2 places\".")] string VerdictTitle,
    [property: Description("Lower-confidence reasons anywhere on the passage (see samples[].confidence). The verdict follows the clearance band; these qualify it and never replace it.")] IReadOnlyList<string> Confidence,
    [property: Description("True when some leg has no required UKC, so it was checked against 0 m.")] bool NoMarginSet,
    [property: Description("The smallest margin over the required UKC, with where and when; null when no sample has a clearance.")] PassageMinimumSummary? Minimum,
    [property: Description("Minutes spent below the UKC setting (0 without ETAs).")] double MinutesBelow,
    [property: Description("Number of separate stretches below the UKC setting.")] int PlacesBelow,
    [property: Description("Total passage length in nautical miles.")] double DistanceNm,
    [property: Description("Number of samples along the passage.")] int SampleCount,
    [property: Description("Per-leg results in route order.")] IReadOnlyList<PassageLegSummary> Legs,
    [property: Description("Stretches with no data: no charted depth, no S-104 coverage, before/after the S-104 time range, masked S-104 values, or a datum mismatch.")] IReadOnlyList<PassageStretchSummary> Gaps,
    [property: Description("Lower-confidence stretches: charted depth from S-101, or datums that can't be confirmed to agree. The samples in them keep their own state (meets_settings, tight or below_setting).")] IReadOnlyList<PassageStretchSummary> LowConfidence,
    [property: Description("Datasets, stations and datums behind the result.")] PassageSourcesSummary Sources,
    [property: Description("Per-sample values; present only when includeSamples is set.")] IReadOnlyList<PassageSampleSummary>? Samples,
    [property: Description("True when samples were thinned to maxSamples.")] bool? SamplesThinned,
    [property: Description("The departure sweep; present only when windowFrom and windowTo, or includeWindow, are set (and, for includeWindow, some S-104 covers the passage).")] PassageWindowSummary? Window,
    [property: Description("Disclaimer to show once with the verdict.")] string Disclaimer);

/// <summary>The smallest margin over the required UKC.</summary>
public sealed record PassageMinimumSummary(
    [property: Description("Zero-based leg index.")] int Leg,
    [property: Description("Sample index.")] int Sample,
    [property: Description("Latitude, WGS-84 decimal degrees.")] double Latitude,
    [property: Description("Longitude, WGS-84 decimal degrees.")] double Longitude,
    [property: Description("Distance from departure, nautical miles.")] double DistanceNm,
    [property: Description("When it occurs (ETA, UTC; the departure time without ETAs).")] DateTimeOffset Time,
    [property: Description("True when time is a real ETA.")] bool HasEta,
    [property: Description("Under-keel clearance there, metres.")] double UkcMeters,
    [property: Description("Required UKC there, metres.")] double RequiredUkcMeters,
    [property: Description("State there (see samples[].state).")] string State,
    [property: Description("Lower-confidence reasons there (see samples[].confidence).")] IReadOnlyList<string> Confidence);

/// <summary>One leg's result.</summary>
public sealed record PassageLegSummary(
    [property: Description("Zero-based leg index.")] int Index,
    [property: Description("Leg name, when known.")] string? Name,
    [property: Description("Distance from departure to the start of the leg, nautical miles.")] double StartNm,
    [property: Description("Leg length, nautical miles.")] double DistanceNm,
    [property: Description("ETA at the start of the leg, UTC; null when unknown.")] DateTimeOffset? Start,
    [property: Description("ETA at the end of the leg, UTC; null when unknown.")] DateTimeOffset? End,
    [property: Description("Draft used on the leg, metres.")] double DraftMeters,
    [property: Description("Speed used on the leg, knots; null when unknown.")] double? SpeedKnots,
    [property: Description("Squat allowance used on the leg, metres.")] double SquatMeters,
    [property: Description("Required UKC on the leg, metres; null when unset (checked against 0 m).")] double? RequiredUkcMeters,
    [property: Description("Smallest UKC margin on the leg; null when no sample has a clearance.")] PassageMinimumSummary? Minimum,
    [property: Description("Worst state on the leg (see samples[].state); tints the leg chip, whatever the confidence.")] string WorstState,
    [property: Description("Minutes below the UKC setting on the leg.")] double MinutesBelow,
    [property: Description("Separate stretches below the UKC setting on the leg.")] int PlacesBelow,
    [property: Description("True when part of the leg is lower confidence (dashed chip border and dashed route line in the band's colour).")] bool LowConfidence,
    [property: Description("Lower-confidence reasons on the leg (see samples[].confidence).")] IReadOnlyList<string> Confidence,
    [property: Description("True when part of the leg has charted depth but no applied water level (\"no tide\").")] bool NoWaterLevel);

/// <summary>A run of samples sharing a gap or low-confidence reason.</summary>
public sealed record PassageStretchSummary(
    [property: Description("Kind: no_charted_depth, no_water_level_coverage, water_level_before_start, water_level_after_end, water_level_no_data, datum_mismatch, charted_depth_from_s101 or datum_unconfirmed.")] string Kind,
    [property: Description("First sample index.")] int FirstSample,
    [property: Description("Last sample index.")] int LastSample,
    [property: Description("Leg the stretch starts on.")] int FirstLeg,
    [property: Description("Leg the stretch ends on.")] int LastLeg,
    [property: Description("Start, nautical miles from departure.")] double StartNm,
    [property: Description("End, nautical miles from departure.")] double EndNm,
    [property: Description("Time at the first sample, UTC.")] DateTimeOffset From,
    [property: Description("Time at the last sample, UTC.")] DateTimeOffset To);

/// <summary>Where the result's values came from.</summary>
public sealed record PassageSourcesSummary(
    [property: Description("S-102 datasets that supplied charted depth.")] IReadOnlyList<string> BathymetryDatasets,
    [property: Description("S-101 cells that supplied charted depth where S-102 didn't (low confidence).")] IReadOnlyList<string> FallbackDatasets,
    [property: Description("S-104 datasets (and stations) that supplied water level.")] IReadOnlyList<PassageWaterLevelReference> WaterLevels,
    [property: Description("Vertical datums of the charted depths.")] IReadOnlyList<PassageDatumReference> DepthDatums,
    [property: Description("Vertical datums of the water levels.")] IReadOnlyList<PassageDatumReference> WaterLevelDatums,
    [property: Description("Largest S-102 depth uncertainty met, metres; null when none.")] double? MaxUncertaintyMeters);

/// <summary>An S-104 source.</summary>
public sealed record PassageWaterLevelReference(
    [property: Description("S-104 dataset id.")] string Dataset,
    [property: Description("\"grid\" or \"station\".")] string Kind,
    [property: Description("Station identifier, for a station series.")] string? Station);

/// <summary>A vertical datum code and its name.</summary>
public sealed record PassageDatumReference(
    [property: Description("S-100 vertical datum code as declared (verticalDatum).")] int Code,
    [property: Description("Datum name, or \"Unknown (code N)\" for a code outside the S-100 list.")] string Label);

/// <summary>One sample.</summary>
public sealed record PassageSampleSummary(
    [property: Description("Sample index.")] int Index,
    [property: Description("Zero-based leg index.")] int Leg,
    [property: Description("Latitude of the shoalest point in the sample's stretch.")] double Latitude,
    [property: Description("Longitude of the shoalest point in the sample's stretch.")] double Longitude,
    [property: Description("Distance from departure, nautical miles.")] double DistanceNm,
    [property: Description("ETA at the sample, UTC (the departure time without ETAs).")] DateTimeOffset Time,
    [property: Description("Charted depth, metres below its datum; null when none.")] double? ChartedDepthMeters,
    [property: Description("Charted depth source: s102, s101_dredged_area, s101_depth_area or s101_sounding.")] string? DepthSource,
    [property: Description("Dataset (cell) of the charted depth.")] string? DepthDataset,
    [property: Description("Grid cell (row,col) for S-102, or feature record id for S-101.")] string? DepthReference,
    [property: Description("S-102 uncertainty, metres.")] double? UncertaintyMeters,
    [property: Description("Water level above its datum, metres (also reported on a datum mismatch, where it isn't applied).")] double? WaterLevelMeters,
    [property: Description("applied, no_coverage, before_start, after_end, no_data or datum_mismatch.")] string WaterLevelStatus,
    [property: Description("S-104 dataset of the water level.")] string? WaterLevelDataset,
    [property: Description("S-104 station, for a station series.")] string? Station,
    [property: Description("S-104 time step nearest the ETA, UTC.")] DateTime? TimeStep,
    [property: Description("same, different or unknown: how the depth and water-level datums compare.")] string? Datum,
    [property: Description("Under-keel clearance = charted depth + water level − draft − squat, metres; null without both.")] double? UkcMeters,
    [property: Description("Required UKC, metres.")] double RequiredUkcMeters,
    [property: Description("Clearance state: meets_settings, tight (within 0.5 m of required), below_setting or no_data. Confidence is reported separately.")] string State,
    [property: Description("Lower-confidence reasons, separate from the state: charted_depth_from_s101 (S-101 stands in for S-102) and datum_unconfirmed (the depth or water-level datum isn't declared, so they can't be confirmed to share a reference). Empty at full confidence.")] IReadOnlyList<string> Confidence);

/// <summary>The departure sweep.</summary>
public sealed record PassageWindowSummary(
    [property: Description("First swept departure, UTC.")] DateTimeOffset From,
    [property: Description("Last swept departure, UTC.")] DateTimeOffset To,
    [property: Description("Minutes between departures.")] int StepMinutes,
    [property: Description("Runs of departures that meet your settings, including those that meet them with lower confidence.")] IReadOnlyList<PassageWindowRangeSummary> Windows,
    [property: Description("Lower-confidence reasons among the departures that meet your settings: the caveat on the window. Empty when none.")] IReadOnlyList<string> WindowConfidence,
    [property: Description("The meeting departure nearest the requested departure; null when none meets.")] DateTimeOffset? NearestMeetingDeparture,
    [property: Description("The trailing run of departures whose arrival is after the S-104 data ends (verdict water_level_ends_before_arrival; never meeting); null when the last departure arrives within the data.")] PassageWindowRangeSummary? ArrivalAfterDataEnds,
    [property: Description("One entry per swept departure.")] IReadOnlyList<PassageDepartureSummary> Departures);

/// <summary>A run of departures that meet your settings.</summary>
public sealed record PassageWindowRangeSummary(
    [property: Description("First meeting departure, UTC.")] DateTimeOffset First,
    [property: Description("Last meeting departure, UTC.")] DateTimeOffset Last);

/// <summary>One swept departure.</summary>
public sealed record PassageDepartureSummary(
    [property: Description("Departure, UTC.")] DateTimeOffset Departure,
    [property: Description("Arrival, UTC; null without ETAs.")] DateTimeOffset? Arrival,
    [property: Description("Verdict code for this departure.")] string Verdict,
    [property: Description("UKC at the smallest margin, metres; null when no sample has a clearance.")] double? MinUkcMeters,
    [property: Description("Minutes below the UKC setting.")] double MinutesBelow,
    [property: Description("Lower-confidence reasons along the passage at this departure.")] IReadOnlyList<string> Confidence);

/// <summary>
/// A route the host can hand to <see cref="GetPassageResultTool"/>, with the
/// values it has for each leg (any of which the request may override).
/// </summary>
/// <param name="RouteId">The route id.</param>
/// <param name="Name">The route name.</param>
/// <param name="Legs">The legs in order.</param>
/// <param name="DefaultDeparture">Departure to use when the request has none (the route's departure, or the view time).</param>
public sealed record PassageRouteSnapshot(
    string RouteId,
    string? Name,
    IReadOnlyList<PassageRouteLeg> Legs,
    DateTimeOffset DefaultDeparture);

/// <summary>One leg of a <see cref="PassageRouteSnapshot"/>, with the effective values the host knows.</summary>
/// <param name="Points">The leg's points (at least two).</param>
/// <param name="Geometry">Rhumb line or great circle.</param>
/// <param name="DraftMeters">Draft, or <c>null</c>.</param>
/// <param name="SpeedKnots">Speed over ground, or <c>null</c>.</param>
/// <param name="SquatMeters">Squat allowance, or <c>null</c>.</param>
/// <param name="RequiredUkcMeters">Required UKC, or <c>null</c>.</param>
/// <param name="Name">Display name, such as "Leg 6 → 7".</param>
/// <param name="PortCrossTrackMeters">The port cross-track limit in metres (a half-width: track to edge), or <c>null</c>; <c>check_route</c> sweeps the corridor.</param>
/// <param name="StarboardCrossTrackMeters">The starboard cross-track limit in metres, or <c>null</c>.</param>
public sealed record PassageRouteLeg(
    IReadOnlyList<GeoPosition> Points,
    LegGeometry Geometry,
    double? DraftMeters,
    double? SpeedKnots,
    double? SquatMeters,
    double? RequiredUkcMeters,
    string? Name,
    double? PortCrossTrackMeters = null,
    double? StarboardCrossTrackMeters = null);

/// <summary>Supplies host routes (such as the viewer's) to <see cref="GetPassageResultTool"/> and <see cref="CheckRouteTool"/>.</summary>
public interface IPassageRouteProvider
{
    /// <summary>Snapshots a route, or returns the host's error (such as route not found).</summary>
    /// <param name="routeId">The route id, or <c>null</c> for the active route.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The route, or an error.</returns>
    Task<ToolResult<PassageRouteSnapshot>> GetRouteAsync(string? routeId, CancellationToken cancellationToken);
}

/// <summary>
/// Checks a passage's under-keel clearance against the loaded S-102, S-101
/// and S-104 data (<see cref="PassageCheckEngine"/>): per-sample and per-leg
/// UKC, states, sources, the minimum, gaps, minutes below the setting, the
/// verdict and, optionally, the departure window.
/// </summary>
/// <remarks>
/// Takes either a host route (the viewer's active or a named route) or a
/// polyline with a draft, the composed query #396 asked for. Not for
/// navigation.
/// </remarks>
public sealed class GetPassageResultTool
{
    /// <summary>Tool name used in error payloads.</summary>
    public const string Name = "get_passage_result";

    /// <summary>The disclaimer shown once with the verdict.</summary>
    public const string DisclaimerPrefix = "Not for navigation · computed from ";

    private readonly IDatasetCatalog _catalog;
    private readonly ICrsTransformFactory _transforms;
    private readonly IPassageRouteProvider? _routes;
    private const int MaxWindowDepartures = 2000;

    private readonly SemaphoreSlim _run = new(1, 1);
    private IReadOnlyList<LoadedDataset>? _cachedSnapshot;
    private PassageDataSources? _cachedSources;

    /// <summary>Creates the tool.</summary>
    /// <param name="catalog">The loaded datasets.</param>
    /// <param name="routes">The host's routes, or <c>null</c> when the host has none (polylines only).</param>
    /// <param name="transforms">CRS transforms, or <c>null</c> for ProjNet.</param>
    public GetPassageResultTool(IDatasetCatalog catalog, IPassageRouteProvider? routes = null, ICrsTransformFactory? transforms = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _routes = routes;
        _transforms = transforms ?? new ProjNetCrsTransformFactory();
    }

    /// <summary>Executes the tool.</summary>
    public async Task<ToolResult<GetPassageResultResult>> InvokeAsync(
        GetPassageResultRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (Validate(request) is { } invalid)
        {
            return ToolResult<GetPassageResultResult>.Err(invalid);
        }

        string? routeId = null;
        string? routeName = null;
        List<PassageLeg> legs;
        DateTimeOffset departure;
        if (request.Polyline is { } polyline)
        {
            if (GeoQueryValidator.Validate(new GeoQuery.Polyline(polyline)) is { } geometryError)
            {
                return ToolResult<GetPassageResultResult>.Err(geometryError);
            }

            if (request.DraftMeters is null)
            {
                return ToolResult<GetPassageResultResult>.Err(new InvalidArgument("draftMeters", "is required with a polyline"));
            }

            var geometry = request.GreatCircle ? LegGeometry.GreatCircle : LegGeometry.Rhumb;
            legs = new List<PassageLeg>(polyline.Vertices.Count - 1);
            for (var i = 1; i < polyline.Vertices.Count; i++)
            {
                var a = polyline.Vertices[i - 1];
                var b = polyline.Vertices[i];
                legs.Add(new PassageLeg(
                    [new GeoPosition(a.Latitude, a.Longitude), new GeoPosition(b.Latitude, b.Longitude)],
                    request.DraftMeters.Value,
                    request.SpeedKnots,
                    request.SquatMeters ?? 0,
                    request.RequiredUkcMeters,
                    geometry,
                    $"Leg {i} → {i + 1}"));
            }

            departure = request.Departure ?? DateTimeOffset.UtcNow;
        }
        else
        {
            if (_routes is null)
            {
                return ToolResult<GetPassageResultResult>.Err(new InvalidArgument(
                    "polyline", "this host has no routes; pass a polyline and draftMeters"));
            }

            var lookup = await _routes.GetRouteAsync(request.RouteId, cancellationToken).ConfigureAwait(false);
            if (lookup.TryGetError(out var routeError))
            {
                return ToolResult<GetPassageResultResult>.Err(routeError);
            }

            lookup.TryGetValue(out var route);
            if (route!.Legs.Count == 0)
            {
                return ToolResult<GetPassageResultResult>.Err(new InvalidArgument("routeId", "the route needs at least two waypoints"));
            }

            legs = new List<PassageLeg>(route.Legs.Count);
            for (var i = 0; i < route.Legs.Count; i++)
            {
                var leg = route.Legs[i];
                if ((request.DraftMeters ?? leg.DraftMeters) is not { } draft)
                {
                    return ToolResult<GetPassageResultResult>.Err(new InvalidArgument(
                        "draftMeters", $"leg {i} of the route has no draft; set one on the route or pass draftMeters"));
                }

                legs.Add(new PassageLeg(
                    leg.Points,
                    draft,
                    request.SpeedKnots ?? leg.SpeedKnots,
                    request.SquatMeters ?? leg.SquatMeters ?? 0,
                    request.RequiredUkcMeters ?? leg.RequiredUkcMeters,
                    leg.Geometry,
                    leg.Name));
            }

            routeId = route.RouteId;
            routeName = route.Name;
            departure = request.Departure ?? route.DefaultDeparture;
        }

        // One check at a time: the cached sources share CRS transforms and
        // lazily built S-101 indexes between calls.
        await _run.WaitAsync(cancellationToken).ConfigureAwait(false);
        PassageCheckResult result;
        PassageWindowSummary? window = null;
        try
        {
            var sources = SourcesFor(_catalog.Datasets);
            var options = request.SampleSpacingMeters is { } spacing
                ? PassageCheckOptions.Default with { SampleSpacingMeters = spacing }
                : PassageCheckOptions.Default;
            var engine = new PassageCheckEngine(sources.Depth, sources.WaterLevel, options);

            var plan = new PassagePlan(legs, departure);
            var profile = engine.SampleDepths(legs, cancellationToken);
            result = engine.Evaluate(profile, plan, cancellationToken);
            if (request.WindowFrom is { } from && request.WindowTo is { } to)
            {
                var sweep = engine.SweepDepartures(profile, legs, from, to, TimeSpan.FromMinutes(request.WindowStepMinutes), cancellationToken);
                window = Window(sweep, departure);
            }
            else if (request.IncludeWindow
                && engine.SweepWaterLevelRange(profile, legs, TimeSpan.FromMinutes(request.WindowStepMinutes), MaxWindowDepartures, cancellationToken) is { Options.Count: > 0 } sweep)
            {
                window = Window(sweep, departure);
            }
        }
        catch (ArgumentException ex)
        {
            return ToolResult<GetPassageResultResult>.Err(new InvalidArgument("polyline", ex.Message));
        }
        finally
        {
            _run.Release();
        }

        return ToolResult<GetPassageResultResult>.Ok(Project(result, legs, routeId, routeName, request, window));
    }

    private static InvalidArgument? Validate(GetPassageResultRequest request)
    {
        static bool Bad(double? v, bool allowZero = true) =>
            v is { } x && (!double.IsFinite(x) || x < 0 || (!allowZero && x == 0));

        if (Bad(request.DraftMeters, allowZero: false)) return new InvalidArgument("draftMeters", "must be a positive number of metres");
        if (Bad(request.SpeedKnots, allowZero: false)) return new InvalidArgument("speedKnots", "must be a positive number of knots");
        if (Bad(request.SquatMeters)) return new InvalidArgument("squatMeters", "must be zero or more metres");
        if (Bad(request.RequiredUkcMeters)) return new InvalidArgument("requiredUkcMeters", "must be zero or more metres");
        if (Bad(request.SampleSpacingMeters, allowZero: false) || request.SampleSpacingMeters is < 1) return new InvalidArgument("sampleSpacingMeters", "must be at least 1 m");
        if (request.MaxSamples < 1) return new InvalidArgument("maxSamples", "must be at least 1");
        if ((request.WindowFrom is null) != (request.WindowTo is null)) return new InvalidArgument("windowTo", "windowFrom and windowTo go together");
        if (request.WindowFrom is { } f && request.WindowTo is { } t)
        {
            if (t < f) return new InvalidArgument("windowTo", "must not be before windowFrom");
            if (request.WindowStepMinutes < 1) return new InvalidArgument("windowStepMinutes", "must be at least 1");
            if ((t - f).TotalMinutes / request.WindowStepMinutes > MaxWindowDepartures) return new InvalidArgument("windowStepMinutes", "the sweep would exceed 2000 departures");
        }

        if (request.IncludeWindow && request.WindowStepMinutes < 1) return new InvalidArgument("windowStepMinutes", "must be at least 1");

        return null;
    }

    // The sources index S-101 cells lazily, so they are kept while the catalog
    // snapshot is unchanged (the catalog publishes a new list on every change).
    private PassageDataSources SourcesFor(IReadOnlyList<LoadedDataset> snapshot)
    {
        if (!ReferenceEquals(snapshot, _cachedSnapshot) || _cachedSources is null)
        {
            _cachedSources = PassageDataSources.FromDatasets(snapshot, _transforms);
            _cachedSnapshot = snapshot;
        }

        return _cachedSources;
    }

    private static GetPassageResultResult Project(
        PassageCheckResult result,
        IReadOnlyList<PassageLeg> legs,
        string? routeId,
        string? routeName,
        GetPassageResultRequest request,
        PassageWindowSummary? window)
    {
        var samples = result.Samples;
        IReadOnlyList<PassageSampleSummary>? sampleList = null;
        bool? thinned = null;
        if (request.IncludeSamples)
        {
            var kept = Thin(samples, request.MaxSamples);
            thinned = kept.Count < samples.Count;
            sampleList = kept.Select(Sample).ToList();
        }

        var legSummaries = result.Legs.Select(l => new PassageLegSummary(
            l.LegIndex,
            l.Name,
            Nm(l.StartDistanceMeters),
            Nm(l.DistanceMeters),
            l.Start,
            l.End,
            legs[l.LegIndex].DraftMeters,
            legs[l.LegIndex].SpeedKnots,
            legs[l.LegIndex].SquatMeters,
            legs[l.LegIndex].RequiredUkcMeters,
            Minimum(l.Minimum),
            State(l.WorstState),
            Math.Round(l.MinutesBelow, 1),
            l.PlacesBelow,
            l.HasLowConfidence,
            ConfidenceCodes(l.Confidence),
            l.HasNoWaterLevel)).ToList();

        return new GetPassageResultResult(
            routeId,
            routeName,
            result.Departure,
            result.Arrival,
            Verdict(result.Verdict),
            VerdictTitle(result),
            ConfidenceCodes(result.Confidence),
            result.NoMarginSet,
            Minimum(result.Minimum),
            Math.Round(result.MinutesBelow, 1),
            result.PlacesBelow,
            Nm(result.Legs.Sum(l => l.DistanceMeters)),
            samples.Count,
            legSummaries,
            result.Gaps.Select(Stretch).ToList(),
            result.LowConfidence.Select(Stretch).ToList(),
            Sources(result.Sources),
            sampleList,
            thinned,
            window,
            Disclaimer(result.Sources));
    }

    /// <summary>
    /// The handoff's verdict title (C3): data ends before arrival → below →
    /// (incomplete) → tight → meets.
    /// </summary>
    internal static string VerdictTitle(PassageCheckResult result) => result.Verdict switch
    {
        PassageVerdict.WaterLevelEndsBeforeArrival => "Arrival is after the water-level data ends",
        PassageVerdict.BelowSetting => result.PlacesBelow == 1
            ? "Below your UKC setting in 1 place"
            : $"Below your UKC setting in {result.PlacesBelow} places",
        PassageVerdict.IncompleteData => "No data for part of the passage",
        PassageVerdict.MeetsSettingsTight => "Meets your settings · tight",
        _ => "Meets your settings",
    } + (result.NoMarginSet ? " · no margin set" : string.Empty);

    private static string Disclaimer(PassageSources sources)
    {
        var products = new List<string>();
        if (sources.BathymetryDatasets.Count > 0) products.Add("S-102");
        if (sources.FallbackDatasets.Count > 0) products.Add("S-101");
        if (sources.WaterLevelSources.Count > 0) products.Add("S-104");
        return DisclaimerPrefix + (products.Count == 0 ? "no data" : string.Join(", ", products)) + " for your settings";
    }

    private static List<UkcSample> Thin(IReadOnlyList<UkcSample> samples, int max)
    {
        if (samples.Count <= max)
        {
            return [.. samples];
        }

        // Keep the tightest sample of each run, so the minimum and every
        // below-the-setting run survive the thinning.
        var kept = new List<UkcSample>(max);
        var per = (double)samples.Count / max;
        for (var b = 0; b < max; b++)
        {
            var start = (int)(b * per);
            var end = Math.Min(samples.Count, (int)((b + 1) * per));
            UkcSample? best = null;
            for (var i = start; i < end; i++)
            {
                var s = samples[i];
                if (best is null || Margin(s) < Margin(best))
                {
                    best = s;
                }
            }

            if (best is not null)
            {
                kept.Add(best);
            }
        }

        return kept;

        static double Margin(UkcSample s) => s.ClearanceMeters is { } c ? c - s.RequiredUkcMeters : double.PositiveInfinity;
    }

    private static PassageSampleSummary Sample(UkcSample s) => new(
        s.Index,
        s.LegIndex,
        s.Position.Latitude,
        s.Position.Longitude,
        Nm(s.DistanceMeters),
        s.Time,
        Round(s.Depth?.DepthMeters),
        s.Depth?.Kind switch
        {
            ChartedDepthKind.Bathymetry => "s102",
            ChartedDepthKind.DredgedArea => "s101_dredged_area",
            ChartedDepthKind.DepthArea => "s101_depth_area",
            ChartedDepthKind.Sounding => "s101_sounding",
            _ => null,
        },
        s.Depth?.DatasetId,
        s.Depth?.Reference,
        Round(s.Depth?.UncertaintyMeters),
        Round(s.WaterLevel.HeightMeters),
        Snake(s.WaterLevel.Status.ToString()),
        s.WaterLevel.Source?.DatasetId,
        s.WaterLevel.Source?.StationId,
        s.WaterLevel.StepTime,
        s.WaterLevel.Datum is { } d ? Snake(d.ToString()) : null,
        Round(s.ClearanceMeters),
        s.RequiredUkcMeters,
        State(s.State),
        ConfidenceCodes(s.Confidence));

    private static PassageMinimumSummary? Minimum(UkcMinimum? m) => m is null
        ? null
        : new PassageMinimumSummary(
            m.LegIndex,
            m.SampleIndex,
            m.Position.Latitude,
            m.Position.Longitude,
            Nm(m.DistanceMeters),
            m.Time,
            m.HasEta,
            Round(m.ClearanceMeters)!.Value,
            m.RequiredUkcMeters,
            State(m.State),
            ConfidenceCodes(m.Confidence));

    private static PassageStretchSummary Stretch(PassageStretch s) => new(
        Snake(s.Kind.ToString()),
        s.FirstSample,
        s.LastSample,
        s.FirstLeg,
        s.LastLeg,
        Nm(s.StartDistanceMeters),
        Nm(s.EndDistanceMeters),
        s.StartTime,
        s.EndTime);

    private static PassageSourcesSummary Sources(PassageSources s) => new(
        s.BathymetryDatasets,
        s.FallbackDatasets,
        s.WaterLevelSources.Select(w => new PassageWaterLevelReference(
            w.DatasetId, w.Kind == WaterLevelSourceKind.Grid ? "grid" : "station", w.StationId)).ToList(),
        s.DepthDatums.Select(Datum).ToList(),
        s.WaterLevelDatums.Select(Datum).ToList(),
        Round(s.MaxUncertaintyMeters));

    private static PassageDatumReference Datum(int code) => new(code, VerticalDatums.GetLabel(code));

    private static PassageWindowSummary Window(DepartureSweep sweep, DateTimeOffset departure) => new(
        sweep.Options.Count > 0 ? sweep.Options[0].Departure : departure,
        sweep.Options.Count > 0 ? sweep.Options[^1].Departure : departure,
        (int)Math.Round(sweep.Step.TotalMinutes),
        sweep.Windows().Select(w => new PassageWindowRangeSummary(w.First, w.Last)).ToList(),
        ConfidenceCodes(sweep.MeetingConfidence()),
        sweep.NearestMeeting(departure)?.Departure,
        sweep.ArrivalAfterDataEnds() is { } hatched ? new PassageWindowRangeSummary(hatched.First, hatched.Last) : null,
        sweep.Options.Select(o => new PassageDepartureSummary(
            o.Departure, o.Arrival, Verdict(o.Verdict), Round(o.MinimumClearanceMeters), Math.Round(o.MinutesBelow, 1), ConfidenceCodes(o.Confidence))).ToList());

    /// <summary>The wire code for a clearance state: <c>meets_settings</c>, <c>tight</c>, <c>below_setting</c> or <c>no_data</c>.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The code.</returns>
    public static string State(UkcState state) => state switch
    {
        UkcState.MeetsSettings => "meets_settings",
        UkcState.Tight => "tight",
        UkcState.BelowSetting => "below_setting",
        _ => "no_data",
    };

    /// <summary>
    /// The wire codes for lower-confidence reasons, in a fixed order:
    /// <c>charted_depth_from_s101</c>, then <c>datum_unconfirmed</c>.
    /// </summary>
    /// <param name="reasons">The reasons.</param>
    /// <returns>The codes; empty at full confidence.</returns>
    public static IReadOnlyList<string> ConfidenceCodes(ConfidenceReasons reasons)
    {
        var codes = new List<string>(2);
        if (reasons.HasFlag(ConfidenceReasons.ChartedDepthFromS101))
        {
            codes.Add("charted_depth_from_s101");
        }

        if (reasons.HasFlag(ConfidenceReasons.DatumUnconfirmed))
        {
            codes.Add("datum_unconfirmed");
        }

        return codes;
    }

    internal static string Verdict(PassageVerdict verdict) => verdict switch
    {
        PassageVerdict.MeetsSettings => "meets_settings",
        PassageVerdict.MeetsSettingsTight => "meets_settings_tight",
        PassageVerdict.BelowSetting => "below_setting",
        PassageVerdict.IncompleteData => "incomplete_data",
        _ => "water_level_ends_before_arrival",
    };

    // PascalCase enum name → snake_case wire code; digits stay attached ("FromS101" → "from_s101").
    private static string Snake(string pascal)
    {
        var builder = new System.Text.StringBuilder(pascal.Length + 8);
        for (var i = 0; i < pascal.Length; i++)
        {
            if (char.IsUpper(pascal[i]) && i > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(pascal[i]));
        }

        return builder.ToString();
    }

    private static double Nm(double meters) => Math.Round(meters / PassageGeodesy.MetersPerNauticalMile, 3);

    private static double? Round(double? metres) => metres is { } m ? Math.Round(m, 2) : null;
}
