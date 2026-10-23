using System.Globalization;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Testing.Scenarios;
using EncDotNet.S100.Testing.Scenarios.World;
using EncDotNet.S100.Testing.Scenarios.Writers;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Writes "Spit crossing" (#954) once for the test class.</summary>
public sealed class SpitCrossingScenarioFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "s100-viewer-spit-" + Guid.NewGuid().ToString("N"));

    public SpitCrossingScenarioFixture()
    {
        Scenario = ScenarioGenerator.Generate(SyntheticScenarios.SpitCrossing(), _root);
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

    /// <summary>A generated route as the viewer imports it from its S-421 file.</summary>
    public Route Import(string datasetName)
    {
        var path = Scenario.Datasets.Single(d => d.FullPath.EndsWith(datasetName + ".gml", StringComparison.Ordinal)).FullPath;
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
/// The route checks in the viewer (#954): the generated spit route's land and
/// drying ground in the findings list, with or without a draft, the same as
/// <c>check_route</c> returns for it.
/// </summary>
public sealed class RouteCheckFindingsTests(SpitCrossingScenarioFixture fixture) : IClassFixture<SpitCrossingScenarioFixture>, IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 6, 0, 0, TimeSpan.Zero);

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

    /// <summary>Runs the services' posted completions one at a time, as the UI thread does.</summary>
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
        public IReadOnlyList<RouteFinding> RouteChecks =>
            Findings.Findings.Where(f => f.Category == RouteFindingCategory.Chart || f.Code == RouteFindingCodes.OutsideChartCoverage).ToList();

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
        route.SetDeparture(Now);
        routes.Routes.Add(route);
        routes.Routes.SetActiveRoute(route);
        var catalog = new PassageCheckFixture.FixedCatalog(fixture.Datasets);
        var check = new PassageCheckService(routes, catalog,
            checks: () => [new ChartCheck(), new ClearanceCheck()], debounce: TimeSpan.Zero, post: new UiThread().Post);
        var findings = new RouteFindingsService(check, routes, catalog, timeFormat: new Utc());
        routes.IsCheckModeActive = true;
        await findings.WhenIdle();
        return new Setup(routes, route, check, findings, catalog);
    }

    [Fact]
    public async Task The_spit_route_lists_its_drying_ground_and_land()
    {
        using var s = await Start(fixture.Import("421ZZ00SPITX"));

        var checks = s.RouteChecks;
        Assert.Equal(
            [RouteFindingCodes.CrossesDrying, RouteFindingCodes.CrossesDrying, RouteFindingCodes.CrossesLand],
            checks.Select(f => f.Code));
        Assert.All(checks, f =>
        {
            Assert.Equal(RouteFindingGroup.MustReview, f.Group);
            Assert.Equal("101ZZ00SPIT5", f.Feature!.DatasetId);
        });
        Assert.Equal([1, 2, 2], checks.Select(f => f.LegIndex!.Value));
        Assert.Equal("Crosses drying ground", checks[0].Title);
        Assert.StartsWith("Leg 2 → 3 · ", checks[0].Meta, StringComparison.Ordinal);

        // Where the model says the track goes over drying ground and land.
        var definition = fixture.Scenario.Definition;
        var waypoints = definition.Routes.Single(r => r.DatasetName == "421ZZ00SPITX").Waypoints.Select(n => definition.Places[n]).ToList();
        var truth = GroundTruth.Along(definition.World, waypoints);
        Assert.Equal(truth.Count, checks.Count);
        for (var i = 0; i < truth.Count; i++)
        {
            var start = definition.World.Frame.ToGeo(truth[i].Start.X, truth[i].Start.Y);
            var at = checks[i].Location!.Path[0];
            Assert.True(PassageGeodesy.RhumbDistanceMeters(start, at) < 15, $"Finding {i} starts {PassageGeodesy.RhumbDistanceMeters(start, at):0} m from the model's {truth[i].Kind}.");
        }
    }

    [Fact]
    public async Task The_route_checks_run_without_a_draft()
    {
        var route = fixture.Import("421ZZ00SPITX");
        route.SetPassageValue(RoutePassageField.Draft, null);
        for (var i = 0; i < route.Legs.Count; i++)
            route.ResetLegOverride(i, RoutePassageField.Draft);

        using var s = await Start(route);

        Assert.False(s.Check.Input!.HasDraft);
        Assert.Null(s.Check.Current);
        Assert.Equal(3, s.RouteChecks.Count(f => f.Group == RouteFindingGroup.MustReview));
    }

    [Fact]
    public async Task The_corridor_uses_the_imported_cross_track_limits()
    {
        using var s = await Start(fixture.Import("421ZZ00SPITX"));

        Assert.All(s.Check.Input!.Legs, x =>
        {
            Assert.Equal(S421RouteBuilder.CrossTrackLimitMeters, x.PortLimit!.Value.TotalMetres, 9);
            Assert.Equal(S421RouteBuilder.CrossTrackLimitMeters, x.StarboardLimit!.Value.TotalMetres, 9);
        });
    }

    [Fact]
    public async Task Moving_the_waypoint_off_the_shore_clears_the_land()
    {
        using var s = await Start(fixture.Import("421ZZ00SPITX"));
        Assert.Contains(s.RouteChecks, f => f.Code == RouteFindingCodes.CrossesLand);

        // Pull "ashore" back north of the foreshore.
        var frame = fixture.Scenario.Definition.World.Frame;
        s.Route.MoveWaypoint(3, frame.ToGeo(3600, 1450));
        await s.Findings.WhenIdle();

        Assert.DoesNotContain(s.RouteChecks, f => f.Code == RouteFindingCodes.CrossesLand);
        Assert.Single(s.RouteChecks, f => f.Code == RouteFindingCodes.CrossesDrying);
    }

    private sealed class Mariner(MarinerSettings settings) : IMarinerSettingsProvider
    {
        public MarinerSettings Current => settings;

        public event Action<MarinerSettings>? Changed { add { } remove { } }
    }

    [Fact]
    public void The_depth_checks_use_the_mariners_settings()
    {
        // Each leg's dangers are judged against that leg's own keel, so the
        // settings carry only the mariner's depths and the time.
        var settings = new MarinerSettings { SafetyContour = Depth.FromMetres(10), SafetyDepth = Depth.FromMetres(8) };
        var route = new Route("r");
        route.AppendWaypoint(new DataModel.GeoPosition(0, 0));
        route.AppendWaypoint(new DataModel.GeoPosition(0, 0.01));
        route.SetPassageValue(RoutePassageField.Draft, 5);

        var input = PassageCheckService.CheckInput.For(route, Now, settings)!;
        var checkSettings = input.Settings;

        Assert.Equal((10.0, 8.0), (checkSettings.SafetyContour!.Value.TotalMetres, checkSettings.SafetyDepth!.Value.TotalMetres));
        Assert.True(checkSettings.ChecksDepths);
        Assert.Equal(Now, checkSettings.At);
        Assert.False(PassageCheckService.CheckInput.For(route, Now)!.Settings.ChecksDepths);
        // A change to the mariner's depths is a new input; the legs are the same.
        var other = PassageCheckService.CheckInput.For(route, Now, settings with { SafetyDepth = Depth.FromMetres(9) })!;
        Assert.False(input.SameAs(other));
        Assert.True(input.SameLegs(other));
        Assert.Equal("Crosses land", new ViewerFindingFormatter(DepthUnit.Metres, TimeFormat.Utc, Now).Title(new RouteFinding("x", RouteFindingCodes.CrossesLand)));
    }

    [Fact]
    public async Task The_channel_route_has_no_route_check_findings()
    {
        using var s = await Start(fixture.Import("421ZZ00CHAN"));

        Assert.Empty(s.RouteChecks);
    }

    [Fact]
    public async Task Check_route_returns_the_findings_list_route_checks()
    {
        using var s = await Start(fixture.Import("421ZZ00SPITX"));
        var tool = new CheckRouteTool(s.Catalog, new ViewerPassageRouteProvider(s.Routes, new InlineInvoker()));

        var result = await tool.InvokeAsync(new CheckRouteRequest(), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e.Message : "");
        Assert.Equal(s.Route.Id, value!.RouteId);
        Assert.Equal("3 to review", value.Summary.Text);
        Assert.Equal(s.RouteChecks.Select(f => (f.Id, f.Code.Value, f.LegIndex)), value.Findings.Select(f => (f.Id, f.Code, f.LegIndex)));
        for (var i = 0; i < value.Findings.Count; i++)
        {
            Assert.Equal(s.RouteChecks[i].Distance!.Value.TotalMetres / PassageGeodesy.MetersPerNauticalMile, value.Findings[i].DistanceNm!.Value, 3);
            Assert.Equal(s.RouteChecks[i].Feature!.FeatureId, value.Findings[i].Feature!.FeatureId);
        }

        Assert.All(value.Legs, l => Assert.Equal(S421RouteBuilder.CrossTrackLimitMeters, l.PortMeters));
        Assert.Contains("101ZZ00SPIT5", value.Datasets);
    }
}
