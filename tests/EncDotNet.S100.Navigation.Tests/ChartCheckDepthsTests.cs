using EncDotNet.S100.DataModel;
using EncDotNet.S100.Geodesy;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>
/// Charted dangers and the safety contour and depth (#955) on the synthetic
/// leg of <see cref="ChartCheckTests"/>: due east along the equator
/// from 0° to 0,01° E (about 1 112 m), so north is to port. The corridor is
/// the default 50 m each side; dangers are worth checking 185,2 m beyond it.
/// </summary>
public class ChartCheckDepthsTests
{
    // Metres per degree on the WGS-84 ellipsoid at the equator: east (along
    // the track) and north (across it).
    private static readonly double E = Wgs84Geodesy.MetresPerDegree(0).East;
    private static readonly double N = Wgs84Geodesy.MetresPerDegree(0).North;

    private static readonly PassageLeg East = Fixture.Leg([new GeoPosition(0, 0), new GeoPosition(0, 0.01)]);

    private static readonly FakeCell Harbour = new("101HARBOUR", 22_000);

    // Safety contour and safety depth 10 m.
    private static readonly RouteCheckSettings Ten = new()
    {
        At = Fixture.Departure,
        SafetyContour = Depth.FromMetres(10),
        SafetyDepth = Depth.FromMetres(10),
    };

    private static RouteCheckOutcome<ChartCheckResult> Check(IReadOnlyList<RouteChartFeature> features, IChartedDepthSource? bathymetry = null, params PassageLeg[] legs) =>
        Checks.Chart(new FakeCharts([Harbour], features), new PassagePlan(legs.Length == 0 ? [East] : legs), Ten, bathymetry);

    // East, with a draft, squat and required UKC.
    private static PassageLeg EastWithKeel(double draft, double squat, double required) =>
        Fixture.Leg(East.Points, draft, squat: squat, required: required);

    // A point danger metres north (positive, to port) of the track at a longitude.
    private static RouteChartFeature Danger(string type, string id, double longitude, double northMeters, double? valueOfSounding, int? waterLevelEffect = 3, RouteChartCell? cell = null) =>
        new(RouteChartFeatureKind.Danger, cell ?? Harbour, id, type, [], [])
        {
            Points = [new GeoPosition(northMeters / N, longitude)],
            DepthMeters = valueOfSounding,
            WaterLevelEffect = waterLevelEffect,
        };

    private static RouteChartFeature Area(RouteChartFeatureKind kind, string id, double depth, double west, double east, double south = -0.001, double north = 0.001, RouteChartCell? cell = null) =>
        FakeCharts.Area(kind, cell ?? Harbour, id, west, east, south, north) with { DepthMeters = depth };

    private static RouteChartFeature Sounding(string id, double longitude, double northMeters, double depth) =>
        new(RouteChartFeatureKind.Sounding, Harbour, id, "Sounding", [], [])
        {
            Points = [new GeoPosition(northMeters / N, longitude)],
            DepthMeters = depth,
        };

    [Fact]
    public void A_wreck_on_the_track_shallower_than_the_safety_contour_must_be_reviewed()
    {
        var finding = Assert.Single(Check([Danger("Wreck", "5", 0.004, 0, 3.2)]).Findings);

        Assert.Equal(RouteFindingCodes.DangerInCorridor, finding.Code);
        Assert.Equal("route_check.danger_in_corridor.0", finding.Id);
        Assert.Equal(RouteFindingGroup.MustReview, finding.Group);
        Assert.Equal(RouteFindingCategory.Depth, finding.Category);
        Assert.Equal("Wreck in the corridor", finding.Title);
        Assert.Equal("Leg 1 → 2 · Wreck · on the track · 3.2 m · no deeper than safety contour 10 m", finding.Meta);
        Assert.Equal(0.004 * E, finding.Distance!.Value.TotalMetres, 3);
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
        Assert.Equal(0.005 * E, finding.Distance!.Value.TotalMetres, 1);
    }

