using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S122.DataModel;
using EncDotNet.S100.Datasets.S124.DataModel;
using EncDotNet.S100.Datasets.S125.DataModel;
using EncDotNet.S100.Datasets.S129.DataModel;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// The S-111 files read back, through the real readers, as exactly the
/// values the world model gives.
/// </summary>
public sealed class S111RoundTripTests(ApproachWithTideWindowFixture fixture) : IClassFixture<ApproachWithTideWindowFixture>
{
    private readonly FileDatasetCatalog _catalog = ScenarioLoading.Catalog(fixture.Scenario);

    private ScenarioDefinition Definition => fixture.Definition;

    [Fact]
    public void The_grid_holds_the_model_current_at_every_node_and_step()
    {
        var spec = Definition.CurrentGrids[0];
        var grid = CoverageWriters.Grid(Definition.World, spec);
        var source = Assert.IsType<S111CoverageData>(ScenarioLoading.Dataset(_catalog, spec.DatasetName).Data).Source;
        var times = CoverageWriters.Times(Definition.WaterLevelStart, Definition.WaterLevelEnd, spec.Interval);

        Assert.Equal(times, source.Dataset.Coverages.Select(c => DateTime.SpecifyKind(c.TimePoint, DateTimeKind.Utc)));
        foreach (var coverage in source.Dataset.Coverages.Where((_, i) => i % 7 == 0))
        {
            Assert.Equal((grid.Rows, grid.Columns), (coverage.NumPointsLatitudinal, coverage.NumPointsLongitudinal));
            var t = DateTime.SpecifyKind(coverage.TimePoint, DateTimeKind.Utc);
            for (var row = 0; row < grid.Rows; row++)
            {
                for (var col = 0; col < grid.Columns; col++)
                {
                    var node = grid.Node(row, col);
                    var (x, y) = Definition.World.Frame.ToLocal(node.Latitude, node.Longitude);
                    var (speed, direction) = CoverageWriters.S111Value(Definition.World, x, y, t);
                    var value = coverage.Values[row * grid.Columns + col];
                    Assert.Equal(speed, value.Speed);
                    Assert.Equal(direction, value.Direction);
                }
            }
        }
    }

    [Fact]
    public void The_stations_hold_the_model_current_at_every_record()
    {
        var spec = Definition.CurrentStations!;
        var dataset = Assert.IsType<S111StationSeriesData>(ScenarioLoading.Dataset(_catalog, spec.DatasetName).Data).Dataset;

        Assert.Equal(Definition.World.Tide.Stations.Select(s => s.Identifier), dataset.Stations.Select(s => s.Identifier));
        foreach (var station in dataset.Stations)
        {
            var truth = Definition.World.Tide.Stations.Single(s => s.Identifier == station.Identifier);
            for (var i = 0; i < station.NumberOfTimes; i++)
            {
                var (speed, direction) = CoverageWriters.S111Value(Definition.World, truth.X, truth.Y, DateTime.SpecifyKind(station.TimeAt(i), DateTimeKind.Utc));
                Assert.Equal(speed, station.SpeedsKnots[i]);
                Assert.Equal(direction, station.DirectionsDegreesTrue[i]);
            }
        }
    }

    // The streams are physically consistent with S-104: flood (east) while the
    // water rises, ebb (west) while it falls, slack at high and low water.
    // S-104 marks a trend beyond 0,2 m/h, which is 0,3 kn of stream.
    [Fact]
    public void The_stream_floods_while_S104_rises_and_ebbs_while_it_falls()
    {
        var currents = Assert.IsType<S111StationSeriesData>(ScenarioLoading.Dataset(_catalog, Definition.CurrentStations!.DatasetName).Data).Dataset;
        var levels = Assert.IsType<S104StationSeriesData>(ScenarioLoading.Dataset(_catalog, Definition.Stations!.DatasetName).Data).Dataset;

        foreach (var station in levels.Stations)
        {
            var current = currents.Stations.Single(s => s.Identifier == station.Identifier);
            Assert.Equal(station.NumberOfTimes, current.NumberOfTimes);
            var slackChecked = 0;
            for (var i = 0; i < station.NumberOfTimes; i++)
            {
                Assert.Equal(station.TimeAt(i), current.TimeAt(i));
                switch (station.Trends[i])
                {
                    case 2: // increasing
                        Assert.Equal(90f, current.DirectionsDegreesTrue[i]);
                        Assert.True(current.SpeedsKnots[i] >= 0.3f, $"{station.Identifier} at {station.TimeAt(i):O}: {current.SpeedsKnots[i]} kn");
                        break;
                    case 1: // decreasing
                        Assert.Equal(270f, current.DirectionsDegreesTrue[i]);
                        Assert.True(current.SpeedsKnots[i] >= 0.3f, $"{station.Identifier} at {station.TimeAt(i):O}: {current.SpeedsKnots[i]} kn");
                        break;
                }

                // At each S-104 high or low water, the stream is slack.
                if (i > 0 && i < station.NumberOfTimes - 1
                    && Math.Sign(station.Heights[i] - station.Heights[i - 1]) != Math.Sign(station.Heights[i + 1] - station.Heights[i]))
                {
                    Assert.True(current.SpeedsKnots[i] < 0.15f, $"{station.Identifier} at {station.TimeAt(i):O}: {current.SpeedsKnots[i]} kn");
                    slackChecked++;
                }
            }

            Assert.InRange(slackChecked, 6, 10);
        }
    }
}

