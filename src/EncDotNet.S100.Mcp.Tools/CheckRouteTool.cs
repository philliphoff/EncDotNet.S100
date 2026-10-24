using System.ComponentModel;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>Request payload for <see cref="CheckRouteTool"/>.</summary>
/// <param name="RouteId">A host route to check; omit (with no polyline) for the active route.</param>
/// <param name="Polyline">A route given directly: each pair of consecutive vertices is a leg. Takes precedence over <paramref name="RouteId"/>.</param>
/// <param name="CorridorHalfWidthMeters">The corridor's half-width either side of the track, overriding every leg's cross-track limits.</param>
/// <param name="GreatCircle">For a polyline, run legs as great circles instead of rhumb lines.</param>
/// <param name="SafetyContourMeters">The safety contour for the depth checks; with <paramref name="SafetyDepthMeters"/>, turns them on.</param>
/// <param name="SafetyDepthMeters">The safety depth for the danger and sounding checks.</param>
/// <param name="DraftMeters">A draft for every leg; with one, the clearance check runs and dangers are also judged against each leg's keel.</param>
/// <param name="SpeedKnots">A speed over ground for every leg; speeds give the ETAs.</param>
/// <param name="SquatMeters">A fixed squat allowance for every leg.</param>
/// <param name="RequiredUkcMeters">A required under-keel clearance for every leg.</param>
/// <param name="Departure">The departure the ETAs count from, and the time to judge at without speeds.</param>
/// <param name="SampleSpacingMeters">The longest stretch one clearance sample stands for; default 50 m.</param>
/// <param name="IncludeSamples">Include the clearance's per-leg results and samples.</param>
/// <param name="MaxSamples">The most samples to return when <paramref name="IncludeSamples"/> is set.</param>
/// <param name="IncludeWindow">Sweep departures for the departure window: over <paramref name="WindowFrom"/> to <paramref name="WindowTo"/>, or across the S-104 range that covers the route.</param>
/// <param name="WindowFrom">With <paramref name="WindowTo"/>, the first departure of the sweep.</param>
/// <param name="WindowTo">The last departure of the sweep.</param>
/// <param name="WindowStepMinutes">Minutes between swept departures.</param>
public sealed record CheckRouteRequest(
    [property: Description("Id of a host route to check (viewer only). Omit, with no polyline, for the active route.")] string? RouteId = null,
    [property: Description("A route given directly: each pair of consecutive vertices is a leg. Takes precedence over routeId. Its corridorWidthMeters, when set, is the half-width either side of the track.")] GeoPolyline? Polyline = null,
    [property: Description("The corridor's HALF-width in metres: how far it reaches to port, and to starboard, of the track (so the corridor is twice this wide). Overrides every leg's cross-track limits. Without it a route uses each leg's port/starboard XTD, and anything unset uses 50 m.")] double? CorridorHalfWidthMeters = null,
    [property: Description("For a polyline, run legs as great circles instead of rhumb lines.")] bool GreatCircle = false,
    [property: Description("Safety contour in metres. With safetyDepthMeters, turns on the depth checks: depth, dredged and drying areas and S-102 cells shallower than it, and charted dangers. SoundCharts' findings list uses the mariner's settings (get_mariner_settings).")] double? SafetyContourMeters = null,
    [property: Description("Safety depth in metres, for charted dangers and soundings. Goes with safetyContourMeters.")] double? SafetyDepthMeters = null,
    [property: Description("Draft in metres, for every leg (a route's own otherwise). With a draft on every leg the clearance check runs, and each leg's dangers are also judged against its draft + squat + required UKC.")] double? DraftMeters = null,
    [property: Description("Speed over ground in knots, for every leg (a route's own otherwise). Speeds give the ETAs that warnings, seasonal areas and S-104 water levels are judged at. S-111 currents are not applied.")] double? SpeedKnots = null,
    [property: Description("Fixed squat allowance in metres, for every leg (a route's own otherwise; default 0).")] double? SquatMeters = null,
    [property: Description("Required under-keel clearance in metres, for every leg (a route's own otherwise). Unset checks against 0 m and sets clearance.noMarginSet.")] double? RequiredUkcMeters = null,
    [property: Description("ISO 8601 departure time: the ETAs count from it, and without speeds everything is judged at it. Default: the route's departure (the viewer's view time), else now.")] DateTimeOffset? Departure = null,
    [property: Description("Longest stretch one clearance sample stands for, in metres (default 50). Each sample keeps the shoalest depth of its stretch.")] double? SampleSpacingMeters = null,
    [property: Description("Include the clearance's per-leg results and per-sample values (clearance.legs, clearance.samples).")] bool IncludeSamples = false,
    [property: Description("Most samples to return when includeSamples is set (default 500); extra samples are thinned, keeping the tightest of each run.")] int MaxSamples = 500,
    [property: Description("Sweep departures for the departure window (clearance.window): from windowFrom to windowTo when given, else across the whole S-104 time range that covers the route, as SoundCharts' departure band does (over a long forecast the step widens to keep the sweep under 2000 departures). Needs a draft.")] bool IncludeWindow = false,
    [property: Description("With includeWindow and windowTo, the first departure of the sweep.")] DateTimeOffset? WindowFrom = null,
    [property: Description("With includeWindow and windowFrom, the last departure of the sweep (inclusive).")] DateTimeOffset? WindowTo = null,
    [property: Description("Minutes between swept departures (default 5).")] int WindowStepMinutes = 5);

