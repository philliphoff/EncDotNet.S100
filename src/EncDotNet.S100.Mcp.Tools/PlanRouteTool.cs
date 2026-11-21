using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>A <c>plan_route</c> request.</summary>
/// <param name="From">The departure.</param>
/// <param name="To">The arrival.</param>
/// <param name="Via">Stops in between, in order: with them the route runs through every stop.</param>
/// <param name="Vessel">The vessel and its safety settings, as <c>check_route</c> takes them.</param>
/// <param name="ClearanceMeters">The least room the route keeps from land, shoal water, dangers and structures away from the stops.</param>
/// <param name="Prefer">Charted water to prefer: <c>fairways</c>, <c>dredged_areas</c>, <c>recommended_tracks</c>.</param>
/// <param name="Check">Whether to check the planned route and summarise the check.</param>
/// <param name="RouteName">In SoundCharts, create a route of this name from the plan.</param>
/// <param name="Departure">The departure time the check judges at, and the ETAs count from.</param>
/// <param name="Limits">The planner's distances and grid.</param>
public sealed record PlanRouteRequest(
    [property: Description("The departure, {\"latitude\":…,\"longitude\":…}. Moved to navigable water when it isn't (the result says how far).")] GeoPoint From,
    [property: Description("The arrival, {\"latitude\":…,\"longitude\":…}.")] GeoPoint To,
    [property: Description("Stops in between, in order. With them the route runs through every stop: a whole itinerary in one call, with each leg's distance.")] IReadOnlyList<GeoPoint>? Via = null,
    [property: Description("The vessel and its safety settings, the same object check_route takes. The route keeps to water at least the larger of draft + squat + required UKC and the safety contour deep; near the stops, down to draft + squat. Give a draft, a safety contour, or both; speedKnots adds running times.")] VesselArguments? Vessel = null,
    [property: Description("The least room in metres the route keeps from land, shoal water, dangers, structures and uncharted water, away from the stops (default 0: any navigable water; the route still prefers open water).")] double? ClearanceMeters = null,
    [property: Description("Charted water to prefer where it goes the right way: \"fairways\", \"dredged_areas\" and \"recommended_tracks\" (S-101 Fairway, DredgedArea and RecommendedTrack). Default: none.")] IReadOnlyList<string>? Prefer = null,
    [property: Description("Check the planned route with check_route (same vessel and departure) and return a short summary: the verdict, counts by group, the must-review findings away from the stops and the worst findings. Default true. Run check_route on 'polyline' for the full report.")] bool Check = true,
    [property: Description("SoundCharts only: create a route of this name from the plan, make it active, and return its routeId. Without it, 'waypoints' are in the shape append_waypoint takes.")] string? RouteName = null,
    [property: Description("ISO 8601 departure: the check's ETAs count from it. Default now.")] DateTimeOffset? Departure = null,
    [property: Description("The planner's distances and grid.")] PlanRouteLimits? Limits = null);

/// <summary>The planner's distances and grid in a <see cref="PlanRouteRequest"/>.</summary>
/// <param name="BerthRadiusMeters">Within this of a stop, water down to draft + squat is allowed.</param>
/// <param name="SnapRadiusMeters">How far a stop may move to navigable water.</param>
/// <param name="ComfortMeters">Closer than this to anything not navigable costs more.</param>
/// <param name="CellMeters">The grid's cell size.</param>
public sealed record PlanRouteLimits(
    [property: Description("Within this many metres of a stop, water down to draft + squat is allowed and the clearance isn't required, so the route can reach a berth or anchorage (default 300).")] double? BerthRadiusMeters = null,
    [property: Description("How far in metres a stop that isn't navigable may move to navigable water (default 5000, at most 20000).")] double? SnapRadiusMeters = null,
    [property: Description("Closer than this many metres to anything not navigable costs more, so the route keeps to open water where there's room (default 200).")] double? ComfortMeters = null,
    [property: Description("The grid's cell size in metres (default 20, coarser for a long leg so its grid stays under 3,000,000 cells).")] double? CellMeters = null);