/// <summary>The S-421 routes read back as the routes the scenario defines.</summary>
public sealed class S421RoundTripTests(PortPlanVsOwnRouteFixture fixture) : IClassFixture<PortPlanVsOwnRouteFixture>
{
    private readonly FileDatasetCatalog _catalog = ScenarioLoading.Catalog(fixture.Scenario);

    public static TheoryData<int> Routes() => [0, 1];

    [Theory]
    [MemberData(nameof(Routes))]
    public void A_route_reads_back_with_its_waypoints_legs_and_schedule(int index)
    {
        var definition = fixture.Definition;
        var spec = definition.Routes[index];
        var dataset = Assert.IsType<S421DatasetData>(ScenarioLoading.Dataset(_catalog, spec.DatasetName).Data).Model;

        var plan = S421RoutePlan.From(dataset, out var diagnostics);

        Assert.Empty(diagnostics);
        var route = plan.Route;
        Assert.Equal((spec.RouteId, S421RouteBuilder.Edition, spec.Name), (route.RouteId, route.EditionNumber!.Value, route.Info!.Name));
        Assert.Equal(S421RouteBuilder.Positions(definition, spec), route.Waypoints.Select(w => w.Position));
        Assert.Equal(spec.Waypoints, route.Waypoints.Select(w => w.Name));
        Assert.Equal(spec.Waypoints.Count - 1, route.Legs.Count);
        for (var i = 0; i < route.Legs.Count; i++)
        {
            var leg = route.Legs[i];
            Assert.Same(route.Waypoints[i], leg.StartWaypoint);
            Assert.Same(route.Waypoints[i + 1], leg.EndWaypoint);
            Assert.Equal(spec.Passage.DraftMeters, leg.Draft);
            Assert.Equal(spec.Passage.RequiredUkcMeters, leg.StaticUnderKeelClearance);
        }

        var elements = Assert.Single(Assert.Single(route.Schedules).Variants).Elements;
        var distances = S421RouteBuilder.Distances(definition, spec);
        Assert.Equal(new DateTimeOffset(spec.Departure, TimeSpan.Zero), elements[0].Etd);
        Assert.All(elements.Take(elements.Count - 1), e => Assert.Equal(spec.Passage.SpeedKnots, e.PlannedSpeedOverGround));
        for (var i = 1; i < elements.Count; i++)
        {
            Assert.Equal(new DateTimeOffset(S421RouteBuilder.TimeAt(spec, distances[i]), TimeSpan.Zero), elements[i].Eta);
        }
    }
}

/// <summary>The S-124 warnings read back as the warnings the scenario defines.</summary>
public abstract class S124RoundTripTests(GeneratedScenarioFixture fixture, int edition)
{
    private readonly FileDatasetCatalog _catalog = ScenarioLoading.Catalog(fixture.Scenario);

    public static TheoryData<int> Warnings() => new(Enumerable.Range(0, SyntheticScenarios.RouteWarnings.Count));

    [Theory]
    [MemberData(nameof(Warnings))]
    public void A_warning_reads_back_with_its_geometry_and_validity(int index)
    {
        var spec = fixture.Definition.Warnings[index];
        var warning = Project(spec);

        var preamble = Assert.IsType<S124NavwarnPreamble>(warning.Preamble);
        Assert.Equal((spec.WarningNumber, spec.Year), (preamble.MessageSeriesIdentifier!.WarningNumber, preamble.MessageSeriesIdentifier.Year));
        Assert.Equal(spec.Title, preamble.Title);
        Assert.Equal(S124WarningWriter.Agency, preamble.MessageSeriesIdentifier.ProductionAgency);

        var part = Assert.Single(warning.Parts);
        Assert.Equal(spec.Text, part.WarningInformation);
        Assert.Equal(
            spec.Shape.Kind switch
            {
                WarningGeometryKind.Point => S124GeometryKind.Point,
                WarningGeometryKind.Line => S124GeometryKind.Curve,
                _ => S124GeometryKind.Surface,
            },
            part.GeometryKind);
        Assert.Equal(S124WarningWriter.Positions(fixture.Definition.World, spec.Shape), part.Coordinates);

        var range = Assert.Single(part.ValidityRanges);
        Assert.Equal(Utc(spec.ValidFrom), range.Start);
        Assert.Equal(Utc(spec.ValidTo), range.End);
    }

