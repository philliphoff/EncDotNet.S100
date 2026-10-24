using EncDotNet.S100.Navigation;
using EncDotNet.S100.Testing.Scenarios.World;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// "Spit across scales" end to end: generated files, read by the real
/// readers into the passage-check adapters and <see cref="ClearanceCheck"/>,
/// give the under-keel clearance the world model predicts.
/// </summary>
public sealed class SpitAcrossScalesUkcTests(SpitAcrossScalesFixture fixture) : IClassFixture<SpitAcrossScalesFixture>
{
    private const double Draft = 6.0;
    private const double Squat = 0.3;
    private const double Required = 1.0;
    private const string Harbour = "101ZZ00SPIT5";
    private const string Coastal = "101ZZ00SPIT3";

    // On a step of both the 10-minute station series and the hourly grid.
    private static readonly DateTime At = new(2026, 3, 1, 6, 0, 0, DateTimeKind.Utc);

    private readonly RouteCheckSources _sources = ScenarioLoading.Sources(fixture.Scenario);

    private ScenarioDefinition Definition => fixture.Definition;

    private SyntheticWorld World => fixture.Definition.World;

    [Fact]
    public void Channel_west_uses_S102_and_the_west_station()
    {
        var sample = CheckAt("channel-west");

        var depth = Assert.IsType<ChartedDepth>(sample.Depth);
        Assert.Equal(ChartedDepthKind.Bathymetry, depth.Kind);
        Assert.Equal(WorldDatums.LowestAstronomicalTide, depth.VerticalDatum);
        Assert.Equal(S102NodeDepth("channel-west"), depth.DepthMeters, 4);
        Assert.Equal(World.DepthAt(Definition.Place("channel-west")), depth.DepthMeters, 2);

        Assert.Equal(WaterLevelStatus.Applied, sample.WaterLevel.Status);
        Assert.Equal(WaterLevelSourceKind.Station, sample.WaterLevel.Source!.Kind);
        Assert.Equal("SYN-W", sample.WaterLevel.Source.StationId);
        Assert.Equal(DatumAgreement.Same, sample.WaterLevel.Datum);
        var tide = StationHeight("SYN-W", At);
        Assert.Equal(tide, sample.WaterLevel.HeightMeters!.Value, 3);

        Assert.Equal(depth.DepthMeters + tide - Draft - Squat, sample.ClearanceMeters!.Value, 3);
        Assert.Equal(UkcState.MeetsSettings, sample.State);
        Assert.Equal(ConfidenceReasons.None, sample.Confidence);
    }

    [Fact]
    public void Channel_east_uses_S102_and_the_water_level_grid()
    {
        var sample = CheckAt("channel-east");

        var depth = Assert.IsType<ChartedDepth>(sample.Depth);
        Assert.Equal(ChartedDepthKind.Bathymetry, depth.Kind);
        Assert.Equal(S102NodeDepth("channel-east"), depth.DepthMeters, 4);

        // channel-east is an S-104 grid node: the grid wins over the stations.
        Assert.Equal(WaterLevelSourceKind.Grid, sample.WaterLevel.Source!.Kind);
        Assert.Equal("3,5", sample.WaterLevel.Source.GridCell);
        var (x, y) = Definition.Places["channel-east"];
        var tide = (double)(float)World.Tide.HeightAt(x, y, At);
        Assert.Equal(tide, sample.WaterLevel.HeightMeters!.Value, 3);

        Assert.Equal(depth.DepthMeters + tide - Draft - Squat, sample.ClearanceMeters!.Value, 3);
    }

