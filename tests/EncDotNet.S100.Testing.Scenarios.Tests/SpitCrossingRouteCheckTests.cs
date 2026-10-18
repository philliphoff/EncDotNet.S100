using System.Diagnostics;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Testing.Scenarios.World;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// "Spit crossing" end to end (#954): generated S-101 cells, read by the real
/// reader into the route-check adapter and <see cref="RouteCheckEngine"/>,
/// give the land and drying ground the world model predicts.
/// </summary>
public sealed class SpitCrossingRouteCheckTests(SpitCrossingFixture fixture) : IClassFixture<SpitCrossingFixture>
{
    private const string Harbour = "101ZZ00SPIT5";
    private const string Coastal = "101ZZ00SPIT3";

    // The harbour cell samples the model every 25 m and contours between
    // nodes, so its boundaries sit within a few metres of the model's.
    private const double Tolerance = 15;

    private readonly RouteCheckDataSources _sources = ScenarioLoading.RouteCheckSources(fixture.Scenario);

    private ScenarioDefinition Definition => fixture.Definition;

    private SyntheticWorld World => Definition.World;

    [Fact]
    public void The_spit_route_crosses_drying_ground_and_land_where_the_model_says()
    {
        var route = Route("421ZZ00SPITX");
        var truth = GroundTruth.Along(World, Local(route));
        Assert.Equal([Ground.Drying, Ground.Drying, Ground.Land], truth.Select(t => t.Kind));

        var result = Check(route);

        Assert.All(result.Findings, f => Assert.Equal(RouteFindingGroup.MustReview, f.Group));
        Assert.Equal(truth.Count, result.Findings.Count);
        for (var i = 0; i < truth.Count; i++)
        {
            var expected = truth[i];
            var finding = result.Findings[i];
            Assert.Equal(expected.Kind == Ground.Land ? RouteCheckCodes.CrossesLand : RouteCheckCodes.CrossesDrying, finding.Code);
            Assert.Equal(RouteFindingSource.RouteCheck, finding.Source);
            Assert.Equal(expected.Leg, finding.LegIndex);
            Assert.Equal(Harbour, finding.Feature!.DatasetId);
            Assert.EndsWith(expected.Kind == Ground.Land ? " · LandArea" : " · DepthArea", finding.Meta, StringComparison.Ordinal);
            Assert.InRange(finding.DistanceMeters!.Value, Distance(route, expected.Start) - Tolerance, Distance(route, expected.Start) + Tolerance);
            Assert.InRange(finding.Location!.EndDistanceMeters, Distance(route, expected.End) - Tolerance, Distance(route, expected.End) + Tolerance);
        }
    }

    [Fact]
    public void The_spit_dries_where_the_route_crosses_it()
    {
        // Leg 2 runs east along y = 1600 m: the spit dries within about 90 m of its axis.
        var spit = GroundTruth.Along(World, Local(Route("421ZZ00SPITX"))).First();
        Assert.Equal(1, spit.Leg);
        Assert.InRange(spit.Start.X, 2880, 2930);
        Assert.InRange(spit.End.X, 3070, 3120);
    }

    [Fact]
    public void The_channel_route_crosses_nothing()
    {
        var route = Route("421ZZ00CHAN");
        Assert.Empty(GroundTruth.Along(World, Local(route)));

        Assert.Empty(Check(route).Findings);
    }

    [Fact]
    public void The_off_chart_route_leaves_chart_coverage_at_the_coastal_cells_edge()
    {
        var route = Route("421ZZ00OFFC");
        var coastal = Definition.Cells.Single(c => c.DatasetName == Coastal).Extent;

        var result = Check(route);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(RouteCheckCodes.OutsideChartCoverage, finding.Code);
        Assert.Equal(RouteFindingGroup.Check, finding.Group);
        Assert.Equal(RouteFindingSource.Data, finding.Source);
        var edge = Distance(route, (coastal.East, 3050));
        Assert.InRange(finding.DistanceMeters!.Value, edge - Tolerance, edge + Tolerance);
        Assert.Equal(result.LengthMeters, finding.Location!.EndDistanceMeters, 6);
    }

    // #947: the coastal cell's generalised land reaches about 150 m further
    // north than the harbour cell's, but the harbour cell is the finer.
    [Fact]
    public void Coarse_cell_land_does_not_fire_where_the_finer_cell_charts_drying_ground()
    {
        var route = Route("421ZZ00SPITX");
        var land = GroundTruth.Along(World, Local(route)).Single(t => t.Kind == Ground.Land);

        var withBoth = Check(route).Findings.Single(f => f.Code == RouteCheckCodes.CrossesLand);
        var coastalOnly = Check(route, Coastal).Findings.Single(f => f.Code == RouteCheckCodes.CrossesLand);

        Assert.Equal(Harbour, withBoth.Feature!.DatasetId);
        Assert.InRange(withBoth.DistanceMeters!.Value, Distance(route, land.Start) - Tolerance, Distance(route, land.Start) + Tolerance);
        Assert.Equal(Coastal, coastalOnly.Feature!.DatasetId);
        Assert.True(withBoth.DistanceMeters - coastalOnly.DistanceMeters > 100,
            $"The coastal cell's land starts at {coastalOnly.DistanceMeters:0} m and the harbour cell's at {withBoth.DistanceMeters:0} m.");
    }