    [Fact]
    public void The_cancelling_warning_names_the_warning_it_cancels()
    {
        var spec = fixture.Definition.Warnings.Single(w => w.Cancels.Count > 0);
        var reference = Assert.Single(Project(spec).References);

        Assert.Equal(1, reference.ReferenceCategory);
        if (edition == 2)
        {
            Assert.Equal(spec.Cancels, reference.Warnings.Select(w => w.WarningNumber!.Value));
            Assert.Equal(spec.Year, Assert.Single(reference.Warnings).Year);
        }
        else
        {
            Assert.Equal(S124WarningWriter.MessageReference(spec.Cancels[0], spec.Year), reference.MessageReference);
        }
    }

    protected S124NavigationalWarning Project(S124WarningSpec spec)
    {
        var dataset = Assert.IsType<S124DatasetData>(ScenarioLoading.Dataset(_catalog, spec.DatasetName).Data).Model;
        Assert.Equal(edition == 2 ? "2.0.0" : "1.0.0", dataset.DeclaredEdition);
        var warning = S124NavigationalWarning.From(dataset, out var diagnostics);
        Assert.Empty(diagnostics);
        return warning;
    }

    protected static DateTimeOffset? Utc(DateTime? time) => time is { } t ? new DateTimeOffset(t, TimeSpan.Zero) : null;
}

public sealed class S124Edition2RoundTripTests(WarningsOnRouteFixture fixture)
    : S124RoundTripTests(fixture, edition: 2), IClassFixture<WarningsOnRouteFixture>
{
    private readonly WarningsOnRouteFixture _fixture = fixture;

    [Fact]
    public void Edition_2_carries_publication_and_cancellation_times()
    {
        foreach (var spec in _fixture.Definition.Warnings)
        {
            var preamble = Project(spec).Preamble!;
            Assert.Equal(Utc(spec.Published), preamble.PublicationTime);
            Assert.Equal(Utc(spec.CancellationDate), preamble.CancellationDate);
            Assert.Equal(spec.TypeGeneral, preamble.NavwarnTypeGeneral);
        }
    }
}

public sealed class S124Edition1RoundTripTests(WarningsOnRouteEdition1Fixture fixture)
    : S124RoundTripTests(fixture, edition: 1), IClassFixture<WarningsOnRouteEdition1Fixture>;

/// <summary>The S-129 plan reads back as the plan the world model computes.</summary>
public sealed class S129RoundTripTests(PortPlanVsOwnRouteFixture fixture) : IClassFixture<PortPlanVsOwnRouteFixture>
{
    private readonly FileDatasetCatalog _catalog = ScenarioLoading.Catalog(fixture.Scenario);

    private S129PlanSpec Spec => fixture.Definition.UkcPlans[0];

    [Fact]
    public void The_plan_metadata_names_its_route_and_period()
    {
        var plan = Read();
        var route = S129PlanWriter.RouteOf(fixture.Definition, Spec);
        var (start, end) = S129PlanWriter.TimeRange(fixture.Definition, Spec);

        var metadata = Assert.IsType<S129UkcPlanMetadata>(plan.Plan);
        Assert.Equal(route.RouteId, metadata.SourceRoute?.Identifier);
        Assert.Equal("1", metadata.SourceRoute?.Version);
        Assert.Equal(route.Passage.DraftMeters, metadata.MaximumDraught);
        Assert.Equal(new DateTimeOffset(start, TimeSpan.Zero), metadata.FixedTimeRange?.Start);
        Assert.Equal(new DateTimeOffset(end, TimeSpan.Zero), metadata.FixedTimeRange?.End);
        Assert.Equal("Actual Plan", metadata.UnderKeelClearancePurpose);
    }