/// <summary>A stop of a <see cref="PlanRouteResult"/>.</summary>
public sealed record PlanRouteStop(
    [property: Description("The stop's index: 0 is 'from', then the via points, then 'to'.")] int Index,
    [property: Description("\"from\", \"via\" or \"to\".")] string Role,
    [property: Description("The position asked for, [latitude, longitude].")] double[] Requested,
    [property: Description("The position the route uses.")] double Latitude,
    [property: Description("The position the route uses.")] double Longitude,
    [property: Description("Whether the stop moved to navigable water.")] bool Moved,
    [property: Description("Why it moved: land, uncharted, shoal, hazard, no_depth or too_close; null when it didn't.")] string? Reason = null,
    [property: Description("How far it moved, in metres.")] double? MovedMeters = null,
    [property: Description("The bearing it moved on, degrees true.")] double? BearingDegrees = null,
    [property: Description("The least charted depth round the position used, in metres.")] double? DepthMeters = null);

/// <summary>A leg of a <see cref="PlanRouteResult"/>.</summary>
public sealed record PlanRouteLegResult(
    [property: Description("The stop it starts at.")] int From,
    [property: Description("The stop it ends at.")] int To,
    [property: Description("Its length in nautical miles.")] double DistanceNm,
    [property: Description("Its running time in minutes at vessel.speedKnots, with no current; null without a speed.")] double? RunningMinutes,
    [property: Description("Index of its first waypoint in 'waypoints'.")] int FirstWaypoint,
    [property: Description("Index of its last waypoint in 'waypoints'.")] int LastWaypoint,
    [property: Description("The least charted depth along it away from the stops, in metres.")] double? LeastDepthMeters,
    [property: Description("The least room along it away from the stops, in metres.")] double? LeastClearanceMeters,
    [property: Description("The grid cell size it was planned on, in metres.")] double CellMeters);

/// <summary>A waypoint, in the shape <c>append_waypoint</c> takes.</summary>
public sealed record PlanRouteWaypoint(
    [property: Description("Latitude, decimal degrees WGS-84.")] double Lat,
    [property: Description("Longitude, decimal degrees WGS-84.")] double Lon,
    [property: Description("The stop's name (\"from\", \"via 1\", \"to\") when the waypoint is a stop; null otherwise.")] string? Name = null);

/// <summary>A finding in a <see cref="PlanRouteCheck"/>.</summary>
public sealed record PlanRouteFinding(
    [property: Description("The finding's code, as check_route gives it.")] string Code,
    [property: Description("must_review, check or info.")] string Group,
    [property: Description("Its title.")] string Title,
    [property: Description("Where and what.")] string Meta,
    [property: Description("Distance from departure, nautical miles.")] double? DistanceNm);

/// <summary>The short summary of <c>check_route</c> on a planned route.</summary>
public sealed record PlanRouteCheck(
    [property: Description("The verdict kind: must_review, water_level_ends_before_arrival, check or clear.")] string Verdict,
    [property: Description("The verdict in the UI's words.")] string Title,
    [property: Description("Number of must_review findings.")] int MustReview,
    [property: Description("Number of must_review findings farther than the berth radius from every stop: the ones the plan itself is answerable for.")] int MustReviewAwayFromStops,
    [property: Description("Number of check findings.")] int Check,
    [property: Description("Number of info findings.")] int Info,
    [property: Description("Up to 5 findings, worst first (must_review away from the stops first).")] IReadOnlyList<PlanRouteFinding> Worst,
    [property: Description("Checks that didn't fully run, as name: reason.")] IReadOnlyList<string> Partial);

/// <summary>A <c>plan_route</c> result.</summary>
public sealed record PlanRouteResult(
    [property: Description("The route's length in nautical miles.")] double DistanceNm,
    [property: Description("The least charted depth the route keeps to away from the stops, in metres.")] double MinimumDepthMeters,
    [property: Description("The least charted depth allowed near the stops, in metres.")] double BerthDepthMeters,
    [property: Description("The stops, and where the route uses them.")] IReadOnlyList<PlanRouteStop> Stops,
    [property: Description("The legs between consecutive stops.")] IReadOnlyList<PlanRouteLegResult> Legs,
    [property: Description("The route's waypoints in order, in the shape append_waypoint takes.")] IReadOnlyList<PlanRouteWaypoint> Waypoints,
    [property: Description("The route as check_route's polyline argument: JSON text {\"vertices\":[[lat,lon],...]}.")] string Polyline,
    [property: Description("The check's summary; null with check: false.")] PlanRouteCheck? Check,
    [property: Description("With routeName, the id of the route created in SoundCharts.")] string? RouteId,
    [property: Description("What the plan is and isn't.")] string Caveat);

