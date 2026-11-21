using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Testing.Scenarios.World;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// The navigability grid (#1558) over generated S-101 cells, read by the real
/// reader into the route-check adapter, against the world model they were
/// sampled from: land where the model has land, no navigable cell over
/// water shallower than the minimum depth, and the grid's depth fill agreeing
/// with the point sampler the route check uses.
/// </summary>
public sealed class NavigabilityGridScenarioTests(SpitCrossingFixture fixture) : IClassFixture<SpitCrossingFixture>
{
    private const string Harbour = "101ZZ00SPIT5";

    private readonly RouteCheckSources _sources = ScenarioLoading.Sources(fixture.Scenario);

    private ScenarioDefinition Definition => fixture.Definition;

    private SyntheticWorld World => Definition.World;

    [Fact]
    public void Land_and_water_match_the_world_model_away_from_the_shore()
    {
        var grid = Build(cellMeters: 50, minimumDepth: 3);
        int agree = 0, disagree = 0;
        for (var row = 0; row < grid.Rows; row++)
        {
            for (var col = 0; col < grid.Columns; col++)
            {
                var (x, y) = Local(grid.CenterOf(new NavigabilityCell(row, col)));

                // Away from the shore: the model agrees all round the cell.
                if (!Uniform(x, y, 50, out var ground))
                {
                    continue;
                }

                var land = grid.ClassAt(row, col) == NavigabilityCellClass.Land;
                if (land == (ground == Ground.Land))
                {
                    agree++;
                }
                else
                {
                    disagree++;
                }
            }
        }

        Assert.True(agree > 1000, $"Only {agree} cells away from the shore.");
        Assert.True(disagree <= agree / 200, $"{disagree} of {agree + disagree} cells away from the shore disagree with the model.");
        Assert.Contains(grid.Classes.ToArray(), c => c == NavigabilityCellClass.Land);
        Assert.Contains(grid.Classes.ToArray(), c => c == NavigabilityCellClass.Navigable);
        Assert.Contains(grid.Classes.ToArray(), c => c == NavigabilityCellClass.Shoal);
    }

    [Fact]
    public void No_navigable_cell_is_over_water_shallower_than_the_minimum_depth()
    {
        const double minimum = 5;
        var grid = Build(cellMeters: 50, minimumDepth: minimum);
        var navigable = 0;
        for (var row = 0; row < grid.Rows; row++)
        {
            for (var col = 0; col < grid.Columns; col++)
            {
                if (!grid.IsNavigable(row, col))
                {
                    continue;
                }

                navigable++;
                var (x, y) = Local(grid.CenterOf(new NavigabilityCell(row, col)));

                // The cell's least depth is a contour at or below the model's
                // depth anywhere in it (to the harbour cell's 25 m sampling).
                var shoalest = Shoalest(x, y, 25);
                Assert.True(shoalest >= minimum - 1.0, $"Navigable cell ({row},{col}) has {shoalest:0.0} m in the model.");
                Assert.True(grid.LeastDepthAt(row, col) <= World.DepthAt(x, y) + 0.5);
            }
        }

        Assert.True(navigable > 500, $"Only {navigable} navigable cells.");
    }

    [Fact]
    public void The_grid_depth_fill_agrees_with_the_point_sampler()
    {
        var depth = _sources.Depth!;
        var box = Box(Harbour);
        var grid = new WaterMaskGrid(box.SouthLatitude, box.WestLongitude, (box.NorthLatitude - box.SouthLatitude) / 97, (box.EastLongitude - box.WestLongitude) / 101, 98, 102);
        var filled = new float[grid.Count];
        depth.Sample(grid, filled);

        int compared = 0, differ = 0;
        for (var row = 0; row < grid.Rows; row += 3)
        {
            for (var col = 0; col < grid.Columns; col += 3)
            {
                var point = depth.Sample(grid.PositionAt(row, col));
                var value = filled[(row * grid.Columns) + col];
                if (point is null || point.Kind == ChartedDepthKind.Sounding)
                {
                    // The grid leaves out the sounding fallback.
                    continue;
                }

                compared++;
                if (float.IsNaN(value) || Math.Abs(value - point.DepthMeters) > 1e-3)
                {
                    differ++;
                }
            }
        }

        // Points exactly on a polygon edge may fall either side.
        Assert.True(compared > 500, $"Only {compared} points compared.");
        Assert.True(differ <= compared / 100, $"{differ} of {compared} grid depths differ from the point sampler.");
    }

