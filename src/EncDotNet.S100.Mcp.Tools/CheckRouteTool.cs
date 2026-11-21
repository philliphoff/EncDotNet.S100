using System.ComponentModel;
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
/// <param name="GreatCircle">For a polyline, run legs as great circles instead of rhumb lines.</param>
/// <param name="Departure">The departure the ETAs count from, and the time to judge at without speeds.</param>
/// <param name="Vessel">The vessel and its safety settings; each value set replaces every leg's own.</param>
/// <param name="Window">With a value, sweep departures for the departure window.</param>
/// <param name="Weather">The forecast weather and tropical cyclone settings.</param>
/// <param name="Limits">Distances and kinds that tune the chart and area checks.</param>
/// <param name="Samples">The clearance sampling, and whether the samples are returned.</param>
public sealed record CheckRouteRequest(
    [property: Description("Id of a host route to check (viewer only). Omit, with no polyline, for the active route.")] string? RouteId = null,
    [property: Description("A route given directly: each pair of consecutive vertices is a leg. Takes precedence over routeId. Its corridorWidthMeters, when set, is the half-width either side of the track.")] GeoPolyline? Polyline = null,
    [property: Description("For a polyline, run legs as great circles instead of rhumb lines.")] bool GreatCircle = false,
    [property: Description("ISO 8601 departure time: the ETAs count from it, and without speeds everything is judged at it. Default: the route's departure (the viewer's view time), else now.")] DateTimeOffset? Departure = null,
    [property: Description("The vessel and its safety settings. Each value set replaces every leg's own.")] VesselArguments? Vessel = null,
    [property: Description("Sweep departures for the departure window (clearance.window); {} sweeps the S-104 range over the route. Needs a draft.")] DepartureWindowArguments? Window = null,
    [property: Description("The weather check: forecast limits and tropical cyclones.")] WeatherCheckArguments? Weather = null,
    [property: Description("Distances and kinds that tune the chart and area checks.")] RouteCheckLimits? Limits = null,
    [property: Description("Clearance sampling, and whether samples are returned.")] ClearanceSamplesArguments? Samples = null);

/// <summary>The departure sweep of a <see cref="CheckRouteRequest"/>.</summary>
/// <param name="From">With <paramref name="To"/>, the first departure of the sweep.</param>
/// <param name="To">The last departure of the sweep.</param>
/// <param name="StepMinutes">Minutes between swept departures.</param>
/// <param name="Constraints">Passage timing constraints: with them the timing check judges every swept departure.</param>
public sealed record DepartureWindowArguments(
    [property: Description("With to, the first departure of the sweep. Without from and to, the sweep covers the S-104 range that covers the route (over a long forecast the step widens to keep it under 2000 departures).")] DateTimeOffset? From = null,
    [property: Description("With from, the last departure of the sweep (inclusive).")] DateTimeOffset? To = null,
    [property: Description("Minutes between swept departures (default 5).")] int StepMinutes = 5,
    [property: Description("Passage timing (the timing check): currents applied to the ETAs, tidal gates, leg current limits and arrival constraints, judged at the planned departure and every swept departure, with the windows that meet them all (result: timing). Without from and to the timing sweep covers the 24 hours from departure.")] WindowConstraintsArguments? Constraints = null);

/// <summary>The weather check settings of a <see cref="CheckRouteRequest"/>.</summary>
/// <param name="MaxWindKnots">The wind limit, in knots; replaces a route's own.</param>
/// <param name="MaxWaveHeightMeters">The significant wave height limit, in metres; replaces a route's own.</param>
/// <param name="Cyclones">How loaded tropical cyclone advisories are judged.</param>
public sealed record WeatherCheckArguments(
    [property: Description("Wind limit in knots: stretches where a loaded GRIB2 or NetCDF forecast has a higher 10 m wind at the ETA are weather.wind_above_limit (check). Replaces the route's own (RTZ vesselMaxWind). SoundCharts uses the mariner's route weather limits (get_mariner_settings).")] double? MaxWindKnots = null,
    [property: Description("Significant wave height limit in metres (weather.waves_above_limit). Replaces the route's own (RTZ vesselMaxWave).")] double? MaxWaveHeightMeters = null,
    [property: Description("Loaded NHC tropical cyclone advisories. SoundCharts uses the mariner's settings (get_route_weather_settings).")] CycloneCheckArguments? Cyclones = null);

