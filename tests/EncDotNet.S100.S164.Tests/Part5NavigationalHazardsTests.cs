using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.S164.Tests;

/// <summary>
/// S-164 Part 5 route-planning tests (NavigationalHazards,
/// NavigationalHazardsLS) on the S-101 cell <c>10100AA_NAVHZ</c>. The manual
/// plans a route through the features named WP1 to WP18 and compares the
/// indications with reference plots; these run <see cref="RouteChecker"/> on
/// the same route and compare its danger findings with what the plots and the
/// S-101 portrayal catalogue (<c>UDWHAZ05</c>) indicate. Feature ids are the
/// cell's feature record ids.
/// </summary>
public sealed class Part5NavigationalHazardsTests
{
    private const string Cell = "10100AA_NAVHZ.000";
    private const string Overview = "101AA00AA2OVRVU.000";

    /// <summary>The test's safety depth; the manual repeats the route at different safety contours.</summary>
    private const double SafetyDepth = 30;

    [Fact]
    public async Task NavigationalHazards_RouteRunsThroughTheMarkedWaypoints()
    {
        var datasets = await S164RouteCheck.LoadAsync(TestContext.Current.CancellationToken, "NavigationalHazards");

        var waypoints = S164RouteCheck.NamedWaypoints(datasets.Single());

        Assert.Equal(Enumerable.Range(1, 18), waypoints.Keys);
        Assert.All(waypoints.Values, p => Assert.True(S164RouteCheck.Inside(datasets.Single().Bounds, p), $"{p} is outside the cell."));
    }

    [Fact]
    public async Task NavigationalHazards_DangerDepthsFollowThePortrayal()
    {
        // OBSTRN07 and WRECKS05 take a danger's depth from valueOfSounding,
        // else from defaultClearanceDepth. The dangers the 10 m plot boxes are
        // the ones reported.
        var dangers = await DangersAsync(safetyContour: 10, safetyDepth: 10);

        // Wrecks of unknown depth (-15 m, 0 m), 5 m and 4 m wrecks, a 4 m
        // obstruction and three 0.1 m obstructions of unknown depth.
        Assert.Superset(new HashSet<string> { "103", "104", "107", "108", "115", "117", "65", "94", "95", "97" }, dangers);

        // Non-dangerous wrecks at 20.1 m, 42 m wrecks, a 16 m obstruction and
        // a 50 m one.
        Assert.Empty(dangers.Intersect(["105", "106", "116", "118", "64", "93"]));
    }

    public static TheoryData<double, string[], string[]> ContourCases() => new()
    {
        // Safety contour, dangers the plot indicates, dangers it doesn't.
        { 0, ["103", "104", "107", "108", "119", "120"], ["105", "106", "115", "117", "94", "95", "97"] },
        { 10, ["115", "117", "65", "94", "95", "97"], ["105", "106", "64", "116", "118"] },
        { 51, ["105", "106", "116", "118", "64", "92", "93", "96"], [] },
    };

    [Theory]
    [MemberData(nameof(ContourCases))]
    public async Task NavigationalHazards_DangersFollowTheSafetyContour(double safetyContour, string[] indicated, string[] notIndicated)
    {
        var dangers = await DangersAsync(safetyContour, SafetyDepth);

        Assert.Empty(indicated.Except(dangers));
        Assert.Empty(dangers.Intersect(notIndicated));
    }

    [Fact(Skip = "#1127: only wrecks, obstructions and rocks are checked, not every NavHazard the alert catalogue lists.")]
    public async Task NavigationalHazards_IndicatesTheAlertCatalogueHazards()
    {
        // Every feature type here draws AlertReference:NavHazard in the S-101
        // PC, and the route passes over one of each.
        string[] hazards =
        [
            "CardinalBuoy", "CardinalBeacon", "IsolatedDangerBuoy", "IsolatedDangerBeacon", "LateralBuoy",
            "LateralBeacon", "SafeWaterBuoy", "SafeWaterBeacon", "SpecialPurposeGeneralBuoy",
            "SpecialPurposeGeneralBeacon", "InstallationBuoy", "Daymark", "Pile", "MooringBuoy", "Dolphin",
            "LightFloat", "LightVessel", "OffshorePlatform", "OilBarrier", "IceArea", "VirtualAISAidToNavigation",
            "Landmark", "SpanFixed", "CableOverhead", "PipelineOverhead", "Conveyor", "PylonBridgeSupport",
            "LogPond", "FishingFacility",
        ];
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S164RouteCheck.LoadAsync(ct, "NavigationalHazards");
        var plan = S164RouteCheck.Plan(S164RouteCheck.NamedWaypoints(datasets.Single()).Values);

        var report = S164RouteCheck.Checker(datasets).Check(plan, S164RouteCheck.Settings(10, SafetyDepth, S164RouteCheck.TenthOfAMile), ct);

        var found = report.Findings.Select(f => f.Feature?.FeatureType).OfType<string>().ToHashSet();
        Assert.Empty(hazards.Except(found));
    }

    [Fact]
    public async Task NavigationalHazardsLS_UsesTheLargestScaleCell()
    {
        // WP1 to WP8 are charted in the overview cell and cross the NAVHZ
        // cell twice. The manual's plots are still to be drawn; the rule it
        // tests is that where both cells cover the route, only NAVHZ counts.
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S164RouteCheck.LoadAsync(ct, "NavigationalHazards", "NavigationalHazardsOverview");
        var waypoints = S164RouteCheck.NamedWaypoints(datasets.Single(d => d.Id.Value == Overview));
        var plan = S164RouteCheck.Plan(Enumerable.Range(1, 8).Select(i => waypoints[i]));

        var report = S164RouteCheck.Checker(datasets).Check(plan, S164RouteCheck.Settings(30, 30, S164RouteCheck.HalfAMile), ct);

        S164RouteCheck.AssertLargestScale(report, datasets, Cell, Overview);
        Assert.Contains(report.Findings, f => f.Code == RouteFindingCodes.DangerInCorridor && f.Feature?.DatasetId == Cell);
    }

    private static async Task<HashSet<string>> DangersAsync(double safetyContour, double safetyDepth)
    {
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S164RouteCheck.LoadAsync(ct, "NavigationalHazards");
        var plan = S164RouteCheck.Plan(S164RouteCheck.NamedWaypoints(datasets.Single()).Values);

        var report = S164RouteCheck.Checker(datasets).Check(
            plan, S164RouteCheck.Settings(safetyContour, safetyDepth, S164RouteCheck.TenthOfAMile), ct);

        Assert.Equal(RouteCheckState.Ran, report.Status(ChartCheck.CheckName)!.State);
        return report.Findings
            .Where(f => f.Code == RouteFindingCodes.DangerInCorridor || f.Code == RouteFindingCodes.DangerNearRoute)
            .Select(f => f.Feature!.FeatureId)
            .ToHashSet();
    }
}
