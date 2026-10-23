using System.Globalization;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Testing.Scenarios;
using EncDotNet.S100.Testing.Scenarios.Writers;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Writes "Approach with tide window" (#942) once for the test class.
/// </summary>
public sealed class ApproachScenarioFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "s100-viewer-scenario-" + Guid.NewGuid().ToString("N"));

    public ApproachScenarioFixture()
    {
        Scenario = ScenarioGenerator.Generate(SyntheticScenarios.ApproachWithTideWindow(), _root);
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

    public S421RouteSpec RouteSpec => Scenario.Definition.Routes[0];

    /// <summary>The route as the viewer imports it from its S-421 file.</summary>
    public S421Route S421Route()
    {
        var path = Scenario.Datasets.Single(d => d.ProductIdentifier == "S-421").FullPath;
        return S421RoutePlan.From(S421Dataset.Open(path), out _).Route;
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
/// "Approach with tide window" in the viewer: the S-421 route imported
/// (#390), checked against the generated S-101/S-102/S-104/S-111 files, with
/// the departure window (#903), Follow passage (#904) and the findings list
/// (#905) compared with the engine's exact sweep and the world model's ETAs.
/// </summary>
public sealed class ApproachScenarioTests(ApproachScenarioFixture fixture) : IClassFixture<ApproachScenarioFixture>, IDisposable
{
    // 06:00 on 1 March is near low water: the crest has too little water.
    private static readonly DateTimeOffset LowWaterDeparture = new(2026, 3, 1, 6, 0, 0, TimeSpan.Zero);

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record Setup(RoutesService Routes, Route Route, PassageCheckService Check, GlobalTimeService Time, FollowPassageService Follow, RouteFindingsService Findings) : IDisposable
    {
        public void Dispose()
        {
            Findings.Dispose();
            Check.Dispose();
        }
    }

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    [Fact]
    public void The_imported_route_carries_the_scenario_passage()
    {
        var route = RouteS421Mapping.FromS421(fixture.S421Route());

        Assert.Equal(fixture.RouteSpec.RouteId, route.Id);
        Assert.Equal(fixture.RouteSpec.Waypoints, route.Waypoints.Select(w => w.Name));
        Assert.Equal(fixture.RouteSpec.Passage.DraftMeters, route.Passage.DraftMeters);
        Assert.Equal(fixture.RouteSpec.Passage.SpeedKnots, route.Passage.SpeedKnots);
        Assert.Equal(fixture.RouteSpec.Passage.RequiredUkcMeters, route.Passage.RequiredUkcMeters);
        Assert.Equal(new DateTimeOffset(fixture.RouteSpec.Departure, TimeSpan.Zero), route.Passage.Departure);
        Assert.Null(route.Passage.SquatMeters); // S-421 has no squat
    }

    // #903: the window the viewer sweeps is the engine's, which the scenario
    // tests prove exact against the world model.
    [Fact]
    public async Task The_departure_window_is_the_engines_exact_sweep()
    {
        using var s = await CreateAsync(LowWaterDeparture);

        var window = Assert.IsType<PassageDepartureWindow>(s.Check.Window);
        var expected = EngineSweep(s.Route);

        Assert.Equal(PassageCheckService.WindowStep, window.Sweep.Step);
        Assert.Equal(expected.Options.Select(o => (o.Departure, o.Verdict)), window.Sweep.Options.Select(o => (o.Departure, o.Verdict)));
        Assert.Equal(4, window.Windows.Count);
        Assert.DoesNotContain(window.Windows, w => w.First <= LowWaterDeparture && LowWaterDeparture <= w.Last);
    }

    // #905: below the setting over the crest at low water, nothing inside the window.
    [Fact]
    public async Task The_findings_name_the_crest_at_low_water_and_nothing_inside_the_window()
    {
        using var s = await CreateAsync(LowWaterDeparture);
        var crest = fixture.Scenario.Definition.Place("shoal-crest");

        Assert.Equal(PassageVerdict.BelowSetting, s.Check.Current!.Result.Verdict);
        var finding = Assert.Single(s.Findings.Findings);
        Assert.Equal((RouteFindingCodes.BelowSetting, RouteFindingGroup.MustReview, RouteFindingCategory.Clearance, 0),
            (finding.Code, finding.Group, finding.Category, finding.LegIndex!.Value));
        var location = Assert.IsType<RouteFindingLocation>(finding.Location);
        Assert.InRange(crest.Latitude, location.Path.Min(p => p.Latitude), location.Path.Max(p => p.Latitude));

        s.Route.SetDeparture(InsideWindow(s));
        await s.Check.WhenIdle();

        Assert.True(s.Check.Current!.Result.Verdict is PassageVerdict.MeetsSettings or PassageVerdict.MeetsSettingsTight);
        Assert.Empty(s.Findings.Findings);
    }

    // #904: Follow passage pins the view time to the ETA at the minimum (the
    // crest), then at the cursor; both are the model's ETAs.
    [Fact]
    public async Task Follow_passage_shows_the_crest_and_then_the_cursor_at_their_ETAs()
    {
        using var s = await CreateAsync(LowWaterDeparture);
        var departure = InsideWindow(s);
        s.Route.SetDeparture(departure);
        await s.Check.WhenIdle();

        Assert.True(s.Follow.CanFollow);
        Assert.True(s.Follow.SetOn(true));

        var spec = fixture.RouteSpec with { Departure = departure.UtcDateTime };
        var distances = S421RouteBuilder.Distances(fixture.Scenario.Definition, spec);
        var crestEta = S421RouteBuilder.TimeAt(spec, distances[1]);
        Assert.Equal(s.Check.Current!.Result.Minimum!.Time.UtcDateTime, s.Time.CurrentTime);
        Assert.InRange((s.Time.CurrentTime!.Value - crestEta).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(30));

        s.Check.SetCursor(s.Check.Current.Result.Samples[^1].DistanceMeters);
        var arrival = S421RouteBuilder.TimeAt(spec, distances[^1]);
        Assert.Equal(s.Check.Cursor!.Value.Eta!.Value.UtcDateTime, s.Time.CurrentTime);
        Assert.InRange((s.Time.CurrentTime!.Value - arrival).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(30));

        // Choosing another time turns it off.
        s.Time.SetCurrentTime(departure.UtcDateTime);
        Assert.False(s.Follow.IsOn);
    }

    private async Task<Setup> CreateAsync(DateTimeOffset departure)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var definition = fixture.Scenario.Definition;
        var routes = new RoutesService();
        var route = RouteS421Mapping.FromS421(fixture.S421Route());
        route.SetPassageValue(RoutePassageField.Squat, fixture.RouteSpec.Passage.SquatMeters);
        route.SetDeparture(departure);
        routes.Routes.Add(route);
        routes.Routes.SetActiveRoute(route);

        var catalog = new PassageCheckFixture.FixedCatalog(fixture.Datasets);
        var check = new PassageCheckService(routes, catalog, debounce: TimeSpan.Zero);

        var time = new GlobalTimeService();
        var samples = CoverageWriters.Times(definition.WaterLevelStart, definition.WaterLevelEnd, TimeSpan.FromMinutes(10)).ToArray();
        time.ApplySnapshot(new MapsuiMapTimeSnapshot
        {
            Minimum = samples[0],
            Maximum = samples[^1],
            Current = samples[0],
            Samples = samples,
            CoverageSegments = [new MapsuiMapTimeSegment(samples[0], samples[^1])],
            Datasets =
            [
                new MapsuiMapTimedDataset("104ZZ00APPR_STN", samples[0], samples[^1]) { ProductSpec = "S-104", DatasetId = "104ZZ00APPR_STN", Samples = samples },
            ],
        });
        var follow = new FollowPassageService(check, time);
        var findings = new RouteFindingsService(check, routes, catalog, null, timeFormat: new Utc(), clock: new FixedClock(departure));

        routes.IsCheckModeActive = true;
        await check.WhenIdle();
        return new Setup(routes, route, check, time, follow, findings);
    }

    private DepartureSweep EngineSweep(Route route)
    {
        var sources = CatalogRouteCheckSources.FromDatasets(fixture.Datasets, new ProjNetCrsTransformFactory());
        var engine = new PassageCheckEngine(sources.Depth!, sources.WaterLevel!);
        var legs = RouteEtas.ToPassageLegs(route);
        var profile = engine.SampleDepths(legs, TestContext.Current.CancellationToken);
        return engine.SweepWaterLevelRange(profile, legs, PassageCheckService.WindowStep, PassageCheckService.MaxWindowDepartures, TestContext.Current.CancellationToken)!;
    }

    // The middle of the second window (the first starts with the data).
    private static DateTimeOffset InsideWindow(Setup s)
    {
        var (first, last) = s.Check.Window!.Windows[1];
        var middle = first + (last - first) / 2;
        return middle.AddTicks(-(middle.UtcTicks % PassageCheckService.WindowStep.Ticks));
    }
}
