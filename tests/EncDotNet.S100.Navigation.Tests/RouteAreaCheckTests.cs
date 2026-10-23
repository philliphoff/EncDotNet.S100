using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>
/// The area check (#956) on synthetic areas near the equator: one leg due
/// east from 0° to 0,01° E (about 1 112 m), 50 m limits either side, at
/// 6 kn from 06:00 on 1 March.
/// </summary>
public class RouteAreaCheckTests
{
    private static readonly double M = PassageGeodesy.MetersPerDegreeLatitude;
    private static readonly DateTimeOffset Departure = new(2026, 3, 1, 6, 0, 0, TimeSpan.Zero);
    private static readonly PassageLeg East = Fixture.Leg([new GeoPosition(0, 0), new GeoPosition(0, 0.01)]);
    private static readonly PassageLeg EastPassage = Fixture.Leg(East.Points, speed: 6);
    private static readonly RouteCheckSettings AtDeparture = Fixture.Settings(Departure);

    private static IReadOnlyList<GeoPosition> Ring(double south, double west, double north, double east) =>
        [new(south, west), new(north, west), new(north, east), new(south, east), new(south, west)];

    private static RouteArea Area(
        RouteAreaKind kind,
        double west,
        double east,
        double south = -0.001,
        double north = 0.001,
        IReadOnlyList<int>? restrictions = null,
        RouteAreaProduct product = RouteAreaProduct.S101,
        string? name = null,
        IReadOnlyList<IReadOnlyList<GeoPosition>>? holes = null) =>
        new(product, kind, kind.ToString(), [new SurfacePart(Ring(south, west, north, east), holes ?? [])], new RouteFindingFeature("101TEST", FormattableString.Invariant($"{kind}.{west}")))
        {
            Name = name,
            Restrictions = restrictions ?? [],
        };

    // Checks East at 6 kn leaving at Departure, or the given plan, judged at
    // the given time where the plan gives none.
    private static RouteAreaResult Check(RouteArea area, IReadOnlyList<RouteChartCell>? cells = null, PassagePlan? plan = null, DateTimeOffset? at = null) =>
        new RouteAreaCheck(new RouteAreaOptions { Text = RouteAreaText.Default with { Culture = CultureInfo.InvariantCulture } }).Check(
            plan ?? new PassagePlan([EastPassage], Departure),
            Fixture.Settings(at ?? Departure),
            [area],
            cells ?? [],
            TestContext.Current.CancellationToken);

    [Fact]
    public void Entering_an_entry_prohibited_area_must_be_reviewed()
    {
        var result = Check(Area(RouteAreaKind.RestrictedArea, 0.004, 0.006, restrictions: [7]));

        var match = Assert.Single(result.Matches);
        Assert.Equal(RouteAreaRelation.Enters, match.Relation);
        Assert.Equal(0.004 * M, match.StartDistanceMeters, 1.0);
        Assert.Equal(0.006 * M, match.EndDistanceMeters, 1.0);
        var finding = match.Finding!;
        Assert.Equal((RouteAreaCodes.EntersProhibitedArea, RouteFindingGroup.MustReview), (finding.Code, finding.Group));
        Assert.Equal("Enters restricted area", finding.Title);
        Assert.StartsWith("Leg 1 → 2 · S-101 · entry prohibited · ", finding.Meta, StringComparison.Ordinal);
        Assert.Equal(RouteFindingSource.RouteCheck, finding.Source);
        Assert.Equal("route_check.enters_prohibited_area.101TEST/RestrictedArea.0.004", finding.Id);
    }

    [Theory]
    [InlineData(new[] { 8 }, RouteFindingGroup.MustReview)]
    [InlineData(new[] { 27 }, RouteFindingGroup.Check)]
    [InlineData(new int[0], RouteFindingGroup.Check)]
    [InlineData(new[] { 1, 3 }, RouteFindingGroup.Info)]
    public void An_s101_restricted_areas_group_follows_its_restrictions(int[] restrictions, RouteFindingGroup group)
    {
        var finding = Check(Area(RouteAreaKind.RestrictedArea, 0.004, 0.006, restrictions: restrictions)).Findings.Single();

        Assert.Equal(group, finding.Group);
    }