/// <summary>The tropical cyclone settings of a <see cref="WeatherCheckArguments"/>.</summary>
/// <param name="CheckAgainstKnots">The wind radius tested (34, 50 or 64); default 34.</param>
/// <param name="ReportClosestApproach">Whether to report the closest approach to each storm; default on.</param>
/// <param name="MustReviewInside">The weakest radius that is must review ("34", "50", "64" or "never"); default "50".</param>
public sealed record CycloneCheckArguments(
    [property: Description("The outermost wind radius tested at each ETA, in knots: 34 (default), 50 or 64. Stretches inside it and every stronger one are weather.tc_radius_34/_50/_64; the cone (weather.tc_cone) is always tested.")] int? CheckAgainstKnots = null,
    [property: Description("Report the route's closest approach to each storm's centre (weather.tc_cpa, info). Default true.")] bool? ReportClosestApproach = null,
    [property: Description("The weakest wind radius whose findings are must_review: \"64\", \"50\" (default), \"34\" or \"never\". Weaker radii and the cone are check. A radius outside checkAgainstKnots acts as checkAgainstKnots.")] string? MustReviewInside = null);

/// <summary>The distances and kinds that tune a <see cref="CheckRouteRequest"/>'s chart and area checks.</summary>
/// <param name="CorridorHalfWidthMeters">The corridor's half-width either side of the track, overriding every leg's cross-track limits.</param>
/// <param name="TrackClearanceMeters">How close to the track line an aid, structure or overhead feature counts as on the track; default 30 m.</param>
/// <param name="NearbyDistanceMeters">How far beyond the cross-track limits a danger or hazard is near the route; default 185.2 m.</param>
/// <param name="BerthRadiusMeters">How far from the departure and arrival structures that raise the SafetyContour alert are grouped as berth structures; default 100 m, 0 off.</param>
/// <param name="AreaKindsOff">The kinds of prohibited area and area with special conditions whose indication is off, by name.</param>
public sealed record RouteCheckLimits(
    [property: Description("The corridor's HALF-width in metres: how far it reaches to port, and to starboard, of the track (so the corridor is twice this wide). Overrides every leg's cross-track limits. Without it a route uses each leg's port/starboard XTD, and anything unset uses 50 m.")] double? CorridorHalfWidthMeters = null,
    [property: Description("How close to the track line, in metres, an aid to navigation, structure or overhead feature counts as on the track (default 30). A structure on the track is must review.")] double? TrackClearanceMeters = null,
    [property: Description("How far beyond the cross-track limits, in metres, a danger or hazard counts as near the route (default 185.2, 0.1 NM).")] double? NearbyDistanceMeters = null,
    [property: Description("How far from the departure and arrival waypoints, in metres, structures that raise the SafetyContour alert count as the berth (a breakwater, ruin or other non-berth structure the track line cuts stays a crossing; pontoons, floating docks, piers and wharves never do) (default 100; 0 turns it off). They are one route_check.berth_structures finding (info) per end instead of crossings. Pontoons, piers and wharves within 60 m of one already grouped join it, out to 4 times the radius, so a large marina's docks count as one berth.")] double? BerthRadiusMeters = null,
    [property: Description("Kinds of prohibited area and area with special conditions to turn off (S-101 alert catalogue ProhAre kinds): traffic_separation_zone, inshore_traffic_zone, restricted_area, area_to_be_avoided, particularly_sensitive_sea_area, caution_area, offshore_production_area, user_area_to_be_avoided, military_practice_area, seaplane_landing_area, submarine_transit_lane, anchorage_area, marine_farm. Their areas aren't reported; one route_check.area_kind_off finding per kind says the route meets them. Default: every kind on.")] IReadOnlyList<string>? AreaKindsOff = null);

