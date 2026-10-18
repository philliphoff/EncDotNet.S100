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

/// <summary>Request payload for <see cref="CheckRouteTool"/>.</summary>
/// <param name="RouteId">A host route to check; omit (with no polyline) for the active route.</param>
/// <param name="Polyline">A route given directly: each pair of consecutive vertices is a leg. Takes precedence over <paramref name="RouteId"/>.</param>
/// <param name="CorridorHalfWidthMeters">The corridor's half-width either side of the track, overriding every leg's cross-track limits.</param>
/// <param name="GreatCircle">For a polyline, run legs as great circles instead of rhumb lines.</param>
/// <param name="SafetyContourMeters">The safety contour for the depth checks; with <paramref name="SafetyDepthMeters"/>, turns them on.</param>
/// <param name="SafetyDepthMeters">The safety depth for the danger and sounding checks.</param>
/// <param name="DraftMeters">A draft to judge dangers against as well; for a route, overrides the route's.</param>
public sealed record CheckRouteRequest(
    [property: Description("Id of a host route to check (viewer only). Omit, with no polyline, for the active route.")] string? RouteId = null,
    [property: Description("A route given directly: each pair of consecutive vertices is a leg. Takes precedence over routeId. Its corridorWidthMeters, when set, is the half-width either side of the track.")] GeoPolyline? Polyline = null,
    [property: Description("The corridor's HALF-width in metres: how far it reaches to port, and to starboard, of the track (so the corridor is twice this wide). Overrides every leg's cross-track limits. Without it a route uses each leg's port/starboard XTD, and anything unset uses 50 m.")] double? CorridorHalfWidthMeters = null,
    [property: Description("For a polyline, run legs as great circles instead of rhumb lines.")] bool GreatCircle = false,
    [property: Description("Safety contour in metres. With safetyDepthMeters, turns on the depth checks: depth, dredged and drying areas and S-102 cells shallower than it, and charted dangers. SoundCharts' findings list uses the mariner's settings (get_mariner_settings).")] double? SafetyContourMeters = null,
    [property: Description("Safety depth in metres, for charted dangers and soundings. Goes with safetyContourMeters.")] double? SafetyDepthMeters = null,
    [property: Description("Draft in metres: dangers are also judged against draft + squat + required UKC. For a route it overrides the route's draft; without either, dangers are judged against the safety depth only.")] double? DraftMeters = null);

/// <summary>Result of <see cref="CheckRouteTool"/>.</summary>
public sealed record CheckRouteResult(
    [property: Description("Id of the checked route, or null for a polyline.")] string? RouteId,
    [property: Description("Name of the checked route, or null.")] string? RouteName,
    [property: Description("Route length in nautical miles.")] double DistanceNm,
    [property: Description("Counts by group, and the summary line SoundCharts shows.")] CheckRouteSummary Summary,
    [property: Description("The findings, by group (must_review, check, info) and then by distance along the route. Codes are the same as get_findings: route_check.crosses_land, route_check.crosses_drying, route_check.land_in_corridor, route_check.drying_in_corridor, route_check.danger_in_corridor, route_check.danger_near_route, route_check.crosses_safety_contour, route_check.safety_contour_in_corridor, route_check.soundings_below_safety_depth, route_check.crosses_s102_shoal, route_check.s102_shoal_in_corridor and data.outside_chart_coverage.")] IReadOnlyList<CheckRouteFinding> Findings,
    [property: Description("The corridor swept along each leg, in route order.")] IReadOnlyList<CheckRouteLeg> Legs,
    [property: Description("S-101 (and S-401, and translated S-57) cells near the route that the check read.")] IReadOnlyList<string> Datasets,
    [property: Description("Disclaimer to show once with the findings.")] string Disclaimer);

/// <summary>The counts and summary line of a route check.</summary>
public sealed record CheckRouteSummary(
    [property: Description("Number of findings.")] int Count,
    [property: Description("Number of must_review findings (the track crosses land, drying ground or the safety contour, or a danger is inside the limits).")] int MustReview,
    [property: Description("Number of check findings (land, drying ground or shoal water inside the cross-track limits, dangers near the route, leaving chart coverage).")] int Check,
    [property: Description("Number of info findings.")] int Info,
    [property: Description("The summary line, e.g. \"2 to review\", \"1 to check\" or \"No findings for your settings\".")] string Text);

