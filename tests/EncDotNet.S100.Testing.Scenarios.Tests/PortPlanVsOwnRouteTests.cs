using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.S129.DataModel;
using EncDotNet.S100.Datasets.S129.Fusion;
using EncDotNet.S100.Datasets.S129.Fusion.Routing;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// "Port plan vs own route": the port's S-129 plan, the S-421 route it was
/// made for and the vessel's own route, all read from files, checked with
/// <c>S129.Fusion</c> against the world model.
/// </summary>
public sealed class PortPlanVsOwnRouteTests(PortPlanVsOwnRouteFixture fixture) : IClassFixture<PortPlanVsOwnRouteFixture>
{
    private readonly FileDatasetCatalog _catalog = ScenarioLoading.Catalog(fixture.Scenario);

    private ScenarioDefinition Definition => fixture.Definition;

    private S129PlanSpec Spec => Definition.UkcPlans[0];

    [Fact]
    public void The_plan_binds_to_the_port_route_at_every_control_point()
    {
        var binding = S129RouteBinder.Bind(Plan(), Route("421ZZ00APPR"));

        Assert.Collection(
            binding.Mappings,
            m => Assert.Equal((S129RouteMappingKind.OnWaypoint, "shoal-crest"), (m.Mapping.Kind, m.Mapping.Waypoint?.Name)),
            m => Assert.Equal((S129RouteMappingKind.OnWaypoint, "channel-junction"), (m.Mapping.Kind, m.Mapping.Waypoint?.Name)),
            m =>
            {
                Assert.Equal(S129RouteMappingKind.OnLeg, m.Mapping.Kind);
                Assert.Equal("RTE.LEG.3", m.Mapping.Leg?.Id);
                Assert.Equal(0.5, m.Mapping.LegPositionFraction!.Value, 3);
            });
        Assert.All(binding.Mappings, m => Assert.InRange(m.Mapping.DistanceMeters!.Value, 0, 15));
    }

    [Fact]
    public void The_own_route_misses_the_crest_control_point()
    {
        var binding = S129RouteBinder.Bind(Plan(), Route("421ZZ00OWN"));

        Assert.Equal(
            [S129RouteMappingKind.Unmapped, S129RouteMappingKind.OnWaypoint, S129RouteMappingKind.OnLeg],
            binding.Mappings.Select(m => m.Mapping.Kind));
    }

    [Fact]
    public void The_plan_names_the_port_route_not_the_own_route()
    {
        var plan = Plan();

        var port = S129CrossProductResolver.Resolve(plan, route: Route("421ZZ00APPR"));
        var own = S129CrossProductResolver.Resolve(plan, route: Route("421ZZ00OWN"));

        Assert.Equal("ZZ00-APPROACH-1", port.Route?.Value.RouteId);
        Assert.Null(own.Route);
        Assert.Contains(own.Unresolved, u => u.Reason == S129ReferenceResolutionReason.IdentifierMismatch);
    }

    // S129.Fusion against the truth: the depth and tide it reads at each
    // control point give the plan's distance above the UKC limit.
    [Fact]
    public void Fusion_reads_the_model_depth_and_tide_at_every_control_point()
    {
        var plan = Plan();
        var truth = S129PlanWriter.ControlPoints(Definition, Spec);
        var bathymetry = Assert.IsType<S102CoverageData>(ScenarioLoading.Dataset(_catalog, Definition.Surfaces[0].DatasetName).Data).Source;
        var waterLevel = Assert.IsType<S104CoverageData>(ScenarioLoading.Dataset(_catalog, Definition.WaterLevelGrids[0].DatasetName).Data).Source;
        var s102 = CoverageWriters.Grid(Definition.World, Definition.Surfaces[0]);
        var s104 = CoverageWriters.Grid(Definition.World, Definition.WaterLevelGrids[0]);
        var passage = S129PlanWriter.RouteOf(Definition, Spec).Passage;

        for (var i = 0; i < truth.Count; i++)
        {
            var point = plan.ControlPoints[i];
            var depth = Assert.IsType<S129BathymetrySample>(S129PlanFusion.SampleBathymetryAt(point, bathymetry));
            Assert.Equal(CoverageWriters.S102Depth(Definition.World, s102.Node(depth.Row, depth.Column)), depth.Depth);

            var tide = Assert.IsType<S129WaterLevelSample>(S129PlanFusion.SampleWaterLevelAt(point, waterLevel));
            var node = s104.Node(tide.Row, tide.Column);
            var (x, y) = Definition.World.Frame.ToLocal(node.Latitude, node.Longitude);
            Assert.Equal((float)Definition.World.Tide.HeightAt(x, y, DateTime.SpecifyKind(tide.TimeSelected, DateTimeKind.Utc)), tide.Height);
            Assert.InRange((DateTime.SpecifyKind(tide.TimeSelected, DateTimeKind.Utc) - truth[i].ExpectedPassingTime).Duration(), TimeSpan.Zero, TimeSpan.FromMinutes(5));

            // Nearest node and nearest step against the exact point and time: within a few centimetres.
            var above = depth.Depth + tide.Height - passage.DraftMeters - passage.SquatMeters - Spec.UkcLimitMeters;
            Assert.Equal(truth[i].DistanceAboveUkcLimit, point.DistanceAboveUkcLimit);
            Assert.Equal(point.DistanceAboveUkcLimit!.Value, above, 0.08);
        }
    }

