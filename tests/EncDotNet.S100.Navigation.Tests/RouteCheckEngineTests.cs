using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>
/// The route-check engine on synthetic squares near the equator: one leg due
/// east from 0° to 0,01° E (about 1 112 m), where 1° of longitude is
/// <see cref="PassageGeodesy.MetersPerDegreeLatitude"/> metres.
/// </summary>
public class RouteCheckEngineTests
{
    private static readonly double M = PassageGeodesy.MetersPerDegreeLatitude;

    private static readonly PassageLeg East = Fixture.Leg([new GeoPosition(0, 0), new GeoPosition(0, 0.01)]);

    private static readonly FakeCell Harbour = new("101HARBOUR", 22_000);

    private static RouteCheckResult Check(FakeCharts charts, params PassageLeg[] legs) =>
        new RouteCheckEngine(charts).Check(new PassagePlan(legs.Length == 0 ? [East] : legs), Fixture.Settings(), TestContext.Current.CancellationToken);

    [Fact]
    public void Crossing_land_is_found_exactly_and_must_be_reviewed()
    {
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.004, 0.005);

        var result = Check(new FakeCharts([Harbour], [land]));

        var finding = Assert.Single(result.Findings);
        Assert.Equal(RouteCheckCodes.CrossesLand, finding.Code);
        Assert.Equal("route_check.crosses_land.0", finding.Id);
        Assert.Equal(RouteFindingGroup.MustReview, finding.Group);
        Assert.Equal(RouteFindingSource.RouteCheck, finding.Source);
        Assert.Equal(0, finding.LegIndex);
        Assert.Equal(0.004 * M, finding.DistanceMeters!.Value, 3);
        var location = Assert.IsType<RouteFindingLocation>(finding.Location);
        Assert.False(location.IsPoint);
        Assert.Equal(0.004 * M, location.StartDistanceMeters, 3);
        Assert.Equal(0.005 * M, location.EndDistanceMeters, 3);
        Assert.Equal(0.004, location.Path[0].Longitude, 9);
        Assert.Equal(0.005, location.Path[^1].Longitude, 9);
        Assert.Equal(new RouteFindingFeature("101HARBOUR", "7"), finding.Feature);
        Assert.Equal("Crosses land", finding.Title);
        Assert.StartsWith("Leg 1 → 2", finding.Meta, StringComparison.Ordinal);
    }

    [Fact]
    public void Drying_ground_crossed_by_the_track_must_be_reviewed()
    {
        var drying = FakeCharts.Area(RouteChartFeatureKind.DryingArea, Harbour, "8", 0.002, 0.003);

        var finding = Assert.Single(Check(new FakeCharts([Harbour], [drying])).Findings);

        Assert.Equal(RouteCheckCodes.CrossesDrying, finding.Code);
        Assert.Equal(RouteFindingGroup.MustReview, finding.Group);
        Assert.Equal(0.002 * M, finding.DistanceMeters!.Value, 3);
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
        var corridor = Assert.Single(result.Corridors);
        Assert.Equal(50, corridor.PortHalfWidthMeters);
        Assert.Equal(50, corridor.StarboardHalfWidthMeters);
        Assert.Equal(0.01 * M, result.LengthMeters, 3);
    }

    [Fact]
    public void Land_inside_the_cross_track_limits_is_worth_checking()
    {
        // 40 m north of the track: inside the default 50 m half-width to port.
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.004, 0.005, south: 40 / M, north: 0.002);

        var finding = Assert.Single(Check(new FakeCharts([Harbour], [land])).Findings);

        Assert.Equal(RouteCheckCodes.LandInCorridor, finding.Code);
        Assert.Equal(RouteFindingGroup.Check, finding.Group);
        Assert.Equal(RouteFindingSource.RouteCheck, finding.Source);
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

        Assert.Equal(found, result.Findings.Any(f => f.Code == RouteCheckCodes.LandInCorridor));
        Assert.Equal(port, result.Corridors[0].PortHalfWidthMeters);
    }

    [Fact]
    public void The_corridor_override_replaces_every_legs_limits()
    {
        // Land 40 m to port, beside a leg whose own port limit (30 m) misses it.
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.004, 0.005, south: 40 / M, north: 0.002);
        var result = new RouteCheckEngine(new FakeCharts([Harbour], [land])).Check(
            new PassagePlan([Fixture.Leg(East.Points, port: 30, starboard: 30)]),
            new RouteCheckSettings { At = Fixture.Departure, CorridorOverride = Length.FromMetres(45) },
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Findings, f => f.Code == RouteCheckCodes.LandInCorridor);
        Assert.Equal((45, 45), (result.Corridors[0].PortHalfWidthMeters, result.Corridors[0].StarboardHalfWidthMeters));
    }

    [Fact]
    public void Leaving_chart_coverage_is_a_data_check()
    {
        // The cell covers only the western 60 % of the leg.
        var cell = new FakeCell("101WEST", 22_000, p => p.Longitude <= 0.006);

        var finding = Assert.Single(Check(new FakeCharts([cell], [])).Findings);

        Assert.Equal(RouteCheckCodes.OutsideChartCoverage, finding.Code);
        Assert.Equal(RouteFindingGroup.Check, finding.Group);
        Assert.Equal(RouteFindingSource.Data, finding.Source);
        Assert.Null(finding.Feature);
        Assert.InRange(finding.DistanceMeters!.Value, 0.006 * M - 10, 0.006 * M + 10);
        Assert.Equal(0.01 * M, finding.Location!.EndDistanceMeters, 3);
    }

    [Fact]
    public void A_route_with_no_charts_leaves_coverage_on_every_leg()
    {
        var second = Fixture.Leg([new GeoPosition(0, 0.01), new GeoPosition(0.01, 0.01)]);

        var result = Check(new FakeCharts([], []), East, second);

        Assert.Equal([0, 1], result.Findings.Select(f => f.LegIndex!.Value));
        Assert.All(result.Findings, f => Assert.Equal(RouteCheckCodes.OutsideChartCoverage, f.Code));
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

        Assert.Equal(RouteCheckCodes.CrossesLand, finding.Code);
        Assert.True(finding.Location!.IsPoint);
        Assert.Equal(0.007 * M, finding.DistanceMeters!.Value, 3);
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

        Assert.Equal(0, finding.DistanceMeters);
        Assert.Equal(0.001 * M, finding.Location!.EndDistanceMeters, 3);
    }

    [Fact]
    public void Findings_sort_by_group_then_distance()
    {
        var cell = new FakeCell("101WEST", 22_000, p => p.Longitude <= 0.008);
        var drying = FakeCharts.Area(RouteChartFeatureKind.DryingArea, cell, "1", 0.005, 0.006);
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, cell, "2", 0.002, 0.003);

        var codes = Check(new FakeCharts([cell], [drying, land])).Findings.Select(f => f.Code);

        Assert.Equal([RouteCheckCodes.CrossesLand, RouteCheckCodes.CrossesDrying, RouteCheckCodes.OutsideChartCoverage], codes);
    }

    [Fact]
    public void Wording_can_be_replaced()
    {
        var land = FakeCharts.Area(RouteChartFeatureKind.Land, Harbour, "7", 0.004, 0.005);
        var options = new RouteCheckOptions
        {
            Text = new RouteCheckText { CrossesLand = "Traverse la terre", LegFormat = "Segment {0}–{1}", Separator = " / " },
        };

        var finding = Assert.Single(new RouteCheckEngine(new FakeCharts([Harbour], [land]), options).Check(new PassagePlan([East]), Fixture.Settings(), TestContext.Current.CancellationToken).Findings);

        Assert.Equal("Traverse la terre", finding.Title);
        Assert.StartsWith("Segment 1–2 / ", finding.Meta, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cancelled_check_throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new RouteCheckEngine(new FakeCharts([Harbour], [])).Check(new PassagePlan([East]), Fixture.Settings(), cts.Token));
    }

    [Fact]
    public void A_leg_needs_two_points()
    {
        Assert.Throws<ArgumentException>(() =>
            new RouteCheckEngine(new FakeCharts([], [])).Check(new PassagePlan([Fixture.Leg([new GeoPosition(0, 0)])]), Fixture.Settings(), TestContext.Current.CancellationToken));
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

/// <summary>Charted features that are all "near" any corridor; the engine sorts them out.</summary>
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