    [Fact]
    public void Spit_shoulder_falls_back_to_S101_with_both_cells_covering()
    {
        var sample = CheckAt("spit-shoulder");
        var depth = Assert.IsType<ChartedDepth>(sample.Depth);

        Assert.Equal(ChartedDepthKind.DepthArea, depth.Kind);
        Assert.True(depth.IsLowConfidence);
        Assert.Equal(WorldDatums.LowestAstronomicalTide, depth.VerticalDatum);
        Assert.Contains(depth.DatasetId, new[] { Harbour, Coastal });
        Assert.Equal("SYN-W", sample.WaterLevel.Source!.StationId);

        // The cells disagree here: the harbour cell charts the 2–5 m band and
        // the coastal cell's generalised spit dries.
        var (x, y) = Definition.Places["spit-shoulder"];
        Assert.InRange(World.DepthAt(x, y), 2, 5);
        Assert.InRange(S101CellWriter.GeneralisedDepth(World, x, y, CoastalSpec.GeneralisationRadius), -World.HighWaterHeight, 0);
    }

    // Regression for #938: where S-101 cells overlap, the finest cell's depth wins.
    [Fact]
    public void Spit_shoulder_takes_the_harbour_cells_depth()
    {
        var sample = CheckAt("spit-shoulder");

        var depth = Assert.IsType<ChartedDepth>(sample.Depth);
        Assert.Equal(Harbour, depth.DatasetId);
        Assert.Equal(2.0, depth.DepthMeters);
        var tide = StationHeight("SYN-W", At);
        Assert.Equal(2.0 + tide - Draft - Squat, sample.ClearanceMeters!.Value, 3);
        Assert.Equal(UkcState.BelowSetting, sample.State);
        Assert.Equal(ConfidenceReasons.ChartedDepthFromS101, sample.Confidence);
    }

    [Fact]
    public void The_route_agrees_with_the_world_model_at_every_sample()
    {
        var result = CheckRoute();
        var s102 = CoverageWriters.Grid(World, Definition.Surfaces[0]);
        var waterGrid = CoverageWriters.Grid(World, Definition.WaterLevelGrids[0]);

        Assert.NotEmpty(result.Samples);
        foreach (var sample in result.Samples)
        {
            var depth = Assert.IsType<ChartedDepth>(sample.Depth);
            var truth = World.DepthAt(sample.Position);
            if (depth.Kind == ChartedDepthKind.Bathymetry)
            {
                // The reader floors into the containing grid cell (its south-west node).
                var row = (int)Math.Floor((sample.Position.Latitude - s102.OriginLatitude) / s102.SpacingLatitudinal);
                var col = (int)Math.Floor((sample.Position.Longitude - s102.OriginLongitude) / s102.SpacingLongitudinal);
                Assert.Equal(CoverageWriters.S102Depth(World, s102.Node(row, col)), depth.DepthMeters, 4);
            }
            else
            {
                // A charted band minimum is never deeper than the seabed.
                Assert.Equal(ChartedDepthKind.DepthArea, depth.Kind);
                Assert.True(depth.DepthMeters <= truth + 0.05, $"{depth} at {sample.Position} is deeper than the seabed ({truth:0.00} m).");
            }

            Assert.Equal(WaterLevelStatus.Applied, sample.WaterLevel.Status);
            var source = sample.WaterLevel.Source!;
            var utc = sample.Time.UtcDateTime;
            double expected;
            double tolerance;
            if (source.Kind == WaterLevelSourceKind.Station)
            {
                expected = StationHeight(source.StationId!, utc);
                tolerance = 0.005; // linear between 10-minute records
            }
            else
            {
                var cell = source.GridCell!.Split(',').Select(int.Parse).ToArray();
                var node = waterGrid.Node(cell[0], cell[1]);
                var (x, y) = World.Frame.ToLocal(node.Latitude, node.Longitude);
                expected = World.Tide.HeightAt(x, y, utc);
                tolerance = 0.06; // linear between hourly steps
            }

            Assert.InRange(sample.WaterLevel.HeightMeters!.Value, expected - tolerance, expected + tolerance);
        }
    }

