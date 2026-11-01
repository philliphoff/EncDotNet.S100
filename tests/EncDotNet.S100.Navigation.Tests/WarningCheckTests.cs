using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Geodesy;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>
/// The S-124 route check (#957) on synthetic shapes near the equator: one leg
/// due east from 0° to 0,01° E (about 1 112 m), 50 m limits either side, at
/// 6 kn from 06:00.
/// </summary>
public class WarningCheckTests
{
    // Metres per degree on the WGS-84 ellipsoid at the equator: east (along
    // the track) and north (across it).
    private static readonly double E = Wgs84Geodesy.MetresPerDegree(0).East;
    private static readonly double N = Wgs84Geodesy.MetresPerDegree(0).North;
    private static readonly DateTimeOffset Departure = new(2026, 3, 1, 6, 0, 0, TimeSpan.Zero);
    private static readonly PassageLeg East = Fixture.Leg([new GeoPosition(0, 0), new GeoPosition(0, 0.01)]);
    private static readonly PassageLeg EastPassage = Fixture.Leg(East.Points, speed: 6);
    private static readonly RouteCheckSettings AtDeparture = Fixture.Settings(Departure);

    private static NavigationalWarningInfo Info(int number, DateTimeOffset? from = null, DateTimeOffset? to = null) => new()
    {
        Series = "TEST",
        Number = $"{number:000}/26",
        Reference = new NavigationalWarningReference("TEST", number, 2026),
        Validity = from is null && to is null ? [] : [new NavigationalWarningValidity(from, to)],
    };

    private static S100FeatureGeometry Box(double south, double west, double north, double east) => new()
    {
        Primitive = S100GeometryType.Surface,
        ExteriorRing = [new(south, west), new(south, east), new(north, east), new(north, west), new(south, west)],
    };

    private static S100FeatureGeometry Line(params (double Lat, double Lon)[] points) => new()
    {
        Primitive = S100GeometryType.Curve,
        Curves = [points.Select(p => new GeoPosition(p.Lat, p.Lon)).ToArray()],
    };

    private static S100FeatureGeometry Point(double lat, double lon) => new()
    {
        Primitive = S100GeometryType.Point,
        Points = [new GeoPosition(lat, lon)],
    };

    private static RouteCheckOutcome<WarningCheckResult> Check(params RouteWarning[] warnings) =>
        Checks.Warnings(new FakeWarnings(warnings), new PassagePlan([EastPassage], Departure), AtDeparture);

    private static RouteWarning Warning(NavigationalWarningInfo info, params S100FeatureGeometry[] shapes) =>
        new(info, shapes, new RouteFindingFeature("124TEST", $"part.{info.Reference!.Number}"));

    [Fact]
    public void An_area_across_the_track_is_inside_and_must_be_reviewed()
    {
        var result = Check(Warning(Info(1), Box(-0.001, 0.004, 0.001, 0.006)));

        var match = Assert.Single(result.Detail!.Matches);
        Assert.Equal(RouteWarningRelation.Crosses, match.Relation);
        Assert.Equal(0.004 * E, match.StartDistanceMeters, 3);
        Assert.Equal(0.006 * E, match.EndDistanceMeters, 3);
        var finding = Assert.Single(result.Findings);
        Assert.Equal((RouteFindingCodes.WarningOnRoute, RouteFindingGroup.MustReview, RouteFindingCategory.Warning), (finding.Code, finding.Group, finding.Category));
        Assert.Equal("Inside navigational warning 001/26", finding.Title);
        Assert.Equal("s124.124TEST/part.1", finding.Id);
        Assert.Equal(new RouteFindingFeature("124TEST", "part.1"), finding.Feature);
        Assert.False(finding.Location!.IsPoint);
        Assert.Equal("TEST 001/26 · until cancelled · In force at 2026-03-01 06:02 UTC", finding.Meta);
    }

    [Fact]
    public void A_line_across_the_track_crosses_at_a_point()
    {
        var finding = Assert.Single(Check(Warning(Info(2), Line((-0.01, 0.005), (0.01, 0.005)))).Findings);

        Assert.Equal("Crosses navigational warning 002/26", finding.Title);
        Assert.True(finding.Location!.IsPoint);
        Assert.Equal(0.005 * E, finding.Distance!.Value.TotalMetres, 3);
    }

    [Fact]
    public void An_area_inside_the_limits_but_off_the_track_crosses_the_corridor()
    {
        // 30 m north of the track, inside the 50 m port limit.
        var north = 30 / N;
        var match = Assert.Single(Check(Warning(Info(3), Box(north, 0.004, north + 0.001, 0.005))).Detail!.Matches);

        Assert.Equal(RouteWarningRelation.Crosses, match.Relation);
        Assert.Equal(30, match.ClosestMeters, 0.5);
        Assert.Equal(RouteFindingGroup.MustReview, match.Finding!.Group);
    }