/// <summary>One finding of a route check.</summary>
public sealed record CheckRouteFinding(
    [property: Description("Id, unique within this result, e.g. \"route_check.crosses_land.0\".")] string Id,
    [property: Description("Kind of finding: route_check.crosses_land, route_check.crosses_drying, route_check.land_in_corridor, route_check.drying_in_corridor, route_check.danger_in_corridor, route_check.danger_near_route, route_check.crosses_safety_contour, route_check.safety_contour_in_corridor, route_check.soundings_below_safety_depth, route_check.crosses_s102_shoal, route_check.s102_shoal_in_corridor or data.outside_chart_coverage.")] string Code,
    [property: Description("What to do: must_review, check or info.")] string Group,
    [property: Description("The check that produced it: route_check, or data for chart coverage.")] string Source,
    [property: Description("One line, e.g. \"Crosses drying ground\".")] string Title,
    [property: Description("Where and what, e.g. \"Leg 2 → 3 · 0.10 NM · DepthArea\".")] string Meta,
    [property: Description("Zero-based leg the finding is on.")] int? LegIndex,
    [property: Description("Distance from departure in nautical miles to where it starts.")] double? DistanceNm,
    [property: Description("Where it is: a point, or a span of the route.")] CheckRouteLocation? Location,
    [property: Description("The feature behind the finding (open it with describe_feature), or null.")] CheckRouteFeature? Feature);

/// <summary>Where a finding is.</summary>
public sealed record CheckRouteLocation(
    [property: Description("\"point\" or \"span\".")] string Kind,
    [property: Description("Distance from departure to the start, nautical miles.")] double StartNm,
    [property: Description("Distance from departure to the end, nautical miles.")] double EndNm,
    [property: Description("The point, or the positions along the span in route order, as [latitude, longitude] pairs.")] IReadOnlyList<double[]> Path);

/// <summary>A feature behind a finding.</summary>
public sealed record CheckRouteFeature(
    [property: Description("Catalog id of the dataset that holds the feature (as list_datasets).")] string DatasetId,
    [property: Description("The feature's id within the dataset.")] string FeatureId);

/// <summary>The corridor swept along one leg.</summary>
public sealed record CheckRouteLeg(
    [property: Description("Zero-based leg index.")] int Index,
    [property: Description("Distance from departure to the start of the leg, nautical miles.")] double StartNm,
    [property: Description("Leg length, nautical miles.")] double DistanceNm,
    [property: Description("How far the corridor reaches to port of the track, metres (a half-width).")] double PortMeters,
    [property: Description("How far the corridor reaches to starboard of the track, metres (a half-width).")] double StarboardMeters);

/// <summary>
/// Checks a route against the loaded charts (<see cref="RouteCheckEngine"/>,
/// #954): where its track crosses land or drying ground, where either reaches
/// inside its cross-track limits, and where it leaves chart coverage.
/// </summary>
/// <remarks>
/// Takes a host route (the viewer's active or a named route, with each leg's
/// cross-track limits) or a polyline. The findings and codes are the ones the
/// viewer's findings list (<c>get_findings</c>) shows. Not for navigation.
/// </remarks>
public sealed class CheckRouteTool
{
    /// <summary>Tool name used in error payloads.</summary>
    public const string Name = "check_route";

    /// <summary>The disclaimer shown once with the findings.</summary>
    public const string Disclaimer = "Not for navigation · checked against the loaded S-101 charts only";

    private readonly IDatasetCatalog _catalog;
    private readonly IPassageRouteProvider? _routes;
    private readonly ICrsTransformFactory _transforms;
    private readonly object _gate = new();
    private IReadOnlyList<LoadedDataset>? _cachedSnapshot;
    private RouteCheckDataSources? _cachedSources;