    [Fact]
    public void The_control_points_are_the_model_values()
    {
        var plan = Read();
        var truth = S129PlanWriter.ControlPoints(fixture.Definition, Spec);

        Assert.Equal(truth.Count, plan.ControlPoints.Count);
        for (var i = 0; i < truth.Count; i++)
        {
            var point = plan.ControlPoints[i];
            Assert.Equal(truth[i].Name, point.FeatureName?.Name);
            Assert.Equal(truth[i].Position, point.Position);
            Assert.Equal(new DateTimeOffset(truth[i].ExpectedPassingTime, TimeSpan.Zero), point.ExpectedPassingTime);
            Assert.Equal(truth[i].DistanceAboveUkcLimit, point.DistanceAboveUkcLimit);
            Assert.Equal(new DateTimeOffset(truth[i].WindowStart, TimeSpan.Zero), point.FixedTimeRange?.Start);
            Assert.Equal(new DateTimeOffset(truth[i].WindowEnd, TimeSpan.Zero), point.FixedTimeRange?.End);
        }
    }

    [Fact]
    public void The_areas_are_the_model_regions()
    {
        var plan = Read();
        var (nonNavigable, almost) = S129PlanWriter.Areas(fixture.Definition, Spec);

        Assert.NotEmpty(nonNavigable);
        Assert.NotEmpty(almost);
        Assert.Equal(nonNavigable.Select(p => p.Exterior), plan.NonNavigableAreas.Select(a => a.Coordinates));
        Assert.Equal(almost.Select(p => p.Exterior), plan.AlmostNonNavigableAreas.Select(a => a.Coordinates));
        Assert.Equal(GmlWriting.Rectangle(fixture.Definition.World.Frame, Spec.PlanArea).Exterior, plan.PlanArea?.Coordinates);
    }

    private S129UnderKeelClearancePlan Read()
    {
        var dataset = Assert.IsType<S129DatasetData>(ScenarioLoading.Dataset(_catalog, Spec.DatasetName).Data).Model;
        var plan = S129UnderKeelClearancePlan.From(dataset, out var diagnostics);
        Assert.Empty(diagnostics);
        return plan;
    }
}

/// <summary>The S-122 sanctuary reads back as the area the scenario defines.</summary>
public sealed class S122RoundTripTests(WarningsOnRouteFixture fixture) : IClassFixture<WarningsOnRouteFixture>
{
    private readonly FileDatasetCatalog _catalog = ScenarioLoading.Catalog(fixture.Scenario);

    private S122ProtectedAreaSpec Spec => fixture.Definition.ProtectedAreas[0];

    [Fact]
    public void The_area_reads_back_with_its_name_season_and_restriction()
    {
        var dataset = Read();
        var area = Assert.Single(dataset.MarineProtectedAreas);

        Assert.Equal([new S122FeatureName("eng", Spec.Name, 1)], area.FeatureNames);
        Assert.Equal(Spec.Name, area.FeatureName);
        Assert.Equal(S122GeometryKind.Surface, area.GeometryKind);
        Assert.Equal(S122ProtectedAreaWriter.Polygon(fixture.Definition.World, Spec).Exterior, area.Coordinates);
        Assert.Null(area.FixedDateRange);
        Assert.Equal(Spec.Seasons.Select(s => new S122DateRange(s.Start.Text, s.End.Text)), area.PeriodicDateRanges);
        Assert.Equal(
            (Spec.CategoryOfMarineProtectedArea, Spec.CategoryOfRestrictedArea, Spec.Restriction, Spec.Status),
            (area.CategoryOfMarineProtectedArea, area.CategoryOfRestrictedArea, area.Restriction, area.Status));
        Assert.Equal(5, area.Status);
    }

    [Fact]
    public void The_area_names_its_authority()
    {
        var area = Assert.Single(Read().MarineProtectedAreas);

        var binding = Assert.Single(area.InformationReferences);
        Assert.Equal("responsibleAuthority", binding.Role);
        var authority = Assert.IsType<S122Authority>(binding.Target);
        Assert.Equal(Spec.Authority, authority.FeatureName);
        Assert.Equal(Spec.CategoryOfAuthority, authority.CategoryOfAuthority);
    }

    [Fact]
    public void The_coverage_carries_the_display_scales()
    {
        var coverage = Assert.Single(Read().Features.OfType<S122DataCoverage>());

        Assert.Equal((Spec.DisplayScales.Maximum, Spec.DisplayScales.Minimum), (coverage.MaximumDisplayScale, coverage.MinimumDisplayScale));
        Assert.Equal(S122ProtectedAreaWriter.CoveragePolygon(fixture.Definition.World, Spec).Exterior, coverage.Coordinates);
    }

