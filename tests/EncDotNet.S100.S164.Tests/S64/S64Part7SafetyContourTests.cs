using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.S164.Tests.S64;

/// <summary>
/// S-64 §7 route-planning tests (7.1 basic, 7.2 largest scale) on the S-57
/// cell <c>AA3SAFCO</c>, translated to S-101. The cell charts its waypoints
/// WP1 to WP4 and its areas where S-164's <c>SafetyContour</c> cell has them,
/// so these reuse the S-164 test's probes (<see cref="Part7SafetyContourTests"/>).
/// </summary>
public sealed class S64Part7SafetyContourTests
{
    private const string Cell = "7.0 Safety Contour/ENC_ROOT/AA3SAFCO.000";
    private const string Overview = "5.0 Navigational Hazards/Overview/ENC_ROOT/AA2OVRVU.000";

    public static TheoryData<double> SafetyContours() => [0, 6, 11, 13, 43];

    [Fact]
    public async Task SafetyContour_WaypointsAreS164s()
    {
        var waypoints = await S64RouteCheck.NamedWaypointsAsync(Cell, TestContext.Current.CancellationToken);

        Assert.Equal(Enumerable.Range(1, 4), waypoints.Keys);
        Assert.All(Enumerable.Range(0, 4), i =>
        {
            Assert.Equal(Part7SafetyContourTests.Waypoints[i].Latitude, waypoints[i + 1].Latitude, 4);
            Assert.Equal(Part7SafetyContourTests.Waypoints[i].Longitude, waypoints[i + 1].Longitude, 4);
        });
    }

    [Theory]
    [MemberData(nameof(SafetyContours))]
    public async Task SafetyContour_CrossingsFollowTheSafetyContour(double safetyContour)
    {
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S64RouteCheck.LoadAsync(ct, Cell);
        var plan = S164RouteCheck.Plan(Part7SafetyContourTests.Waypoints);
        var settings = S164RouteCheck.Settings(safetyContour, 30, S164RouteCheck.TenthOfAMile);

        var report = S164RouteCheck.Checker(datasets).Check(plan, settings, ct);

        Part7SafetyContourTests.AssertCrossingsFollowTheSafetyContour(report, plan, settings, safetyContour);
    }

    [Fact]
    public async Task SafetyContour_IndicatesTheAlertCatalogueFeatures()
    {
        // At a 0 m safety contour: floating docks, hulks, land charted as
        // points and curves, pontoons, shoreline constructions and unsurveyed
        // areas, each drawing AlertReference:SafetyContour in the S-101 PC.
        string[] expected = ["FloatingDock", "Hulk", "LandArea", "Pontoon", "ShorelineConstruction", "UnsurveyedArea"];
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S64RouteCheck.LoadAsync(ct, Cell);

        var report = S164RouteCheck.Checker(datasets).Check(
            S164RouteCheck.Plan(Part7SafetyContourTests.Waypoints), S164RouteCheck.Settings(0, 30, S164RouteCheck.TenthOfAMile), ct);

        var found = report.Findings.Select(f => f.Feature?.FeatureType).OfType<string>().ToHashSet();
        Assert.Empty(expected.Except(found));
    }

    [Fact]
    public async Task SafetyContourLS_UsesTheLargestScaleCell()
    {
        // 7.2: WP11, WP24, WP25 and WP26 are charted in the overview cell; the
        // first leg crosses the SAFCO cell.
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S64RouteCheck.LoadAsync(ct, Cell, Overview);
        var waypoints = await S64RouteCheck.NamedWaypointsAsync(Overview, ct);
        var plan = S164RouteCheck.Plan([waypoints[11], waypoints[24], waypoints[25], waypoints[26]]);

        var report = S164RouteCheck.Checker(datasets).Check(plan, S164RouteCheck.Settings(11, 30, S164RouteCheck.HalfAMile), ct);

        S164RouteCheck.AssertLargestScale(report, datasets, Path.GetFileName(Cell), Path.GetFileName(Overview));
        Assert.Contains(report.Findings, f => f.Code == RouteFindingCodes.CrossesSafetyContour && f.Feature?.DatasetId == Path.GetFileName(Cell));
    }
}