    [Fact]
    public void The_route_falls_back_to_S101_only_off_the_S102_surface()
    {
        var result = CheckRoute();
        var surface = Definition.Surfaces[0].Extent;

        foreach (var sample in result.Samples)
        {
            var (x, y) = World.Frame.ToLocal(sample.Position.Latitude, sample.Position.Longitude);
            var onSurface = surface.Contains(x, y) && x < surface.East - Definition.Surfaces[0].Spacing && y < surface.North - Definition.Surfaces[0].Spacing;
            if (onSurface)
            {
                Assert.Equal(ChartedDepthKind.Bathymetry, sample.Depth!.Kind);
            }
            else if (!surface.Contains(x, y))
            {
                Assert.True(sample.Depth!.IsLowConfidence);
            }
        }

        var fallback = Assert.Single(result.LowConfidence, s => s.Kind == PassageStretchKind.ChartedDepthFromS101);
        Assert.NotEmpty(result.Sources.FallbackDatasets);
        Assert.Equal(PassageVerdict.BelowSetting, result.Verdict);

        // The shallowest charted water is off the spit, inside the fallback stretch.
        var minimum = Assert.IsType<UkcMinimum>(result.Minimum);
        Assert.InRange(minimum.DistanceMeters, fallback.StartDistanceMeters, fallback.EndDistanceMeters);
    }

    // Regression for #938, along the whole fallback stretch.
    [Fact]
    public void The_route_charts_fallback_depths_from_the_harbour_cell()
    {
        var result = CheckRoute();
        var harbour = Definition.Cells.Single(c => c.DatasetName == Harbour);
        var limits = new List<double> { -World.HighWaterHeight };
        limits.AddRange(harbour.DepthContours);

        foreach (var sample in result.Samples.Where(s => s.Depth!.IsLowConfidence))
        {
            Assert.Equal(Harbour, sample.Depth!.DatasetId);

            // Away from the band edges, DRVAL1 is the harbour band's lower limit.
            var truth = World.DepthAt(sample.Position);
            var band = limits.Last(l => l <= truth);
            if (limits.All(l => Math.Abs(truth - l) > 0.3))
            {
                Assert.Equal(band, sample.Depth.DepthMeters);
            }
        }

        Assert.Equal([Harbour], result.Sources.FallbackDatasets);
    }

    private S101CellSpec CoastalSpec => Definition.Cells.Single(c => c.DatasetName == Coastal);

    private PassageCheckResult CheckRoute()
    {
        var points = Definition.RoutePositions;
        var legs = points.Zip(points.Skip(1), (a, b) => PassageLegs.Leg([a, b], Draft, speedKnots: 8, squat: Squat, ukc: Required)).ToList();
        var departure = new DateTimeOffset(new DateTime(2026, 3, 1, 4, 0, 0, DateTimeKind.Utc));
        return ScenarioChecks.Clearance(_sources, new PassagePlan(legs, departure), PassageLegs.Settings(departure));
    }

    /// <summary>
    /// The check of a 1 m leg starting just north-east of a named place (so
    /// the S-102 lookup lands in the cell whose south-west node is the
    /// place), departing at <see cref="At"/>.
    /// </summary>
    private UkcSample CheckAt(string place)
    {
        var (x, y) = Definition.Places[place];
        var start = World.Frame.ToGeo(x + 0.1, y + 0.1);
        var end = World.Frame.ToGeo(x + 1.1, y + 0.1);
        var leg = PassageLegs.Leg([start, end], Draft, speedKnots: 10, squat: Squat, ukc: Required);
        var result = ScenarioChecks.Clearance(_sources, new PassagePlan([leg], new DateTimeOffset(At)), PassageLegs.Settings(new DateTimeOffset(At)));
        return Assert.Single(result.Samples);
    }

    private double S102NodeDepth(string place)
    {
        var (x, y) = Definition.Places[place];
        var spec = Definition.Surfaces[0];
        var grid = CoverageWriters.Grid(World, spec);
        var row = (int)Math.Round((y - spec.Extent.South) / spec.Spacing);
        var col = (int)Math.Round((x - spec.Extent.West) / spec.Spacing);
        return CoverageWriters.S102Depth(World, grid.Node(row, col));
    }

    private double StationHeight(string id, DateTime utc)
    {
        var station = World.Tide.Stations.Single(s => s.Identifier == id);
        return (float)World.Tide.HeightAt(station.X, station.Y, utc);
    }
}
