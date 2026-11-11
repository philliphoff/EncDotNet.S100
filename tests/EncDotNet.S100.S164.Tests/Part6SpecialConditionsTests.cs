using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.S164.Tests;

/// <summary>
/// S-164 Part 6 route-planning tests (SpecialConditions,
/// SpecialConditionsLS) on the S-101 cell <c>10100AA_ARSPC</c>. The manual
/// selects one kind of area at a time and checks that a route from WP1 to WP4
/// indicates the areas of that kind; these run <see cref="RouteChecker"/> on
/// the route and check that each area the reference plot boxes has a finding.
/// Feature ids are the cell's feature record ids.
/// </summary>
/// <remarks>
/// The cell doesn't chart WP1 to WP4. They lie on its two rows of areas, at
/// 39°48'N and 39°47'N, one minute of longitude beyond the first and last
/// areas, as the plots show them.
/// </remarks>
public sealed class Part6SpecialConditionsTests
{
    private const string Cell = "10100AA_ARSPC.000";
    private const string Overview = "101AA00AA2OVRVU.000";

    private static readonly GeoPosition[] Waypoints =
    [
        new(39.8, -104.98333),
        new(39.8, -104.76667),
        new(39.78333, -104.76667),
        new(39.78333, -104.98333),
    ];

    /// <summary>The kinds of area the manual selects in turn, and the features each plot boxes.</summary>
    public static IEnumerable<TheoryDataRow<string, string>> Selections() =>
    [
        new("Traffic separation zone", "5"),
        new("Inshore traffic zone", "6"),
        new("Restricted area", "7"),
        new("Caution area", "8"),
        new("Caution area (point)", "9"),
        new("Offshore production area", "10"),
        new("Area to be avoided", "11"),
        new("Military practice area", "12"),
        new("Military practice area (point)", "13"),
        new("Seaplane landing area", "14"),
        new("Seaplane landing area (point)", "15"),
        new("Submarine transit lane", "16"),
        new("Anchorage area", "17"),
        new("Anchorage area (point)", "18"),
        new("Marine farm/aquaculture", "19"),
        new("Marine farm/aquaculture (point)", "20"),
        new("PSSA", "22"),
    ];

    [Theory]
    [MemberData(nameof(Selections))]
    public async Task SpecialConditions_IndicatesTheSelectedArea(string selection, string featureId)
    {
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S164RouteCheck.LoadAsync(ct, "SpecialConditions");

        var report = S164RouteCheck.Checker(datasets).Check(
            S164RouteCheck.Plan(Waypoints), S164RouteCheck.Settings(0, 30, S164RouteCheck.TenthOfAMile), ct);

        Assert.True(
            report.Findings.Any(f => f.Category == RouteFindingCategory.Area && f.Feature?.FeatureId == featureId),
            $"{selection}: no area finding for feature {featureId}.");
    }

    [Fact]
    public async Task UserSelectionProhibitedAreas_AKindThatsOffIsNotIndicated()
    {
        // S-164 lets the mariner select the kinds; with anchorage areas off,
        // their areas (17, and 18 charted as a point) aren't reported, and one
        // finding says that the indication of the kind is off.
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S164RouteCheck.LoadAsync(ct, "SpecialConditions");
        var settings = S164RouteCheck.Settings(0, 30, S164RouteCheck.TenthOfAMile);

        var report = S164RouteCheck.Checker(datasets).Check(
            S164RouteCheck.Plan(Waypoints),
            new RouteCheckSettings
            {
                At = settings.At,
                SafetyContour = settings.SafetyContour,
                SafetyDepth = settings.SafetyDepth,
                CorridorOverride = settings.CorridorOverride,
                AreaKindsOff = [RouteAreaKinds.AnchorageArea],
            },
            ct);

        Assert.DoesNotContain(report.Findings, f => f.Feature?.FeatureId is "17" or "18");
        var off = Assert.Single(report.Findings, f => f.Code == RouteFindingCodes.AreaKindOff);
        Assert.Equal(RouteAreaKinds.AnchorageArea, ((AreaKindOffDetails)off.Details!).AreaKind);
        Assert.Equal(RouteFindingGroup.Info, off.Group);
        Assert.Contains(report.Findings, f => f.Feature?.FeatureId == "8");
    }

    [Fact]
    public async Task SpecialConditionsLS_UsesTheLargestScaleCell()
    {
        // WP20 to WP22 are charted in the overview cell and cross the ARSPC
        // cell's first row of areas. The manual's plot is still to be drawn.
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S164RouteCheck.LoadAsync(ct, "SpecialConditions", "NavigationalHazardsOverview");
        var waypoints = S164RouteCheck.NamedWaypoints(datasets.Single(d => d.Id.Value == Overview));
        var plan = S164RouteCheck.Plan([waypoints[20], waypoints[22]]);

        var report = S164RouteCheck.Checker(datasets).Check(plan, S164RouteCheck.Settings(0, 30, S164RouteCheck.HalfAMile), ct);

        S164RouteCheck.AssertLargestScale(report, datasets, Cell, Overview);
        Assert.Contains(report.Findings, f => f.Category == RouteFindingCategory.Area && f.Feature?.DatasetId == Cell);
    }
}