    /// <summary>Creates the tool.</summary>
    /// <param name="catalog">The loaded datasets.</param>
    /// <param name="routes">The host's routes, or <c>null</c> when the host has none (polylines only).</param>
    /// <param name="transforms">CRS transforms for projected S-102 grids, or <c>null</c> for ProjNet.</param>
    public CheckRouteTool(IDatasetCatalog catalog, IPassageRouteProvider? routes = null, ICrsTransformFactory? transforms = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _routes = routes;
        _transforms = transforms ?? new ProjNetCrsTransformFactory();
    }

    /// <summary>Executes the tool.</summary>
    public async Task<ToolResult<CheckRouteResult>> InvokeAsync(CheckRouteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.CorridorHalfWidthMeters is { } w && (!double.IsFinite(w) || w < 0))
        {
            return ToolResult<CheckRouteResult>.Err(new InvalidArgument("corridorHalfWidthMeters", "must be zero or more metres"));
        }

        if ((request.SafetyContourMeters is null) != (request.SafetyDepthMeters is null))
        {
            return ToolResult<CheckRouteResult>.Err(new InvalidArgument("safetyDepthMeters", "safetyContourMeters and safetyDepthMeters go together"));
        }

        foreach (var (name, value) in new[] { ("safetyContourMeters", request.SafetyContourMeters), ("safetyDepthMeters", request.SafetyDepthMeters), ("draftMeters", request.DraftMeters) })
        {
            if (value is { } v && (!double.IsFinite(v) || v < 0))
            {
                return ToolResult<CheckRouteResult>.Err(new InvalidArgument(name, "must be zero or more metres"));
            }
        }

        RouteCheckDepths? depths = request.SafetyContourMeters is { } contour && request.SafetyDepthMeters is { } safetyDepth
            ? new RouteCheckDepths(contour, safetyDepth) { DraftMeters = request.DraftMeters }
            : null;

        string? routeId = null;
        string? routeName = null;
        var legs = new List<RouteCheckLeg>();
        if (request.Polyline is { } polyline)
        {
            if (GeoQueryValidator.Validate(new GeoQuery.Polyline(polyline)) is { } geometryError)
            {
                return ToolResult<CheckRouteResult>.Err(geometryError);
            }

            var half = request.CorridorHalfWidthMeters ?? polyline.CorridorWidthMeters;
            var geometry = request.GreatCircle ? LegGeometry.GreatCircle : LegGeometry.Rhumb;
            for (var i = 1; i < polyline.Vertices.Count; i++)
            {
                var a = polyline.Vertices[i - 1];
                var b = polyline.Vertices[i];
                legs.Add(new RouteCheckLeg(
                    [new GeoPosition(a.Latitude, a.Longitude), new GeoPosition(b.Latitude, b.Longitude)], half, half, geometry));
            }
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

            if (depths is not null && request.DraftMeters is null && route.Legs.All(l => l.DraftMeters is not null))
            {
                // The deepest keel on any leg, as SoundCharts' findings list judges it.
                var keel = route.Legs.MaxBy(l => l.DraftMeters!.Value + (l.SquatMeters ?? 0) + (l.RequiredUkcMeters ?? 0))!;
                depths = depths with { DraftMeters = keel.DraftMeters, SquatMeters = keel.SquatMeters ?? 0, RequiredUkcMeters = keel.RequiredUkcMeters ?? 0 };
            }
            else if (depths is not null && request.DraftMeters is not null && route.Legs.Count > 0)
            {
                depths = depths with { SquatMeters = route.Legs.Max(l => l.SquatMeters ?? 0), RequiredUkcMeters = route.Legs.Max(l => l.RequiredUkcMeters ?? 0) };
            }

            foreach (var leg in route.Legs)
            {
                legs.Add(new RouteCheckLeg(
                    leg.Points,
                    request.CorridorHalfWidthMeters ?? leg.PortCrossTrackMeters,
                    request.CorridorHalfWidthMeters ?? leg.StarboardCrossTrackMeters,
                    leg.Geometry));
            }

            routeId = route.RouteId;
            routeName = route.Name;
        }

