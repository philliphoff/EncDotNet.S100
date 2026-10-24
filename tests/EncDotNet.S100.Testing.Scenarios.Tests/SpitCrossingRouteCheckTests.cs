using System.Diagnostics;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Testing.Scenarios.World;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// "Spit crossing" end to end (#954): generated S-101 cells, read by the real
/// reader into the route-check adapter and <see cref="ChartCheck"/>,
/// give the land and drying ground the world model predicts.
/// </summary>
public sealed class SpitCrossingRouteCheckTests(SpitCrossingFixture fixture) : IClassFixture<SpitCrossingFixture>
{
    private const string Harbour = "101ZZ00SPIT5";
    private const string Coastal = "101ZZ00SPIT3";

    // The harbour cell samples the model every 25 m and contours between
    // nodes, so its boundaries sit within a few metres of the model's.
    private const double Tolerance = 15;

    private readonly RouteCheckSources _sources = ScenarioLoading.Sources(fixture.Scenario);

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
            Assert.Equal(expected.Kind == Ground.Land ? RouteFindingCodes.CrossesLand : RouteFindingCodes.CrossesDrying, finding.Code);
            Assert.Equal(RouteFindingCategory.Chart, finding.Category);
            Assert.Equal(expected.Leg, finding.LegIndex);
            Assert.Equal(Harbour, finding.Feature!.DatasetId);
            Assert.EndsWith(expected.Kind == Ground.Land ? " · LandArea" : " · DepthArea", finding.Meta, StringComparison.Ordinal);
            Assert.InRange(finding.Distance!.Value.TotalMetres, Distance(route, expected.Start) - Tolerance, Distance(route, expected.Start) + Tolerance);
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
        Assert.Equal(RouteFindingCodes.OutsideChartCoverage, finding.Code);
        Assert.Equal(RouteFindingGroup.Check, finding.Group);
        Assert.Equal(RouteFindingCategory.Data, finding.Category);
        var edge = Distance(route, (coastal.East, 3050));
        Assert.InRange(finding.Distance!.Value.TotalMetres, edge - Tolerance, edge + Tolerance);
        Assert.Equal(result.Detail!.LengthMeters, finding.Location!.EndDistanceMeters, 6);
    }

    // #947: the coastal cell's generalised land reaches about 150 m further
    // north than the harbour cell's, but the harbour cell is the finer.
    [Fact]
    public void Coarse_cell_land_does_not_fire_where_the_finer_cell_charts_drying_ground()
    {
        var route = Route("421ZZ00SPITX");
        var land = GroundTruth.Along(World, Local(route)).Single(t => t.Kind == Ground.Land);

        var withBoth = Check(route).Findings.Single(f => f.Code == RouteFindingCodes.CrossesLand);
        var coastalOnly = Check(route, Coastal).Findings.Single(f => f.Code == RouteFindingCodes.CrossesLand);

        Assert.Equal(Harbour, withBoth.Feature!.DatasetId);
        Assert.InRange(withBoth.Distance!.Value.TotalMetres, Distance(route, land.Start) - Tolerance, Distance(route, land.Start) + Tolerance);
        Assert.Equal(Coastal, coastalOnly.Feature!.DatasetId);
        Assert.True(withBoth.Distance!.Value.TotalMetres - coastalOnly.Distance!.Value.TotalMetres > 100,
            $"The coastal cell's land starts at {coastalOnly.Distance.Value.TotalMetres:0} m and the harbour cell's at {withBoth.Distance.Value.TotalMetres:0} m.");
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
            Assert.Equal(RouteFindingCodes.DryingInCorridor, f.Code);
            Assert.Equal(Harbour, f.Feature!.DatasetId);
        });

        Assert.Contains(Check(Legs(route), Coastal).Findings, f => f.Code == RouteFindingCodes.CrossesDrying);
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

        // With the depth checks (#955) and S-102, as the viewer runs it.
        // A fresh check each run, so none reuses the last sweep.
        var plan = new PassagePlan(legs);
        var settings = PassageLegs.Settings(DateTimeOffset.UnixEpoch, 10, 10);
        var first = Stopwatch.StartNew();
        var result = ScenarioChecks.Chart(_sources, plan, settings);
        first.Stop();

        // The first checks run on tier-0 JIT code (about ten times slower);
        // a drag re-checks dozens of times, so the steady state is what
        // matters. Check until it settles (30 runs, or 20 s on a slow
        // machine) and take the fastest of the last five: a shared CI
        // runner's noise only ever adds time.
        var times = new List<double>();
        var budget = Stopwatch.StartNew();
        while (times.Count < 30 && (times.Count < 5 || budget.Elapsed < TimeSpan.FromSeconds(20)))
        {
            var run = Stopwatch.StartNew();
            ScenarioChecks.Chart(_sources, plan, settings);
            times.Add(run.Elapsed.TotalMilliseconds);
        }

        var settled = times.TakeLast(5).ToList();
        var each = settled.Min();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Route check, {result.Detail!.LengthMeters / PassageGeodesy.MetersPerNauticalMile:0.0} NM in {legs.Count} legs: " +
            $"first {first.Elapsed.TotalMilliseconds:0} ms (builds the cells' indexes), settled {each:0.0}–{settled.Max():0.0} ms per check " +
            $"after {times.Count} checks (the second took {times[0]:0} ms); {result.Findings.Count} findings: " +
            string.Join(", ", result.Findings.GroupBy(f => f.Code).Select(g => $"{g.Key} ×{g.Count()}")) + ".");

        Assert.InRange(result.Detail!.LengthMeters / PassageGeodesy.MetersPerNauticalMile, 20, 35);
        Assert.Contains(result.Findings, f => f.Code == RouteFindingCodes.CrossesDrying);

        // About 120 ms on Apple silicon, but about 1 s on a 2-core GitHub
        // ubuntu runner, so the bound leaves room for CI; a regression that
        // makes checks several times slower still fails.
        Assert.True(each < 2500, $"A check took {each:0} ms.");
    }

    private RouteCheckOutcome<ChartCheckResult> Check(IReadOnlyList<PassageLeg> legs, params string[] only)
    {
        var sources = only.Length == 0
            ? _sources
            : CatalogRouteCheckSources.FromDatasets(ScenarioLoading.Catalog(fixture.Scenario).Datasets.Where(d => only.Contains(d.Id.Value)), new ProjNetCrsTransformFactory());
        return ScenarioChecks.Chart(sources, new PassagePlan(legs), PassageLegs.Settings(DateTimeOffset.UnixEpoch));
    }

    private S421RouteSpec Spec(string datasetName) => Definition.Routes.Single(r => r.DatasetName == datasetName);

    private List<PassageLeg> Route(string datasetName) =>
        Legs(S421RouteBuilder.Positions(Definition, Spec(datasetName)));

    private static List<PassageLeg> Legs(IReadOnlyList<GeoPosition> positions) =>
        positions.Zip(positions.Skip(1), (a, b) => PassageLegs.Leg([a, b], halfWidth: S421RouteBuilder.CrossTrackLimitMeters)).ToList();

    private List<(double X, double Y)> Local(List<PassageLeg> legs) =>
        legs.Select(l => l.Points[0]).Append(legs[^1].Points[^1])
            .Select(p => World.Frame.ToLocal(p.Latitude, p.Longitude))
            .ToList();

    // The distance the engine reports for a local point on the route: whole
    // legs before it, then along its own leg.
    private double Distance(List<PassageLeg> legs, (double X, double Y) point)
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