    [Theory]
    [InlineData(RouteAreaKind.CautionArea, RouteAreaCodes.CautionArea, RouteFindingGroup.Check)]
    [InlineData(RouteAreaKind.AnchorageArea, RouteAreaCodes.AnchorageArea, RouteFindingGroup.Check)]
    [InlineData(RouteAreaKind.SeparationZone, RouteAreaCodes.TrafficArea, RouteFindingGroup.Check)]
    [InlineData(RouteAreaKind.PrecautionaryArea, RouteAreaCodes.TrafficArea, RouteFindingGroup.Check)]
    [InlineData(RouteAreaKind.SubmarineTransitLane, RouteAreaCodes.TrafficArea, RouteFindingGroup.Check)]
    [InlineData(RouteAreaKind.InshoreTrafficZone, RouteAreaCodes.TrafficArea, RouteFindingGroup.Info)]
    [InlineData(RouteAreaKind.MilitaryPracticeArea, RouteAreaCodes.RestrictedArea, RouteFindingGroup.Check)]
    public void Other_kinds_are_checked_or_noted(RouteAreaKind kind, string code, RouteFindingGroup group)
    {
        var finding = Check(Area(kind, 0.004, 0.006)).Findings.Single();

        Assert.Equal((code, group), (finding.Code, finding.Group));
    }

    [Fact]
    public void An_s122_area_is_never_must_review()
    {
        var prohibited = Check(Area(RouteAreaKind.MarineProtectedArea, 0.004, 0.006, restrictions: [7], product: RouteAreaProduct.S122, name: "Reef Reserve")).Findings.Single();
        var fishing = Check(Area(RouteAreaKind.MarineProtectedArea, 0.004, 0.006, restrictions: [3], product: RouteAreaProduct.S122)).Findings.Single();

        Assert.Equal((RouteAreaCodes.ProtectedArea, RouteFindingGroup.Check), (prohibited.Code, prohibited.Group));
        Assert.Equal("Crosses Reef Reserve", prohibited.Title);
        Assert.StartsWith("Leg 1 → 2 · S-122 marine protected area · entry prohibited", prohibited.Meta, StringComparison.Ordinal);
        Assert.Equal(RouteFindingGroup.Info, fishing.Group);
    }

    [Fact]
    public void An_area_only_inside_the_limits_is_one_group_lighter()
    {
        // 30 m north of the track, inside the 50 m port limit.
        var prohibited = Check(Area(RouteAreaKind.RestrictedArea, 0.004, 0.006, south: 30 / M, north: 0.002, restrictions: [7]));
        var caution = Check(Area(RouteAreaKind.CautionArea, 0.004, 0.006, south: 30 / M, north: 0.002));

        var match = Assert.Single(prohibited.Matches);
        Assert.Equal(RouteAreaRelation.InCorridor, match.Relation);
        Assert.Equal((RouteAreaCodes.ProhibitedAreaInCorridor, RouteFindingGroup.Check), (match.Finding!.Code, match.Finding.Group));
        Assert.Equal("Restricted area inside the cross-track limits", match.Finding.Title);
        Assert.Equal(0.004 * M, match.StartDistanceMeters, 30.0);
        Assert.Equal(0.006 * M, match.EndDistanceMeters, 30.0);
        Assert.Equal(RouteFindingGroup.Info, caution.Findings.Single().Group);
    }

