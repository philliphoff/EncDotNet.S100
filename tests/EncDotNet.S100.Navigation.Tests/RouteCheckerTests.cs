using EncDotNet.S100.DataModel;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>
/// The checker over synthetic data near the equator: the verdict, the status
/// of each check, and what it reuses between runs.
/// </summary>
public class RouteCheckerTests
{
    private static readonly double M = PassageGeodesy.MetersPerDegreeLatitude;

    private static readonly FakeCell Harbour = new("101HARBOUR", 22_000);

    private static readonly GeoPosition A = new(0, 0);

    private static readonly GeoPosition B = new(0, 0.05);

    private static readonly GeoPosition C = new(0, 0.1);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Two legs, A → B → C, with a draft and a speed.</summary>
    private static PassagePlan TwoLegs(GeoPosition? end = null, double? draft = 10, DateTimeOffset? departure = null) =>
        new(
        [
            Fixture.Leg([A, B], draft: draft, speed: 6, required: 2, name: "Leg 1 → 2"),
            Fixture.Leg([B, end ?? C], draft: 10, speed: 6, required: 2, name: "Leg 2 → 3"),
        ],
        departure ?? Fixture.Departure);

    private static RouteCheckSettings DepthSettings(DateTimeOffset? at = null) => new()
    {
        At = at ?? Fixture.Departure,
        SafetyContour = Depth.FromMetres(10),
        SafetyDepth = Depth.FromMetres(10),
    };

    [Fact]
    public void Without_sources_every_check_says_why_it_was_skipped()
    {
        var report = new RouteChecker(RouteCheckSources.Empty).Check(TwoLegs(), Fixture.Settings(), Token);

        Assert.Equal(
        [
            new RouteCheckStatus("chart", RouteCheckState.Skipped, RouteCheckReasons.ChartsMissing, 0),
            new RouteCheckStatus("areas", RouteCheckState.Skipped, RouteCheckReasons.AreasMissing, 0),
            new RouteCheckStatus("warnings", RouteCheckState.Skipped, RouteCheckReasons.WarningsMissing, 0),
            new RouteCheckStatus("clearance", RouteCheckState.Skipped, RouteCheckReasons.DepthMissing, 0),
        ],
        report.Checks);
        Assert.Empty(report.Findings);
        Assert.Equal(new RouteCheckVerdict(RouteCheckVerdictKind.Clear, null), report.Verdict);
        Assert.Null(report.Chart);
        Assert.Null(report.Clearance);
    }

    [Fact]
    public void The_chart_check_without_safety_settings_runs_only_in_part()
    {
        var sources = new RouteCheckSources { Charts = new FakeCharts([Harbour], []) };

        var partial = new RouteChecker(sources).Check(TwoLegs(), Fixture.Settings(), Token).Status(ChartCheck.CheckName);
        var full = new RouteChecker(sources).Check(TwoLegs(), DepthSettings(), Token).Status(ChartCheck.CheckName);

        Assert.Equal(new RouteCheckStatus("chart", RouteCheckState.Partial, RouteCheckReasons.SafetySettingsMissing, 0), partial);
        Assert.Equal(new RouteCheckStatus("chart", RouteCheckState.Ran, null, 0), full);
    }

    [Fact]
    public void A_leg_without_a_draft_skips_the_clearance_check_but_not_the_others()
    {
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.02, 0.03);
        var sources = new RouteCheckSources
        {
            Charts = new FakeCharts([Harbour], [land]),
            Depth = new FakeDepth((_, _) => FakeDepth.Bathymetry(15)),
            WaterLevel = FakeWater.Constant(0),
        };

        var report = new RouteChecker(sources).Check(TwoLegs(draft: null), Fixture.Settings(), Token);