    [Fact]
    public void A_source_gives_the_warnings_near_the_route_and_judges_with_its_set()
    {
        // 800 m aside is within the 0,5 NM near distance; 50 km aside is never asked about.
        var aside = Warning(Info(5), Point(800 / N, 0.005));
        var far = Warning(Info(6), Point(0.5, 0.005));
        var source = new RouteWarningList([aside, far]);

        var result = Checks.Warnings(source, new PassagePlan([EastPassage], Departure), AtDeparture);

        var match = Assert.Single(result.Detail!.Matches);
        Assert.Same(aside, match.Warning);
        Assert.Equal(RouteWarningRelation.Near, match.Relation);
        Assert.Equal(Check(aside).Findings.Select(f => f.Id), result.Findings.Select(f => f.Id));
    }

    [Fact]
    public void A_point_in_the_corridor_or_within_half_a_mile_is_near()
    {
        var onTrack = Warning(Info(4), Point(0, 0.005));
        var aside = Warning(Info(5), Point(800 / N, 0.005));
        var far = Warning(Info(6), Point(1000 / N, 0.005));

        var result = Check(onTrack, aside, far);

        Assert.Equal(RouteWarningRelation.Near, result.Detail!.Matches[0].Relation);
        Assert.Equal(RouteWarningRelation.Near, result.Detail!.Matches[1].Relation);
        Assert.Equal(RouteWarningRelation.Off, result.Detail!.Matches[2].Relation);
        Assert.Null(result.Detail!.Matches[2].Finding);
        Assert.All(result.Findings, f => Assert.Equal((RouteFindingCodes.WarningNearRoute, RouteFindingGroup.Check), (f.Code, f.Group)));
        Assert.Equal("Navigational warning 004/26 near the route", result.Findings[0].Title);
    }

    [Fact]
    public void The_near_distance_is_configurable()
    {
        var result = Checks.Warnings(
            new FakeWarnings([Warning(Info(7), Point(800 / N, 0.005))]),
            new PassagePlan([East]),
            AtDeparture,
            new RouteCheckOptions { Warnings = new RouteWarningOptions { NearDistanceMeters = 100 } });

        Assert.Equal(RouteWarningRelation.Off, Assert.Single(result.Detail!.Matches).Relation);
    }

    [Fact]
    public void A_hole_in_an_area_is_outside_it()
    {
        var area = Box(-0.002, 0.002, 0.002, 0.008) with
        {
            InteriorRings = [Box(-0.001, 0.003, 0.001, 0.007).ExteriorRing],
        };

        var match = Assert.Single(Check(Warning(Info(8), area)).Detail!.Matches);

        // Enters at 0,002°, leaves for the last time at 0,008°: the span covers the hole.
        Assert.Equal(0.002 * E, match.StartDistanceMeters, 3);
        Assert.Equal(0.008 * E, match.EndDistanceMeters, 3);
    }