    [Fact]
    public void An_area_beyond_the_limits_gives_nothing()
    {
        var result = Check(Area(RouteAreaKind.RestrictedArea, 0.004, 0.006, south: 80 / M, north: 0.002, restrictions: [7]));

        Assert.Equal(RouteAreaRelation.Off, Assert.Single(result.Matches).Relation);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void A_hole_is_outside_the_area()
    {
        // A hole 0,0045°–0,0055° E, wider than the corridor, round a short leg inside it.
        var hole = Ring(-0.0005, 0.0045, 0.0005, 0.0055).Reverse().ToList();
        var area = Area(RouteAreaKind.RestrictedArea, 0.004, 0.006, south: -0.002, north: 0.002, restrictions: [7], holes: [hole]);

        var inside = new RouteAreaCheck().Check(
            new PassagePlan([Fixture.Leg([new GeoPosition(0, 0.0046), new GeoPosition(0, 0.0054)], port: 10, starboard: 10)]),
            AtDeparture,
            [area],
            [],
            TestContext.Current.CancellationToken);

        Assert.Equal(RouteAreaRelation.Off, Assert.Single(inside.Matches).Relation);
    }

    [Fact]
    public void A_season_outside_the_passage_is_info_not_in_force()
    {
        var area = Area(RouteAreaKind.RestrictedArea, 0.004, 0.006, restrictions: [27], name: "Summer Zone") with
        {
            PeriodicDateRanges = [new RouteAreaDateRange("--06-01", "--08-31")],
        };

        var finding = Check(area).Findings.Single();

        Assert.Equal((RouteAreaCodes.AreaNotInForce, RouteFindingGroup.Info), (finding.Code, finding.Group));
        Assert.Equal("Summer Zone not in force during the passage", finding.Title);
        Assert.Contains("speed restricted · 1 Jun–31 Aug · 2026-03-01 06:0", finding.Meta, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_etas_an_area_is_judged_at_the_departure_or_without_one_at_the_check_time()
    {
        var area = Area(RouteAreaKind.RestrictedArea, 0.004, 0.006, restrictions: [27]) with
        {
            FixedDateRange = new RouteAreaDateRange("2026-04-01", null),
        };
        var may = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        // No speed, but a departure: judged at the departure, not the check time.
        var atDeparture = Check(area, plan: new PassagePlan([East], Departure), at: may).Findings.Single();

        // No speed and no departure: judged at the check time.
        var atCheckTime = Check(area, plan: new PassagePlan([East]), at: may).Findings.Single();

        Assert.Equal("Restricted area not in force at 2026-03-01 06:00 UTC", atDeparture.Title);
        Assert.Equal((RouteAreaCodes.RestrictedArea, RouteFindingGroup.Check), (atCheckTime.Code, atCheckTime.Group));
        Assert.Contains("from 1 Apr 2026", atCheckTime.Meta, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--03-01", "--05-31", "2026-03-01T06:00Z", true)]
    [InlineData("--03-01", "--05-31", "2026-02-28T23:59Z", false)]
    [InlineData("----0301", "----0531", "2026-05-31T23:00Z", true)]
    [InlineData("--11-01", "--02-28", "2026-01-15T12:00Z", true)]
    [InlineData("--11-01", "--02-28", "2026-07-15T12:00Z", false)]
    [InlineData("2020-06-01", "2020-08-31", "2026-07-01T00:00Z", true)]
    [InlineData("??", "--08-31", "2026-01-01T00:00Z", true)]
    public void Seasons_repeat_by_month_and_day(string start, string end, string at, bool inForce)
    {
        var time = DateTimeOffset.Parse(at, CultureInfo.InvariantCulture);
        var area = Area(RouteAreaKind.RestrictedArea, 0, 1) with { PeriodicDateRanges = [new RouteAreaDateRange(start, end)] };

        Assert.Equal(inForce, RouteAreaCheck.InForceOver(area, time, time));
    }

    [Theory]
    [InlineData("2026-03-01", "2026-03-01", null, "2026-03-01T23:59Z", true)]
    [InlineData("2026-03-01", "2026-03-01", null, "2026-03-02T00:00Z", false)]
    [InlineData("20260301", "20260301", "120000", "2026-03-01T12:30Z", false)]
    [InlineData(null, "2026-03-01", null, "2026-02-01T00:00Z", true)]
    [InlineData("2026-03-02", null, null, "2026-03-01T00:00Z", false)]
    public void A_fixed_range_ends_with_its_last_day(string? start, string? end, string? endTime, string at, bool inForce)
    {
        var time = DateTimeOffset.Parse(at, CultureInfo.InvariantCulture);
        var area = Area(RouteAreaKind.RestrictedArea, 0, 1) with { FixedDateRange = new RouteAreaDateRange(start, end, TimeOfDayEnd: endTime) };

        Assert.Equal(inForce, RouteAreaCheck.InForceOver(area, time, time));
    }

    [Fact]
    public void Only_the_finest_cell_counts_where_cells_overlap()
    {
        var fine = new FakeCell("101FINE", 22_000);
        var coarse = new FakeCell("101COARSE", 350_000);
        var inFine = Area(RouteAreaKind.CautionArea, 0.004, 0.006) with { Cell = fine, Feature = new RouteFindingFeature("101FINE", "1") };
        var inCoarse = Area(RouteAreaKind.CautionArea, 0.004, 0.006) with { Cell = coarse, Feature = new RouteFindingFeature("101COARSE", "1") };

        var result = new RouteAreaCheck().Check(
            new PassagePlan([East]),
            AtDeparture,
            [inFine, inCoarse],
            [fine, coarse],
            TestContext.Current.CancellationToken);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("101FINE", finding.Feature!.DatasetId);
        Assert.Equal(RouteAreaRelation.Off, result.Matches[1].Relation);

        // Where the fine cell doesn't cover the place, the coarse cell's area counts.
        var patchy = new FakeCell("101FINE", 22_000, p => p.Longitude > 0.008);
        var coarseOnly = new RouteAreaCheck().Check(
            new PassagePlan([East]),
            AtDeparture,
            [inCoarse],
            [patchy, coarse],
            TestContext.Current.CancellationToken);
        Assert.Single(coarseOnly.Findings);
    }

    [Fact]
    public void A_source_is_queried_per_leg_and_areas_are_counted_once()
    {
        var area = Area(RouteAreaKind.CautionArea, 0.004, 0.006);
        var source = new FakeAreas([area, area]);
        var north = Fixture.Leg([new GeoPosition(0, 0.01), new GeoPosition(0.01, 0.01)]);

        var result = new RouteAreaCheck().Check(new PassagePlan([East, north]), AtDeparture, source, TestContext.Current.CancellationToken);

        Assert.Equal(2, source.Queries);
        Assert.Single(result.Matches);
        Assert.Single(result.Findings);
    }

    [Theory]
    [InlineData("--03-01", 3, 1)]
    [InlineData("--0301", 3, 1)]
    [InlineData("----0301", 3, 1)]
    [InlineData("2026-03-01", 3, 1)]
    [InlineData("20260301", 3, 1)]
    [InlineData("--02-29", 2, 29)]
    public void Truncated_dates_give_a_month_and_day(string text, int month, int day)
    {
        Assert.Equal((month, day), RouteAreaCheck.DayOfYear(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("--13-01")]
    [InlineData("--02-30")]
    [InlineData("---01")]
    public void Other_text_gives_no_day(string? text)
    {
        Assert.Null(RouteAreaCheck.DayOfYear(text));
    }

    private sealed class FakeAreas(IReadOnlyList<RouteArea> areas) : IRouteAreaSource
    {
        public int Queries { get; private set; }

        public IReadOnlyList<RouteArea> AreasNear(EncDotNet.S100.Pipelines.BoundingBox extent)
        {
            Queries++;
            return areas;
        }

        public IReadOnlyList<RouteChartCell> CellsNear(EncDotNet.S100.Pipelines.BoundingBox extent) => [];
    }
}
