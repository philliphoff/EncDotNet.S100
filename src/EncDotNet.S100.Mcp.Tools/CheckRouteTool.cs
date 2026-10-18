using System.ComponentModel;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>Request payload for <see cref="CheckRouteTool"/>.</summary>
/// <param name="RouteId">A host route to check; omit (with no polyline) for the active route.</param>
/// <param name="Polyline">A route given directly: each pair of consecutive vertices is a leg. Takes precedence over <paramref name="RouteId"/>.</param>
/// <param name="CorridorHalfWidthMeters">The corridor's half-width either side of the track, overriding every leg's cross-track limits.</param>
/// <param name="GreatCircle">For a polyline, run legs as great circles instead of rhumb lines.</param>
public sealed record CheckRouteRequest(
    [property: Description("Id of a host route to check (viewer only). Omit, with no polyline, for the active route.")] string? RouteId = null,
    [property: Description("A route given directly: each pair of consecutive vertices is a leg. Takes precedence over routeId. Its corridorWidthMeters, when set, is the half-width either side of the track.")] GeoPolyline? Polyline = null,
    [property: Description("The corridor's HALF-width in metres: how far it reaches to port, and to starboard, of the track (so the corridor is twice this wide). Overrides every leg's cross-track limits. Without it a route uses each leg's port/starboard XTD, and anything unset uses 50 m.")] double? CorridorHalfWidthMeters = null,
    [property: Description("For a polyline, run legs as great circles instead of rhumb lines.")] bool GreatCircle = false);

/// <summary>Result of <see cref="CheckRouteTool"/>.</summary>
public sealed record CheckRouteResult(
    [property: Description("Id of the checked route, or null for a polyline.")] string? RouteId,
    [property: Description("Name of the checked route, or null.")] string? RouteName,
    [property: Description("Route length in nautical miles.")] double DistanceNm,
    [property: Description("Counts by group, and the summary line SoundCharts shows.")] CheckRouteSummary Summary,
    [property: Description("The findings, by group (must_review, check, info) and then by distance along the route. Codes are the same as get_findings: route_check.crosses_land, route_check.crosses_drying, route_check.land_in_corridor, route_check.drying_in_corridor and data.outside_chart_coverage.")] IReadOnlyList<CheckRouteFinding> Findings,
    [property: Description("The corridor swept along each leg, in route order.")] IReadOnlyList<CheckRouteLeg> Legs,
    [property: Description("S-101 (and S-401, and translated S-57) cells near the route that the check read.")] IReadOnlyList<string> Datasets,
    [property: Description("Disclaimer to show once with the findings.")] string Disclaimer);

/// <summary>The counts and summary line of a route check.</summary>
public sealed record CheckRouteSummary(
    [property: Description("Number of findings.")] int Count,
    [property: Description("Number of must_review findings (the track crosses land or drying ground).")] int MustReview,
    [property: Description("Number of check findings (land or drying ground inside the cross-track limits; leaving chart coverage).")] int Check,
    [property: Description("Number of info findings.")] int Info,
    [property: Description("The summary line, e.g. \"2 to review\", \"1 to check\" or \"No findings for your settings\".")] string Text);

/// <summary>One finding of a route check.</summary>
public sealed record CheckRouteFinding(
    [property: Description("Id, unique within this result, e.g. \"route_check.crosses_land.0\".")] string Id,
    [property: Description("Kind of finding: route_check.crosses_land, route_check.crosses_drying, route_check.land_in_corridor, route_check.drying_in_corridor or data.outside_chart_coverage.")] string Code,
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
    private readonly object _gate = new();
    private IReadOnlyList<LoadedDataset>? _cachedSnapshot;
    private RouteCheckEngine? _cachedEngine;
    private RouteCheckDataSources? _cachedSources;

    /// <summary>Creates the tool.</summary>
    /// <param name="catalog">The loaded datasets.</param>
    /// <param name="routes">The host's routes, or <c>null</c> when the host has none (polylines only).</param>
    public CheckRouteTool(IDatasetCatalog catalog, IPassageRouteProvider? routes = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _routes = routes;
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

        var (engine, sources) = EngineFor(_catalog.Datasets);
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

    // The sources index S-101 cells lazily, so they are kept while the catalog
    // snapshot is unchanged (the catalog publishes a new list on every change).
    private (RouteCheckEngine Engine, RouteCheckDataSources Sources) EngineFor(IReadOnlyList<LoadedDataset> snapshot)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(snapshot, _cachedSnapshot) || _cachedEngine is null || _cachedSources is null)
            {
                _cachedSources = RouteCheckDataSources.FromDatasets(snapshot);
                _cachedEngine = new RouteCheckEngine(_cachedSources.Charts, RouteCheckOptions.Default with
                {
                    Text = RouteCheckText.Default with { Culture = System.Globalization.CultureInfo.InvariantCulture },
                });
                _cachedSnapshot = snapshot;
            }

            return (_cachedEngine, _cachedSources);
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