        var sources = SourcesFor(_catalog.Datasets);
        var engine = sources.CreateEngine(Options with { Depths = depths });
        RouteCheckResult result;
        try
        {
            result = await Task.Run(() => engine.Check(legs, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            return ToolResult<CheckRouteResult>.Err(new InvalidArgument("polyline", ex.Message));
        }

        return ToolResult<CheckRouteResult>.Ok(Project(result, routeId, routeName, sources));
    }

    /// <summary>The summary line for a set of findings, as SoundCharts' no-draft verdict titles.</summary>
    /// <param name="mustReview">Number of must_review findings.</param>
    /// <param name="check">Number of check findings.</param>
    /// <returns>The line.</returns>
    public static string SummaryText(int mustReview, int check) =>
        mustReview > 0 ? $"{mustReview} to review"
        : check > 0 ? $"{check} to check"
        : "No findings for your settings";

    private static readonly RouteCheckOptions Options = RouteCheckOptions.Default with
    {
        Text = RouteCheckText.Default with { Culture = System.Globalization.CultureInfo.InvariantCulture },
    };

    // The sources index S-101 cells lazily, so they are kept while the catalog
    // snapshot is unchanged (the catalog publishes a new list on every change).
    private RouteCheckDataSources SourcesFor(IReadOnlyList<LoadedDataset> snapshot)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(snapshot, _cachedSnapshot) || _cachedSources is null)
            {
                _cachedSources = RouteCheckDataSources.FromDatasets(snapshot, _transforms);
                _cachedSnapshot = snapshot;
            }

            return _cachedSources;
        }
    }

    private static CheckRouteResult Project(RouteCheckResult result, string? routeId, string? routeName, RouteCheckDataSources sources)
    {
        var findings = result.Findings;
        var mustReview = findings.Count(f => f.Group == RouteFindingGroup.MustReview);
        var check = findings.Count(f => f.Group == RouteFindingGroup.Check);
        var info = findings.Count(f => f.Group == RouteFindingGroup.Info);

        var datasets = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var corridor in result.Corridors)
        {
            foreach (var cell in sources.Charts.CellsNear(Extent(corridor.Ring)))
            {
                datasets.Add(cell.DatasetId);
            }
        }

        return new CheckRouteResult(
            routeId,
            routeName,
            Nm(result.LengthMeters),
            new CheckRouteSummary(findings.Count, mustReview, check, info, SummaryText(mustReview, check)),
            findings.Select(Project).ToList(),
            result.Corridors.Select(c => new CheckRouteLeg(
                c.LegIndex,
                Nm(c.StartDistanceMeters),
                Nm(c.EndDistanceMeters - c.StartDistanceMeters),
                c.PortHalfWidthMeters,
                c.StarboardHalfWidthMeters)).ToList(),
            datasets.ToList(),
            Disclaimer);
    }

    private static CheckRouteFinding Project(RouteFinding f) => new(
        f.Id,
        f.Code,
        f.Group switch
        {
            RouteFindingGroup.MustReview => "must_review",
            RouteFindingGroup.Check => "check",
            _ => "info",
        },
        f.Source == RouteFindingSource.RouteCheck ? "route_check" : "data",
        f.Title,
        f.Meta,
        f.LegIndex,
        f.DistanceMeters is { } d ? Nm(d) : null,
        f.Location is { } l
            ? new CheckRouteLocation(
                l.IsPoint ? "point" : "span",
                Nm(l.StartDistanceMeters),
                Nm(l.EndDistanceMeters),
                l.Path.Select(p => new[] { p.Latitude, p.Longitude }).ToList())
            : null,
        f.Feature is { } feature ? new CheckRouteFeature(feature.DatasetId, feature.FeatureId) : null);

    private static EncDotNet.S100.Pipelines.BoundingBox Extent(IReadOnlyList<GeoPosition> ring) => new(
        ring.Min(p => p.Latitude),
        ring.Min(p => p.Longitude),
        ring.Max(p => p.Latitude),
        ring.Max(p => p.Longitude));

    private static double Nm(double meters) => Math.Round(meters / PassageGeodesy.MetersPerNauticalMile, 4);
}
