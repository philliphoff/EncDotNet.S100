using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// "Approach with tide window" end to end (#903): the route read from its
/// S-421 file, the depths from S-102 and the tide from the S-104 stations,
/// swept over two days of departures. The departures that meet the settings
/// are exactly those for which every sample has water, computed from the
/// model's depths and the station's records.
/// </summary>
public sealed class ApproachWithTideWindowTests(ApproachWithTideWindowFixture fixture) : IClassFixture<ApproachWithTideWindowFixture>
{
    private static readonly DateTimeOffset SweepFrom = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SweepTo = new(2026, 3, 2, 23, 30, 0, TimeSpan.Zero);
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(10);

    private readonly FileDatasetCatalog _catalog = ScenarioLoading.Catalog(fixture.Scenario);

    private ScenarioDefinition Definition => fixture.Definition;

    private S421RouteSpec RouteSpec => Definition.Routes[0];

    [Fact]
    public void The_legs_come_from_the_S421_route()
    {
        var legs = ScenarioPassage.Legs(_catalog, RouteSpec);

        Assert.Equal(3, legs.Count);
        Assert.All(legs, leg =>
        {
            Assert.Equal(RouteSpec.Passage.DraftMeters, leg.DraftMeters);
            Assert.Equal(RouteSpec.Passage.SpeedKnots, leg.SpeedKnots);
            Assert.Equal(RouteSpec.Passage.RequiredUkcMeters, leg.RequiredUkcMeters);
        });
        Assert.Equal(S421RouteBuilder.Positions(Definition, RouteSpec), legs.Select(l => l.Points[0]).Append(legs[^1].Points[^1]));
    }

    [Fact]
    public void Every_sample_takes_its_depth_from_S102_and_its_tide_from_SYN_W()
    {
        var result = Check(SweepFrom);
        var surface = Definition.Surfaces[0];
        var grid = CoverageWriters.Grid(Definition.World, surface);

        Assert.NotEmpty(result.Samples);
        foreach (var sample in result.Samples)
        {
            var depth = Assert.IsType<ChartedDepth>(sample.Depth);
            Assert.Equal(ChartedDepthKind.Bathymetry, depth.Kind);
            var row = (int)Math.Floor((sample.Position.Latitude - grid.OriginLatitude) / grid.SpacingLatitudinal);
            var col = (int)Math.Floor((sample.Position.Longitude - grid.OriginLongitude) / grid.SpacingLongitudinal);
            Assert.Equal(CoverageWriters.S102Depth(Definition.World, grid.Node(row, col)), depth.DepthMeters, 4);
            Assert.Equal("SYN-W", sample.WaterLevel.Source?.StationId);
        }

        Assert.Equal(ConfidenceReasons.None, result.Confidence);
    }

    // #903: the departure sweep, with exact expected windows.
    [Fact]
    public void The_departure_windows_are_exactly_where_every_sample_has_water()
    {
        var (engine, legs) = Engine();
        var profile = engine.SampleDepths(legs, TestContext.Current.CancellationToken);
        var sweep = engine.SweepDepartures(profile, legs, SweepFrom, SweepTo, Step, TestContext.Current.CancellationToken);
        var expected = ExpectedMeets();

        Assert.Equal(expected.Count, sweep.Options.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.True(expected[i].Meets == sweep.Options[i].MeetsSettings, $"Departure {expected[i].Departure:O}: expected {(expected[i].Meets ? "meets" : "below")}, sweep says {sweep.Options[i].Verdict}.");
            Assert.Equal(expected[i].Departure, sweep.Options[i].Departure);
        }

        // Near each of the four high waters in the two days, and only there.
        var windows = sweep.Windows();
        Assert.Equal(4, windows.Count);
        Assert.All(windows, w => Assert.InRange((w.Last - w.First).TotalMinutes, 60, 240));
        Assert.All(sweep.Options.Where(o => !o.MeetsSettings), o => Assert.Equal(PassageVerdict.BelowSetting, o.Verdict));
    }

    [Fact]
    public void The_shoal_crest_bounds_every_window()
    {
        var (engine, legs) = Engine();
        var profile = engine.SampleDepths(legs, TestContext.Current.CancellationToken);
        var sweep = engine.SweepDepartures(profile, legs, SweepFrom, SweepTo, Step, TestContext.Current.CancellationToken);
        var crest = Definition.Place("shoal-crest");

        foreach (var (first, last) in sweep.Windows())
        {
            foreach (var departure in new[] { first - Step, last + Step }.Where(d => d >= SweepFrom && d <= SweepTo))
            {
                var minimum = Assert.IsType<UkcMinimum>(Check(departure).Minimum);
                Assert.True(Math.Abs(minimum.Position.Latitude - crest.Latitude) * WorldFrameMetres < 50, $"Minimum at {minimum.Position} for {departure:O}.");
            }
        }
    }