    [Fact]
    public void Each_side_has_its_own_limit_for_dangers()
    {
        // 40 m to port: outside a 30 m port limit, so only nearby.
        var finding = Assert.Single(Check([Danger("Wreck", "1", 0.005, 40, 1)], legs: Fixture.Leg(East.Points, port: 30, starboard: 200)).Findings);

        Assert.Equal(RouteFindingCodes.DangerNearRoute, finding.Code);
    }

    [Fact]
    public void A_danger_deeper_than_the_safety_contour_isnt_reported()
    {
        Assert.Empty(Check([Danger("Wreck", "1", 0.005, 0, 12.5)]).Findings);
    }

    [Theory]
    // UDWHAZ05 (#1151): the safety contour decides, a danger at it counts,
    // and the safety depth plays no part.
    [InlineData(10, 30, 20.1, false)]
    [InlineData(30, 10, 20.1, true)]
    [InlineData(10, 30, 10.0, true)]
    public void Dangers_are_judged_against_the_safety_contour(double contour, double safetyDepth, double depth, bool reported)
    {
        var settings = new RouteCheckSettings
        {
            At = Fixture.Departure,
            SafetyContour = Depth.FromMetres(contour),
            SafetyDepth = Depth.FromMetres(safetyDepth),
        };

        var findings = Checks.Chart(new FakeCharts([Harbour], [Danger("Wreck", "1", 0.005, 0, depth)]), new PassagePlan([East]), settings).Findings;

        Assert.Equal(reported ? 1 : 0, findings.Count);
    }

