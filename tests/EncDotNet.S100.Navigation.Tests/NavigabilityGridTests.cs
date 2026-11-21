using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>The navigability grid (#1558) over fake masks and depths.</summary>
public sealed class NavigabilityGridTests
{
    // About 1.1 km square near the equator, in 100 m cells (11 x 11).
    private static readonly BoundingBox Box = new(0, 0, 0.01, 0.01);

    [Fact]
    public void Cells_take_the_mask_and_the_least_depth_at_the_centre_and_corners()
    {
        // Land west of 0.002°; 10 m everywhere else except a 3 m shoal east of 0.008°.
        var grid = Build(
            mask: p => p.Longitude < 0.002 ? WaterMaskClass.Land : WaterMaskClass.Water,
            depth: (lat, lon) => lon > 0.008 ? 3 : 10,
            minimumDepth: 5);

        Assert.Equal(NavigabilityCellClass.Land, grid.ClassAt(5, 0));
        Assert.Equal(NavigabilityCellClass.Navigable, grid.ClassAt(5, 5));
        Assert.Equal(NavigabilityCellClass.Shoal, grid.ClassAt(5, grid.Columns - 1));
        Assert.Equal(10, grid.LeastDepthAt(5, 5));

        // The cell whose east corners are past the shoal line takes the 3 m.
        var edge = Enumerable.Range(0, grid.Columns).First(c => grid.ClassAt(5, c) == NavigabilityCellClass.Shoal);
        Assert.True(grid.CenterOf(new NavigabilityCell(5, edge)).Longitude < 0.008 + (0.0009 / 2));
        Assert.Equal(3, grid.LeastDepthAt(5, edge));
    }

    [Fact]
    public void Water_with_no_depth_and_uncharted_cells_are_not_navigable()
    {
        var grid = Build(
            mask: p => p.Latitude > 0.008 ? WaterMaskClass.Uncharted : WaterMaskClass.Water,
            depth: (lat, lon) => lon > 0.005 ? null : 20,
            minimumDepth: 2);

        Assert.Equal(NavigabilityCellClass.Uncharted, grid.ClassAt(grid.Rows - 1, 0));
        Assert.Equal(NavigabilityCellClass.NoDepth, grid.ClassAt(0, grid.Columns - 1));
        Assert.Equal(NavigabilityCellClass.Navigable, grid.ClassAt(0, 0));
    }

    [Fact]
    public void A_wall_separates_two_basins_until_a_gap_joins_them()
    {
        // 2.2 km square; a land wall along 0.01° with, in the second grid, a
        // 200 m gap in the middle.
        var box = new BoundingBox(0, 0, 0.02, 0.02);
        var walled = Build(mask: Wall(gap: false), depth: (_, _) => 10, minimumDepth: 2, box);
        var gapped = Build(mask: Wall(gap: true), depth: (_, _) => 10, minimumDepth: 2, box);
        GeoPosition west = new(0.004, 0.005), east = new(0.004, 0.015);

        var blocked = walled.Connect(west, east);
        Assert.False(blocked.Connected);
        Assert.Equal("no_navigable_way", blocked.Reason);
        Assert.Equal(2, walled.ComponentSizes.Count);

        var open = gapped.Connect(west, east);
        Assert.True(open.Connected);
        Assert.Single(gapped.ComponentSizes);

        // The narrowest place is in the gap, not beside the wall on the way to it.
        var narrowest = gapped.CenterOf(open.Narrowest!.Value);
        Assert.InRange(narrowest.Longitude, 0.009, 0.011);
        Assert.InRange(narrowest.Latitude, 0.009, 0.011);
        Assert.InRange(open.NarrowestClearanceMeters!.Value, 50, 250);
    }

    [Fact]
    public void Connect_says_why_an_end_is_not_navigable()
    {
        var grid = Build(mask: p => p.Longitude < 0.002 ? WaterMaskClass.Land : WaterMaskClass.Water, depth: (_, _) => 10, minimumDepth: 2);

        Assert.Equal("from_not_navigable", grid.Connect(new(0.005, 0.001), new(0.005, 0.009)).Reason);
        Assert.Equal("to_not_navigable", grid.Connect(new(0.005, 0.009), new(0.005, 0.001)).Reason);
        Assert.Equal("from_off_grid", grid.Connect(new(1, 1), new(0.005, 0.009)).Reason);
    }

    [Fact]
    public void Clearance_grows_away_from_land()
    {
        var grid = Build(mask: p => p.Longitude < 0.002 ? WaterMaskClass.Land : WaterMaskClass.Water, depth: (_, _) => 10, minimumDepth: 2);

        var firstWater = Enumerable.Range(0, grid.Columns).First(c => grid.IsNavigable(5, c));
        Assert.Equal(0, grid.ClearanceMeters(5, firstWater - 1));
        Assert.True(grid.ClearanceMeters(5, firstWater + 3) > grid.ClearanceMeters(5, firstWater + 1));
    }

    [Fact]
    public void Dangers_mark_their_cells_unless_deeper_than_the_hazard_depth()
    {
        var cell = new FakeCell("101AA", new CellRank(12000));
        RouteChartFeature Danger(string id, double lat, double lon, double? depth) =>
            new(RouteChartFeatureKind.Danger, cell, id, "Wreck", [], []) { Points = [new GeoPosition(lat, lon)], DepthMeters = depth };
        var charts = new FakeCharts([cell], [Danger("1", 0.003, 0.003, 1.5), Danger("2", 0.007, 0.007, 30), Danger("3", 0.005, 0.005, null)]);

        var grid = NavigabilityGrid.Build(
            new RouteCheckSources { WaterMask = new FakeMask(_ => WaterMaskClass.Water), Depth = new FakeDepth((_, _) => FakeDepth.DepthArea(20)), Charts = charts },
            new NavigabilityGridRequest(Box, 100, 5),
            TestContext.Current.CancellationToken);

        Assert.Equal(NavigabilityCellClass.Hazard, Class(grid, 0.003, 0.003));
        Assert.Equal(NavigabilityCellClass.Navigable, Class(grid, 0.007, 0.007));
        Assert.Equal(NavigabilityCellClass.Hazard, Class(grid, 0.005, 0.005));
        Assert.Equal(["101AA"], grid.HazardDatasets);
    }

    [Fact]
    public void Structures_the_chart_check_flags_mark_their_cells_but_aids_and_bridges_do_not()
    {
        // #1560: a breakwater (SafetyContour) and a pile (NavHazard structure)
        // block their cells; a buoy (an aid) and a bridge (overhead) don't.
        var cell = new FakeCell("101AA", new CellRank(12000));
        RouteChartFeature Alerted(string id, string type, double lat, double lon, RouteFindingAlert alert) =>
            new(RouteChartFeatureKind.Alerted, cell, id, type, [], []) { Points = [new GeoPosition(lat, lon)], Alerts = [new RouteChartAlert(alert)] };
        var charts = new FakeCharts(
            [cell],
            [
                Alerted("1", "ShorelineConstruction", 0.002, 0.002, RouteFindingAlert.SafetyContour),
                Alerted("2", "Pile", 0.004, 0.004, RouteFindingAlert.NavHazard),
                Alerted("3", "LateralBuoy", 0.006, 0.006, RouteFindingAlert.NavHazard),
                Alerted("4", "Bridge", 0.008, 0.008, RouteFindingAlert.NavHazard),
            ]);

        var grid = NavigabilityGrid.Build(
            new RouteCheckSources { WaterMask = new FakeMask(_ => WaterMaskClass.Water), Depth = new FakeDepth((_, _) => FakeDepth.DepthArea(20)), Charts = charts },
            new NavigabilityGridRequest(Box, 100, 5),
            TestContext.Current.CancellationToken);

        Assert.Equal(NavigabilityCellClass.Hazard, Class(grid, 0.002, 0.002));
        Assert.Equal(NavigabilityCellClass.Hazard, Class(grid, 0.004, 0.004));
        Assert.Equal(NavigabilityCellClass.Navigable, Class(grid, 0.006, 0.006));
        Assert.Equal(NavigabilityCellClass.Navigable, Class(grid, 0.008, 0.008));
    }

    [Fact]
    public void A_danger_in_a_coarser_cell_does_not_count_where_a_finer_cell_charts()
    {
        var fine = new FakeCell("101FINE", new CellRank(12000));
        var coarse = new FakeCell("101COARSE", new CellRank(90000));
        var danger = new RouteChartFeature(RouteChartFeatureKind.Danger, coarse, "9", "Obstruction", [], []) { Points = [new GeoPosition(0.005, 0.005)], DepthMeters = 1 };

        var grid = NavigabilityGrid.Build(
            new RouteCheckSources { WaterMask = new FakeMask(_ => WaterMaskClass.Water), Depth = new FakeDepth((_, _) => FakeDepth.DepthArea(20)), Charts = new FakeCharts([fine, coarse], [danger]) },
            new NavigabilityGridRequest(Box, 100, 5),
            TestContext.Current.CancellationToken);

        Assert.Equal(NavigabilityCellClass.Navigable, Class(grid, 0.005, 0.005));
    }

    [Fact]
    public void The_cell_cap_rejects_big_grids_and_suggests_a_size_that_fits()
    {
        var request = new NavigabilityGridRequest(new BoundingBox(48, -124, 49, -122), 10, 2);
        Assert.True(request.CellCount() > NavigabilityGridRequest.MaxCells);
        Assert.Throws<ArgumentException>(() => NavigabilityGrid.Build(RouteCheckSources.Empty, request, TestContext.Current.CancellationToken));

        var fits = request.CellMetersFor(NavigabilityGridRequest.MaxCells);
        Assert.True((request with { CellMeters = fits }).CellCount() <= NavigabilityGridRequest.MaxCells);
        Assert.True((request with { CellMeters = fits * 0.9 }).CellCount() > NavigabilityGridRequest.MaxCells);
    }

    [Fact]
    public void Cells_are_found_by_position()
    {
        var grid = Build(mask: _ => WaterMaskClass.Water, depth: (_, _) => 10, minimumDepth: 2);
        var cell = new NavigabilityCell(3, 7);

        Assert.Equal(cell, grid.CellAt(grid.CenterOf(cell)));
        Assert.Null(grid.CellAt(new GeoPosition(-0.01, 0.005)));
    }

    private static NavigabilityCellClass Class(NavigabilityGrid grid, double lat, double lon)
    {
        var cell = grid.CellAt(new GeoPosition(lat, lon))!.Value;
        return grid.ClassAt(cell.Row, cell.Column);
    }

    private static Func<GeoPosition, WaterMaskClass> Wall(bool gap) =>
        p => Math.Abs(p.Longitude - 0.01) < 0.0011 && (!gap || Math.Abs(p.Latitude - 0.01) > 0.0011) ? WaterMaskClass.Land : WaterMaskClass.Water;

    private static NavigabilityGrid Build(Func<GeoPosition, WaterMaskClass> mask, Func<double, double, double?> depth, double minimumDepth, BoundingBox? box = null) =>
        NavigabilityGrid.Build(
            new RouteCheckSources
            {
                WaterMask = new FakeMask(mask),
                Depth = new FakeDepth((lat, lon) => depth(lat, lon) is { } d ? FakeDepth.DepthArea(d) : null),
            },
            new NavigabilityGridRequest(box ?? Box, 100, minimumDepth),
            TestContext.Current.CancellationToken);

    private sealed class FakeMask(Func<GeoPosition, WaterMaskClass> classify) : IWaterMask
    {
        public WaterMaskClass Classify(GeoPosition position) => classify(position);
    }

    private sealed class FakeCell(string id, CellRank rank) : RouteChartCell(id)
    {
        public override CellRank? Rank => rank;

        public override bool Covers(GeoPosition position) => true;
    }
}