/// <summary>No route could be planned.</summary>
[Description("Raised when plan_route can't plan: a stop has no navigable water near it (stop_not_navigable), or no way through navigable water joins two stops (no_navigable_way).")]
public sealed record NoRoutePlanned(
    [property: Description("stop_not_navigable or no_navigable_way.")] string Reason,
    [property: Description("The stop, for stop_not_navigable.")] int? StopIndex,
    [property: Description("The leg, for no_navigable_way.")] int? LegIndex,
    [property: Description("What to try.")] string Detail) : ToolError("no_route_planned", Detail);

/// <summary>Creates a route in the host, for <c>plan_route</c>'s <c>routeName</c>.</summary>
public interface IPassageRouteWriter
{
    /// <summary>Creates a route from waypoints and makes it active.</summary>
    /// <param name="name">The route's name.</param>
    /// <param name="waypoints">The waypoints, with a name for each stop.</param>
    /// <param name="cancellationToken">Cancels the creation.</param>
    /// <returns>The new route's id, or the host's error.</returns>
    Task<ToolResult<string>> CreateRouteAsync(string name, IReadOnlyList<PlanRouteWaypoint> waypoints, CancellationToken cancellationToken);
}

/// <summary>
/// <c>plan_route</c> (#1560): a suggested route through a list of stops for a
/// vessel, planned on the navigability grid (<see cref="RoutePlanner"/>) and
/// checked with <c>check_route</c>.
/// </summary>
public sealed class PlanRouteTool
{
    /// <summary>The tool's name.</summary>
    public const string Name = "plan_route";

    private const string Caveat =
        "A suggestion from the charted depths (S-102, else the finest S-101/S-57 cell) with no water level applied, kept to water at least " +
        "minimumDepthMeters deep away from the stops. Check it, look at it on the chart, and plan it yourself before using it. Not for navigation.";

    private static readonly ConditionalWeakTable<S101Dataset, PreferredFeatures> Preferred = new();

    private readonly IDatasetCatalog _catalog;
    private readonly RouteCheckSourceCache _sources;
    private readonly CheckRouteTool _check;
    private readonly IPassageRouteWriter? _writer;