    [Fact]
    public void Coarse_cell_drying_does_not_fire_where_the_finer_cell_charts_water()
    {
        // "Spit across scales"'s own route passes spit-shoulder, where the
        // coastal cell's generalised spit dries and the harbour cell charts 2–5 m.
        var route = SyntheticScenarios.SpitAcrossScales().Route.Select(Definition.Place).ToList();
        var (x, y) = Definition.Places["spit-shoulder"];
        Assert.Equal(Ground.Water, GroundTruth.At(World, x, y));
        Assert.True(S101CellWriter.GeneralisedDepth(World, x, y, Definition.Cells.Single(c => c.DatasetName == Coastal).GeneralisationRadius) < 0);

        Assert.Empty(GroundTruth.Along(World, route.Select(p => World.Frame.ToLocal(p.Latitude, p.Longitude)).ToList()));

        // The track crosses nothing. The harbour cell's own drying spit is
        // inside the 100 m limits of the leg north-east from spit-shoulder.
        var findings = Check(Legs(route)).Findings;
        Assert.DoesNotContain(findings, f => f.Group == RouteFindingGroup.MustReview);
        Assert.All(findings, f =>
        {
            Assert.Equal(RouteCheckCodes.DryingInCorridor, f.Code);
            Assert.Equal(Harbour, f.Feature!.DatasetId);
        });

        Assert.Contains(Check(Legs(route), Coastal).Findings, f => f.Code == RouteCheckCodes.CrossesDrying);
    }

    [Fact]
    public void A_Solent_size_route_checks_at_interactive_speed()
    {
        // About 25 NM in 12 legs, zig-zagging over both cells, the drying
        // spit and bank, and the shore.
        (double X, double Y)[] local =
        [
            (-9000, 9000), (-4000, 1500), (0, 4000), (2400, 1600), (3600, 1600), (4800, 4600),
            (8000, 3000), (12000, 6000), (15000, 2000), (12000, -2000), (6000, 1000), (1000, 2600), (-6000, 6000),
        ];
        var legs = Legs(local.Select(p => World.Frame.ToGeo(p.X, p.Y)).ToList());
        var engine = new RouteCheckEngine(_sources.Charts);
        var first = Stopwatch.StartNew();
        var result = engine.Check(legs, TestContext.Current.CancellationToken);
        first.Stop();

        const int runs = 5;
        var again = Stopwatch.StartNew();
        for (var i = 0; i < runs; i++)
        {
            engine.Check(legs, TestContext.Current.CancellationToken);
        }

        again.Stop();
        var each = again.Elapsed.TotalMilliseconds / runs;
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Route check, {result.LengthMeters / PassageGeodesy.MetersPerNauticalMile:0.0} NM in {legs.Count} legs: " +
            $"first {first.Elapsed.TotalMilliseconds:0} ms (builds the cells' indexes), then {each:0.0} ms per check; {result.Findings.Count} findings.");

        Assert.InRange(result.LengthMeters / PassageGeodesy.MetersPerNauticalMile, 20, 35);
        Assert.Contains(result.Findings, f => f.Code == RouteCheckCodes.CrossesDrying);

        // Generous, so a loaded CI machine doesn't flake; the PR records the measured time.
        Assert.True(each < 1000, $"A check took {each:0} ms.");
    }

    private RouteCheckResult Check(IReadOnlyList<RouteCheckLeg> legs, params string[] only)
    {
        var sources = only.Length == 0
            ? _sources
            : RouteCheckDataSources.FromDatasets(ScenarioLoading.Catalog(fixture.Scenario).Datasets.Where(d => only.Contains(d.Id.Value)));
        return new RouteCheckEngine(sources.Charts).Check(legs, TestContext.Current.CancellationToken);
    }

    private S421RouteSpec Spec(string datasetName) => Definition.Routes.Single(r => r.DatasetName == datasetName);

    private List<RouteCheckLeg> Route(string datasetName) =>
        Legs(S421RouteBuilder.Positions(Definition, Spec(datasetName)));

    private static List<RouteCheckLeg> Legs(IReadOnlyList<GeoPosition> positions) =>
        positions.Zip(positions.Skip(1), (a, b) => new RouteCheckLeg(
            [a, b],
            PortCrossTrackMeters: S421RouteBuilder.CrossTrackLimitMeters,
            StarboardCrossTrackMeters: S421RouteBuilder.CrossTrackLimitMeters)).ToList();

    private List<(double X, double Y)> Local(List<RouteCheckLeg> legs) =>
        legs.Select(l => l.Points[0]).Append(legs[^1].Points[^1])
            .Select(p => World.Frame.ToLocal(p.Latitude, p.Longitude))
            .ToList();

    // The distance the engine reports for a local point on the route: whole
    // legs before it, then along its own leg.
    private double Distance(List<RouteCheckLeg> legs, (double X, double Y) point)
    {
        var position = World.Frame.ToGeo(point.X, point.Y);
        var best = (Offset: double.MaxValue, Distance: 0.0);
        var along = 0.0;
        foreach (var leg in legs)
        {
            var (a, b) = (leg.Points[0], leg.Points[1]);
            var length = PassageGeodesy.RhumbDistanceMeters(a, b);
            var fromStart = PassageGeodesy.RhumbDistanceMeters(a, position);
            var offset = fromStart + PassageGeodesy.RhumbDistanceMeters(position, b) - length;
            if (offset < best.Offset)
            {
                best = (offset, along + fromStart);
            }

            along += length;
        }

        return best.Distance;
    }
}
