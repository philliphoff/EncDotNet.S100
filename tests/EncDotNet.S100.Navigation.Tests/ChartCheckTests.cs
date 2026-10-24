using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>
/// The chart check on synthetic squares near the equator: one leg due
/// east from 0° to 0,01° E (about 1 112 m), where 1° of longitude is
/// <see cref="PassageGeodesy.MetersPerDegreeLatitude"/> metres.
/// </summary>
public class ChartCheckTests
{
    private static readonly double M = PassageGeodesy.MetersPerDegreeLatitude;

    private static readonly PassageLeg East = Fixture.Leg([new GeoPosition(0, 0), new GeoPosition(0, 0.01)]);

    private static readonly FakeCell Harbour = new("101HARBOUR", 22_000);

    private static RouteCheckOutcome<ChartCheckResult> Check(FakeCharts charts, params PassageLeg[] legs) =>
        Checks.Chart(charts, new PassagePlan(legs.Length == 0 ? [East] : legs), Fixture.Settings());

    [Fact]
    public void Crossing_land_is_found_exactly_and_must_be_reviewed()
    {
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.004, 0.005);

        var result = Check(new FakeCharts([Harbour], [land]));

        var finding = Assert.Single(result.Findings);
        Assert.Equal(RouteFindingCodes.CrossesLand, finding.Code);
        Assert.Equal("route_check.crosses_land.0", finding.Id);
        Assert.Equal(RouteFindingGroup.MustReview, finding.Group);
        Assert.Equal(RouteFindingCategory.Chart, finding.Category);
        Assert.Equal(0, finding.LegIndex);
        Assert.Equal(0.004 * M, finding.Distance!.Value.TotalMetres, 3);
        var location = Assert.IsType<RouteFindingLocation>(finding.Location);
        Assert.False(location.IsPoint);
        Assert.Equal(0.004 * M, location.StartDistanceMeters, 3);
        Assert.Equal(0.005 * M, location.EndDistanceMeters, 3);
        Assert.Equal(0.004, location.Path[0].Longitude, 9);
        Assert.Equal(0.005, location.Path[^1].Longitude, 9);
        Assert.Equal(new RouteFindingFeature("101HARBOUR", "7") { FeatureType = "LandArea" }, finding.Feature);
        Assert.Equal("Crosses land", finding.Title);
        Assert.StartsWith("Leg 1 → 2", finding.Meta, StringComparison.Ordinal);
    }

    [Fact]
    public void Drying_ground_crossed_by_the_track_must_be_reviewed()
    {
        var drying = FakeCharts.Area(RouteChartFeatureKind.DryingArea, Harbour, "8", 0.002, 0.003);

        var finding = Assert.Single(Check(new FakeCharts([Harbour], [drying])).Findings);

        Assert.Equal(RouteFindingCodes.CrossesDrying, finding.Code);
        Assert.Equal(RouteFindingGroup.MustReview, finding.Group);
        Assert.Equal(0.002 * M, finding.Distance!.Value.TotalMetres, 3);
        Assert.Equal(0.003 * M, finding.Location!.EndDistanceMeters, 3);
    }

    [Fact]
    public void Adjacent_areas_of_one_kind_make_one_finding_from_the_first()
    {
        var a = FakeCharts.Area(RouteChartFeatureKind.DryingArea, Harbour, "1", 0.002, 0.003);
        var b = FakeCharts.Area(RouteChartFeatureKind.DryingArea, Harbour, "2", 0.003, 0.0045);

        var finding = Assert.Single(Check(new FakeCharts([Harbour], [b, a])).Findings);

        Assert.Equal("1", finding.Feature!.FeatureId);
        Assert.Equal(0.002 * M, finding.Location!.StartDistanceMeters, 3);
        Assert.Equal(0.0045 * M, finding.Location.EndDistanceMeters, 3);
    }

    [Fact]
    public void A_clear_track_has_no_findings()
    {
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.004, 0.005, south: 0.002, north: 0.003);

        var result = Check(new FakeCharts([Harbour], [land]));

        Assert.Empty(result.Findings);
        var corridor = Assert.Single(result.Detail!.Corridors);
        Assert.Equal(50, corridor.PortHalfWidthMeters);
        Assert.Equal(50, corridor.StarboardHalfWidthMeters);
        Assert.Equal(0.01 * M, result.Detail!.LengthMeters, 3);
    }

    [Fact]
    public void Land_inside_the_cross_track_limits_is_worth_checking()
    {
        // 40 m north of the track: inside the default 50 m half-width to port.
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.004, 0.005, south: 40 / M, north: 0.002);

        var finding = Assert.Single(Check(new FakeCharts([Harbour], [land])).Findings);

        Assert.Equal(RouteFindingCodes.LandInCorridor, finding.Code);
        Assert.Equal(RouteFindingGroup.Check, finding.Group);
        Assert.Equal(RouteFindingCategory.Chart, finding.Category);
        Assert.InRange(finding.Location!.StartDistanceMeters, 0.004 * M, 0.004 * M + 10);
        Assert.InRange(finding.Location.EndDistanceMeters, 0.005 * M - 10, 0.005 * M);
    }

    [Theory]
    // Land 40 m to port: a 30 m port limit misses it, whatever the starboard limit.
    [InlineData(30.0, 200.0, false)]
    [InlineData(45.0, 0.0, true)]
    public void Each_side_has_its_own_cross_track_limit(double port, double starboard, bool found)
    {
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.004, 0.005, south: 40 / M, north: 0.002);

        var result = Check(new FakeCharts([Harbour], [land]), Fixture.Leg(East.Points, port: port, starboard: starboard));

        Assert.Equal(found, result.Findings.Any(f => f.Code == RouteFindingCodes.LandInCorridor));
        Assert.Equal(port, result.Detail!.Corridors[0].PortHalfWidthMeters);
    }

    [Fact]
    public void The_corridor_override_replaces_every_legs_limits()
    {
        // Land 40 m to port, beside a leg whose own port limit (30 m) misses it.
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.004, 0.005, south: 40 / M, north: 0.002);
        var result = Checks.Chart(
            new FakeCharts([Harbour], [land]),
            new PassagePlan([Fixture.Leg(East.Points, port: 30, starboard: 30)]),
            new RouteCheckSettings { At = Fixture.Departure, CorridorOverride = Length.FromMetres(45) });

        Assert.Contains(result.Findings, f => f.Code == RouteFindingCodes.LandInCorridor);
        Assert.Equal((45, 45), (result.Detail!.Corridors[0].PortHalfWidthMeters, result.Detail!.Corridors[0].StarboardHalfWidthMeters));
    }

    [Fact]
    public void Leaving_chart_coverage_is_a_data_check()
    {
        // The cell covers only the western 60 % of the leg.
        var cell = new FakeCell("101WEST", 22_000, p => p.Longitude <= 0.006);

        var finding = Assert.Single(Check(new FakeCharts([cell], [])).Findings);

        Assert.Equal(RouteFindingCodes.OutsideChartCoverage, finding.Code);
        Assert.Equal(RouteFindingGroup.Check, finding.Group);
        Assert.Equal(RouteFindingCategory.Data, finding.Category);
        Assert.Null(finding.Feature);
        Assert.InRange(finding.Distance!.Value.TotalMetres, 0.006 * M - 10, 0.006 * M + 10);
        Assert.Equal(0.01 * M, finding.Location!.EndDistanceMeters, 3);
    }

    [Fact]
    public void A_route_with_no_charts_leaves_coverage_on_every_leg()
    {
        var second = Fixture.Leg([new GeoPosition(0, 0.01), new GeoPosition(0.01, 0.01)]);

        var result = Check(new FakeCharts([], []), East, second);

        Assert.Equal([0, 1], result.Findings.Select(f => f.LegIndex!.Value));
        Assert.All(result.Findings, f => Assert.Equal(RouteFindingCodes.OutsideChartCoverage, f.Code));
        Assert.Equal(0.01 * M, result.Findings[1].Location!.StartDistanceMeters, 3);
        Assert.Equal(0.02 * M, result.Findings[1].Location!.EndDistanceMeters, 1);
        Assert.Equal("Leg 2 → 3", result.Findings[1].Meta.Split(" · ")[0]);
    }

    [Fact]
    public void A_coarse_cells_land_is_ignored_where_a_finer_cell_covers()
    {
        var coastal = new FakeCell("101COASTAL", 350_000);
        var coarseLand = FakeCharts.Area(RouteChartFeatureKind.Land, coastal, "1", 0.004, 0.005);

        // The harbour cell covers the east half and charts water there.
        var harbour = new FakeCell("101HARBOUR", 22_000, p => p.Longitude >= 0.0045);
        var result = Check(new FakeCharts([coastal, harbour], [coarseLand]));

        // Only the western half, where the coastal cell is the finest, counts.
        var finding = Assert.Single(result.Findings);
        Assert.Equal("101COASTAL", finding.Feature!.DatasetId);
        Assert.Equal(0.004 * M, finding.Location!.StartDistanceMeters, 3);
        Assert.InRange(finding.Location.EndDistanceMeters, 0.0045 * M - 1, 0.0045 * M + 10);
    }

    [Fact]
    public void Cells_of_the_same_scale_both_count()
    {
        var a = new FakeCell("101A", 22_000);
        var b = new FakeCell("101B", 22_000);
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, b, "1", 0.004, 0.005);

        var finding = Assert.Single(Check(new FakeCharts([a, b], [land])).Findings);

        Assert.Equal("101B", finding.Feature!.DatasetId);
    }

    [Fact]
    public void A_coastline_without_land_is_a_land_crossing()
    {
        var coastline = new RouteChartFeature(RouteChartFeatureKind.Coastline, Harbour, "9", "Coastline", [],
            [[new GeoPosition(-0.001, 0.007), new GeoPosition(0.001, 0.007)]]);

        var finding = Assert.Single(Check(new FakeCharts([Harbour], [coastline])).Findings);

        Assert.Equal(RouteFindingCodes.CrossesLand, finding.Code);
        Assert.True(finding.Location!.IsPoint);
        Assert.Equal(0.007 * M, finding.Distance!.Value.TotalMetres, 3);
        Assert.Equal("Coastline", finding.Meta.Split(" · ")[^1]);
    }

    [Fact]
    public void A_coastline_on_charted_land_adds_nothing()
    {
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.007, 0.011);
        var coastline = new RouteChartFeature(RouteChartFeatureKind.Coastline, Harbour, "9", "Coastline", [],
            [[new GeoPosition(-0.001, 0.007), new GeoPosition(0.001, 0.007)]]);

        var finding = Assert.Single(Check(new FakeCharts([Harbour], [coastline, land])).Findings);

        Assert.Equal("7", finding.Feature!.FeatureId);
    }

    [Fact]
    public void A_hole_in_the_land_is_water()
    {
        var hole = FakeCharts.Ring(0.0045, 0.0055, -0.001, 0.001);
        var land = new RouteChartFeature(RouteChartFeatureKind.Land, Harbour, "7", "LandArea",
            [new SurfacePart(FakeCharts.Ring(0.004, 0.006, -0.002, 0.002), [hole])], []);

        var findings = Check(new FakeCharts([Harbour], [land])).Findings;

        Assert.Equal(2, findings.Count);
        Assert.Equal(0.0045 * M, findings[0].Location!.EndDistanceMeters, 3);
        Assert.Equal(0.0055 * M, findings[1].Location!.StartDistanceMeters, 3);
    }

    [Fact]
    public void A_track_that_starts_on_land_reports_it_from_departure()
    {
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", -0.001, 0.001);

        var finding = Assert.Single(Check(new FakeCharts([Harbour], [land])).Findings);

        Assert.Equal(0, finding.Distance?.TotalMetres);
        Assert.Equal(0.001 * M, finding.Location!.EndDistanceMeters, 3);
    }

    [Fact]
    public void Findings_sort_by_group_then_distance()
    {
        var cell = new FakeCell("101WEST", 22_000, p => p.Longitude <= 0.008);
        var drying = FakeCharts.Area(RouteChartFeatureKind.DryingArea, cell, "1", 0.005, 0.006);
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, cell, "2", 0.002, 0.003);

        var codes = Check(new FakeCharts([cell], [drying, land])).Findings.Select(f => f.Code);

        Assert.Equal([RouteFindingCodes.CrossesLand, RouteFindingCodes.CrossesDrying, RouteFindingCodes.OutsideChartCoverage], codes);
    }

    [Fact]
    public void Wording_can_be_replaced()
    {
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.004, 0.005);
        var options = new RouteCheckOptions { Formatter = new FrenchFormatter() };

        var finding = Assert.Single(Checks.Chart(new FakeCharts([Harbour], [land]), new PassagePlan([East]), Fixture.Settings(), options: options).Findings);

        Assert.Equal("Traverse la terre", finding.Title);
        Assert.StartsWith("Segment 1–2 / ", finding.Meta, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cancelled_check_throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            Checks.Chart(new FakeCharts([Harbour], []), new PassagePlan([East]), Fixture.Settings(), cancellationToken: cts.Token));
    }

    [Fact]
    public void A_leg_needs_two_points()
    {
        Assert.Throws<ArgumentException>(() =>
            Checks.Chart(new FakeCharts([], []), new PassagePlan([Fixture.Leg([new GeoPosition(0, 0)])]), Fixture.Settings()));
    }

    [Fact]
    public void Without_charts_the_check_is_skipped()
    {
        var outcome = Checks.Run(new ChartCheck(), new PassagePlan([East]), Fixture.Settings(), RouteCheckSources.Empty);

        Assert.Equal((RouteCheckState.Skipped, RouteCheckReasons.ChartsMissing), (outcome.State, outcome.Reason));
        Assert.Empty(outcome.Findings);
        Assert.Null(outcome.Detail);
    }

    [Fact]
    public void The_untyped_run_gives_the_same_outcome_as_the_typed_one()
    {
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.004, 0.005);
        var context = new RouteCheckContext(new PassagePlan([East]), Fixture.Settings(), new RouteCheckSources { Charts = new FakeCharts([Harbour], [land]) });
        IRouteCheck check = new ChartCheck();

        var outcome = check.Run(context, TestContext.Current.CancellationToken);

        var typed = Assert.IsType<RouteCheckOutcome<ChartCheckResult>>(outcome);
        Assert.Same(typed.Detail, outcome.Detail);
        Assert.Equal(RouteFindingCodes.CrossesLand, Assert.Single(outcome.Findings).Code);
    }

    [Fact]
    public void The_last_sweep_is_kept_while_only_the_time_changes()
    {
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.004, 0.005);
        var charts = new CountingCharts(new FakeCharts([Harbour], [land]));
        var sources = new RouteCheckSources { Charts = charts };
        var check = new ChartCheck();

        var first = Checks.Run(check, new PassagePlan([East], Fixture.Departure), Fixture.Settings(), sources);
        var later = Checks.Run(check, new PassagePlan([East], Fixture.Departure.AddHours(2)), Fixture.Settings(Fixture.Departure.AddHours(2)), sources);
        Assert.Equal(1, charts.Calls);
        Assert.Same(first.Detail, later.Detail);

        // Other options or a wider default limit sweep again.
        Checks.Run(check, new PassagePlan([East]), Fixture.Settings(), sources, new RouteCheckOptions { Chart = new ChartCheckOptions { StationSpacingMeters = 5 } });
        Checks.Run(check, new PassagePlan([East]), Fixture.Settings(), sources, new RouteCheckOptions { DefaultLimit = Length.FromMetres(80) });
        Assert.Equal(3, charts.Calls);
    }

    [Fact]
    public void Options_that_cant_sweep_are_refused()
    {
        var options = new RouteCheckOptions { Chart = new ChartCheckOptions { StationSpacingMeters = 0 } };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Checks.Chart(new FakeCharts([Harbour], []), new PassagePlan([East]), Fixture.Settings(), options: options));
    }

    [Theory]
    [InlineData("LandArea", SkinOfTheEarthClass.Land)]
    [InlineData("DepthArea", SkinOfTheEarthClass.Water)]
    [InlineData("DredgedArea", SkinOfTheEarthClass.Water)]
    [InlineData("UnsurveyedArea", SkinOfTheEarthClass.Water)]
    [InlineData("Wreck", SkinOfTheEarthClass.None)]
    public void Skin_of_the_earth_classifies_group_1_areas(string featureType, SkinOfTheEarthClass expected)
    {
        Assert.Equal(expected, SkinOfTheEarth.Classify(featureType));
    }

    [Theory]
    [InlineData("DepthArea", "depthRangeMinimumValue", "-1.5", true)]
    [InlineData("DepthArea", "DRVAL1", "-0.1", true)]
    [InlineData("DepthArea", "depthRangeMinimumValue", "0", false)]
    [InlineData("DepthArea", "depthRangeMinimumValue", "2", false)]
    [InlineData("DepthArea", "depthRangeMaximumValue", "0", true)]
    [InlineData("DredgedArea", "depthRangeMinimumValue", "-1", false)]
    public void A_depth_area_dries_below_the_datum(string featureType, string code, string value, bool drying)
    {
        Assert.Equal(drying, SkinOfTheEarth.IsDrying(featureType, new Dictionary<string, object?> { [code] = value }));
    }
}