    [Fact]
    public void Time_is_judged_at_the_eta_where_the_vessel_meets_the_warning()
    {
        // 6 kn: 0,005° (556 m) takes 3 min.
        var before = Warning(Info(9, Departure, Departure.AddMinutes(2)), Line((-0.01, 0.005), (0.01, 0.005)));
        var during = Warning(Info(10, Departure.AddMinutes(2), Departure.AddMinutes(4)), Line((-0.01, 0.005), (0.01, 0.005)));

        var result = Check(before, during);

        Assert.Equal((RouteFindingCodes.WarningNotInForce, RouteFindingGroup.Info), (result.Detail!.Matches[0].Finding!.Code, result.Detail!.Matches[0].Finding!.Group));
        Assert.Equal(RouteFindingGroup.MustReview, result.Detail!.Matches[1].Finding!.Group);
        Assert.True(((WarningDetails)result.Detail!.Matches[1].Finding!.Details!).AtEta);
        Assert.StartsWith("Not in force at 2026-03-01 06:03", result.Detail!.Matches[0].Finding!.Meta.Split(" · ")[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Cancellation_by_date_or_by_another_warning_published_in_time()
    {
        var line = Line((-0.01, 0.005), (0.01, 0.005));
        var selfCancelled = Warning(Info(11) with { CancellationDate = Departure }, line);
        var cancelled = Warning(Info(12), line);
        var cancels = Warning(
            Info(13) with { PublicationTime = Departure.AddMinutes(1), Cancels = [new NavigationalWarningReference("TEST", 12, 26)] },
            Point(1, 1));
        var lateCancelled = Warning(Info(14), line);
        var cancelsLate = Warning(
            Info(15) with { PublicationTime = Departure.AddHours(1), Cancels = [new NavigationalWarningReference(null, 14, null)] },
            Point(1, 1));

        var result = Check(selfCancelled, cancelled, cancels, lateCancelled, cancelsLate);

        Assert.Equal(RouteWarningStatus.Cancelled, ((WarningDetails)result.Detail!.Matches[0].Finding!.Details!).Status);
        Assert.Equal((RouteFindingCodes.WarningCancelled, RouteFindingGroup.Info), (result.Detail!.Matches[1].Finding!.Code, result.Detail!.Matches[1].Finding!.Group));
        Assert.Equal("Navigational warning 012/26 cancelled", result.Detail!.Matches[1].Finding!.Title);
        Assert.EndsWith(" · Cancelled", result.Detail!.Matches[1].Finding!.Meta, StringComparison.Ordinal);
        Assert.Equal(RouteWarningStatus.InForce, ((WarningDetails)result.Detail!.Matches[3].Finding!.Details!).Status);
    }

    [Fact]
    public void A_warning_crossing_two_legs_is_one_finding_on_the_first()
    {
        var legs = new[]
        {
            Fixture.Leg([new GeoPosition(0, 0), new GeoPosition(0, 0.005)]),
            Fixture.Leg([new GeoPosition(0, 0.005), new GeoPosition(0.005, 0.005)]),
        };
        var result = Checks.Warnings(
            new FakeWarnings([Warning(Info(16), Box(-0.001, 0.004, 0.002, 0.006))]),
            new PassagePlan(legs),
            AtDeparture);

        var match = Assert.Single(result.Detail!.Matches);
        Assert.Equal(0, match.LegIndex);
        Assert.Equal(0.004 * E, match.StartDistanceMeters, 1);
        Assert.Equal((0.005 * E) + (0.002 * N), match.EndDistanceMeters, 1);
        Assert.False(((WarningDetails)match.Finding!.Details!).AtEta);
        Assert.Equal(Departure, ((WarningDetails)match.Finding.Details!).From);
    }

    [Fact]
    public void References_match_on_number_year_and_series()
    {
        var reference = new NavigationalWarningReference("NAVAREA I", 12, 2026);

        Assert.True(reference.Matches(new NavigationalWarningReference("NAVAREAI", 12, 26)));
        Assert.True(reference.Matches(new NavigationalWarningReference(null, 12, null)));
        Assert.False(reference.Matches(new NavigationalWarningReference("NAVAREA I", 12, 2025)));
        Assert.False(reference.Matches(new NavigationalWarningReference("NAVAREA II", 12, 2026)));
        Assert.False(reference.Matches(new NavigationalWarningReference("NAVAREA I", 13, 2026)));
    }

    [Fact]
    public void A_warning_left_out_of_the_latest_bulletin_is_cancelled_and_says_why()
    {
        // #992: a bulletin with no parts on the route still ends the parts it leaves out.
        var line = Line((-0.01, 0.005), (0.01, 0.005));
        var listed = Warning(Info(20) with { PublicationTime = Departure.AddDays(-5) }, line);
        var dropped = Warning(Info(21) with { PublicationTime = Departure.AddDays(-4) }, line);
        var later = Warning(Info(22) with { PublicationTime = Departure.AddHours(-1) }, line);
        var bulletin = Info(30) with
        {
            PublicationTime = Departure.AddDays(-1),
            InForceBulletin = new NavigationalWarningBulletin([new NavigationalWarningReference("TEST", 20, 2026)], NoMessageOnHand: false),
        };
        var set = new NavigationalWarningSet([listed.Info, dropped.Info, later.Info, bulletin]);

        var result = Checks.Warnings(new FakeWarnings([listed, dropped, later], set), new PassagePlan([EastPassage], Departure), AtDeparture);

        Assert.Equal(RouteWarningStatus.InForce, ((WarningDetails)result.Detail!.Matches[0].Finding!.Details!).Status);
        Assert.Equal(RouteWarningStatus.InForce, ((WarningDetails)result.Detail!.Matches[2].Finding!.Details!).Status);
        var finding = result.Detail!.Matches[1].Finding!;
        Assert.Equal((RouteFindingCodes.WarningCancelled, RouteFindingGroup.Info), (finding.Code, finding.Group));
        Assert.Equal("Navigational warning 021/26 not in in-force bulletin TEST 030/26", finding.Title);
        Assert.EndsWith(" · Cancelled — not in in-force bulletin TEST 030/26", finding.Meta, StringComparison.Ordinal);
        Assert.Equal(NavigationalWarningStatusReason.NotInBulletin, ((WarningDetails)finding.Details!).Withdrawal!.Reason);
    }

    [Fact]
    public void Without_a_set_cancellations_resolve_within_the_checked_warnings()
    {
        var line = Line((-0.01, 0.005), (0.01, 0.005));
        var cancelled = Warning(Info(23), line);
        var cancels = Warning(Info(24) with { Cancels = [new NavigationalWarningReference("TEST", 23, 2026)] }, line);

        var result = Check(cancelled, cancels);

        Assert.Equal(NavigationalWarningStatusReason.Cancelled, ((WarningDetails)result.Detail!.Matches[0].Finding!.Details!).Withdrawal!.Reason);
        Assert.Null(((WarningDetails)result.Detail!.Matches[1].Finding!.Details!).Withdrawal);
    }

    [Fact]
    public void Without_warnings_the_check_is_skipped()
    {
        var outcome = Checks.Run(new WarningCheck(), new PassagePlan([EastPassage], Departure), AtDeparture, RouteCheckSources.Empty);

        Assert.Equal((RouteCheckState.Skipped, RouteCheckReasons.WarningsMissing), (outcome.State, outcome.Reason));
        Assert.Empty(outcome.Findings);
        Assert.Null(outcome.Detail);
    }
}