    [Fact]
    public void A_draft_also_flags_dangers_shallower_than_draft_squat_and_ukc()
    {
        // 12,5 m clears the 10 m safety contour, but not 11 + 0,5 + 1,5 = 13 m.
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

        // 2 m is below both 10 m and 2,7 m; 5 m only below the safety contour.
        var findings = Check([Danger("Wreck", "1", 0.003, 0, 2), Danger("Wreck", "2", 0.006, 0, 5)], legs: shallowDraft).Findings;

        Assert.Equal(2, findings.Count);
        Assert.EndsWith("shallower than draft + squat + UKC (2.7 m)", findings[0].Meta, StringComparison.Ordinal);
        Assert.EndsWith("no deeper than safety contour 10 m", findings[1].Meta, StringComparison.Ordinal);
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

    [Theory]
    // A default clearance depth (#1547) is judged by but not shown: the
    // water level's effect is, as for a danger without a depth.
    [InlineData(7, "floating")]
    [InlineData(4, "covers and uncovers")]
    [InlineData(3, "depth unknown")]
    [InlineData(null, "depth unknown")]
    public void A_danger_judged_by_its_default_clearance_depth_describes_its_water_level_instead(int? waterLevelEffect, string reason)
    {
        var obstruction = Danger("Obstruction", "1", 0.005, -39, -15, waterLevelEffect) with { DepthIsDefault = true };

        var finding = Assert.Single(Check([obstruction], legs: EastWithKeel(2, 0.2, 0.3)).Findings);

        Assert.Equal("Obstruction in the corridor", finding.Title);
        Assert.EndsWith(" · Obstruction · 39 m to starboard · " + reason + " · shallower than draft + squat + UKC (2.5 m)", finding.Meta, StringComparison.Ordinal);
        Assert.DoesNotContain("-15", finding.Meta, StringComparison.Ordinal);
        var details = Assert.IsType<DangerDetails>(finding.Details);
        Assert.True(details.DepthIsDefault);
        Assert.Equal(-15, details.Depth!.Value.TotalMetres);
        Assert.Equal(waterLevelEffect, details.WaterLevelEffect);
    }

    [Fact]
    public void A_sounded_danger_shows_its_depth_whatever_its_water_level()
    {
        var finding = Assert.Single(Check([Danger("Obstruction", "1", 0.005, 0, 1.2, waterLevelEffect: 7)]).Findings);

        Assert.False(Assert.IsType<DangerDetails>(finding.Details).DepthIsDefault);
        Assert.EndsWith(" · on the track · 1.2 m · no deeper than safety contour 10 m", finding.Meta, StringComparison.Ordinal);
    }

    [Fact]
    public void A_danger_counts_in_water_already_shallower_than_the_safety_contour_too()
    {
        // UDWHAZ05 (#1151): the 5 m area north of the track holds a 3 m
        // wreck, the 20 m area south of it a 3 m obstruction; both are alerts.
        var shallow = Area(RouteChartFeatureKind.DepthArea, "20", 5, 0, 0.01, south: 80 / N, north: 0.003);
        var deep = Area(RouteChartFeatureKind.DepthArea, "21", 20, 0, 0.01, south: -0.003, north: 80 / N);

        var findings = Check([shallow, deep, Danger("Wreck", "1", 0.005, 120, 3), Danger("Obstruction", "2", 0.005, -120, 3)]).Findings;

        Assert.Equal(
            ["1", "2"],
            findings.Where(f => f.Code == RouteFindingCodes.DangerInCorridor || f.Code == RouteFindingCodes.DangerNearRoute)
                .Select(f => f.Feature!.FeatureId).Order());
    }

    [Fact]
    public void A_coarse_cells_danger_is_ignored_where_a_finer_cell_covers()
    {
        var coastal = new FakeCell("101COASTAL", 90_000);
        var charts = new FakeCharts([Harbour, coastal], [Danger("Wreck", "1", 0.005, 0, 3, cell: coastal)]);

        var result = Checks.Chart(charts, new PassagePlan([East]), Ten);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void A_danger_near_a_turn_is_reported_once_on_the_closer_leg()
    {
        var north = Fixture.Leg([new GeoPosition(0, 0.01), new GeoPosition(0.01, 0.01)]);

        // 30 m east of the second leg and 30 m north of the end of the first.
        var finding = Assert.Single(Check([Danger("Wreck", "1", 0.01 + 30 / E, 30, 3)], legs: [East, north]).Findings);

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
        Assert.Equal(0.002 * E, crossing.Location!.StartDistanceMeters, 3);
        Assert.Equal(0.007 * E, crossing.Location.EndDistanceMeters, 3);
        Assert.Equal("1", crossing.Feature!.FeatureId);
        Assert.EndsWith(" · DepthArea · least -1.0 m · shallower than safety contour 10 m", crossing.Meta, StringComparison.Ordinal);
        Assert.DoesNotContain(findings, f => f.Code == RouteFindingCodes.SafetyContourInCorridor);
    }

    [Fact]
    public void Shallow_water_off_the_track_inside_the_limits_is_worth_checking()
    {
        var findings = Check([Area(RouteChartFeatureKind.DepthArea, "1", 5, 0.004, 0.006, south: 30 / N, north: 0.002)]).Findings;

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

    // A feature whose portrayal raises the SafetyContour alert (#1127), with
    // no water level unless a test gives one.
    private static RouteChartFeature SafetyContourFeature(RouteChartFeature feature) =>
        feature with { Kind = RouteChartFeatureKind.Alerted, WaterLevelEffect = null, Alerts = [new RouteChartAlert(RouteFindingAlert.SafetyContour)] };

    [Fact]
    public void An_area_that_raises_the_SafetyContour_alert_is_safety_contour_ground()
    {
        var pontoon = SafetyContourFeature(Area(RouteChartFeatureKind.Land, "9", 0, 0.004, 0.005) with { FeatureType = "Pontoon", DepthMeters = null });

        var finding = Assert.Single(Check([pontoon]).Findings);

        Assert.Equal(RouteFindingCodes.CrossesSafetyContour, finding.Code);
        Assert.Equal("Pontoon", finding.Feature!.FeatureType);
        Assert.Equal(0.004 * E, finding.Location!.StartDistanceMeters, 1);
    }

    [Fact]
    public void A_structure_that_raises_the_SafetyContour_alert_is_titled_for_the_structure()
    {
        // #1548: a pontoon mid-route is still a crossing to review, but not a depth.
        var pontoon = SafetyContourFeature(Area(RouteChartFeatureKind.Land, "9", 0, 0.004, 0.005) with { FeatureType = "Pontoon", DepthMeters = null });
        var shore = SafetyContourFeature(Danger("ShorelineConstruction", "10", 0.007, 40, null));

        var findings = Check([pontoon, shore]).Findings;

        Assert.Equal(2, findings.Count);
        Assert.Equal(RouteFindingGroup.MustReview, findings[0].Group);
        Assert.Equal("Crosses a structure", findings[0].Title);
        Assert.Equal("Leg 1 → 2 · 0.06 NM · Pontoon · safety contour alert", findings[0].Meta);
        var details = Assert.IsType<SafetyContourFeatureDetails>(findings[0].Details);
        Assert.Equal("Pontoon", details.FeatureType);
        Assert.True(details.IsStructure);
        Assert.Equal(RouteFindingCodes.SafetyContourInCorridor, findings[1].Code);
        Assert.Equal("Structure inside the cross-track limits", findings[1].Title);
        Assert.Equal("Leg 1 → 2 · ShorelineConstruction · safety contour alert", findings[1].Meta);
    }

    [Fact]
    public void Unsurveyed_areas_and_land_keep_the_safety_contour_title()
    {
        var unsurveyed = SafetyContourFeature(Area(RouteChartFeatureKind.Land, "9", 0, 0, 0.0005) with { FeatureType = "UnsurveyedArea", DepthMeters = null });
        var land = SafetyContourFeature(Danger("LandArea", "14", 0.0099, 0, null));

        var findings = Check([unsurveyed, land]).Findings;

        // Even at the departure and arrival: they aren't berth structures.
        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(RouteFindingCodes.CrossesSafetyContour, f.Code));
        Assert.All(findings, f => Assert.Equal("Crosses the safety contour", f.Title));
        Assert.All(findings, f => Assert.False(Assert.IsType<SafetyContourFeatureDetails>(f.Details).IsStructure));
    }

    [Fact]
    public void Structures_at_the_departure_and_arrival_berths_are_one_info_finding_per_end()
    {
        // #1548: the departure is at 0°, the arrival at 0,01° E (about 1 112 m).
        // A floating dock 40 m to port of the departure, a pontoon across the
        // track 50 m from the arrival and a shoreline construction on it.
        var dock = SafetyContourFeature(Danger("FloatingDock", "1", 0, 40, null));
        var pontoon = SafetyContourFeature(Area(RouteChartFeatureKind.Land, "2", 0, 0.0095, 0.0097) with { FeatureType = "Pontoon", DepthMeters = null });
        var shore = SafetyContourFeature(Danger("ShorelineConstruction", "3", 0.01, 0, null)) with { ShorelineConstructionCategory = Pier };
        var midRoute = SafetyContourFeature(Danger("Pontoon", "4", 0.005, 0, null));

        var findings = Check([dock, pontoon, shore, midRoute]).Findings;

        var crossing = Assert.Single(findings, f => f.Code == RouteFindingCodes.CrossesSafetyContour);
        Assert.Equal("4", crossing.Feature!.FeatureId);
        var berths = findings.Where(f => f.Code == RouteFindingCodes.BerthStructures).ToList();
        Assert.Equal(2, berths.Count);
        Assert.DoesNotContain(findings, f => f.Code == RouteFindingCodes.SafetyContourInCorridor);

        var departure = berths.Single(f => ((BerthStructuresDetails)f.Details!).End == RouteEnd.Departure);
        Assert.Equal(RouteFindingGroup.Info, departure.Group);
        Assert.Equal(RouteFindingCategory.Chart, departure.Category);
        Assert.Equal("Structures at the departure berth", departure.Title);
        Assert.Equal("Leg 1 → 2 · 1 within 100 m · FloatingDock", departure.Meta);
        Assert.Equal(0, departure.Distance!.Value.TotalMetres);
        Assert.Equal(new GeoPosition(0, 0), departure.Location!.Path[0]);

        var arrival = berths.Single(f => ((BerthStructuresDetails)f.Details!).End == RouteEnd.Arrival);
        var details = (BerthStructuresDetails)arrival.Details!;
        Assert.Equal("Structures at the arrival berth", arrival.Title);
        Assert.Equal("Leg 1 → 2 · 2 within 100 m · Pontoon, ShorelineConstruction", arrival.Meta);
        Assert.Equal(["2", "3"], details.Features.Select(f => f.FeatureId));
        Assert.Equal(2, details.CrossingCount);
        Assert.Equal(0.01 * E, arrival.Distance!.Value.TotalMetres, 1);
        Assert.Equal("2", arrival.Feature!.FeatureId);
    }

    [Fact]
    public void A_marinas_docks_past_the_radius_join_the_berth_when_they_join_its_structures()
    {
        // #1626: the arrival is at 0.01° E. Pontoons on the track 50, 100, 150
        // and 200 m from it, 50 m apart, are one dock system; one 400 m out,
        // 200 m past the last, isn't. A breakwater 150 m out (not a berth)
        // next to the docks stays a finding of its own.
        RouteChartFeature Pontoon(string id, double metres) =>
            SafetyContourFeature(Danger("Pontoon", id, 0.01 - (metres / E), 0, null));
        var breakwater = SafetyContourFeature(Danger("ShorelineConstruction", "9", 0.01 - (150 / E), 0, null)) with { ShorelineConstructionCategory = 1 };

        var findings = Check([Pontoon("1", 50), Pontoon("2", 100), Pontoon("3", 150), Pontoon("4", 200), Pontoon("5", 400), breakwater]).Findings;

        var berth = Assert.Single(findings, f => f.Code == RouteFindingCodes.BerthStructures);
        Assert.Equal(["1", "2", "3", "4"], ((BerthStructuresDetails)berth.Details!).Features.Select(f => f.FeatureId).Order());
        var crossings = findings.Where(f => f.Code == RouteFindingCodes.CrossesSafetyContour).Select(f => f.Feature!.FeatureId).Order();
        Assert.Equal(["5", "9"], crossings);
    }

    private const int Pier = 4;

    [Theory]
    // #1548: a breakwater, mole or training wall guards the harbour, and a
    // ruined pier or one that covers and uncovers is a hazard, however close
    // to the berth, when the track cuts it; so is a shoreline construction
    // of unknown category.
    [InlineData(1, null, null)]
    [InlineData(3, null, null)]
    [InlineData(7, null, null)]
    [InlineData(null, null, null)]
    [InlineData(Pier, 2, null)]
    [InlineData(Pier, null, 4)]
    [InlineData(Pier, null, 3)]
    public void A_structure_that_is_not_a_berth_stays_a_crossing_at_the_berth(int? category, int? condition, int? waterLevel)
    {
        // Departure at 0°: the structure across the track 20 m from it, and a
        // pontoon 40 m to port of it, which is a berth.
        var structure = SafetyContourFeature(Danger("ShorelineConstruction", "1", 0.00018, 0, null)) with
        {
            ShorelineConstructionCategory = category,
            Condition = condition,
            WaterLevelEffect = waterLevel,
        };
        var pontoon = SafetyContourFeature(Danger("Pontoon", "2", 0, 40, null));

        var findings = Check([structure, pontoon]).Findings;

        var crossing = Assert.Single(findings, f => f.Code == RouteFindingCodes.CrossesSafetyContour);
        Assert.Equal("1", crossing.Feature!.FeatureId);
        Assert.Equal(RouteFindingGroup.MustReview, crossing.Group);
        Assert.Equal("Crosses a structure", crossing.Title);
        Assert.False(Assert.IsType<SafetyContourFeatureDetails>(crossing.Details).IsBerth);
        var berth = Assert.Single(findings, f => f.Code == RouteFindingCodes.BerthStructures);
        Assert.Equal(["2"], ((BerthStructuresDetails)berth.Details!).Features.Select(f => f.FeatureId));
    }

    [Theory]
    // #1548: a marina entrance can be narrower than the track clearance
    // either side, so a breakwater the track only passes near is part of
    // the berth; one the track line cuts is not.
    [InlineData(20.0, false)]
    [InlineData(-10.0, false)]
    [InlineData(0.0, true)]
    public void A_breakwater_at_the_berth_stays_a_crossing_only_when_the_track_cuts_it(double northMeters, bool crossing)
    {
        // A breakwater running east from 15 m to 45 m from the departure,
        // northMeters off the track, so on or within the 30 m track clearance.
        var breakwater = SafetyContourFeature(new RouteChartFeature(
            RouteChartFeatureKind.Alerted,
            Harbour,
            "1",
            "ShorelineConstruction",
            [],
            [[new GeoPosition(northMeters / N, 0.000135), new GeoPosition(northMeters / N, 0.000405)]])) with
        { ShorelineConstructionCategory = 1 };

        var findings = Check([breakwater]).Findings;

        var finding = Assert.Single(findings);
        Assert.Equal(crossing ? RouteFindingCodes.CrossesSafetyContour : RouteFindingCodes.BerthStructures, finding.Code);
        if (crossing)
        {
            Assert.Equal("Crosses a structure", finding.Title);
            Assert.True(Assert.IsType<SafetyContourFeatureDetails>(finding.Details).CutByTrack);
        }
    }

    [Fact]
    public void A_breakwater_the_track_crosses_is_cut_by_the_track()
    {
        // Across the track, north to south, 30 m from the departure.
        var breakwater = SafetyContourFeature(new RouteChartFeature(
            RouteChartFeatureKind.Alerted,
            Harbour,
            "1",
            "ShorelineConstruction",
            [],
            [[new GeoPosition(20 / N, 0.00027), new GeoPosition(-20 / N, 0.00027)]])) with
        { ShorelineConstructionCategory = 1 };
        var pontoon = SafetyContourFeature(new RouteChartFeature(
            RouteChartFeatureKind.Alerted,
            Harbour,
            "2",
            "Pontoon",
            [],
            [[new GeoPosition(20 / N, 0.00054), new GeoPosition(-20 / N, 0.00054)]]));

        var findings = Check([breakwater, pontoon]).Findings;

        var crossing = Assert.Single(findings, f => f.Code == RouteFindingCodes.CrossesSafetyContour);
        Assert.Equal("1", crossing.Feature!.FeatureId);
        Assert.Equal(RouteFindingGroup.MustReview, crossing.Group);
        // The pontoon the track crosses is a berth: grouped all the same.
        var berth = Assert.Single(findings, f => f.Code == RouteFindingCodes.BerthStructures);
        Assert.Equal(["2"], ((BerthStructuresDetails)berth.Details!).Features.Select(f => f.FeatureId));
    }

    [Theory]
    [InlineData("Pontoon", null)]
    [InlineData("FloatingDock", null)]
    [InlineData("ShorelineConstruction", Pier)]
    [InlineData("ShorelineConstruction", 6)]
    [InlineData("ShorelineConstruction", 14)]
    [InlineData("ShorelineConstruction", 16)]
    public void A_berthing_structure_at_the_berth_is_grouped(string featureType, int? category)
    {
        var structure = SafetyContourFeature(Danger(featureType, "1", 0.00018, 0, null)) with { ShorelineConstructionCategory = category };

        var findings = Check([structure]).Findings;

        var berth = Assert.Single(findings);
        Assert.Equal(RouteFindingCodes.BerthStructures, berth.Code);
        Assert.Equal(1, ((BerthStructuresDetails)berth.Details!).CrossingCount);
    }

    [Theory]
    [InlineData(0.0, 2)]
    [InlineData(30.0, 2)]
    [InlineData(60.0, 0)]
    public void The_berth_radius_is_a_setting_and_zero_turns_it_off(double radius, int crossings)
    {
        // A pontoon across the track 50 m from the arrival.
        var pontoon = SafetyContourFeature(Area(RouteChartFeatureKind.Land, "2", 0, 0.0095, 0.0097) with { FeatureType = "Pontoon", DepthMeters = null });
        var shore = SafetyContourFeature(Danger("ShorelineConstruction", "3", 0.0096, 0, null)) with { ShorelineConstructionCategory = Pier };
        var settings = new RouteCheckSettings
        {
            At = Fixture.Departure,
            SafetyContour = Depth.FromMetres(10),
            SafetyDepth = Depth.FromMetres(10),
            BerthRadius = Length.FromMetres(radius),
        };

        var findings = Checks.Chart(new FakeCharts([Harbour], [pontoon, shore]), new PassagePlan([East]), settings).Findings;

        Assert.Equal(crossings, findings.Count(f => f.Code == RouteFindingCodes.CrossesSafetyContour));
        Assert.Equal(crossings == 0 ? 1 : 0, findings.Count(f => f.Code == RouteFindingCodes.BerthStructures));
    }

    [Theory]
    // Track clearance 30 m, port limit 50 m.
    [InlineData(20.0, "route_check.crosses_safety_contour")]
    [InlineData(40.0, "route_check.safety_contour_in_corridor")]
    [InlineData(80.0, null)]
    public void A_point_that_raises_the_SafetyContour_alert_is_crossed_within_the_track_clearance(double northMeters, string? code)
    {
        var land = SafetyContourFeature(Danger("LandArea", "14", 0.005, northMeters, null));

        var findings = Check([land]).Findings;

        if (code is null)
        {
            Assert.Empty(findings);
            return;
        }

        var finding = Assert.Single(findings);
        Assert.Equal(code, finding.Code.Value);
        Assert.Equal("14", finding.Feature!.FeatureId);
        Assert.True(finding.Location!.IsPoint);
    }

    [Fact]
    public void SafetyContour_features_need_the_depth_settings()
    {
        var land = SafetyContourFeature(Danger("LandArea", "14", 0.005, 0, null));

        var result = Checks.Chart(new FakeCharts([Harbour], [land]), new PassagePlan([East]), new RouteCheckSettings { At = Fixture.Departure });

        Assert.Empty(result.Findings);
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
            // At the 10 m contour: it counts, as SOUNDG03 counts it.
            Sounding("7", 0.005, 0, 10.0),
            // Outside the limits, deeper than the contour, and inside a shallow area.
            Sounding("7", 0.005, 70, 2.0),
            Sounding("7", 0.006, 0, 10.1),
            Sounding("7", 0.0095, 0, 1.0),
            Area(RouteChartFeatureKind.DepthArea, "8", 2, 0.009, 0.0098),
        ]).Findings.Where(f => f.Code == RouteFindingCodes.SoundingsBelowSafetyContour).ToList();

        Assert.Equal(3, findings.Count);
        Assert.All(findings, f => Assert.Equal(RouteFindingGroup.Check, f.Group));
        Assert.Equal(0.003 * E, findings[0].Location!.StartDistanceMeters, 1);
        Assert.Equal(0.0036 * E, findings[0].Location!.EndDistanceMeters, 1);
        Assert.Contains("3 soundings · least 4.2 m · no deeper than safety contour 10 m · shallower than safety depth 10 m", findings[0].Meta, StringComparison.Ordinal);
        Assert.EndsWith(" · Sounding · least 10.0 m · no deeper than safety contour 10 m", findings[1].Meta, StringComparison.Ordinal);
        Assert.True(findings[2].Location!.IsPoint);
        Assert.EndsWith(" · Sounding · least 9.9 m · no deeper than safety contour 10 m · shallower than safety depth 10 m", findings[2].Meta, StringComparison.Ordinal);
    }