/// <summary>A chart cell with a fixed rank and a coverage test (everywhere by default).</summary>
internal sealed class FakeCell(string datasetId, int? rankingScale, Func<GeoPosition, bool>? covers = null) : RouteChartCell(datasetId)
{
    public override int? RankingScale { get; } = rankingScale;

    public override bool Covers(GeoPosition position) => covers?.Invoke(position) ?? true;
}

/// <summary>Charted features that are all "near" any corridor; the check sorts them out.</summary>
internal sealed class FakeCharts(IReadOnlyList<RouteChartCell> cells, IReadOnlyList<RouteChartFeature> features) : IRouteChartSource
{
    public IReadOnlyList<RouteChartCell> CellsNear(BoundingBox extent) => cells;

    public IReadOnlyList<RouteChartFeature> FeaturesInCorridor(IReadOnlyList<GeoPosition> corridor, BoundingBox extent) => features;

    /// <summary>A rectangular area from <paramref name="west"/> to <paramref name="east"/> (degrees), straddling the equator by default.</summary>
    public static RouteChartFeature Area(RouteChartFeatureKind kind, RouteChartCell cell, string id, double west, double east, double south = -0.001, double north = 0.001) =>
        new(kind, cell, id, kind == RouteChartFeatureKind.Land ? "LandArea" : "DepthArea", [new SurfacePart(Ring(west, east, south, north), [])], []);

    public static IReadOnlyList<GeoPosition> Ring(double west, double east, double south, double north) =>
    [
        new(south, west),
        new(south, east),
        new(north, east),
        new(north, west),
        new(south, west),
    ];
}

/// <summary>Counts the corridor queries of another chart source.</summary>
internal sealed class CountingCharts(IRouteChartSource inner) : IRouteChartSource
{
    public int Calls { get; private set; }

    public IReadOnlyList<RouteChartCell> CellsNear(BoundingBox extent) => inner.CellsNear(extent);

    public IReadOnlyList<RouteChartFeature> FeaturesInCorridor(IReadOnlyList<GeoPosition> corridor, BoundingBox extent)
    {
        Calls++;
        return inner.FeaturesInCorridor(corridor, extent);
    }
}

/// <summary>A formatter with other wording, as a host with its own resources has.</summary>
internal sealed class FrenchFormatter : RouteFindingFormatter
{
    protected override string CrossesLand => "Traverse la terre";

    protected override string LegFormat => "Segment {0}–{1}";

    protected override string Separator => " / ";
}
