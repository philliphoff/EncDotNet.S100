using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// "Warnings on route" through <see cref="RouteWarningCheck"/> (#957): the
/// S-124 files, read by the real readers into the route-warning adapter,
/// give the answer <see cref="RouteWarnings.For"/> predicts, in both editions.
/// </summary>
public abstract class WarningsOnRouteCheckTests(GeneratedScenarioFixture fixture, int edition)
{
    private readonly FileDatasetCatalog _catalog = ScenarioLoading.Catalog(fixture.Scenario);

    private ScenarioDefinition Definition => fixture.Definition;

    private S421RouteSpec Route => Definition.Routes[0];

    // The oracle's "near" is within the routes' cross-track limits.
    private static RouteWarningOptions OracleOptions => new() { NearDistanceMeters = RouteWarnings.NearDistanceMeters };

    [Fact]
    public void Every_warning_relates_to_the_route_as_the_model_says()
    {
        var truth = RouteWarnings.For(Definition, Route);
        var legs = ScenarioPassage.Legs(_catalog, Route);
        var result = Check(legs, Route.Departure, OracleOptions);

        // The model measures in the world's local metres, the check along the legs on the ellipsoid.
        var scale = PassageSchedule.Compute(legs, Departure(Route.Departure)).TotalDistanceMeters / LocalLength();
        foreach (var expected in truth)
        {
            var match = Match(result, expected.WarningNumber);
            Assert.Equal(expected.Relation, Relation(match.Relation));
            if (expected.Relation == WarningRouteRelation.Off)
            {
                Assert.Null(match.Finding);
                continue;
            }

            Assert.Equal(expected.StartDistanceMeters * scale, match.StartDistanceMeters, 1.0);
            Assert.Equal(expected.EndDistanceMeters * scale, match.EndDistanceMeters, 1.0);
            var warning = match.Finding!.Warning!;
            Assert.True(warning.AtEta);
            Assert.InRange((warning.From.UtcDateTime - expected.StartTime!.Value).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(2));
            Assert.InRange((warning.Until.UtcDateTime - expected.EndTime!.Value).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(2));
            Assert.Equal(expected.InForce, warning.Status == RouteWarningStatus.InForce);
        }
    }

    [Fact]
    public void Severity_follows_relation_and_time()
    {
        var result = Check(ScenarioPassage.Legs(_catalog, Route), Route.Departure, OracleOptions);

        Assert.Equal((RouteWarningCodes.OnRoute, RouteFindingGroup.MustReview), Kind(result, 1));
        Assert.Equal((RouteWarningCodes.NotInForce, RouteFindingGroup.Info), Kind(result, 2));
        Assert.Equal((RouteWarningCodes.NearRoute, RouteFindingGroup.Check), Kind(result, 3));
        Assert.Null(Match(result, 4).Finding);
        Assert.Equal((RouteWarningCodes.Cancelled, RouteFindingGroup.Info), Kind(result, 5));
        Assert.Null(Match(result, 6).Finding);

        // Edition 1.0.0 has no cancellation date, so the tow stays in force.
        Assert.Equal(
            edition == 1 ? (RouteWarningCodes.OnRoute, RouteFindingGroup.MustReview) : (RouteWarningCodes.Cancelled, RouteFindingGroup.Info),
            Kind(result, 7));

        Assert.All(result.Findings, f => Assert.Equal(RouteFindingSource.S124, f.Source));
        Assert.Equal(result.Findings.OrderBy(f => f.Group).ThenBy(f => f.DistanceMeters), result.Findings);
    }

    [Fact]
    public void A_finding_names_its_warning_and_feature()
    {
        var finding = Match(Check(ScenarioPassage.Legs(_catalog, Route), Route.Departure, OracleOptions), 1).Finding!;

        Assert.Equal("Inside navigational warning 001/26", finding.Title);
        Assert.StartsWith("ZZ00 LOCAL 001/26 · ", finding.Meta, StringComparison.Ordinal);
        Assert.Contains("2026-03-01 05:00 UTC–2026-03-01 08:00 UTC", finding.Meta, StringComparison.Ordinal);
        Assert.EndsWith("In force at 2026-03-01 06:08 UTC", finding.Meta, StringComparison.Ordinal);
        Assert.Equal("124ZZ00W0001", finding.Feature!.DatasetId);
        Assert.Equal(0, finding.LegIndex);
        Assert.False(finding.Location!.IsPoint);
        Assert.Equal(finding.Location.StartDistanceMeters, finding.DistanceMeters);
        Assert.Contains("SYNTHETIC. Survey vessel", finding.Warning!.Info.Texts[0].Text, StringComparison.Ordinal);

        var cable = Match(Check(ScenarioPassage.Legs(_catalog, Route), Route.Departure, OracleOptions), 2).Finding!;
        Assert.Equal("Navigational warning 002/26 not in force", cable.Title);
        Assert.True(cable.Location!.IsPoint);
    }

    [Fact]
    public void Within_half_a_mile_by_default()
    {
        var result = Check(ScenarioPassage.Legs(_catalog, Route), Route.Departure, options: null);

        // The moved diving area is 550 m north of the track: near at 0,5 NM, off at 100 m.
        Assert.Equal((RouteWarningCodes.NearRoute, RouteFindingGroup.Check), Kind(result, 6));
        Assert.Null(Match(result, 4).Finding);
    }

