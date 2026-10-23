using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// "Restricted areas on route" through <see cref="RouteAreaCheck"/> (#956):
/// the generated S-101 cells and S-122 datasets, read by the real readers
/// into the area adapter, give one finding per area as the scenario places
/// them.
/// </summary>
public sealed class RestrictedAreasOnRouteCheckTests(RestrictedAreasOnRouteFixture fixture) : IClassFixture<RestrictedAreasOnRouteFixture>
{
    private readonly FileDatasetCatalog _catalog = ScenarioLoading.Catalog(fixture.Scenario);

    private S421RouteSpec Route => fixture.Definition.Routes[0];

    [Fact]
    public void Each_area_gives_the_finding_the_scenario_places()
    {
        var result = Check(Route.Departure);

        Assert.Equal((RouteAreaCodes.EntersProhibitedArea, RouteFindingGroup.MustReview), Kind(result, "Firing Range Alpha (fictitious)"));
        Assert.Equal((RouteAreaCodes.RestrictedArea, RouteFindingGroup.Info), Kind(result, "Cable Landing Zone (fictitious)"));
        Assert.Equal((RouteAreaCodes.ProhibitedAreaInCorridor, RouteFindingGroup.Check), Kind(result, "Range Danger Zone (fictitious)"));
        Assert.Equal((RouteAreaCodes.CautionArea, RouteFindingGroup.Check), Kind(result, "Spit Shoal Caution (fictitious)"));
        Assert.Equal((RouteAreaCodes.AreaNotInForce, RouteFindingGroup.Info), Kind(result, "Summer Speed Zone (fictitious)"));
        Assert.Equal((RouteAreaCodes.ProtectedArea, RouteFindingGroup.Check), Kind(result, "Synthetic Sound Seal Sanctuary (fictitious)"));
        Assert.Equal((RouteAreaCodes.ProtectedArea, RouteFindingGroup.Check), Kind(result, "Synthetic Sound Berth Reserve (fictitious)"));
        Assert.All(result.Findings, f => Assert.Equal(RouteFindingSource.RouteCheck, f.Source));
    }

    [Fact]
    public void A_coarse_cells_copy_of_an_area_gives_no_second_finding()
    {
        var result = Check(Route.Departure);

        // Every S-101 area is charted in both cells; only the harbour cell's counts.
        foreach (var name in new[] { "Firing Range Alpha (fictitious)", "Spit Shoal Caution (fictitious)" })
        {
            var finding = Assert.Single(result.Findings, f => f.Title.Contains(name, StringComparison.Ordinal));
            Assert.Equal("101ZZ00SPIT5", finding.Feature!.DatasetId);
        }

        // An area only the coastal cell charts, where the harbour cell covers the place, doesn't count.
        var exercise = Assert.Single(result.Matches, m => m.Area.Name == "Coastal Exercise Area (fictitious)");
        Assert.Equal(RouteAreaRelation.Off, exercise.Relation);
        Assert.Null(exercise.Finding);
    }

    [Fact]
    public void Spans_follow_the_areas_along_the_route()
    {
        var result = Check(Route.Departure);
        var scale = result.Matches.Count == 0 ? 1 : LengthScale();

        var firing = Match(result, "Firing Range Alpha (fictitious)");
        Assert.Equal(RouteAreaRelation.Enters, firing.Relation);
        Assert.Equal((600 - 300) * scale, firing.StartDistanceMeters, 2.0);
        Assert.Equal((900 - 300) * scale, firing.EndDistanceMeters, 2.0);

        var seal = Match(result, "Synthetic Sound Seal Sanctuary (fictitious)");
        Assert.Equal((3100 - 300) * scale, seal.StartDistanceMeters, 2.0);
        Assert.Equal((3700 - 300) * scale, seal.EndDistanceMeters, 2.0);

        // The route ends in the reserve's hole: only 5500–5550 m is inside.
        var berth = Match(result, "Synthetic Sound Berth Reserve (fictitious)");
        Assert.Equal(RouteAreaRelation.Enters, berth.Relation);
        Assert.Equal((5500 - 300) * scale, berth.StartDistanceMeters, 2.0);
        Assert.Equal((5550 - 300) * scale, berth.EndDistanceMeters, 2.0);
    }

    [Fact]
    public void A_finding_names_the_area_its_restriction_and_category()
    {
        var firing = Finding(Check(Route.Departure), "Firing Range Alpha (fictitious)");

        Assert.Equal("Enters Firing Range Alpha (fictitious)", firing.Title);
        Assert.StartsWith("Leg 1 → 2 · S-101 restricted area · entry prohibited · military area · 2026-03-01 06:0", firing.Meta, StringComparison.Ordinal);
        Assert.Equal(0, firing.LegIndex);
        Assert.False(firing.Location!.IsPoint);

        var summer = Finding(Check(Route.Departure), "Summer Speed Zone (fictitious)");
        Assert.Equal("Summer Speed Zone (fictitious) not in force during the passage", summer.Title);
        Assert.Contains("speed restricted · 1 Jun–31 Aug", summer.Meta, StringComparison.Ordinal);
    }

    [Fact]
    public void A_summer_passage_brings_the_seasonal_zone_into_force()
    {
        var summer = Check(new DateTime(2026, 7, 1, 6, 0, 0, DateTimeKind.Utc));

        Assert.Equal((RouteAreaCodes.RestrictedArea, RouteFindingGroup.Check), Kind(summer, "Summer Speed Zone (fictitious)"));

        // The seal sanctuary's season (1 March to 31 May) is over by July.
        Assert.Equal((RouteAreaCodes.AreaNotInForce, RouteFindingGroup.Info), Kind(summer, "Synthetic Sound Seal Sanctuary (fictitious)"));
    }