    /// <summary>Creates the tool.</summary>
    /// <param name="catalog">The loaded datasets.</param>
    /// <param name="sources">The route-check sources, shared with <c>check_route</c>.</param>
    /// <param name="check">The route check, for the summary.</param>
    /// <param name="writer">Creates routes in the host, or <c>null</c> when the host has none.</param>
    public PlanRouteTool(IDatasetCatalog catalog, RouteCheckSourceCache sources, CheckRouteTool check, IPassageRouteWriter? writer)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(check);
        _catalog = catalog;
        _sources = sources;
        _check = check;
        _writer = writer;
    }

    /// <summary>Plans, checks and optionally creates the route.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the planning.</param>
    /// <returns>The plan, or the error.</returns>
    public async Task<ToolResult<PlanRouteResult>> InvokeAsync(PlanRouteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Validate(request) is { } invalid)
        {
            return ToolResult<PlanRouteResult>.Err(invalid);
        }

        var vessel = request.Vessel!;
        var keel = (vessel.DraftMeters ?? 0) + (vessel.SquatMeters ?? 0);
        var minimum = Math.Max(keel + (vessel.RequiredUkcMeters ?? 0), vessel.SafetyContourMeters ?? double.NegativeInfinity);
        var berthDepth = vessel.DraftMeters is null ? minimum : Math.Min(minimum, keel);
        var stops = new List<GeoPoint> { request.From };
        stops.AddRange(request.Via ?? []);
        stops.Add(request.To);
        var positions = stops.Select(p => new GeoPosition(p.Latitude, p.Longitude)).ToList();

        var datasets = _catalog.Datasets;
        var sources = _sources.Get(datasets);
        var (areas, lines) = PreferredWater(datasets, request.Prefer ?? [], positions);
        var limits = request.Limits ?? new PlanRouteLimits();
        var planRequest = new RoutePlanRequest(positions, minimum)
        {
            ClearanceMeters = request.ClearanceMeters ?? 0,
            ComfortMeters = limits.ComfortMeters ?? RoutePlanRequest.DefaultComfortMeters,
            BerthRadiusMeters = limits.BerthRadiusMeters ?? RoutePlanRequest.DefaultBerthRadiusMeters,
            BerthDepthMeters = berthDepth,
            SnapRadiusMeters = limits.SnapRadiusMeters ?? NavigableSnap.DefaultSearchRadiusMeters,
            CellMeters = limits.CellMeters,
            PreferredAreas = areas,
            PreferredLines = lines,
        };

        var planned = await Task.Run(() => RoutePlanner.Plan(sources, planRequest, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (planned.Plan is not { } plan)
        {
            var failure = planned.Failure!;
            return ToolResult<PlanRouteResult>.Err(new NoRoutePlanned(failure.Reason, failure.StopIndex, failure.LegIndex, failure.Message));
        }

        var waypoints = Waypoints(plan);
        var polyline = PolylineJson(plan.Waypoints);
        PlanRouteCheck? check = null;
        if (request.Check)
        {
            var report = await _check.InvokeAsync(
                new CheckRouteRequest(
                    Polyline: new GeoPolyline([.. plan.Waypoints.Select(p => new GeoPoint(p.Latitude, p.Longitude))]),
                    Departure: request.Departure,
                    Vessel: vessel),
                cancellationToken).ConfigureAwait(false);
            if (report.TryGetValue(out var value))
            {
                check = Summarise(value!, plan, planRequest.BerthRadiusMeters);
            }
        }

        string? routeId = null;
        if (request.RouteName is { } name)
        {
            var created = await _writer!.CreateRouteAsync(name, waypoints, cancellationToken).ConfigureAwait(false);
            if (!created.TryGetValue(out routeId))
            {
                created.TryGetError(out var error);
                return ToolResult<PlanRouteResult>.Err(error!);
            }
        }

        return ToolResult<PlanRouteResult>.Ok(new PlanRouteResult(
            Nm(plan.DistanceMeters),
            Math.Round(minimum, 2),
            Math.Round(berthDepth, 2),
            [.. plan.Stops.Select(s => Stop(s, plan.Stops.Count))],
            Legs(plan, vessel.SpeedKnots),
            waypoints,
            polyline,
            check,
            routeId,
            Caveat));
    }

    private ToolError? Validate(PlanRouteRequest request)
    {
        static bool Bad(GeoPoint? p) => p is null || !double.IsFinite(p.Latitude) || !double.IsFinite(p.Longitude)
            || p.Latitude < -90 || p.Latitude > 90 || p.Longitude < -180 || p.Longitude > 180;

        if (Bad(request.From))
        {
            return new InvalidArgument("from", "give {\"latitude\":…,\"longitude\":…} in WGS-84 range");
        }

        if (Bad(request.To))
        {
            return new InvalidArgument("to", "give {\"latitude\":…,\"longitude\":…} in WGS-84 range");
        }

        if (request.Via is { } via && via.Any(Bad))
        {
            return new InvalidArgument("via", "each via point is {\"latitude\":…,\"longitude\":…} in WGS-84 range");
        }

        if (request.Via is { Count: > 48 })
        {
            return new InvalidArgument("via", "at most 48 via points");
        }

        if (request.Vessel is not { } vessel || (vessel.DraftMeters is null && vessel.SafetyContourMeters is null))
        {
            return new InvalidArgument("vessel", "give vessel.draftMeters, vessel.safetyContourMeters, or both: the route keeps to water at least that deep");
        }

        if (vessel.Validate() is var (name, problem))
        {
            return new InvalidArgument($"vessel.{name}", problem);
        }

        if (Arguments.Negative(request.ClearanceMeters))
        {
            return new InvalidArgument("clearanceMeters", "must be zero or more metres");
        }

        foreach (var prefer in request.Prefer ?? [])
        {
            if (PreferredType(prefer) is null)
            {
                return new InvalidArgument("prefer", $"'{prefer}' isn't one of fairways, dredged_areas or recommended_tracks");
            }
        }

        if (request.Limits is { } limits)
        {
            if (Arguments.Negative(limits.BerthRadiusMeters))
            {
                return new InvalidArgument("limits.berthRadiusMeters", "must be zero or more metres");
            }

            if (Arguments.NotPositive(limits.SnapRadiusMeters) || limits.SnapRadiusMeters > NavigableSnap.MaxSearchRadiusMeters)
            {
                return new InvalidArgument("limits.snapRadiusMeters", $"must be more than 0 and at most {NavigableSnap.MaxSearchRadiusMeters:0} m");
            }

            if (Arguments.Negative(limits.ComfortMeters))
            {
                return new InvalidArgument("limits.comfortMeters", "must be zero or more metres");
            }

            if (limits.CellMeters is { } cell && !(cell >= 1 && double.IsFinite(cell)))
            {
                return new InvalidArgument("limits.cellMeters", "must be at least 1 m");
            }
        }

        if (request.RouteName is not null)
        {
            if (string.IsNullOrWhiteSpace(request.RouteName))
            {
                return new InvalidArgument("routeName", "give a name, or leave it out");
            }

            if (_writer is null)
            {
                return new InvalidArgument("routeName", "this host has no routes to create (SoundCharts only); pass 'polyline' to check_route, or the waypoints to create_route and append_waypoint");
            }
        }

        return null;
    }

    private static string? PreferredType(string value) =>
        value.Trim().Replace("-", "_", StringComparison.Ordinal).Replace(" ", "_", StringComparison.Ordinal).ToLowerInvariant() switch
        {
            "fairways" or "fairway" => "Fairway",
            "dredged_areas" or "dredged_area" => "DredgedArea",
            "recommended_tracks" or "recommended_track" => "RecommendedTrack",
            _ => null,
        };

    // The preferred areas and lines of the loaded S-101 (and translated S-57) cells round the stops.
    private static (IReadOnlyList<SurfacePart> Areas, IReadOnlyList<IReadOnlyList<GeoPosition>> Lines) PreferredWater(
        IReadOnlyList<LoadedDataset> datasets,
        IReadOnlyList<string> prefer,
        IReadOnlyList<GeoPosition> stops)
    {
        var types = prefer.Select(PreferredType).OfType<string>().ToHashSet(StringComparer.Ordinal);
        if (types.Count == 0)
        {
            return ([], []);
        }

        // The stops' box, padded as far as the planner may look.
        var pad = 0.5;
        var box = new BoundingBox(
            stops.Min(p => p.Latitude) - pad,
            stops.Min(p => p.Longitude) - pad,
            stops.Max(p => p.Latitude) + pad,
            stops.Max(p => p.Longitude) + pad);
        var areas = new List<SurfacePart>();
        var lines = new List<IReadOnlyList<GeoPosition>>();
        foreach (var dataset in datasets)
        {
            if (dataset.Data is not S101DatasetData s101 || !Overlaps(dataset.Bounds, box))
            {
                continue;
            }

            var features = Preferred.GetValue(s101.Dataset, d => new PreferredFeatures(d));
            foreach (var (type, surfaces, curves) in features.Items)
            {
                if (types.Contains(type))
                {
                    areas.AddRange(surfaces);
                    lines.AddRange(curves);
                }
            }
        }

        return (areas, lines);
    }

    private static bool Overlaps(BoundingBox a, BoundingBox b) =>
        a.SouthLatitude <= b.NorthLatitude && a.NorthLatitude >= b.SouthLatitude
        && a.WestLongitude <= b.EastLongitude && a.EastLongitude >= b.WestLongitude;

    private static PlanRouteStop Stop(RoutePlanStop stop, int count) => new(
        stop.Index,
        stop.Index == 0 ? "from" : stop.Index == count - 1 ? "to" : "via",
        [stop.Requested.Latitude, stop.Requested.Longitude],
        Math.Round(stop.Position.Latitude, 6),
        Math.Round(stop.Position.Longitude, 6),
        stop.Moved,
        stop.Moved ? stop.Reason : null,
        stop.Moved ? stop.DistanceMeters : null,
        stop.Moved ? stop.BearingDegrees : null,
        stop.DepthMeters);

    private static List<PlanRouteWaypoint> Waypoints(RoutePlan plan)
    {
        var result = new List<PlanRouteWaypoint>(plan.Waypoints.Count);
        var count = plan.Stops.Count;
        foreach (var leg in plan.Legs)
        {
            for (var k = result.Count == 0 ? 0 : 1; k < leg.Points.Count; k++)
            {
                string? name = k == 0 ? StopName(leg.From, count) : k == leg.Points.Count - 1 ? StopName(leg.To, count) : null;
                var p = leg.Points[k];
                result.Add(new PlanRouteWaypoint(Math.Round(p.Latitude, 6), Math.Round(p.Longitude, 6), name));
            }
        }

        return result;
    }

    private static string StopName(int index, int count) =>
        index == 0 ? "from" : index == count - 1 ? "to" : string.Create(CultureInfo.InvariantCulture, $"via {index}");

    private static List<PlanRouteLegResult> Legs(RoutePlan plan, double? speedKnots)
    {
        var result = new List<PlanRouteLegResult>(plan.Legs.Count);
        var first = 0;
        foreach (var leg in plan.Legs)
        {
            var last = first + leg.Points.Count - 1;
            var nm = Nm(leg.DistanceMeters);
            result.Add(new PlanRouteLegResult(
                leg.From,
                leg.To,
                nm,
                speedKnots is { } knots ? Math.Round(leg.DistanceMeters / 1852 / knots * 60, 1) : null,
                first,
                last,
                leg.LeastDepthMeters,
                leg.LeastClearanceMeters,
                leg.CellMeters));
            first = last;
        }

        return result;
    }

    private static PlanRouteCheck Summarise(CheckRouteResult report, RoutePlan plan, double berthRadiusMeters)
    {
        // Where each stop is along the route, in nautical miles.
        var at = new List<double> { 0 };
        foreach (var leg in plan.Legs)
        {
            at.Add(at[^1] + (leg.DistanceMeters / 1852));
        }

        var berthNm = berthRadiusMeters / 1852;
        bool Away(CheckRouteFinding f) =>
            f.Location is { } location
                ? at.All(s => location.EndNm < s - berthNm || location.StartNm > s + berthNm)
                : f.DistanceNm is not { } d || at.All(s => Math.Abs(d - s) > berthNm);

        var must = report.Findings.Where(f => f.Group == "must_review").ToList();
        var away = must.Where(Away).ToList();
        var worst = away
            .Concat(must.Except(away))
            .Concat(report.Findings.Where(f => f.Group == "check"))
            .Take(5)
            .Select(f => new PlanRouteFinding(f.Code, f.Group, f.Title, f.Meta, f.DistanceNm))
            .ToList();
        return new PlanRouteCheck(
            report.Verdict.Kind,
            report.Verdict.Title,
            report.Summary.MustReview,
            away.Count,
            report.Summary.Check,
            report.Summary.Info,
            worst,
            [.. report.Checks.Where(c => c.State != "ran").Select(c => $"{c.Name}: {c.Reason ?? c.State}")]);
    }

    private static string PolylineJson(IReadOnlyList<GeoPosition> points)
    {
        var text = new StringBuilder("{\"vertices\":[");
        for (var k = 0; k < points.Count; k++)
        {
            if (k > 0)
            {
                text.Append(',');
            }

            text.Append(CultureInfo.InvariantCulture, $"[{Math.Round(points[k].Latitude, 6)},{Math.Round(points[k].Longitude, 6)}]");
        }

        return text.Append("]}").ToString();
    }

    private static double Nm(double meters) => Math.Round(meters / 1852, 2);

    // A cell's Fairway, DredgedArea and RecommendedTrack geometry, read once.
    private sealed class PreferredFeatures(S101Dataset dataset)
    {
        private readonly Lazy<List<(string Type, IReadOnlyList<SurfacePart> Surfaces, IReadOnlyList<IReadOnlyList<GeoPosition>> Curves)>> _items = new(() =>
        {
            var items = new List<(string, IReadOnlyList<SurfacePart>, IReadOnlyList<IReadOnlyList<GeoPosition>>)>();
            foreach (var feature in new S101VectorSource(dataset).GetFeatures())
            {
                if (feature.FeatureType is not ("Fairway" or "DredgedArea" or "RecommendedTrack") || feature is not IS100Feature s100)
                {
                    continue;
                }

                items.Add((feature.FeatureType, s100.Surfaces, s100.Curves));
            }

            return items;
        });

        public List<(string Type, IReadOnlyList<SurfacePart> Surfaces, IReadOnlyList<IReadOnlyList<GeoPosition>> Curves)> Items => _items.Value;
    }
}