/// <summary>
/// Checks a route with <see cref="RouteChecker"/> against the loaded data:
/// the charts (land, drying ground, the safety contour, dangers and chart
/// coverage), regulated and protected areas, S-124 warnings, and, with a
/// draft, the under-keel clearance. Returns the whole <see cref="RouteCheckReport"/>.
/// </summary>
/// <remarks>
/// Takes a host route (the viewer's active or a named route, with each leg's
/// cross-track limits and vessel values) or a polyline. The findings and
/// codes are the ones the viewer's findings list (<c>get_findings</c>) shows.
/// Not for navigation.
/// </remarks>
public sealed class CheckRouteTool
{
    /// <summary>Tool name used in error payloads.</summary>
    public const string Name = "check_route";

    private const int MaxWindowDepartures = 2000;

    private readonly IDatasetCatalog _catalog;
    private readonly IPassageRouteProvider? _routes;
    private readonly RouteCheckSourceCache _sources;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private (RouteCheckSources Sources, RouteCheckOptions Options, RouteChecker Checker)? _checker;

    /// <summary>Creates the tool.</summary>
    /// <param name="catalog">The loaded datasets.</param>
    /// <param name="routes">The host's routes, or <c>null</c> when the host has none (polylines only).</param>
    /// <param name="sources">
    /// The sources shared with the host's other route checks, or <c>null</c>
    /// for the tool's own over ProjNet.
    /// </param>
    /// <param name="time">The clock for "now", or <c>null</c> for the system clock.</param>
    public CheckRouteTool(IDatasetCatalog catalog, IPassageRouteProvider? routes = null, RouteCheckSourceCache? sources = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _routes = routes;
        _sources = sources ?? new RouteCheckSourceCache(new ProjNetCrsTransformFactory());
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Executes the tool.</summary>
    public async Task<ToolResult<CheckRouteResult>> InvokeAsync(CheckRouteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (Validate(request) is { } invalid)
        {
            return ToolResult<CheckRouteResult>.Err(invalid);
        }

        string? routeId = null;
        string? routeName = null;
        var legs = new List<PassageLeg>();
        DateTimeOffset? departure;
        if (request.Polyline is { } polyline)
        {
            if (GeoQueryValidator.Validate(new GeoQuery.Polyline(polyline)) is { } geometryError)
            {
                return ToolResult<CheckRouteResult>.Err(geometryError);
            }

            var half = polyline.CorridorWidthMeters is { } width ? Length.FromMetres(width) : (Length?)null;
            var geometry = request.GreatCircle ? LegGeometry.GreatCircle : LegGeometry.Rhumb;
            for (var i = 1; i < polyline.Vertices.Count; i++)
            {
                var a = polyline.Vertices[i - 1];
                var b = polyline.Vertices[i];
                legs.Add(new PassageLeg
                {
                    Points = [new GeoPosition(a.Latitude, a.Longitude), new GeoPosition(b.Latitude, b.Longitude)],
                    Geometry = geometry,
                    PortLimit = half,
                    StarboardLimit = half,
                    Draft = request.DraftMeters is { } draft ? Depth.FromMetres(draft) : null,
                    Speed = request.SpeedKnots is { } speed ? Speed.FromKnots(speed) : null,
                    Squat = Depth.FromMetres(request.SquatMeters ?? 0),
                    RequiredUkc = request.RequiredUkcMeters is { } ukc ? Depth.FromMetres(ukc) : null,
                    Name = $"Leg {i} → {i + 1}",
                });
            }

            departure = request.Departure;
        }
        else
        {
            if (_routes is null)
            {
                return ToolResult<CheckRouteResult>.Err(new InvalidArgument("polyline", "this host has no routes; pass a polyline"));
            }

            var lookup = await _routes.GetRouteAsync(request.RouteId, cancellationToken).ConfigureAwait(false);
            if (lookup.TryGetError(out var routeError))
            {
                return ToolResult<CheckRouteResult>.Err(routeError);
            }

            lookup.TryGetValue(out var route);
            if (route!.Legs.Count == 0)
            {
                return ToolResult<CheckRouteResult>.Err(new InvalidArgument("routeId", "the route needs at least two waypoints"));
            }

            // The request's vessel values replace every leg's; anything it
            // leaves unset keeps the leg's own.
            legs.AddRange(route.Legs.Select(l => l.ToPassageLeg(request.DraftMeters, request.SpeedKnots, request.SquatMeters, request.RequiredUkcMeters)));
            routeId = route.RouteId;
            routeName = route.Name;
            departure = request.Departure ?? route.DefaultDeparture;
        }

        var plan = new PassagePlan(legs, departure);
        var settings = new RouteCheckSettings
        {
            At = departure ?? _time.GetUtcNow(),
            SafetyContour = request.SafetyContourMeters is { } contour ? Depth.FromMetres(contour) : null,
            SafetyDepth = request.SafetyDepthMeters is { } safetyDepth ? Depth.FromMetres(safetyDepth) : null,
            CorridorOverride = request.CorridorHalfWidthMeters is { } corridor ? Length.FromMetres(corridor) : null,
            DepartureWindow = !request.IncludeWindow ? null
                : request.WindowFrom is { } from && request.WindowTo is { } to ? DepartureRange.Between(from, to, TimeSpan.FromMinutes(request.WindowStepMinutes))
                : DepartureRange.WaterLevelData(TimeSpan.FromMinutes(request.WindowStepMinutes), MaxWindowDepartures),
        };

        var options = request.SampleSpacingMeters is { } spacing
            ? RouteCheckOptions.Default with { Clearance = PassageCheckOptions.Default with { SampleSpacingMeters = spacing } }
            : RouteCheckOptions.Default;
        var checker = Checker(_sources.Get(_catalog.Datasets), options);
        RouteCheckReport report;
        try
        {
            report = await Task.Run(() => checker.Check(plan, settings, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            return ToolResult<CheckRouteResult>.Err(new InvalidArgument("polyline", ex.Message));
        }

        return ToolResult<CheckRouteResult>.Ok(CheckRouteResult.From(report, routeId, routeName, request.IncludeSamples, request.MaxSamples));
    }

    /// <summary>
    /// The checker for the loaded data. One is kept while the data and
    /// options stay the same, so its checks reuse what the last run sampled.
    /// </summary>
    private RouteChecker Checker(RouteCheckSources sources, RouteCheckOptions options)
    {
        lock (_gate)
        {
            if (_checker is { } last && ReferenceEquals(last.Sources, sources) && last.Options == options)
            {
                return last.Checker;
            }

            var checker = new RouteChecker(sources, options);
            _checker = (sources, options, checker);
            return checker;
        }
    }

    private static InvalidArgument? Validate(CheckRouteRequest request)
    {
        static bool Bad(double? v, bool allowZero = true) =>
            v is { } x && (!double.IsFinite(x) || x < 0 || (!allowZero && x == 0));

        if (Bad(request.CorridorHalfWidthMeters)) return new InvalidArgument("corridorHalfWidthMeters", "must be zero or more metres");
        if ((request.SafetyContourMeters is null) != (request.SafetyDepthMeters is null)) return new InvalidArgument("safetyDepthMeters", "safetyContourMeters and safetyDepthMeters go together");
        if (Bad(request.SafetyContourMeters)) return new InvalidArgument("safetyContourMeters", "must be zero or more metres");
        if (Bad(request.SafetyDepthMeters)) return new InvalidArgument("safetyDepthMeters", "must be zero or more metres");
        if (Bad(request.DraftMeters, allowZero: false)) return new InvalidArgument("draftMeters", "must be a positive number of metres");
        if (Bad(request.SpeedKnots, allowZero: false)) return new InvalidArgument("speedKnots", "must be a positive number of knots");
        if (Bad(request.SquatMeters)) return new InvalidArgument("squatMeters", "must be zero or more metres");
        if (Bad(request.RequiredUkcMeters)) return new InvalidArgument("requiredUkcMeters", "must be zero or more metres");
        if (Bad(request.SampleSpacingMeters, allowZero: false) || request.SampleSpacingMeters is < 1) return new InvalidArgument("sampleSpacingMeters", "must be at least 1 m");
        if (request.MaxSamples < 1) return new InvalidArgument("maxSamples", "must be at least 1");
        if ((request.WindowFrom is null) != (request.WindowTo is null)) return new InvalidArgument("windowTo", "windowFrom and windowTo go together");
        if (request.WindowFrom is not null && !request.IncludeWindow) return new InvalidArgument("includeWindow", "windowFrom and windowTo need includeWindow");
        if (request.WindowStepMinutes < 1) return new InvalidArgument("windowStepMinutes", "must be at least 1");
        if (request.WindowFrom is { } f && request.WindowTo is { } t)
        {
            if (t < f) return new InvalidArgument("windowTo", "must not be before windowFrom");
            if ((t - f).TotalMinutes / request.WindowStepMinutes > MaxWindowDepartures) return new InvalidArgument("windowStepMinutes", "the sweep would exceed 2000 departures");
        }

        return null;
    }
}
