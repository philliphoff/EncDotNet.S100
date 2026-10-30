using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.S164.Tests;

/// <summary>
/// S-164 Part 7 route-planning tests (SafetyContour, SafetyContourLS) on the
/// S-101 cell <c>10100AA_SAFCO</c>. The manual repeats a route from WP1 to
/// WP4 at safety contours of 0, 6, 11, 13 and 43 m and compares the
/// indications with reference plots; these run <see cref="RouteChecker"/> on
/// the route and check where it reports crossing the safety contour.
/// </summary>
/// <remarks>
/// The cell doesn't chart WP1 to WP4. They lie on its two rows of features,
/// at 39°39'N and 39°37.5'N, as the plots show them.
/// </remarks>
public sealed class Part7SafetyContourTests
{
    private const string Cell = "10100AA_SAFCO.000";
    private const string Overview = "101AA00AA2OVRVU.000";

    private static readonly GeoPosition[] Waypoints =
    [
        new(39.65, -104.98333),
        new(39.65, -104.76667),
        new(39.625, -104.76667),
        new(39.625, -104.98333),
    ];

    /// <summary>
    /// Places on the route, each in water whose least depth decides when it
    /// lies beyond the safety contour: the leg, the longitude and that depth.
    /// </summary>
    private static readonly (string Name, int Leg, double Longitude, double Depth)[] Probes =
    [
        ("12 m dredged area", 0, -104.95, 12),
        ("10 m depth area, west", 0, -104.925, 10),
        ("10 m depth area, east", 0, -104.875, 10),
        ("10 m depth area, south", 2, -104.825, 10),
        ("5 m dredged area", 2, -104.85, 5),
        ("42 m dredged area", 2, -104.9, 42),
        ("50 m depth area", 2, -104.975, 50),
    ];

    public static TheoryData<double> SafetyContours() => [0, 6, 11, 13, 43];

    [Theory]
    [MemberData(nameof(SafetyContours))]
    public async Task SafetyContour_CrossingsFollowTheSafetyContour(double safetyContour)
    {
        // The plots mark the route wherever the water is shallower than the
        // safety contour: the 5 m dredged area from 6 m, the 10 m depth area
        // from 11 m except the 12 m and 42 m dredged areas in it, the 12 m one
        // from 13 m and the 42 m one at 43 m.
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S164RouteCheck.LoadAsync(ct, "SafetyContour");
        var plan = S164RouteCheck.Plan(Waypoints);
        var settings = S164RouteCheck.Settings(safetyContour, 30, S164RouteCheck.TenthOfAMile);

        var report = S164RouteCheck.Checker(datasets).Check(plan, settings, ct);

        var track = RouteTrack.Build(plan, settings, Length.FromMetres(50));
        var crossings = report.Findings
            .Where(f => f.Code == RouteFindingCodes.CrossesSafetyContour)
            .Select(f => f.Location!)
            .ToList();
        var wrong = Probes
            .Where(p =>
            {
                var leg = track.Legs[p.Leg];
                var from = Waypoints[p.Leg].Longitude;
                var at = leg.StartMeters + (p.Longitude - from) / (Waypoints[p.Leg + 1].Longitude - from) * leg.LengthMeters;
                var reported = crossings.Any(c => c.StartDistanceMeters <= at && at <= c.EndDistanceMeters);
                return reported != (p.Depth < safetyContour);
            })
            .Select(p => p.Name)
            .ToArray();

        Assert.True(
            wrong.Length == 0,
            $"Safety contour {safetyContour} m: wrong at {string.Join(", ", wrong)}. Crossings: "
            + string.Join("; ", crossings.Select(c => $"{c.Path[0].Longitude:F4} to {c.Path[^1].Longitude:F4}")));
    }

    [Fact(Skip = "#1127: only land areas, depth, dredged and drying areas are checked, not every SafetyContour feature the alert catalogue lists.")]
    public async Task SafetyContour_IndicatesTheAlertCatalogueFeatures()
    {
        // At a 0 m safety contour the plot boxes the features on the route
        // that draw AlertReference:SafetyContour in the S-101 PC: floating
        // docks, hulks, land charted as points and curves, pontoons, shoreline
        // constructions and unsurveyed areas.
        string[] expected = ["FloatingDock", "Hulk", "LandArea", "Pontoon", "ShorelineConstruction", "UnsurveyedArea"];
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S164RouteCheck.LoadAsync(ct, "SafetyContour");

        var report = S164RouteCheck.Checker(datasets).Check(
            S164RouteCheck.Plan(Waypoints), S164RouteCheck.Settings(0, 30, S164RouteCheck.TenthOfAMile), ct);

        var found = report.Findings.Select(f => f.Feature?.FeatureType).OfType<string>().ToHashSet();
        Assert.Empty(expected.Except(found));
        // The point and curve features of the "Second Point" and "Second Istmus" land.
        Assert.Superset(new HashSet<string> { "14", "15" }, report.Findings.Select(f => f.Feature?.FeatureId).OfType<string>().ToHashSet());
    }

    [Fact]
    public async Task SafetyContourLS_UsesTheLargestScaleCell()
    {
        // WP11, WP24, WP25 and WP26 are charted in the overview cell; the
        // first leg crosses the SAFCO cell. The manual's plot is still to be
        // drawn; the rule it tests is that only SAFCO counts where both cover.
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S164RouteCheck.LoadAsync(ct, "SafetyContour", "NavigationalHazardsOverview");
        var waypoints = S164RouteCheck.NamedWaypoints(datasets.Single(d => d.Id.Value == Overview));
        var plan = S164RouteCheck.Plan([waypoints[11], waypoints[24], waypoints[25], waypoints[26]]);

        var report = S164RouteCheck.Checker(datasets).Check(plan, S164RouteCheck.Settings(11, 30, S164RouteCheck.HalfAMile), ct);

        S164RouteCheck.AssertLargestScale(report, datasets, Cell, Overview);
        Assert.Contains(report.Findings, f => f.Code == RouteFindingCodes.CrossesSafetyContour && f.Feature?.DatasetId == Cell);
    }
}