    [Fact]
    public void The_spit_cuts_the_channel_off_for_a_deep_draft_but_not_a_shallow_one()
    {
        var shallow = Build(cellMeters: 50, minimumDepth: 1);
        var deep = Build(cellMeters: 50, minimumDepth: 30);

        Assert.True(shallow.ComponentSizes.Max() > deep.ComponentSizes.DefaultIfEmpty(0).Max());

        // Two navigable cells of the largest area connect, with a bottleneck.
        var largest = shallow.ComponentSizes.Select((size, label) => (size, label)).MaxBy(c => c.size).label;
        var cells = Enumerable.Range(0, shallow.Rows)
            .SelectMany(r => Enumerable.Range(0, shallow.Columns).Select(c => new NavigabilityCell(r, c)))
            .Where(c => shallow.ComponentAt(c.Row, c.Column) == largest)
            .ToList();
        var connection = shallow.Connect(shallow.CenterOf(cells[0]), shallow.CenterOf(cells[^1]));
        Assert.True(connection.Connected);
        Assert.NotNull(connection.Narrowest);
        Assert.True(connection.NarrowestClearanceMeters > 0);
    }

    private NavigabilityGrid Build(double cellMeters, double minimumDepth) =>
        NavigabilityGrid.Build(_sources, new NavigabilityGridRequest(Box(Harbour), cellMeters, minimumDepth), TestContext.Current.CancellationToken);

    private BoundingBox Box(string cell)
    {
        var extent = Definition.Cells.Single(c => c.DatasetName == cell).Extent;
        var sw = World.Frame.ToGeo(extent.West, extent.South);
        var ne = World.Frame.ToGeo(extent.East, extent.North);
        return new BoundingBox(sw.Latitude, sw.Longitude, ne.Latitude, ne.Longitude);
    }

    private (double X, double Y) Local(GeoPosition p) => World.Frame.ToLocal(p.Latitude, p.Longitude);

    private bool Uniform(double x, double y, double radius, out Ground ground)
    {
        ground = GroundTruth.At(World, x, y);
        for (var dx = -radius; dx <= radius; dx += radius / 2)
        {
            for (var dy = -radius; dy <= radius; dy += radius / 2)
            {
                if (GroundTruth.At(World, x + dx, y + dy) != ground)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private double Shoalest(double x, double y, double half)
    {
        var least = double.MaxValue;
        for (var dx = -half; dx <= half; dx += half / 2)
        {
            for (var dy = -half; dy <= half; dy += half / 2)
            {
                least = Math.Min(least, World.DepthAt(x + dx, y + dy));
            }
        }

        return least;
    }
}

/// <summary>The navigability grid's danger cells over "Dangers near route" (#955's generated point dangers).</summary>
public sealed class NavigabilityGridDangerTests(DangersNearRouteFixture fixture) : IClassFixture<DangersNearRouteFixture>
{
    private const string Harbour = "101ZZ00SPIT5";

    [Fact]
    public void Dangers_at_or_shallower_than_the_hazard_depth_mark_their_cells()
    {
        const double threshold = 10;
        var definition = fixture.Definition;
        var world = definition.World;
        var harbour = definition.Cells.Single(c => c.DatasetName == Harbour);
        var sw = world.Frame.ToGeo(harbour.Extent.West, harbour.Extent.South);
        var ne = world.Frame.ToGeo(harbour.Extent.East, harbour.Extent.North);
        var sources = ScenarioLoading.Sources(fixture.Scenario);
        var grid = NavigabilityGrid.Build(
            sources,
            new NavigabilityGridRequest(new BoundingBox(sw.Latitude, sw.Longitude, ne.Latitude, ne.Longitude), 25, MinimumDepthMeters: 2, HazardDepthMeters: threshold),
            TestContext.Current.CancellationToken);

        var checkedHazards = 0;
        foreach (var hazard in definition.Hazards)
        {
            if (!harbour.Extent.Contains(hazard.Position.X, hazard.Position.Y) || !hazard.ChartedIn(harbour))
            {
                continue;
            }

            var position = world.Frame.ToGeo(hazard.Position.X, hazard.Position.Y);
            var cell = grid.CellAt(position)!.Value;

            // A danger that reaches the surface keeps no depth, so it's always a hazard.
            var reachesSurface = hazard.WaterLevelEffect is 1 or 2 or 4 or 5;
            var expected = reachesSurface || hazard.ValueOfSounding is not { } depth || depth <= threshold;
            Assert.True(expected == grid.Hazards[(cell.Row * grid.Columns) + cell.Column], $"{hazard.Name} ({hazard.ValueOfSounding?.ToString() ?? "no depth"} m): expected hazard {expected}.");
            checkedHazards++;
        }

        Assert.True(checkedHazards >= 3, $"Only {checkedHazards} dangers checked.");
        Assert.Contains(Harbour, grid.HazardDatasets);
    }
}
