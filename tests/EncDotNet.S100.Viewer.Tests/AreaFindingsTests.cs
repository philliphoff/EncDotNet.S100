using System.Globalization;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Testing.Scenarios;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Writes "Restricted areas on route" (#956) once for the test class.</summary>
public sealed class RestrictedAreasScenarioFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "s100-viewer-areas-" + Guid.NewGuid().ToString("N"));

    public RestrictedAreasScenarioFixture()
    {
        Scenario = ScenarioGenerator.Generate(SyntheticScenarios.RestrictedAreasOnRoute(), _root);
        var catalogue = ExchangeCatalogueReader.Read(Scenario.CataloguePath);
        Datasets = FileDatasetCatalog.Build(
            catalogue.DatasetDiscoveryMetadata
                .Select(d => new FileDatasetInput(
                    new DatasetId(Path.GetFileNameWithoutExtension(d.RelativePath)),
                    d.ProductSpecification!.ProductIdentifier!,
                    Path.Combine(Scenario.Root, d.RelativePath)))
                .ToList(),
            new ProjNetCrsTransformFactory()).Datasets;
    }

    public GeneratedScenario Scenario { get; }

    public IReadOnlyList<LoadedDataset> Datasets { get; }

    /// <summary>The scenario's route as the viewer imports it from its S-421 file.</summary>
    public Route Import()
    {
        var path = Scenario.Datasets.Single(d => d.FullPath.EndsWith("421ZZ00WARN.gml", StringComparison.Ordinal)).FullPath;
        return RouteS421Mapping.FromS421(S421RoutePlan.From(S421Dataset.Open(path), out _).Route);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>
/// The area checks in the viewer (#956): the generated restricted and
/// protected areas in the findings list through <see cref="AreaFindingProducer"/>,
/// the same as <c>check_route</c> returns for the route.
/// </summary>
public sealed class AreaFindingsTests(RestrictedAreasScenarioFixture fixture) : IClassFixture<RestrictedAreasScenarioFixture>, IDisposable
{
    private static readonly DateTimeOffset Departure = new(2026, 3, 1, 6, 0, 0, TimeSpan.Zero);

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private sealed class UiThread
    {
        private readonly Lock _lock = new();

        public void Post(Action action)
        {
            lock (_lock)
                action();
        }
    }

    private sealed record Setup(RoutesService Routes, Route Route, PassageCheckService Check, RouteFindingsService Findings, PassageCheckFixture.FixedCatalog Catalog) : IDisposable
    {
        public IReadOnlyList<RouteFinding> Areas =>
            Findings.Findings.Where(f => f.Category == RouteFindingCategory.Area).ToList();

        public void Dispose()
        {
            Findings.Dispose();
            Check.Dispose();
        }
    }

    private async Task<Setup> Start(Route route)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var routes = new RoutesService();
        route.SetDeparture(Departure);
        routes.Routes.Add(route);
        routes.Routes.SetActiveRoute(route);
        var catalog = new PassageCheckFixture.FixedCatalog(fixture.Datasets);
        var check = new PassageCheckService(routes, catalog, debounce: TimeSpan.Zero);
        var sources = new RouteCheckSourceCache(new ProjNetCrsTransformFactory());
        var findings = new RouteFindingsService(check, routes, catalog, timeFormat: new Utc(), clock: new FixedClock(Departure),
            producers: [new RouteCheckFindingProducer(sources: sources), new AreaFindingProducer(sources)], post: new UiThread().Post);
        routes.IsCheckModeActive = true;
        await findings.WhenIdle();
        return new Setup(routes, route, check, findings, catalog);
    }

    [Fact]
    public async Task The_areas_on_the_route_are_in_the_findings_list()
    {
        using var s = await Start(fixture.Import());

        var areas = s.Areas;
        Assert.Equal(
            [
                (RouteFindingCodes.EntersProhibitedArea, RouteFindingGroup.MustReview),
                (RouteFindingCodes.ProhibitedAreaInCorridor, RouteFindingGroup.Check),
                (RouteFindingCodes.ProtectedArea, RouteFindingGroup.Check),
                (RouteFindingCodes.CautionArea, RouteFindingGroup.Check),
                (RouteFindingCodes.ProtectedArea, RouteFindingGroup.Check),
                (RouteFindingCodes.RestrictedArea, RouteFindingGroup.Info),
                (RouteFindingCodes.AreaNotInForce, RouteFindingGroup.Info),
            ],
            areas.Select(f => (f.Code, f.Group)));
        Assert.All(areas, f => Assert.Equal(RouteFindingCategory.Area, f.Category));

        var firing = areas[0];
        Assert.Equal("Enters Firing Range Alpha (fictitious)", firing.Title);
        Assert.StartsWith("Leg 1 → 2 · S-101 restricted area · entry prohibited · military area · 06:0", firing.Meta, StringComparison.Ordinal);
        Assert.Equal("101ZZ00SPIT5", firing.Feature!.DatasetId);

        var seal = areas[2];
        Assert.Equal("Crosses Synthetic Sound Seal Sanctuary (fictitious)", seal.Title);
        Assert.Equal("122ZZ00SEAL", seal.Feature!.DatasetId);
        Assert.EndsWith("· 1 Mar–31 May · 06:11Z–06:13Z", seal.Meta, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_summer_departure_moves_the_seasonal_areas()
    {
        using var s = await Start(fixture.Import());

        s.Route.SetDeparture(new DateTimeOffset(2026, 7, 1, 6, 0, 0, TimeSpan.Zero));
        await s.Findings.WhenIdle();

        Assert.Contains(s.Areas, f => f.Code == RouteFindingCodes.RestrictedArea && f.Group == RouteFindingGroup.Check && f.Title.Contains("Summer Speed Zone", StringComparison.Ordinal));
        Assert.Contains(s.Areas, f => f.Code == RouteFindingCodes.AreaNotInForce && f.Title.Contains("Seal Sanctuary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Check_route_returns_the_same_area_findings()
    {
        using var s = await Start(fixture.Import());
        var tool = new CheckRouteTool(s.Catalog, new ViewerPassageRouteProvider(s.Routes, new InlineInvoker()));

        var result = await tool.InvokeAsync(new CheckRouteRequest(), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e.Message : "");
        var areas = value!.Findings.Where(f => f.Category == "area").ToList();
        Assert.Equal(s.Areas.Select(f => (f.Id, f.Code.Value, f.Group switch
        {
            RouteFindingGroup.MustReview => "must_review",
            RouteFindingGroup.Check => "check",
            _ => "info",
        })), areas.Select(f => (f.Id, f.Code, f.Group)));
        Assert.All(areas, f => Assert.Equal("area", f.Category));
        Assert.Equal("Enters Firing Range Alpha (fictitious)", areas[0].Title);
        Assert.Contains("122ZZ00SEAL", value.Datasets);
        Assert.Contains("122ZZ00BERTH", value.Datasets);
        Assert.True(value.Summary.MustReview >= 1);
    }

    [Fact]
    public async Task Check_route_judges_a_polyline_at_the_given_time()
    {
        using var s = await Start(fixture.Import());
        var tool = new CheckRouteTool(s.Catalog);
        var points = s.Route.Waypoints.Select(w => new EncDotNet.S100.Datasets.Pipelines.Geometry.GeoPoint(w.Position.Latitude, w.Position.Longitude)).ToList();

        var summer = await tool.InvokeAsync(
            new CheckRouteRequest(Polyline: new EncDotNet.S100.Datasets.Pipelines.Geometry.GeoPolyline(points), CorridorHalfWidthMeters: 100, Departure: new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero)),
            TestContext.Current.CancellationToken);

        Assert.True(summer.TryGetValue(out var value), summer.TryGetError(out var e) ? e.Message : "");
        Assert.Contains(value!.Findings, f => f.Code == RouteFindingCodes.AreaNotInForce.Value && f.Title == "Synthetic Sound Seal Sanctuary (fictitious) not in force at 2026-07-01 12:00 UTC");
    }
}
