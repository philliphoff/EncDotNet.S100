using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.S164.Tests.S64;

/// <summary>
/// S-64 §6 route-planning tests (6.1 basic, 6.2 largest scale) on the S-57
/// cell <c>AA3ARSPC</c>, translated to S-101. The manual selects one kind of
/// area at a time and checks that a route from WP1 to WP4 indicates the areas
/// of that kind; these run <see cref="RouteChecker"/> on the route and check
/// that each kind the cell charts has an area finding.
/// </summary>
public sealed class S64Part6SpecialConditionsTests
{
    private const string Cell = "6.0 Special Conditions/ENC_ROOT/AA3ARSPC.000";
    private const string Overview = "5.0 Navigational Hazards/Overview/ENC_ROOT/AA2OVRVU.000";

    /// <summary>The kinds of area the cell charts on the route, as S-101 feature types.</summary>
    public static TheoryData<string> Kinds() =>
    [
        "SeparationZoneOrLine", "InshoreTrafficZone", "RestrictedArea", "CautionArea", "OffshoreProductionArea",
        "MilitaryPracticeArea", "SeaplaneLandingArea", "SubmarineTransitLane", "AnchorageArea", "MarineFarmCulture",
    ];

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task SpecialConditions_IndicatesTheArea(string featureType)
    {
        // Traffic separation zones and submarine transit lanes were dropped
        // by translation until #1239, and area findings had no feature type
        // until #1240.
        var report = await CheckAsync(S164RouteCheck.Settings(0, 30, S164RouteCheck.TenthOfAMile));

        Assert.Contains(report.Findings, f => f.Category == RouteFindingCategory.Area && f.Feature?.FeatureType == featureType);
    }

    [Fact]
    public async Task UserSelectionProhibitedAreas_AKindThatsOffIsNotIndicated()
    {
        // With anchorage areas off their areas aren't reported, and one
        // finding says that the indication of the kind is off.
        var settings = S164RouteCheck.Settings(0, 30, S164RouteCheck.TenthOfAMile);

        var report = await CheckAsync(new RouteCheckSettings
        {
            At = settings.At,
            SafetyContour = settings.SafetyContour,
            SafetyDepth = settings.SafetyDepth,
            CorridorOverride = settings.CorridorOverride,
            AreaKindsOff = [RouteAreaKinds.AnchorageArea],
        });

        Assert.DoesNotContain(report.Findings, f => f.Feature?.FeatureType == "AnchorageArea");
        var off = Assert.Single(report.Findings, f => f.Code == RouteFindingCodes.AreaKindOff);
        Assert.Equal(RouteAreaKinds.AnchorageArea, ((AreaKindOffDetails)off.Details!).AreaKind);
        Assert.Contains(report.Findings, f => f.Feature?.FeatureType == "RestrictedArea");
    }

    [Fact]
    public async Task SpecialConditionsLS_UsesTheLargestScaleCell()
    {
        // 6.2: WP20 to WP22 are charted in the overview cell and cross the
        // ARSPC cell's first row of areas.
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S64RouteCheck.LoadAsync(ct, Cell, Overview);
        var waypoints = await S64RouteCheck.NamedWaypointsAsync(Overview, ct);
        var plan = S164RouteCheck.Plan([waypoints[20], waypoints[22]]);

        var report = S164RouteCheck.Checker(datasets).Check(plan, S164RouteCheck.Settings(0, 30, S164RouteCheck.HalfAMile), ct);

        S164RouteCheck.AssertLargestScale(report, datasets, Path.GetFileName(Cell), Path.GetFileName(Overview));
        Assert.Contains(report.Findings, f => f.Category == RouteFindingCategory.Area && f.Feature?.DatasetId == Path.GetFileName(Cell));
    }

    private static async Task<RouteCheckReport> CheckAsync(RouteCheckSettings settings)
    {
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S64RouteCheck.LoadAsync(ct, Cell);
        var plan = S164RouteCheck.Plan((await S64RouteCheck.NamedWaypointsAsync(Cell, ct)).Values);
        return S164RouteCheck.Checker(datasets).Check(plan, settings, ct);
    }
}