    [Fact]
    public void A_later_departure_moves_warnings_in_and_out_of_force()
    {
        var legs = ScenarioPassage.Legs(_catalog, Route);
        var afternoon = Check(legs, Route.Departure.AddHours(7), OracleOptions);

        // 13:00: the survey (05–08) has ended and the cable laying (12–18) has begun.
        Assert.Equal((RouteWarningCodes.NotInForce, RouteFindingGroup.Info), Kind(afternoon, 1));
        Assert.Equal((RouteWarningCodes.OnRoute, RouteFindingGroup.MustReview), Kind(afternoon, 2));
        Assert.Equal("Crosses navigational warning 002/26", Match(afternoon, 2).Finding!.Title);
    }

    [Fact]
    public void A_slower_passage_reaches_the_survey_after_it_ends()
    {
        // At 0,5 kn the survey area, 2,1 km in, is reached after 08:00.
        var legs = ScenarioPassage.Legs(_catalog, Route).Select(l => l.WithSpeed(0.5)).ToList();
        var slow = Check(legs, Route.Departure, OracleOptions);

        Assert.Equal((RouteWarningCodes.NotInForce, RouteFindingGroup.Info), Kind(slow, 1));
        Assert.StartsWith("Not in force at 2026-03-01 08:", Match(slow, 1).Finding!.Meta.Split(" · ")[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Without_speeds_warnings_are_judged_at_the_fallback_time()
    {
        // A plan without a departure or speeds is judged at the settings' time.
        var legs = ScenarioPassage.Legs(_catalog, Route).Select(l => l.WithSpeed(null)).ToList();
        var viewTime = new DateTimeOffset(2026, 3, 1, 13, 0, 0, TimeSpan.Zero);
        var result = new RouteWarningCheck(OracleOptions).Check(
            Plan(legs, departure: null),
            PassageLegs.Settings(viewTime),
            RouteWarningSources.FromDatasets(_catalog.Datasets),
            TestContext.Current.CancellationToken);

        var cable = Match(result, 2).Finding!;
        Assert.Equal(RouteFindingGroup.MustReview, cable.Group);
        Assert.False(cable.Warning!.AtEta);
        Assert.Equal(viewTime, cable.Warning.From);
    }

    [Fact]
    public void Without_speeds_warnings_are_judged_at_the_departure()
    {
        // With a departure, legs without a speed are judged at it, not at the settings' time.
        var legs = ScenarioPassage.Legs(_catalog, Route).Select(l => l.WithSpeed(null)).ToList();
        var departure = Departure(Route.Departure);
        var result = new RouteWarningCheck(OracleOptions).Check(
            Plan(legs, departure),
            PassageLegs.Settings(new DateTimeOffset(2026, 3, 1, 13, 0, 0, TimeSpan.Zero)),
            RouteWarningSources.FromDatasets(_catalog.Datasets),
            TestContext.Current.CancellationToken);

        var cable = Match(result, 2).Finding!;
        Assert.False(cable.Warning!.AtEta);
        Assert.Equal(departure, cable.Warning.From);
    }

    private RouteWarningResult Check(IReadOnlyList<PassageLeg> legs, DateTime departure, RouteWarningOptions? options) =>
        new RouteWarningCheck(options).Check(
            Plan(legs, Departure(departure)),
            PassageLegs.Settings(Departure(departure)),
            RouteWarningSources.FromDatasets(_catalog.Datasets),
            TestContext.Current.CancellationToken);

    private static PassagePlan Plan(IReadOnlyList<PassageLeg> legs, DateTimeOffset? departure) =>
        new(legs.Select(l => l.WithHalfWidth(S421RouteBuilder.CrossTrackLimitMeters)).ToList(), departure);

    private static DateTimeOffset Departure(DateTime utc) => new(utc, TimeSpan.Zero);

    private static RouteWarningMatch Match(RouteWarningResult result, int number) =>
        Assert.Single(result.Matches, m => m.Warning.Info.Reference?.Number == number);

    private static (string Code, RouteFindingGroup Group) Kind(RouteWarningResult result, int number) =>
        Match(result, number).Finding is { } f ? (f.Code, f.Group) : ("none", RouteFindingGroup.Info);

    private static WarningRouteRelation Relation(RouteWarningRelation relation) => relation switch
    {
        RouteWarningRelation.Crosses => WarningRouteRelation.Crosses,
        RouteWarningRelation.Near => WarningRouteRelation.Near,
        _ => WarningRouteRelation.Off,
    };

    private double LocalLength()
    {
        var path = Route.Waypoints.Select(name => Definition.Places[name]).ToList();
        return path.Zip(path.Skip(1), (a, b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2))).Sum();
    }
}

public sealed class WarningsOnRouteCheckEdition2Tests(WarningsOnRouteFixture fixture)
    : WarningsOnRouteCheckTests(fixture, edition: 2), IClassFixture<WarningsOnRouteFixture>;

public sealed class WarningsOnRouteCheckEdition1Tests(WarningsOnRouteEdition1Fixture fixture)
    : WarningsOnRouteCheckTests(fixture, edition: 1), IClassFixture<WarningsOnRouteEdition1Fixture>;