    [Fact]
    public void Without_speeds_areas_are_judged_at_the_view_time()
    {
        // A plan without a departure or speeds is judged at the settings' time.
        var legs = ScenarioPassage.Legs(_catalog, Route).Select(l => l.WithSpeed(null)).ToList();
        var viewTime = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var result = new RouteAreaCheck().Check(
            Plan(legs, departure: null),
            PassageLegs.Settings(viewTime),
            CatalogRouteCheckSources.FromDatasets(_catalog.Datasets, new ProjNetCrsTransformFactory()).Areas!,
            TestContext.Current.CancellationToken);

        Assert.Equal((RouteAreaCodes.RestrictedArea, RouteFindingGroup.Check), Kind(result, "Summer Speed Zone (fictitious)"));
        var seal = Finding(result, "Synthetic Sound Seal Sanctuary (fictitious)");
        Assert.Equal("Synthetic Sound Seal Sanctuary (fictitious) not in force at 2026-06-15 12:00 UTC", seal.Title);
    }

    private RouteAreaResult Check(DateTime departure)
    {
        var legs = ScenarioPassage.Legs(_catalog, Route);
        var start = new DateTimeOffset(departure, TimeSpan.Zero);
        return new RouteAreaCheck().Check(
            Plan(legs, start),
            PassageLegs.Settings(start),
            CatalogRouteCheckSources.FromDatasets(_catalog.Datasets, new ProjNetCrsTransformFactory()).Areas!,
            TestContext.Current.CancellationToken);
    }

    // Metres along the legs on the ellipsoid per local metre.
    private double LengthScale()
    {
        var legs = ScenarioPassage.Legs(_catalog, Route);
        var path = Route.Waypoints.Select(name => fixture.Definition.Places[name]).ToList();
        var local = path.Zip(path.Skip(1), (a, b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2))).Sum();
        return PassageSchedule.Compute(legs, new DateTimeOffset(Route.Departure, TimeSpan.Zero)).TotalDistanceMeters / local;
    }

    private static PassagePlan Plan(IReadOnlyList<PassageLeg> legs, DateTimeOffset? departure) =>
        new(legs.Select(l => l.WithHalfWidth(S421RouteBuilder.CrossTrackLimitMeters)).ToList(), departure);

    private static RouteAreaMatch Match(RouteAreaResult result, string name) =>
        Assert.Single(result.Matches, m => m.Area.Name == name && m.Finding is not null);

    private static RouteFinding Finding(RouteAreaResult result, string name) => Match(result, name).Finding!;

    private static (string Code, RouteFindingGroup Group) Kind(RouteAreaResult result, string name) =>
        Finding(result, name) is var f ? (f.Code, f.Group) : default;
}

/// <summary>
/// The S-122 seal sanctuary of "Warnings on route" (#956), which the #958
/// mock lists as the route's one route-check finding: CHECK, 3100–3700 m.
/// </summary>
public sealed class WarningsOnRouteAreaTests(WarningsOnRouteFixture fixture) : IClassFixture<WarningsOnRouteFixture>
{
    private readonly FileDatasetCatalog _catalog = ScenarioLoading.Catalog(fixture.Scenario);

    [Fact]
    public void The_seal_sanctuary_is_crossed_and_in_force()
    {
        var route = fixture.Definition.Routes[0];
        var legs = ScenarioPassage.Legs(_catalog, route);
        var departure = new DateTimeOffset(route.Departure, TimeSpan.Zero);
        var schedule = PassageSchedule.Compute(legs, departure);
        var result = new RouteAreaCheck().Check(
            new PassagePlan(legs.Select(l => l.WithHalfWidth(S421RouteBuilder.CrossTrackLimitMeters)).ToList(), departure),
            PassageLegs.Settings(departure),
            CatalogRouteCheckSources.FromDatasets(_catalog.Datasets, new ProjNetCrsTransformFactory()).Areas!,
            TestContext.Current.CancellationToken);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(RouteAreaCodes.ProtectedArea, finding.Code);
        Assert.Equal(RouteFindingGroup.Check, finding.Group);
        Assert.Equal("Crosses Synthetic Sound Seal Sanctuary (fictitious)", finding.Title);
        Assert.Equal("Leg 1 → 2 · S-122 marine protected area · speed restricted · seal sanctuary, IUCN IV · 1 Mar–31 May · 2026-03-01 06:11 UTC–2026-03-01 06:13 UTC", finding.Meta);
        Assert.Equal("122ZZ00SEAL", finding.Feature!.DatasetId);

        // 3100–3700 m in the world's local frame, from the west entrance at 300 m.
        var path = route.Waypoints.Select(name => fixture.Definition.Places[name]).ToList();
        var scale = schedule.TotalDistanceMeters / path.Zip(path.Skip(1), (a, b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2))).Sum();
        Assert.Equal(2800 * scale, finding.Location!.StartDistanceMeters, 2.0);
        Assert.Equal(3400 * scale, finding.Location.EndDistanceMeters, 2.0);
    }
}

/// <summary>"Restricted areas on route" with the default seed.</summary>
public sealed class RestrictedAreasOnRouteFixture() : GeneratedScenarioFixture(SyntheticScenarios.RestrictedAreasOnRoute());