    // The windows follow the model's continuous tide at SYN-W to within a sweep step.
    [Fact]
    public void The_windows_agree_with_the_continuous_tide_within_a_step()
    {
        var (engine, legs) = Engine();
        var profile = engine.SampleDepths(legs, TestContext.Current.CancellationToken);
        var sweep = engine.SweepDepartures(profile, legs, SweepFrom, SweepTo, Step, TestContext.Current.CancellationToken);
        var crestSample = Check(SweepFrom).Samples.MinBy(s => s.Depth!.DepthMeters)!;
        var offset = crestSample.Time - SweepFrom;
        var station = Definition.World.Tide.Stations.Single(s => s.Identifier == "SYN-W");
        var needed = RouteSpec.Passage.DraftMeters + RouteSpec.Passage.SquatMeters + RouteSpec.Passage.RequiredUkcMeters - crestSample.Depth!.DepthMeters;

        foreach (var option in sweep.Options)
        {
            var tide = Definition.World.Tide.HeightAt(station.X, station.Y, (option.Departure + offset).UtcDateTime);
            if (Math.Abs(tide - needed) > 0.9 * Step.TotalHours)
            {
                Assert.Equal(tide >= needed, option.MeetsSettings);
            }
        }
    }

    private const double WorldFrameMetres = World.WorldFrame.MetresPerDegreeLatitude;

    private (PassageCheckEngine Engine, IReadOnlyList<PassageLeg> Legs) Engine()
    {
        var sources = PassageDataSources.FromDatasets(_catalog.Datasets, new Crs.ProjNet.ProjNetCrsTransformFactory());
        return (new PassageCheckEngine(sources.Depth, sources.WaterLevel), ScenarioPassage.Legs(_catalog, RouteSpec));
    }

    private PassageCheckResult Check(DateTimeOffset departure)
    {
        var (engine, legs) = Engine();
        return engine.Check(new PassagePlan(legs, departure), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Whether each departure meets the settings, from the samples' S-102
    /// depths (checked against the model above) and SYN-W's S-104 records
    /// interpolated linearly to each sample's ETA (S-104 Ed 2.0.0 §10.2.3).
    /// </summary>
    private List<(DateTimeOffset Departure, bool Meets)> ExpectedMeets()
    {
        var reference = Check(SweepFrom);
        var station = Assert.IsType<S104StationSeriesData>(ScenarioLoading.Dataset(_catalog, Definition.Stations!.DatasetName).Data)
            .Dataset.Stations.Single(s => s.Identifier == "SYN-W");
        var passage = RouteSpec.Passage;
        var expected = new List<(DateTimeOffset, bool)>();
        for (var departure = SweepFrom; departure <= SweepTo; departure += Step)
        {
            var meets = reference.Samples.All(sample =>
            {
                var eta = departure + (sample.Time - SweepFrom);
                var clearance = sample.Depth!.DepthMeters + Interpolate(station, eta.UtcDateTime) - passage.DraftMeters - passage.SquatMeters;
                return clearance >= passage.RequiredUkcMeters;
            });
            expected.Add((departure, meets));
        }

        return expected;
    }

    private static double Interpolate(WaterLevelStation station, DateTime utc)
    {
        var start = DateTime.SpecifyKind(station.StartTime, DateTimeKind.Utc);
        var position = (utc - start) / station.TimeRecordInterval;
        var k = (int)Math.Floor(position);
        var f = position - k;
        return f == 0 ? station.Heights[k] : station.Heights[k] + f * (station.Heights[k + 1] - station.Heights[k]);
    }
}

/// <summary>Builds passage legs from a scenario's S-421 file, as a host would.</summary>
public static class ScenarioPassage
{
    /// <summary>
    /// The legs of the route in <paramref name="spec"/>'s S-421 file: each
    /// leg's draft and static UKC, and the planned speed from the manual
    /// schedule. Squat isn't in S-421, so it comes from the scenario.
    /// </summary>
    public static IReadOnlyList<PassageLeg> Legs(FileDatasetCatalog catalog, S421RouteSpec spec)
    {
        var dataset = Assert.IsType<S421DatasetData>(ScenarioLoading.Dataset(catalog, spec.DatasetName).Data).Model;
        var route = S421RoutePlan.From(dataset, out var diagnostics).Route;
        Assert.Empty(diagnostics);
        var elements = route.Schedules[0].Variants[0].Elements;
        return route.Legs.Select((leg, i) => new PassageLeg(
            [leg.StartWaypoint!.Position, leg.EndWaypoint!.Position],
            leg.Draft!.Value,
            SpeedKnots: elements.Single(e => e.WaypointNumber == leg.StartWaypoint.WaypointNumber).PlannedSpeedOverGround,
            SquatMeters: spec.Passage.SquatMeters,
            RequiredUkcMeters: leg.StaticUnderKeelClearance)).ToList();
    }
}