/// <summary>The clearance sampling of a <see cref="CheckRouteRequest"/>.</summary>
/// <param name="SpacingMeters">The longest stretch one clearance sample stands for; default 50 m.</param>
/// <param name="Include">Include the clearance's per-leg results and samples.</param>
/// <param name="Max">The most samples to return when <paramref name="Include"/> is set.</param>
public sealed record ClearanceSamplesArguments(
    [property: Description("Longest stretch one clearance sample stands for, in metres (default 50). Each sample keeps the shoalest depth of its stretch.")] double? SpacingMeters = null,
    [property: Description("Include the clearance's per-leg results and per-sample values (clearance.legs, clearance.samples).")] bool Include = false,
    [property: Description("Most samples to return when include is set (default 500); extra samples are thinned, keeping the tightest of each run.")] int Max = 500);

/// <summary>
/// Checks a route with <see cref="RouteChecker"/> against the loaded data:
/// the charts (land, drying ground, the safety contour, dangers and chart
/// coverage), regulated and protected areas, S-124 warnings, with a draft
/// the under-keel clearance, and with wind or wave limits the forecast
/// weather along the route. Returns the whole <see cref="RouteCheckReport"/>.
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
    private (RouteCheckSources Sources, RouteCheckOptions Options, bool Timing, RouteChecker Checker)? _checker;

    /// <summary>Creates the tool.</summary>
    /// <param name="catalog">The loaded datasets.</param>
    /// <param name="routes">The host's routes, or <c>null</c> when the host has none (polylines only).</param>
    /// <param name="sources">
    /// The sources shared with the host's other route checks.
    /// </param>
    /// <param name="time">The clock for "now".</param>
    public CheckRouteTool(IDatasetCatalog catalog, IPassageRouteProvider? routes, RouteCheckSourceCache sources, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _routes = routes;
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(time);
        _sources = sources;
        _time = time;
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
        WeatherLimits? vesselLimits = null;
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
            var vessel = request.Vessel;
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
                    Draft = vessel?.DraftMeters is { } draft ? Depth.FromMetres(draft) : null,
                    Speed = vessel?.SpeedKnots is { } speed ? Speed.FromKnots(speed) : null,
                    Squat = Depth.FromMetres(vessel?.SquatMeters ?? 0),
                    RequiredUkc = vessel?.RequiredUkcMeters is { } ukc ? Depth.FromMetres(ukc) : null,
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
            legs.AddRange(route.Legs.Select(l => l.ToPassageLeg(request.Vessel?.DraftMeters, request.Vessel?.SpeedKnots, request.Vessel?.SquatMeters, request.Vessel?.RequiredUkcMeters)));
            routeId = route.RouteId;
            routeName = route.Name;
            vesselLimits = route.VesselLimits;
            departure = request.Departure ?? route.DefaultDeparture;
        }

        var plan = new PassagePlan(legs, departure) { VesselLimits = vesselLimits };
        var settings = BuildSettings(request, departure ?? _time.GetUtcNow());

        var options = request.Samples?.SpacingMeters is { } spacing
            ? RouteCheckOptions.Default with { Clearance = PassageCheckOptions.Default with { SampleSpacingMeters = spacing } }
            : RouteCheckOptions.Default;
        var checker = Checker(_sources.Get(_catalog.Datasets), options, settings.Timing is not null);
        RouteCheckReport report;
        try
        {
            report = await Task.Run(() => checker.Check(plan, settings, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            return ToolResult<CheckRouteResult>.Err(new InvalidArgument("polyline", ex.Message));
        }

        return ToolResult<CheckRouteResult>.Ok(CheckRouteResult.From(report, routeId, routeName, request.Samples?.Include ?? false, request.Samples?.Max ?? 500));
    }

    // The one mapping from a request to the settings: every host's
    // check_route reaches it through this tool.
    private static RouteCheckSettings BuildSettings(CheckRouteRequest request, DateTimeOffset at)
    {
        var vessel = request.Vessel;
        var limits = request.Limits;
        var weather = request.Weather;
        var cyclones = weather?.Cyclones;
        var window = request.Window;
        return new()
        {
            At = at,
            SafetyContour = vessel?.SafetyContourMeters is { } contour ? Depth.FromMetres(contour) : null,
            SafetyDepth = vessel?.SafetyDepthMeters is { } safetyDepth ? Depth.FromMetres(safetyDepth) : null,
            CorridorOverride = limits?.CorridorHalfWidthMeters is { } corridor ? Length.FromMetres(corridor) : null,
            TrackClearance = limits?.TrackClearanceMeters is { } clearance ? Length.FromMetres(clearance) : null,
            NearbyDistance = limits?.NearbyDistanceMeters is { } nearby ? Length.FromMetres(nearby) : null,
            BerthRadius = limits?.BerthRadiusMeters is { } berth ? Length.FromMetres(berth) : null,
            AreaKindsOff = limits?.AreaKindsOff is { } off ? off.Select(name => RouteAreaKinds.Parse(name)!.Value).ToList() : [],
            WeatherLimits = weather?.MaxWindKnots is null && weather?.MaxWaveHeightMeters is null ? null : new WeatherLimits
            {
                MaxWind = weather.MaxWindKnots is { } wind ? Speed.FromKnots(wind) : null,
                MaxWaveHeight = weather.MaxWaveHeightMeters is { } wave ? Length.FromMetres(wave) : null,
            },
            TropicalCyclones = cyclones is null || (cyclones.CheckAgainstKnots is null && cyclones.ReportClosestApproach is null && cyclones.MustReviewInside is null)
                ? null
                : new TropicalCycloneCheckSettings
                {
                    CheckAgainst = cyclones.CheckAgainstKnots is { } against ? CycloneRadius(against)!.Value : TropicalCycloneRadius.Knots34,
                    ReportClosestApproach = cyclones.ReportClosestApproach ?? true,
                    MustReviewInside = cyclones.MustReviewInside is { } inside ? MustReviewInside(inside) : TropicalCycloneRadius.Knots50,
                },
            Timing = window?.Constraints is { } constraints ? Timing(constraints) : null,
            DepartureWindow = window is null ? null
                : window.From is { } from && window.To is { } to ? DepartureRange.Between(from, to, TimeSpan.FromMinutes(window.StepMinutes))
                : DepartureRange.WaterLevelData(TimeSpan.FromMinutes(window.StepMinutes), MaxWindowDepartures),
        };
    }

    private static PassageTimingSettings Timing(WindowConstraintsArguments constraints) => new()
    {
        ApplyCurrents = constraints.ApplyCurrents,
        Gates = [.. (constraints.Gates ?? []).Select(g => new TimingGate
        {
            Name = g.Name,
            WaypointIndex = g.Waypoint,
            Position = g.Latitude is { } lat && g.Longitude is { } lon ? new GeoPosition(lat, lon) : null,
            Station = g.Station,
            SlackWithin = g.SlackWithinMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null,
            MaxCurrent = g.MaxCurrentKnots is { } max ? Speed.FromKnots(max) : null,
            FairOnly = g.FairOnly,
        })],
        LegLimits = [.. (constraints.LegLimits ?? []).Select(l => new LegCurrentLimit { LegIndex = l.Leg, MaxOpposing = Speed.FromKnots(l.MaxOpposingKnots) })],
        ArriveBy = constraints.ArriveBy,
        ArriveNotBefore = constraints.ArriveNotBefore,
        ArrivalTide = constraints.ArrivalTide is { } tide ? new ArrivalTideRange { MinMeters = tide.MinMeters, MaxMeters = tide.MaxMeters } : null,
    };

    /// <summary>
    /// The checker for the loaded data. One is kept while the data and
    /// options stay the same, so its checks reuse what the last run sampled.
    /// </summary>
    private RouteChecker Checker(RouteCheckSources sources, RouteCheckOptions options, bool timing)
    {
        lock (_gate)
        {
            if (_checker is { } last && ReferenceEquals(last.Sources, sources) && last.Options == options && last.Timing == timing)
            {
                return last.Checker;
            }

            // The timing check runs only when asked for, so other reports keep their five checks.
            var checker = new RouteChecker(sources, options, timing ? [new TimingCheck()] : null);
            _checker = (sources, options, timing, checker);
            return checker;
        }
    }

    /// <summary>A wind radius from its knots, or <c>null</c> for anything but 34, 50 and 64.</summary>
    /// <param name="knots">The knots.</param>
    /// <returns>The radius, or <c>null</c>.</returns>
    public static TropicalCycloneRadius? CycloneRadius(int knots) => knots switch
    {
        34 => TropicalCycloneRadius.Knots34,
        50 => TropicalCycloneRadius.Knots50,
        64 => TropicalCycloneRadius.Knots64,
        _ => null,
    };

    // "never" or a radius's knots, already validated.
    private static TropicalCycloneRadius? MustReviewInside(string value) =>
        string.Equals(value, "never", StringComparison.OrdinalIgnoreCase)
            ? null
            : CycloneRadius(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture));

    private static InvalidArgument? Validate(CheckRouteRequest request)
    {
        if (request.Limits is { } limits)
        {
            if (Arguments.Negative(limits.CorridorHalfWidthMeters)) return new InvalidArgument("limits.corridorHalfWidthMeters", "must be zero or more metres");
            if (Arguments.Negative(limits.TrackClearanceMeters)) return new InvalidArgument("limits.trackClearanceMeters", "must be zero or more metres");
            if (Arguments.Negative(limits.NearbyDistanceMeters)) return new InvalidArgument("limits.nearbyDistanceMeters", "must be zero or more metres");
            if (Arguments.Negative(limits.BerthRadiusMeters)) return new InvalidArgument("limits.berthRadiusMeters", "must be zero or more metres");
            if (limits.AreaKindsOff?.FirstOrDefault(name => RouteAreaKinds.Parse(name) is null) is { } unknown)
                return new InvalidArgument("limits.areaKindsOff", $"'{unknown}' isn't a kind: {string.Join(", ", RouteAreaKinds.Names.Values)}");
        }

        if (request.Vessel?.Validate() is { } vesselProblem)
            return new InvalidArgument("vessel." + vesselProblem.Name, vesselProblem.Problem);

        if (request.Weather is { } weather)
        {
            if (Arguments.NotPositive(weather.MaxWindKnots)) return new InvalidArgument("weather.maxWindKnots", "must be a positive number of knots");
            if (Arguments.NotPositive(weather.MaxWaveHeightMeters)) return new InvalidArgument("weather.maxWaveHeightMeters", "must be a positive number of metres");
            if (weather.Cyclones?.CheckAgainstKnots is { } against && CycloneRadius(against) is null)
                return new InvalidArgument("weather.cyclones.checkAgainstKnots", "must be 34, 50 or 64");
            if (weather.Cyclones?.MustReviewInside is { } inside && !string.Equals(inside, "never", StringComparison.OrdinalIgnoreCase)
                && !(int.TryParse(inside, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var knots) && CycloneRadius(knots) is not null))
                return new InvalidArgument("weather.cyclones.mustReviewInside", "must be \"34\", \"50\", \"64\" or \"never\"");
        }

        if (request.Samples is { } samples)
        {
            if (Arguments.NotPositive(samples.SpacingMeters) || samples.SpacingMeters is < 1) return new InvalidArgument("samples.spacingMeters", "must be at least 1 m");
            if (samples.Max < 1) return new InvalidArgument("samples.max", "must be at least 1");
        }

        if (request.Window is { } window)
        {
            if ((window.From is null) != (window.To is null)) return new InvalidArgument("window.to", "from and to go together");
            if (window.StepMinutes < 1) return new InvalidArgument("window.stepMinutes", "must be at least 1");
            if (window.Constraints is { } constraints && ValidateConstraints(constraints) is { } constraintError) return constraintError;
            if (window.From is { } f && window.To is { } t)
            {
                if (t < f) return new InvalidArgument("window.to", "must not be before from");
                if ((t - f).TotalMinutes / window.StepMinutes > MaxWindowDepartures) return new InvalidArgument("window.stepMinutes", "the sweep would exceed 2000 departures");
            }
        }

        return null;
    }

    private static InvalidArgument? ValidateConstraints(WindowConstraintsArguments constraints)
    {
        const string prefix = "window.constraints.";
        var gates = constraints.Gates ?? [];
        for (var i = 0; i < gates.Count; i++)
        {
            var g = gates[i];
            var at = $"{prefix}gates[{i}].";
            if (g is null) return new InvalidArgument($"{prefix}gates", $"gate {i} is null");
            var hasPosition = g.Latitude is not null && g.Longitude is not null;
            if ((g.Waypoint is not null) == hasPosition) return new InvalidArgument(at + "waypoint", "give a waypoint, or a latitude and longitude");
            if (g.Waypoint is < 0) return new InvalidArgument(at + "waypoint", "must be zero or more");
            if (g.Latitude is { } lat && (!double.IsFinite(lat) || Math.Abs(lat) > 90)) return new InvalidArgument(at + "latitude", "must be in [-90, 90]");
            if (g.Longitude is { } lon && (!double.IsFinite(lon) || Math.Abs(lon) > 180)) return new InvalidArgument(at + "longitude", "must be in [-180, 180]");
            if (Arguments.NotPositive(g.SlackWithinMinutes)) return new InvalidArgument(at + "slackWithinMinutes", "must be a positive number of minutes");
            if (Arguments.Negative(g.MaxCurrentKnots)) return new InvalidArgument(at + "maxCurrentKnots", "must be zero or more knots");
            if (g.SlackWithinMinutes is null && g.MaxCurrentKnots is null && !g.FairOnly) return new InvalidArgument(at + "slackWithinMinutes", "a gate needs slackWithinMinutes, maxCurrentKnots or fairOnly");
        }

        var limits = constraints.LegLimits ?? [];
        for (var i = 0; i < limits.Count; i++)
        {
            if (limits[i] is null) return new InvalidArgument($"{prefix}legLimits", $"limit {i} is null");
            if (limits[i].Leg is < 0) return new InvalidArgument($"{prefix}legLimits[{i}].leg", "must be zero or more");
            if (Arguments.Negative(limits[i].MaxOpposingKnots)) return new InvalidArgument($"{prefix}legLimits[{i}].maxOpposingKnots", "must be zero or more knots");
        }

        if (constraints.ArriveBy is { } by && constraints.ArriveNotBefore is { } notBefore && by < notBefore)
            return new InvalidArgument(prefix + "arriveBy", "must not be before arriveNotBefore");
        if (constraints.ArrivalTide is { } tide)
        {
            if (tide.MinMeters is null && tide.MaxMeters is null) return new InvalidArgument(prefix + "arrivalTide", "give minMeters, maxMeters or both");
            if (tide.MinMeters is { } min && tide.MaxMeters is { } max && max < min) return new InvalidArgument(prefix + "arrivalTide.maxMeters", "must not be below minMeters");
        }

        return null;
    }
}
