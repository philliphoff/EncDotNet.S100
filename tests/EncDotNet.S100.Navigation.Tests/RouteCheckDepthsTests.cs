using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>
/// Charted dangers and the safety contour and depth (#955) on the synthetic
/// leg of <see cref="RouteCheckEngineTests"/>: due east along the equator
/// from 0° to 0,01° E (about 1 112 m), so north is to port. The corridor is
/// the default 50 m each side; dangers are worth checking 185,2 m beyond it.
/// </summary>
public class RouteCheckDepthsTests
{
    private static readonly double M = PassageGeodesy.MetersPerDegreeLatitude;

    private static readonly PassageLeg East = Fixture.Leg([new GeoPosition(0, 0), new GeoPosition(0, 0.01)]);

    private static readonly FakeCell Harbour = new("101HARBOUR", 22_000);

    // Safety contour and safety depth 10 m.
    private static readonly RouteCheckSettings Ten = new()
    {
        At = Fixture.Departure,
        SafetyContour = Depth.FromMetres(10),
        SafetyDepth = Depth.FromMetres(10),
    };

    private static readonly ChartCheckOptions Options = new();

    private static RouteCheckResult Check(IReadOnlyList<RouteChartFeature> features, IChartedDepthSource? bathymetry = null, params PassageLeg[] legs) =>
        new RouteCheckEngine(new FakeCharts([Harbour], features), Options, bathymetry)
            .Check(new PassagePlan(legs.Length == 0 ? [East] : legs), Ten, TestContext.Current.CancellationToken);

    // East, with a draft, squat and required UKC.
    private static PassageLeg EastWithKeel(double draft, double squat, double required) =>
        Fixture.Leg(East.Points, draft, squat: squat, required: required);

    // A point danger metres north (positive, to port) of the track at a longitude.
    private static RouteChartFeature Danger(string type, string id, double longitude, double northMeters, double? valueOfSounding, int? waterLevelEffect = 3, RouteChartCell? cell = null) =>
        new(RouteChartFeatureKind.Danger, cell ?? Harbour, id, type, [], [])
        {
            Points = [new GeoPosition(northMeters / M, longitude)],
            DepthMeters = valueOfSounding,
            WaterLevelEffect = waterLevelEffect,
        };

    private static RouteChartFeature Area(RouteChartFeatureKind kind, string id, double depth, double west, double east, double south = -0.001, double north = 0.001, RouteChartCell? cell = null) =>
        FakeCharts.Area(kind, cell ?? Harbour, id, west, east, south, north) with { DepthMeters = depth };

    private static RouteChartFeature Sounding(string id, double longitude, double northMeters, double depth) =>
        new(RouteChartFeatureKind.Sounding, Harbour, id, "Sounding", [], [])
        {
            Points = [new GeoPosition(northMeters / M, longitude)],
            DepthMeters = depth,
        };

    [Fact]
    public void A_wreck_on_the_track_shallower_than_the_safety_depth_must_be_reviewed()
    {
        var finding = Assert.Single(Check([Danger("Wreck", "5", 0.004, 0, 3.2)]).Findings);

        Assert.Equal(RouteFindingCodes.DangerInCorridor, finding.Code);
        Assert.Equal("route_check.danger_in_corridor.0", finding.Id);
        Assert.Equal(RouteFindingGroup.MustReview, finding.Group);
        Assert.Equal(RouteFindingCategory.Depth, finding.Category);
        Assert.Equal("Wreck in the corridor", finding.Title);
        Assert.Equal("Leg 1 → 2 · Wreck · on the track · 3.2 m · shallower than safety depth 10 m", finding.Meta);
        Assert.Equal(0.004 * M, finding.Distance!.Value.TotalMetres, 3);
        Assert.True(finding.Location!.IsPoint);
        Assert.Equal(0.004, finding.Location.Path[0].Longitude, 9);
        Assert.Equal(new RouteFindingFeature("101HARBOUR", "5") { FeatureType = "Wreck" }, finding.Feature);
    }