    [Fact]
    public void The_crest_window_closes_where_fusion_says_the_keel_reaches_the_limit()
    {
        var plan = Plan();
        var crest = plan.ControlPoints[0];
        var bathymetry = Assert.IsType<S102CoverageData>(ScenarioLoading.Dataset(_catalog, Definition.Surfaces[0].DatasetName).Data).Source;
        var waterLevel = Assert.IsType<S104CoverageData>(ScenarioLoading.Dataset(_catalog, Definition.WaterLevelGrids[0].DatasetName).Data).Source;
        var passage = S129PlanWriter.RouteOf(Definition, Spec).Passage;
        var depth = S129PlanFusion.SampleBathymetryAt(crest, bathymetry)!.Depth;
        double Above(DateTimeOffset t) => depth + S129PlanFusion.SampleWaterLevelAt(crest, waterLevel, t)!.Height
            - passage.DraftMeters - passage.SquatMeters - Spec.UkcLimitMeters;

        var window = crest.FixedTimeRange!;
        Assert.True(window.End - window.Start < TimeSpan.FromHours(4), "The crest is passable only near high water.");
        Assert.True(Above(window.Start!.Value + TimeSpan.FromMinutes(20)) > 0);
        Assert.True(Above(window.Start!.Value - TimeSpan.FromMinutes(20)) < 0);
        Assert.True(Above(window.End!.Value - TimeSpan.FromMinutes(20)) > 0);
        Assert.True(Above(window.End!.Value + TimeSpan.FromMinutes(20)) < 0);
    }

    // The passage check of the port route at the plan's departure meets the
    // plan: its margin over the crest is the plan's distance above the limit.
    [Fact]
    public void The_passage_check_agrees_with_the_plan_at_the_crest()
    {
        var sources = PassageDataSources.FromDatasets(_catalog.Datasets, new Crs.ProjNet.ProjNetCrsTransformFactory());
        var route = Definition.Routes.Single(r => r.DatasetName == Spec.Route);
        var legs = ScenarioPassage.Legs(_catalog, route);
        var result = new PassageCheckEngine(sources.Depth, sources.WaterLevel)
            .Check(new PassagePlan(legs, new DateTimeOffset(route.Departure, TimeSpan.Zero)), PassageLegs.Settings(new DateTimeOffset(route.Departure, TimeSpan.Zero)), TestContext.Current.CancellationToken);

        Assert.True(result.Verdict is PassageVerdict.MeetsSettings or PassageVerdict.MeetsSettingsTight, result.Verdict.ToString());
        var minimum = Assert.IsType<UkcMinimum>(result.Minimum);
        var crest = Plan().ControlPoints[0];
        Assert.Equal(crest.DistanceAboveUkcLimit!.Value, minimum.ClearanceMeters - minimum.RequiredUkcMeters, 0.08);
        Assert.InRange((minimum.Time - crest.ExpectedPassingTime!.Value).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    private S129UnderKeelClearancePlan Plan()
    {
        var dataset = Assert.IsType<S129DatasetData>(ScenarioLoading.Dataset(_catalog, Spec.DatasetName).Data).Model;
        return S129UnderKeelClearancePlan.From(dataset, out _);
    }

    private S421Route Route(string id)
    {
        var dataset = Assert.IsType<S421DatasetData>(ScenarioLoading.Dataset(_catalog, id).Data).Model;
        return S421RoutePlan.From(dataset, out _).Route;
    }
}
