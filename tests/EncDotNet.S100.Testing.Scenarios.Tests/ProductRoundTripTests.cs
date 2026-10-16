using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S124.DataModel;
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