    [Theory]
    // Port limit 50 m, then 185,2 m nearby.
    [InlineData(40.0, "route_check.danger_in_corridor", "40 m to port")]
    [InlineData(-45.0, "route_check.danger_in_corridor", "45 m to starboard")]
    [InlineData(120.0, "route_check.danger_near_route", "120 m to port")]
    [InlineData(-230.0, "route_check.danger_near_route", "230 m to starboard")]
    [InlineData(240.0, null, null)]
    public void Dangers_are_ranked_by_their_true_distance_from_the_centreline(double northMeters, string? code, string? where)
    {
        var findings = Check([Danger("Obstruction", "9", 0.005, northMeters, 2.0)]).Findings;

        if (code is null)
        {
            Assert.Empty(findings);
            return;
        }

        var finding = Assert.Single(findings);
        Assert.Equal(code, finding.Code.Value);
        Assert.Equal(code == RouteFindingCodes.DangerInCorridor.Value ? RouteFindingGroup.MustReview : RouteFindingGroup.Check, finding.Group);
        Assert.Equal(code == RouteFindingCodes.DangerInCorridor.Value ? "Obstruction in the corridor" : "Obstruction near the route", finding.Title);
        Assert.Contains($" · {where} · ", finding.Meta, StringComparison.Ordinal);
        Assert.Equal(0.005 * M, finding.Distance!.Value.TotalMetres, 1);
    }

    [Fact]
    public void Each_side_has_its_own_limit_for_dangers()
    {
        // 40 m to port: outside a 30 m port limit, so only nearby.
        var finding = Assert.Single(Check([Danger("Wreck", "1", 0.005, 40, 1)], legs: Fixture.Leg(East.Points, port: 30, starboard: 200)).Findings);

        Assert.Equal(RouteFindingCodes.DangerNearRoute, finding.Code);
    }

    [Fact]
    public void A_danger_deeper_than_the_safety_depth_isnt_reported()
    {
        Assert.Empty(Check([Danger("Wreck", "1", 0.005, 0, 12.5)]).Findings);
    }

    [Fact]
    public void A_draft_also_flags_dangers_shallower_than_draft_squat_and_ukc()
    {
        // 12,5 m clears the 10 m safety depth, but not 11 + 0,5 + 1,5 = 13 m.
        var wreck = Danger("Wreck", "1", 0.005, 0, 12.5);
        var withDraft = EastWithKeel(11, 0.5, 1.5);

        Assert.Empty(Check([wreck]).Findings);
        var finding = Assert.Single(Check([wreck], legs: withDraft).Findings);
        Assert.EndsWith(" · 12.5 m · shallower than draft + squat + UKC (13 m)", finding.Meta, StringComparison.Ordinal);
        Assert.Equal(13, withDraft.KeelMetres);
    }