    // The scenario's truth: the transit crosses the sanctuary while the
    // seasonal restriction is in force, and would not in high summer.
    [Fact]
    public void The_route_crosses_the_sanctuary_in_season()
    {
        var definition = fixture.Definition;
        var route = definition.Routes[0];
        var path = route.Waypoints.Select(name => definition.Places[name]).ToList();

        var (relation, start, end) = RouteWarnings.Relate(path, WarningGeometryKind.Area, WarningShape.Area(Spec.Area).Points);

        Assert.Equal(WarningRouteRelation.Crosses, relation);
        Assert.Equal(Spec.Area.East - Spec.Area.West, end - start, 6);
        Assert.True(Spec.InForce(S421RouteBuilder.TimeAt(route, start)));
        Assert.True(Spec.InForce(S421RouteBuilder.TimeAt(route, end)));
        Assert.False(Spec.InForce(new DateTime(2026, 7, 1, 6, 0, 0, DateTimeKind.Utc)));
    }

    private S122MarineProtectedAreaDataset Read()
    {
        var dataset = Assert.IsType<S122DatasetData>(ScenarioLoading.Dataset(_catalog, Spec.DatasetName).Data).Model;
        Assert.Equal("2.0.0", dataset.DeclaredEdition);
        var typed = S122MarineProtectedAreaDataset.From(dataset, out var diagnostics);
        Assert.Empty(diagnostics);
        return typed;
    }
}

/// <summary>The S-125 buoys and lights read back as the aids the scenario defines.</summary>
public sealed class S125RoundTripTests(WarningsOnRouteFixture fixture) : IClassFixture<WarningsOnRouteFixture>
{
    private readonly FileDatasetCatalog _catalog = ScenarioLoading.Catalog(fixture.Scenario);

    private S125AidsSpec Spec => fixture.Definition.Aids!;

    [Fact]
    public void Every_buoy_reads_back_at_its_position_and_kind()
    {
        var dataset = Read();
        var buoys = dataset.Aids.OfType<S125Buoy>().ToList();

        Assert.Equal(Spec.Buoys.Count, buoys.Count);
        for (var i = 0; i < Spec.Buoys.Count; i++)
        {
            var spec = Spec.Buoys[i];
            var buoy = buoys.Single(b => b.Id == Spec.BuoyId(i));
            Assert.Equal(S125AidsWriter.FeatureType(spec.Mark), buoy.FeatureType);
            Assert.Equal(
                spec.Mark switch
                {
                    BuoyMark.PortHand or BuoyMark.StarboardHand => S125BuoyKind.Lateral,
                    BuoyMark.SafeWater => S125BuoyKind.SafeWater,
                    _ => S125BuoyKind.Cardinal,
                },
                buoy.Kind);
            Assert.Equal(S125AidsWriter.Position(fixture.Definition.World, spec), buoy.Position);
            Assert.Equal(spec.Number, buoy.ExtraAttributes["atoNNumber"]);
            Assert.Null(buoy.HostStructure);
        }
    }

    [Fact]
    public void Every_light_is_hosted_by_its_buoy()
    {
        var dataset = Read();
        var lights = dataset.Aids.OfType<S125Light>().ToList();

        var lit = Enumerable.Range(0, Spec.Buoys.Count).Where(i => Spec.Buoys[i].Light is not null).ToList();
        Assert.Equal(lit.Count, lights.Count);
        foreach (var i in lit)
        {
            var light = lights.Single(l => l.Id == Spec.LightId(i));
            Assert.Equal(S125LightKind.AllAround, light.Kind);
            Assert.Equal(Spec.BuoyId(i), light.HostStructure?.Id);
            Assert.Equal(S125AidsWriter.Position(fixture.Definition.World, Spec.Buoys[i]), light.Position);
        }
    }

    // The lateral buoys gate the dredged channel: port hand on its north
    // edge, starboard hand on its south edge (buoyage eastward, IALA A).
    [Fact]
    public void The_lateral_buoys_mark_the_channel_edges()
    {
        var channel = fixture.Definition.DredgedChannels[0].Area;

        Assert.All(Spec.Buoys.Where(b => b.Mark == BuoyMark.PortHand), b => Assert.Equal(channel.North, b.Y));
        Assert.All(Spec.Buoys.Where(b => b.Mark == BuoyMark.StarboardHand), b => Assert.Equal(channel.South, b.Y));
    }

    private S125AtonDataset Read()
    {
        var dataset = Assert.IsType<S125DatasetData>(ScenarioLoading.Dataset(_catalog, Spec.DatasetName).Data).Model;
        Assert.Equal("1.0.0", dataset.DeclaredEdition);
        var typed = S125AtonDataset.From(dataset, out var diagnostics);
        Assert.Empty(diagnostics);
        return typed;
    }
}
