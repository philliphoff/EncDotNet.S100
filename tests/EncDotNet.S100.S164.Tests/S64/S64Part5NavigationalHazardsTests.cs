using System.Globalization;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.S164.Tests.S64;

/// <summary>
/// S-64 §5 route-planning tests (5.1 basic, 5.2 largest scale) on the S-57
/// cell <c>AA3NAVHZ</c>, translated to S-101. The manual plans a route through
/// the features named WP1 to WP18 and repeats it at fifteen safety contours.
/// S-164's <c>NavigationalHazards</c> cell is the same chart converted to
/// S-101 and moved a quarter of a degree west, so these run
/// <see cref="RouteChecker"/> over both and expect the same dangers in the
/// same places: the S-57 path should judge them as the S-101 one does.
/// </summary>
public sealed class S64Part5NavigationalHazardsTests
{
    private const string Cell = "5.0 Navigational Hazards/ENC_ROOT/AA3NAVHZ.000";
    private const string Overview = "5.0 Navigational Hazards/Overview/ENC_ROOT/AA2OVRVU.000";

    /// <summary>How far east of S-164's cell the S-64 one lies, in degrees.</summary>
    private const double Offset = 0.25;

    /// <summary>
    /// A danger only the S-57 cell charts: a floating obstruction area
    /// (WATLEV 7) on the route that S-164's cell leaves out.
    /// </summary>
    private const string S57Only = "Obstruction 39.96667 -104.94707";

    /// <summary>The test's safety depth.</summary>
    private const double SafetyDepth = 30;

    [Fact]
    public async Task NavigationalHazards_RouteRunsThroughTheMarkedWaypoints()
    {
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S64RouteCheck.LoadAsync(ct, Cell);

        var waypoints = await S64RouteCheck.NamedWaypointsAsync(Cell, ct);

        Assert.Equal(Enumerable.Range(1, 18), waypoints.Keys);
        Assert.All(waypoints.Values, p => Assert.True(S164RouteCheck.Inside(datasets.Single().Bounds, p), $"{p} is outside the cell."));
    }

    /// <summary>The safety contours the manual repeats the route at.</summary>
    public static TheoryData<double> SafetyContours() => [0, 2, 4, 5, 6, 8, 9, 10, 11, 16, 21, 31, 42, 50, 51];

    [Theory]
    [MemberData(nameof(SafetyContours))]
    public async Task NavigationalHazards_DangersMatchTheS101Cell(double safetyContour)
    {
        // The wrecks, obstructions and rocks the S-57 path reports, by type and
        // place, are those S-164's S-101 cell gives. Before #1237 the
        // translated wrecks of unknown depth took S-52's default depths and
        // didn't match.
        var ct = TestContext.Current.CancellationToken;
        var s57 = await S64RouteCheck.LoadAsync(ct, Cell);
        var s101 = await S164RouteCheck.LoadAsync(ct, "NavigationalHazards");
        var settings = S164RouteCheck.Settings(safetyContour, SafetyDepth, S164RouteCheck.TenthOfAMile);

        var fromS57 = Dangers(S164RouteCheck.Checker(s57).Check(
            S164RouteCheck.Plan((await S64RouteCheck.NamedWaypointsAsync(Cell, ct)).Values), settings, ct), 0);
        Assert.Contains(S57Only, fromS57);
        fromS57 = [.. fromS57.Where(d => d != S57Only)];
        var fromS101 = Dangers(S164RouteCheck.Checker(s101).Check(
            S164RouteCheck.Plan(S164RouteCheck.NamedWaypoints(s101.Single()).Values), settings, ct), Offset);

        Assert.NotEmpty(fromS57);
        Assert.True(
            fromS57.SequenceEqual(fromS101),
            $"Only on the S-57 path: {string.Join("; ", fromS57.Except(fromS101))}. Only on the S-101 path: {string.Join("; ", fromS101.Except(fromS57))}.");
    }

    [Fact]
    public async Task NavigationalHazards_IndicatesTheAlertCatalogueHazards()
    {
        // The hazard types S-164's version of the test finds on the route
        // (Part5NavigationalHazardsTests), less those the S-57 cell charts
        // differently: no virtual AIS aids, a dolphin charted as an area
        // clear of the route, and the bridge drawn as a whole (Bridge) rather
        // than as its span.
        string[] hazards =
        [
            "CardinalBuoy", "CardinalBeacon", "IsolatedDangerBuoy", "IsolatedDangerBeacon", "LateralBuoy",
            "LateralBeacon", "SafeWaterBuoy", "SafeWaterBeacon", "SpecialPurposeGeneralBuoy",
            "SpecialPurposeGeneralBeacon", "InstallationBuoy", "Daymark", "Pile", "MooringBuoy",
            "LightFloat", "LightVessel", "OffshorePlatform", "OilBarrier", "IceArea",
            "Bridge", "CableOverhead", "PipelineOverhead", "Conveyor", "PylonBridgeSupport",
            "LogPond", "FishingFacility",
        ];
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S64RouteCheck.LoadAsync(ct, Cell);
        var plan = S164RouteCheck.Plan((await S64RouteCheck.NamedWaypointsAsync(Cell, ct)).Values);

        var report = S164RouteCheck.Checker(datasets).Check(plan, S164RouteCheck.Settings(10, SafetyDepth, S164RouteCheck.TenthOfAMile), ct);

        var found = report.Findings.Select(f => f.Feature?.FeatureType).OfType<string>().ToHashSet();
        Assert.Empty(hazards.Except(found));
    }

    [Fact]
    public async Task NavigationalHazardsLS_UsesTheLargestScaleCell()
    {
        // 5.2: WP1 to WP8 are charted in the overview cell and cross the
        // NAVHZ cell twice; where both cover the route only NAVHZ counts.
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S64RouteCheck.LoadAsync(ct, Cell, Overview);
        var waypoints = await S64RouteCheck.NamedWaypointsAsync(Overview, ct);
        var plan = S164RouteCheck.Plan(Enumerable.Range(1, 8).Select(i => waypoints[i]));

        var report = S164RouteCheck.Checker(datasets).Check(plan, S164RouteCheck.Settings(30, 30, S164RouteCheck.HalfAMile), ct);

        S164RouteCheck.AssertLargestScale(report, datasets, Path.GetFileName(Cell), Path.GetFileName(Overview));
        Assert.Contains(report.Findings, f => f.Code == RouteFindingCodes.DangerInCorridor && f.Feature?.DatasetId == Path.GetFileName(Cell));
    }

    /// <summary>
    /// The dangers in <paramref name="report"/> as their type and their first
    /// position, moved <paramref name="east"/> degrees east and rounded to
    /// about a metre.
    /// </summary>
    private static string[] Dangers(RouteCheckReport report, double east)
    {
        Assert.Equal(RouteCheckState.Ran, report.Status(ChartCheck.CheckName)!.State);
        return report.Findings
            .Where(f => f.Code == RouteFindingCodes.DangerInCorridor || f.Code == RouteFindingCodes.DangerNearRoute)
            .Select(f => (f.Feature!.FeatureType, Position: f.Location!.Path[0]))
            .Select(d => string.Create(CultureInfo.InvariantCulture, $"{d.FeatureType} {d.Position.Latitude:F5} {d.Position.Longitude + east:F5}"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