    [Fact]
    public void The_meta_line_names_the_keel_threshold_when_both_fire()
    {
        var shallowDraft = EastWithKeel(2, 0.2, 0.5);

        // 2 m is below both 10 m and 2,7 m; 5 m only below the safety depth.
        var findings = Check([Danger("Wreck", "1", 0.003, 0, 2), Danger("Wreck", "2", 0.006, 0, 5)], legs: shallowDraft).Findings;

        Assert.Equal(2, findings.Count);
        Assert.EndsWith("shallower than draft + squat + UKC (2.7 m)", findings[0].Meta, StringComparison.Ordinal);
        Assert.EndsWith("shallower than safety depth 10 m", findings[1].Meta, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(5, "awash")]
    [InlineData(4, "covers and uncovers")]
    [InlineData(2, "above water")]
    [InlineData(3, "depth unknown")]
    [InlineData(null, "depth unknown")]
    public void A_danger_without_a_depth_is_always_a_danger(int? waterLevelEffect, string reason)
    {
        var finding = Assert.Single(Check([Danger("UnderwaterAwashRock", "1", 0.005, -20, null, waterLevelEffect)]).Findings);

        Assert.Equal("Rock in the corridor", finding.Title);
        Assert.EndsWith(" · UnderwaterAwashRock · 20 m to starboard · " + reason, finding.Meta, StringComparison.Ordinal);
    }

    [Fact]
    public void A_danger_in_water_already_shallower_than_the_safety_contour_isnt_isolated()
    {
        // The 5 m area north of the track holds a 3 m wreck; the 20 m area south of it a 3 m obstruction.
        var shallow = Area(RouteChartFeatureKind.DepthArea, "20", 5, 0, 0.01, south: 80 / M, north: 0.003);
        var deep = Area(RouteChartFeatureKind.DepthArea, "21", 20, 0, 0.01, south: -0.003, north: 80 / M);

        var findings = Check([shallow, deep, Danger("Wreck", "1", 0.005, 120, 3), Danger("Obstruction", "2", 0.005, -120, 3)]).Findings;

        var danger = Assert.Single(findings, f => f.Code == RouteFindingCodes.DangerInCorridor || f.Code == RouteFindingCodes.DangerNearRoute);
        Assert.Equal("2", danger.Feature!.FeatureId);
    }

    [Fact]
    public void A_coarse_cells_danger_is_ignored_where_a_finer_cell_covers()
    {
        var coastal = new FakeCell("101COASTAL", 90_000);
        var charts = new FakeCharts([Harbour, coastal], [Danger("Wreck", "1", 0.005, 0, 3, cell: coastal)]);

        var result = new RouteCheckEngine(charts, Options).Check(new PassagePlan([East]), Ten, TestContext.Current.CancellationToken);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void A_danger_near_a_turn_is_reported_once_on_the_closer_leg()
    {
        var north = Fixture.Leg([new GeoPosition(0, 0.01), new GeoPosition(0.01, 0.01)]);

        // 30 m east of the second leg and 30 m north of the end of the first.
        var finding = Assert.Single(Check([Danger("Wreck", "1", 0.01 + 30 / M, 30, 3)], legs: [East, north]).Findings);

        Assert.Equal(1, finding.LegIndex);
        Assert.Contains("30 m to starboard", finding.Meta, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_shallow_bank_is_one_safety_contour_crossing()
    {
        // Two areas shallower than 10 m and a drying core, end to end across the track.
        var findings = Check(
        [
            Area(RouteChartFeatureKind.DepthArea, "1", 5, 0.002, 0.004),
            Area(RouteChartFeatureKind.DryingArea, "2", -1, 0.004, 0.005),
            Area(RouteChartFeatureKind.DredgedArea, "3", 8, 0.005, 0.007),
            Area(RouteChartFeatureKind.DepthArea, "4", 15, 0.007, 0.01),
        ]).Findings;

        var crossing = Assert.Single(findings, f => f.Code == RouteFindingCodes.CrossesSafetyContour);
        Assert.Equal(RouteFindingGroup.MustReview, crossing.Group);
        Assert.Equal(0.002 * M, crossing.Location!.StartDistanceMeters, 3);
        Assert.Equal(0.007 * M, crossing.Location.EndDistanceMeters, 3);
        Assert.Equal("1", crossing.Feature!.FeatureId);
        Assert.EndsWith(" · DepthArea · least -1.0 m · shallower than safety contour 10 m", crossing.Meta, StringComparison.Ordinal);
        Assert.DoesNotContain(findings, f => f.Code == RouteFindingCodes.SafetyContourInCorridor);
    }

    [Fact]
    public void Shallow_water_off_the_track_inside_the_limits_is_worth_checking()
    {
        var findings = Check([Area(RouteChartFeatureKind.DepthArea, "1", 5, 0.004, 0.006, south: 30 / M, north: 0.002)]).Findings;

        var finding = Assert.Single(findings);
        Assert.Equal(RouteFindingCodes.SafetyContourInCorridor, finding.Code);
        Assert.Equal(RouteFindingGroup.Check, finding.Group);
        Assert.Contains("least 5.0 m · shallower than safety contour 10 m", finding.Meta, StringComparison.Ordinal);
    }

    [Fact]
    public void Areas_at_or_below_the_safety_contour_are_not_reported()
    {
        Assert.Empty(Check([Area(RouteChartFeatureKind.DepthArea, "1", 10, 0, 0.01)]).Findings);
    }

    [Fact]
    public void Shoal_soundings_close_together_make_one_finding()
    {
        var findings = Check(
        [
            Sounding("7", 0.003, 10, 6.1),
            Sounding("7", 0.0033, -20, 4.2),
            Sounding("7", 0.0036, 5, 8.0),
            Sounding("7", 0.008, 0, 9.9),
            // Outside the limits, deeper than the safety depth, and inside a shallow area.
            Sounding("7", 0.005, 70, 2.0),
            Sounding("7", 0.005, 0, 10.0),
            Sounding("7", 0.0095, 0, 1.0),
            Area(RouteChartFeatureKind.DepthArea, "8", 2, 0.009, 0.0098),
        ]).Findings.Where(f => f.Code == RouteFindingCodes.SoundingsBelowSafetyDepth).ToList();

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(RouteFindingGroup.Check, f.Group));
        Assert.Equal(0.003 * M, findings[0].Location!.StartDistanceMeters, 1);
        Assert.Equal(0.0036 * M, findings[0].Location!.EndDistanceMeters, 1);
        Assert.Contains("3 soundings · least 4.2 m · shallower than safety depth 10 m", findings[0].Meta, StringComparison.Ordinal);
        Assert.True(findings[1].Location!.IsPoint);
        Assert.EndsWith(" · Sounding · least 9.9 m · shallower than safety depth 10 m", findings[1].Meta, StringComparison.Ordinal);
    }

    [Fact]
    public void S102_shoals_on_the_track_must_be_reviewed_and_off_it_checked()
    {
        // A 5 m grid: 6 m between 400 and 500 m along the track; 8 m north of
        // the track (10–40 m to port) between 800 and 850 m.
        var bathymetry = new FakeDepth((lat, lon) =>
        {
            var x = lon * M;
            var y = lat * M;
            var depth = x is >= 400 and <= 500 ? 6.0 : x is >= 800 and <= 850 && y is >= 10 and <= 40 ? 8.0 : 20.0;
            return new ChartedDepth(depth, ChartedDepthKind.Bathymetry, 0.4, null, "102TEST", $"{(int)(y / 5)},{(int)(x / 5)}");
        }, gridSpacingMeters: 5);

        var findings = Check([], bathymetry: bathymetry).Findings;

        Assert.Equal([RouteFindingCodes.CrossesS102Shoal, RouteFindingCodes.S102ShoalInCorridor], findings.Select(f => f.Code));
        var crossing = findings[0];
        Assert.Equal(RouteFindingGroup.MustReview, crossing.Group);
        Assert.Null(crossing.Feature);
        Assert.InRange(crossing.Location!.StartDistanceMeters, 395, 405);
        Assert.InRange(crossing.Location.EndDistanceMeters, 495, 505);
        Assert.Matches(@"least 6\.0 m ± 0\.4 m at cell 0,\d+ of 102TEST · shallower than safety contour 10 m$", crossing.Meta);
        var off = findings[1];
        Assert.Equal(RouteFindingGroup.Check, off.Group);
        Assert.InRange(off.Location!.StartDistanceMeters, 795, 805);
        Assert.InRange(off.Location.EndDistanceMeters, 845, 855);
        Assert.Contains("least 8.0 m", off.Meta, StringComparison.Ordinal);
    }

    [Fact]
    public void S102_samples_from_another_kind_of_depth_are_ignored()
    {
        var vector = new FakeDepth((_, _) => FakeDepth.DepthArea(2), gridSpacingMeters: 25);

        Assert.Empty(Check([], bathymetry: vector).Findings);
    }

    [Fact]
    public void Without_depths_the_depth_checks_dont_run()
    {
        var charts = new FakeCharts([Harbour], [Danger("Wreck", "1", 0.005, 0, 1), Area(RouteChartFeatureKind.DepthArea, "2", 1, 0.002, 0.003)]);

        Assert.Empty(new RouteCheckEngine(charts).Check(new PassagePlan([East]), Fixture.Settings(), TestContext.Current.CancellationToken).Findings);
    }

    [Fact]
    public void Depths_come_from_the_mariners_settings()
    {
        var mariner = new MarinerSettings { SafetyContour = Depth.FromMetres(12), SafetyDepth = Depth.FromMetres(8) };

        var settings = RouteCheckSettings.FromMarinerSettings(mariner, Fixture.Departure);

        Assert.Equal(12, settings.SafetyContour?.TotalMetres);
        Assert.Equal(8, settings.SafetyDepth?.TotalMetres);
        Assert.True(settings.ChecksDepths);
        Assert.Equal(Fixture.Departure, settings.At);
        Assert.Null(East.KeelMetres);
    }

    [Fact]
    public void Each_leg_judges_its_dangers_against_its_own_keel()
    {
        // The same 12,5 m wreck beside each leg: under the deep leg's
        // 11 + 0,5 + 1,5 = 13 m keel it's a danger; under the shallow leg's
        // 5 m keel, and the 10 m safety depth, it isn't.
        var deep = EastWithKeel(11, 0.5, 1.5);
        var shallow = Fixture.Leg([new GeoPosition(0, 0.01), new GeoPosition(0, 0.02)], 4, squat: 0.5, required: 0.5);

        var findings = Check([Danger("Wreck", "1", 0.005, 0, 12.5), Danger("Wreck", "2", 0.015, 0, 12.5)], legs: [deep, shallow]).Findings;

        var finding = Assert.Single(findings);
        Assert.Equal(0, finding.LegIndex);
        Assert.Equal("1", finding.Feature?.FeatureId);
        Assert.EndsWith("shallower than draft + squat + UKC (13 m)", finding.Meta, StringComparison.Ordinal);
    }
}