    [Fact]
    public void S102_shoals_on_the_track_must_be_reviewed_and_off_it_checked()
    {
        // A 5 m grid: 6 m between 400 and 500 m along the track; 8 m north of
        // the track (10–40 m to port) between 800 and 850 m.
        var bathymetry = new FakeDepth((lat, lon) =>
        {
            var x = lon * E;
            var y = lat * N;
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

        var outcome = Checks.Chart(charts, new PassagePlan([East]), Fixture.Settings());

        Assert.Empty(outcome.Findings);
        Assert.Equal(RouteCheckState.Partial, outcome.State);
        Assert.Equal(RouteCheckReasons.SafetySettingsMissing, outcome.Reason);
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
        // 5 m keel, and the 10 m safety contour, it isn't.
        var deep = EastWithKeel(11, 0.5, 1.5);
        var shallow = Fixture.Leg([new GeoPosition(0, 0.01), new GeoPosition(0, 0.02)], 4, squat: 0.5, required: 0.5);

        var findings = Check([Danger("Wreck", "1", 0.005, 0, 12.5), Danger("Wreck", "2", 0.015, 0, 12.5)], legs: [deep, shallow]).Findings;

        var finding = Assert.Single(findings);
        Assert.Equal(0, finding.LegIndex);
        Assert.Equal("1", finding.Feature?.FeatureId);
        Assert.EndsWith("shallower than draft + squat + UKC (13 m)", finding.Meta, StringComparison.Ordinal);
    }
}
