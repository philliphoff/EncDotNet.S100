using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Tides.Stations;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// check_route's window constraints (#1561) over NOAA's recorded current
/// stations (tests/datasets/Tides/Stations/noaa-currents.json): a 2.2 NM
/// approach to Deception Pass (PUG1701_33) at 8 kn.
/// </summary>
public sealed class CheckRouteTimingTests
{
    private const double Knots = 8;
    private static readonly GeoPolyline Approach = new([new GeoPoint(48.4065, -122.70), new GeoPoint(48.4065, -122.644)]);

    private static readonly Lazy<TideStationCatalogue> Catalogue = new(() =>
        TideStationCatalogueFile.LoadAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "Tides", "noaa-currents.json")).GetAwaiter().GetResult());

    private static CheckRouteTool Tool(TideStationCatalogue? catalogue) =>
        new(new FakeDatasetCatalog(), null,
            new RouteCheckSourceCache(new ProjNetCrsTransformFactory(), new RouteCheckSourceOptions { TidePredictions = catalogue }),
            TimeProvider.System);

    private static DateTimeOffset FirstSlack()
    {
        var predictor = Catalogue.Value.TryCreateCurrentPrediction("PUG1701_33")!.Predictor;
        var slack = predictor.FindEvents(new DateTime(2027, 7, 18, 12, 0, 0, DateTimeKind.Utc), new DateTime(2027, 7, 19, 3, 0, 0, DateTimeKind.Utc)).First(e => e.IsSlack);
        return new DateTimeOffset(DateTime.SpecifyKind(slack.Time, DateTimeKind.Utc));
    }

    private static CheckRouteRequest Request(DateTimeOffset slack, WindowConstraintsArguments constraints) => new(
        Polyline: Approach,
        Departure: slack.AddMinutes(-30),
        Vessel: new VesselArguments(SpeedKnots: Knots),
        Window: new DepartureWindowArguments(From: slack.AddHours(-2), To: slack, StepMinutes: 5, Constraints: constraints));

    [Fact]
    public async Task A_slack_gate_at_the_pass_times_the_departure()
    {
        var slack = FirstSlack();
        var constraints = new WindowConstraintsArguments(Gates: [new GateArguments(Name: "Deception Pass", Waypoint: 1, Station: "Deception Pass", SlackWithinMinutes: 15)]);

        var result = await Tool(Catalogue.Value).InvokeAsync(Request(slack, constraints), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var error) ? error.Message : "");
        Assert.Equal("ran", value!.Checks.Single(c => c.Name == TimingCheck.CheckName).State);
        var timing = value.Timing!;

        // departure = slack − distance to the pass / speed, within a step.
        var toPass = TimeSpan.FromHours(value.DistanceNm / Knots);
        var window = Assert.Single(timing.Windows);
        Assert.InRange((window.Best - (slack - toPass)).TotalMinutes, -5, 5);
        Assert.Equal("PUG1701_33", timing.Planned.Gates.Single().StationId);
        Assert.Equal(25, timing.Departures!.Count);
        Assert.Equal(5, timing.StepMinutes);
    }

    [Fact]
    public async Task Applied_currents_change_the_running_time_through_the_pass()
    {
        var slack = FirstSlack();

        var result = await Tool(Catalogue.Value).InvokeAsync(Request(slack, new WindowConstraintsArguments(ApplyCurrents: true)), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var timing = value!.Timing!;
        Assert.True(timing.CurrentsApplied);
        Assert.Contains(timing.CurrentSources, s => s.Id == "PUG1701_33");
        var times = timing.Departures!.Select(d => d.RunningMinutes).Distinct().ToList();
        Assert.True(times.Count > 1, "the running time should change with the stream");
    }

    [Fact]
    public async Task Without_constraints_the_report_has_no_timing_check()
    {
        var result = await Tool(Catalogue.Value).InvokeAsync(new CheckRouteRequest(Polyline: Approach, Vessel: new VesselArguments(SpeedKnots: Knots)), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.DoesNotContain(value!.Checks, c => c.Name == TimingCheck.CheckName);
        Assert.Null(value.Timing);
    }

    [Fact]
    public async Task Applying_currents_without_predictions_is_partial()
    {
        var slack = FirstSlack();

        var result = await Tool(null).InvokeAsync(Request(slack, new WindowConstraintsArguments(ApplyCurrents: true)), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var check = value!.Checks.Single(c => c.Name == TimingCheck.CheckName);
        Assert.Equal("partial", check.State);
        Assert.Equal(TimingReasons.CurrentsMissing, check.Reason);
    }

    [Theory]
    [InlineData("gates[0].waypoint")]
    [InlineData("gates[0].slackWithinMinutes")]
    [InlineData("legLimits[0].maxOpposingKnots")]
    [InlineData("arrivalTide")]
    public async Task Bad_constraints_are_refused(string parameter)
    {
        var constraints = parameter switch
        {
            "gates[0].waypoint" => new WindowConstraintsArguments(Gates: [new GateArguments(SlackWithinMinutes: 10)]),
            "gates[0].slackWithinMinutes" => new WindowConstraintsArguments(Gates: [new GateArguments(Waypoint: 0)]),
            "legLimits[0].maxOpposingKnots" => new WindowConstraintsArguments(LegLimits: [new LegLimitArguments(MaxOpposingKnots: -1)]),
            _ => new WindowConstraintsArguments(ArrivalTide: new ArrivalTideArguments()),
        };

        var result = await Tool(null).InvokeAsync(Request(FirstSlack(), constraints), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("window.constraints." + parameter, Assert.IsType<InvalidArgument>(error).Parameter);
    }
}