        Assert.Equal(new RouteCheckStatus("clearance", RouteCheckState.Skipped, RouteCheckReasons.DraftMissing, 0), report.Status(ClearanceCheck.CheckName));
        Assert.Null(report.Clearance);
        Assert.Equal(RouteFindingCodes.CrossesLand, Assert.Single(report.Findings).Code);
    }

    [Fact]
    public void Crossing_land_is_must_review_and_names_the_verdict_over_a_shallower_clearance()
    {
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.07, 0.08);
        var sources = new RouteCheckSources
        {
            Charts = new FakeCharts([Harbour], [land]),
            Depth = new FakeDepth((_, lon) => FakeDepth.Bathymetry(lon < 0.01 ? 11 : 15)),
            WaterLevel = FakeWater.Constant(0),
        };

        var report = new RouteChecker(sources).Check(TwoLegs(), Fixture.Settings(), Token);

        Assert.Equal(RouteCheckVerdictKind.MustReview, report.Verdict.Kind);
        // Below the UKC setting comes first by distance, but land ranks first in the verdict.
        Assert.Equal(RouteFindingCodes.BelowSetting, report.Findings[0].Code);
        Assert.Equal(RouteFindingCodes.CrossesLand, report.Verdict.Headline!.Code);
        Assert.Equal(new RouteFindingCounts(2, 0, 0), report.Counts);
        Assert.Equal(PassageVerdict.BelowSetting, report.Clearance!.Verdict);
        Assert.Equal(new RouteCheckStatus("clearance", RouteCheckState.Ran, null, 1), report.Status(ClearanceCheck.CheckName));
    }

    [Fact]
    public void Below_the_ukc_setting_alone_names_the_verdict()
    {
        var sources = new RouteCheckSources
        {
            Depth = new FakeDepth((_, _) => FakeDepth.Bathymetry(11)),
            WaterLevel = FakeWater.Constant(0),
        };

        var report = new RouteChecker(sources).Check(TwoLegs(), Fixture.Settings(), Token);

        Assert.Equal(RouteCheckVerdictKind.MustReview, report.Verdict.Kind);
        Assert.Equal(RouteFindingCodes.BelowSetting, report.Verdict.Headline!.Code);
    }

    [Fact]
    public void Water_level_ending_before_arrival_gives_the_neutral_verdict()
    {
        // The water level ends 30 minutes after departure; the passage takes about an hour.
        var end = Fixture.Departure.UtcDateTime.AddMinutes(30);
        var sources = new RouteCheckSources
        {
            Depth = new FakeDepth((_, _) => FakeDepth.Bathymetry(15)),
            WaterLevel = new FakeWater((_, _) => [new FakeSeries(Fixture.Departure.UtcDateTime.AddHours(-1), end, _ => 0)]),
        };

        var report = new RouteChecker(sources).Check(TwoLegs(), Fixture.Settings(), Token);

        Assert.Equal(RouteCheckVerdictKind.WaterLevelEndsBeforeArrival, report.Verdict.Kind);
        Assert.Equal(RouteFindingCodes.WaterLevelEnds, report.Verdict.Headline!.Code);
    }

    [Fact]
    public void Land_in_the_corridor_is_check_and_names_the_verdict()
    {
        // 40 m north of the track: inside the default 50 m half-width to port.
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.02, 0.03, south: 40 / M, north: 0.002);
        var sources = new RouteCheckSources { Charts = new FakeCharts([Harbour], [land]) };

        var report = new RouteChecker(sources).Check(TwoLegs(), Fixture.Settings(), Token);

        Assert.Equal(RouteCheckVerdictKind.Check, report.Verdict.Kind);
        Assert.Equal(RouteFindingCodes.LandInCorridor, report.Verdict.Headline!.Code);
        Assert.Equal(["101HARBOUR"], report.DatasetsUsed);
    }

    [Fact]
    public void The_default_limit_applies_to_every_check()
    {
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.02, 0.03, south: 40 / M, north: 0.002);
        var sources = new RouteCheckSources { Charts = new FakeCharts([Harbour], [land]) };
        var options = RouteCheckOptions.Default with { DefaultLimit = Length.FromMetres(30) };

        var report = new RouteChecker(sources, options).Check(TwoLegs(), Fixture.Settings(), Token);

        Assert.Empty(report.Findings);
        Assert.Equal(30, report.Track.Legs[0].PortMeters);
        Assert.Equal(30, report.Chart!.Corridors[0].PortHalfWidthMeters);
    }

    [Fact]
    public void The_options_formatter_renders_every_finding()
    {
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.02, 0.03);
        var sources = new RouteCheckSources { Charts = new FakeCharts([Harbour], [land]) };

        var report = new RouteChecker(sources, RouteCheckOptions.Default with { Formatter = new FrenchFormatter() }).Check(TwoLegs(), Fixture.Settings(), Token);

        Assert.Equal("Traverse la terre", Assert.Single(report.Findings).Title);
    }

    [Fact]
    public void Changing_the_departure_reuses_the_chart_sweep_and_the_depth_samples()
    {
        var charts = new CountingCharts(new FakeCharts([Harbour], []));
        var depth = new FakeDepth((_, _) => FakeDepth.Bathymetry(15));
        var checker = new RouteChecker(new RouteCheckSources { Charts = charts, Depth = depth, WaterLevel = FakeWater.Constant(0) });

        var first = checker.Check(TwoLegs(), DepthSettings(), Token);
        var (chartCalls, depthCalls) = (charts.Calls, depth.Calls);
        var later = checker.Check(TwoLegs(departure: Fixture.Departure.AddHours(1)), DepthSettings(), Token);

        Assert.True(chartCalls > 0 && depthCalls > 0);
        Assert.Equal(chartCalls, charts.Calls);
        Assert.Equal(depthCalls, depth.Calls);
        Assert.Same(first.Chart, later.Chart);
        Assert.Equal(Fixture.Departure.AddHours(1), later.Clearance!.Departure);
    }

    [Fact]
    public void Changing_the_safety_settings_sweeps_the_charts_again()
    {
        var charts = new CountingCharts(new FakeCharts([Harbour], []));
        var checker = new RouteChecker(new RouteCheckSources { Charts = charts });

        checker.Check(TwoLegs(), Fixture.Settings(), Token);
        var calls = charts.Calls;
        checker.Check(TwoLegs(), DepthSettings(), Token);

        Assert.True(charts.Calls > calls);
    }

    [Fact]
    public void Moving_the_last_waypoint_resamples_only_the_last_leg()
    {
        var depth = new FakeDepth((_, _) => FakeDepth.Bathymetry(15));
        var checker = new RouteChecker(new RouteCheckSources { Depth = depth, WaterLevel = FakeWater.Constant(0) });

        checker.Check(TwoLegs(), Fixture.Settings(), Token);
        var all = depth.Calls;
        checker.Check(TwoLegs(end: new GeoPosition(0.001, 0.1)), Fixture.Settings(), Token);
        var moved = depth.Calls - all;

        Assert.InRange(moved, 1, (all / 2) + 2);

        // A fresh checker samples both legs again.
        var fresh = new FakeDepth((_, _) => FakeDepth.Bathymetry(15));
        new RouteChecker(new RouteCheckSources { Depth = fresh, WaterLevel = FakeWater.Constant(0) })
            .Check(TwoLegs(end: new GeoPosition(0.001, 0.1)), Fixture.Settings(), Token);
        Assert.True(fresh.Calls > moved);
    }

    [Fact]
    public void A_departure_window_is_swept_once_while_only_the_departure_changes()
    {
        var checker = new RouteChecker(new RouteCheckSources
        {
            Depth = new FakeDepth((_, _) => FakeDepth.Bathymetry(15)),
            WaterLevel = FakeWater.Constant(0),
        });
        var window = new RouteCheckSettings { At = Fixture.Departure, DepartureWindow = DepartureRange.WaterLevelData(TimeSpan.FromHours(1)) };

        var first = checker.Check(TwoLegs(), window, Token);
        var later = checker.Check(TwoLegs(departure: Fixture.Departure.AddMinutes(30)), window, Token);
        var none = checker.Check(TwoLegs(), Fixture.Settings(), Token);

        Assert.NotNull(first.Departures);
        Assert.NotEmpty(first.Departures!.Options);
        Assert.Same(first.Departures, later.Departures);
        Assert.Null(none.Departures);
    }

    [Fact]
    public void A_fixed_departure_window_sweeps_every_step()
    {
        var checker = new RouteChecker(new RouteCheckSources
        {
            Depth = new FakeDepth((_, _) => FakeDepth.Bathymetry(15)),
            WaterLevel = FakeWater.Constant(0),
        });
        var range = DepartureRange.Between(Fixture.Departure, Fixture.Departure.AddHours(2), TimeSpan.FromHours(1));

        var report = checker.Check(TwoLegs(), new RouteCheckSettings { At = Fixture.Departure, DepartureWindow = range }, Token);

        Assert.Equal(3, report.Departures!.Options.Count);
    }

    [Fact]
    public void A_custom_check_runs_after_the_built_in_ones_and_its_findings_join_the_list()
    {
        var custom = new FixedCheck("ferries", RouteCheckOutcome.Ran(
        [
            new RouteFinding("custom.0", RouteFindingCodes.CrossesLand) { Distance = Length.FromMetres(10) },
        ],
        detail: "ok"));

        var report = new RouteChecker(RouteCheckSources.Empty, additionalChecks: [custom]).Check(TwoLegs(), Fixture.Settings(), Token);

        Assert.Equal(["chart", "areas", "warnings", "clearance", "ferries"], report.Checks.Select(c => c.Name));
        Assert.Equal("custom.0", report.Verdict.Headline!.Id);
        Assert.Equal("ok", report.Detail<string>("ferries"));
        Assert.Same(report.Track, custom.Context!.Track);
        Assert.Same(report.Schedule, custom.Context.Schedule);
    }

    [Fact]
    public void Checks_with_the_same_name_are_refused()
    {
        var a = new FixedCheck("x", RouteCheckOutcome.Skipped("no"));

        Assert.Throws<ArgumentException>(() => new RouteChecker(RouteCheckSources.Empty, [a, new FixedCheck("x", RouteCheckOutcome.Skipped("no"))]));
    }

    [Fact]
    public void Only_the_given_checks_run()
    {
        var report = new RouteChecker(RouteCheckSources.Empty, [new ClearanceCheck()]).Check(TwoLegs(), Fixture.Settings(), Token);

        Assert.Equal(["clearance"], report.Checks.Select(c => c.Name));
    }

    [Fact]
    public void The_track_gives_positions_and_paths_across_legs()
    {
        var plan = TwoLegs();
        var track = RouteTrack.Build(plan, Fixture.Settings(), Length.FromMetres(50));
        var leg = 0.05 * M;

        Assert.Equal(2 * leg, track.LengthMeters, 3);
        Assert.Equal(leg, track.Legs[1].StartMeters, 3);
        Assert.Equal(0, track.LegAt(leg));
        Assert.Equal(1, track.LegAt(leg + 1));
        Assert.Equal(0.075, track.PositionAt(1.5 * leg).Longitude, 9);
        Assert.Equal(C, track.PositionAt(10 * leg));

        var path = track.PathBetween(0.5 * leg, 1.5 * leg);
        Assert.Equal([0.025, 0.05, 0.075], path.Select(p => Math.Round(p.Longitude, 9)));
        Assert.True(track.Location(leg, leg).IsPoint);
    }

    [Fact]
    public void A_great_circle_path_follows_the_curve()
    {
        var plan = new PassagePlan([Fixture.Leg([new GeoPosition(50, -10), new GeoPosition(50, 10)], geometry: LegGeometry.GreatCircle)]);
        var track = RouteTrack.Build(plan, Fixture.Settings(), Length.FromMetres(50));

        var path = track.PathBetween(0, track.LengthMeters);

        Assert.Equal(RouteTrack.GeodesicSegments + 1, path.Count);
        Assert.True(path[path.Count / 2].Latitude > 50.3);
    }

    /// <summary>A check that returns a fixed outcome and keeps the context it was given.</summary>
    private sealed class FixedCheck(string name, RouteCheckOutcome outcome) : IRouteCheck
    {
        public string Name => name;

        public RouteCheckContext? Context { get; private set; }

        public RouteCheckOutcome Run(RouteCheckContext context, CancellationToken cancellationToken)
        {
            Context = context;
            return outcome;
        }
    }
}
