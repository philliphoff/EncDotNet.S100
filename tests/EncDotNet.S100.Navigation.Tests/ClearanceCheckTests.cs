using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>
/// The clearance check on the synthetic passage of <see cref="Fixture"/>,
/// each test on a fresh check unless it's about what the check reuses.
/// </summary>
public class ClearanceCheckTests
{
    private static PassageCheckResult Check(
        Func<double, double, ChartedDepth?> depth,
        FakeWater? water = null,
        PassageLeg? leg = null,
        double? gridSpacing = null,
        DateTimeOffset? departure = null) =>
        Clearance(new FakeDepth(depth, gridSpacing), water ?? FakeWater.Constant(0), Fixture.Plan(leg, departure));

    // Runs a fresh check, or the given one, and returns its clearance.
    private static PassageCheckResult Clearance(IChartedDepthSource depth, IWaterLevelSource water, PassagePlan plan, RouteCheckSettings? settings = null, ClearanceCheck? check = null, RouteCheckSources? sources = null) =>
        Checks.Run(check ?? new ClearanceCheck(), plan, settings ?? Fixture.Settings(), sources ?? new RouteCheckSources { Depth = depth, WaterLevel = water }).Detail!.Clearance;

    // Runs a fresh check with a departure window and returns its sweep.
    private static DepartureSweep? Sweep(IChartedDepthSource depth, IWaterLevelSource water, PassageLeg[] legs, DepartureRange window) =>
        Checks.Clearance(depth, water, new PassagePlan(legs, Fixture.Departure), Fixture.Settings(window: window)).Detail!.Departures;

    [Fact]
    public void Clearance_is_depth_plus_water_level_minus_draft_and_squat()
    {
        var result = Check((_, _) => FakeDepth.Bathymetry(15), FakeWater.Constant(1.2), Fixture.Leg(draft: 10, squat: 0.5, required: 2));

        var sample = result.Samples[0];
        Assert.Equal(15 + 1.2 - 10 - 0.5, sample.ClearanceMeters!.Value, 6);
        Assert.Equal(WaterLevelStatus.Applied, sample.WaterLevel.Status);
        Assert.Equal(1.2, sample.WaterLevel.HeightMeters!.Value, 6);
        Assert.Equal(UkcState.MeetsSettings, sample.State);
        Assert.All(result.Samples, s => Assert.Equal(UkcState.MeetsSettings, s.State));
        Assert.Equal(PassageVerdict.MeetsSettings, result.Verdict);
        Assert.False(result.NoMarginSet);
        Assert.Empty(result.Gaps);
        Assert.Empty(result.LowConfidence);
    }

    [Theory]
    // depth − draft(10) vs required 2 with the 0,5 m tight margin.
    [InlineData(12.6, ClearanceBand.Meets, UkcState.MeetsSettings, PassageVerdict.MeetsSettings)]
    [InlineData(12.5, ClearanceBand.Meets, UkcState.MeetsSettings, PassageVerdict.MeetsSettings)]
    [InlineData(12.4, ClearanceBand.Tight, UkcState.Tight, PassageVerdict.MeetsSettingsTight)]
    [InlineData(12.0, ClearanceBand.Tight, UkcState.Tight, PassageVerdict.MeetsSettingsTight)]
    [InlineData(11.9, ClearanceBand.Below, UkcState.BelowSetting, PassageVerdict.BelowSetting)]
    public void Thresholds_classify_meets_tight_and_below(double depth, ClearanceBand band, UkcState state, PassageVerdict verdict)
    {
        var result = Check((_, _) => FakeDepth.Bathymetry(depth), leg: Fixture.Leg(draft: 10, required: 2));

        Assert.All(result.Samples, s =>
        {
            Assert.Equal(band, s.Band);
            Assert.Equal(state, s.State);
        });
        Assert.Equal(verdict, result.Verdict);
    }

    [Theory]
    [InlineData(10.6, UkcState.MeetsSettings)]
    [InlineData(10.3, UkcState.Tight)]
    [InlineData(10.0, UkcState.Tight)]
    [InlineData(9.9, UkcState.BelowSetting)]
    public void Unset_required_ukc_checks_against_zero_and_says_no_margin_is_set(double depth, UkcState state)
    {
        var result = Check((_, _) => FakeDepth.Bathymetry(depth), leg: Fixture.Leg(draft: 10, required: null));

        Assert.True(result.NoMarginSet);
        Assert.All(result.Samples, s =>
        {
            Assert.Equal(0, s.RequiredUkcMeters);
            Assert.Equal(state, s.State);
        });
    }

    [Fact]
    public void A_shoal_is_reported_with_where_and_when_and_minutes_below()
    {
        // 9 m between 0,04° and 0,05° E (about 1,1 km); 15 m elsewhere.
        var result = Check((_, lon) => FakeDepth.Bathymetry(lon is >= 0.04 and < 0.05 ? 9 : 15), leg: Fixture.Leg(draft: 10, required: 1));

        Assert.Equal(PassageVerdict.BelowSetting, result.Verdict);
        Assert.Equal(1, result.PlacesBelow);
        var min = Assert.IsType<UkcMinimum>(result.Minimum);
        Assert.Equal(-1, min.ClearanceMeters, 6);
        Assert.InRange(min.Position.Longitude, 0.04, 0.05);
        Assert.True(min.HasEta);

        // ETA is departure plus distance at 6 kn: 0,04° ≈ 4,45 km ≈ 24 min.
        var minutesToShoal = (min.Time - Fixture.Departure).TotalMinutes;
        Assert.InRange(minutesToShoal, 23.5, 30.5);

        // About 1,11 km at 6 kn (3,087 m/s) is 6 minutes; allow a sample either side.
        Assert.InRange(result.MinutesBelow, 5.0, 7.0);
        var leg = Assert.Single(result.Legs);
        Assert.Equal(UkcState.BelowSetting, leg.WorstState);
        Assert.Equal(result.MinutesBelow, leg.MinutesBelow, 6);
        Assert.Equal(min.SampleIndex, leg.Minimum!.SampleIndex);
    }

    [Fact]
    public void Each_reported_sample_keeps_the_shoalest_depth_of_its_stretch()
    {
        // A 3 m-wide pinnacle at 0,0303° E is narrower than the 50 m sample
        // spacing but wider than the grid step, so it must still be found.
        var result = Check(
            (_, lon) => FakeDepth.Bathymetry(Math.Abs(lon - 0.0303) < 0.000014 ? 5 : 20),
            leg: Fixture.Leg(draft: 10, required: 1),
            gridSpacing: 2);

        Assert.Equal(PassageVerdict.BelowSetting, result.Verdict);
        Assert.Equal(5 - 10, result.Minimum!.ClearanceMeters, 6);
    }

    [Fact]
    public void Steps_respect_the_s102_grid_and_samples_respect_the_spacing()
    {
        var depth = new FakeDepth((_, _) => FakeDepth.Bathymetry(20), gridSpacingMeters: 4);

        var result = Clearance(depth, FakeWater.Constant(0), Fixture.Plan());

        // About 11,1 km at half a 4 m cell = 2 m per step.
        Assert.InRange(depth.Calls, 5400, 5700);
        Assert.All(result.Samples, s => Assert.True(s.EndDistanceMeters - s.StartDistanceMeters <= 50 + 1e-6));
        Assert.Equal(0, result.Samples[0].StartDistanceMeters);
        Assert.Equal(PassageGeodesy.DistanceMeters(Fixture.Start, Fixture.End, LegGeometry.Rhumb), result.Samples[^1].EndDistanceMeters, 6);
        for (var i = 1; i < result.Samples.Count; i++)
        {
            Assert.Equal(result.Samples[i - 1].EndDistanceMeters, result.Samples[i].StartDistanceMeters, 6);
        }
    }

    [Fact]
    public void The_first_stretch_outside_s102_is_low_confidence_through_the_s101_fallback()
    {
        // About the first 1,5 NM (0,025° E) has only an S-101 depth area.
        const double edge = 0.025;
        var result = Check(
            (_, lon) => lon < edge ? FakeDepth.DepthArea(14) : FakeDepth.Bathymetry(16),
            leg: Fixture.Leg(draft: 10, required: 2),
            gridSpacing: 10);

        var stretch = Assert.Single(result.LowConfidence);
        Assert.Equal(PassageStretchKind.ChartedDepthFromS101, stretch.Kind);
        Assert.Equal(0, stretch.FirstSample);
        Assert.Equal(0, stretch.StartDistanceMeters);
        var edgeMeters = edge * PassageGeodesy.MetersPerDegreeLatitude;
        Assert.InRange(stretch.EndDistanceMeters, edgeMeters - 5, edgeMeters + 5);

        foreach (var s in result.Samples)
        {
            if (s.Index <= stretch.LastSample)
            {
                Assert.Equal(ChartedDepthKind.DepthArea, s.Depth!.Kind);
                Assert.True(s.Depth.IsLowConfidence);
                // The band is kept; confidence rides alongside it (#939).
                Assert.Equal(UkcState.MeetsSettings, s.State);
                Assert.Equal(ConfidenceReasons.ChartedDepthFromS101, s.Confidence);
            }
            else
            {
                Assert.Equal(ChartedDepthKind.Bathymetry, s.Depth!.Kind);
                Assert.Equal(UkcState.MeetsSettings, s.State);
                Assert.False(s.IsLowConfidence);
            }
        }

        // Low confidence alone doesn't change the verdict.
        Assert.Equal(PassageVerdict.MeetsSettings, result.Verdict);
        Assert.True(result.Legs[0].HasLowConfidence);
        Assert.Equal(ConfidenceReasons.ChartedDepthFromS101, result.Legs[0].Confidence);
        Assert.Equal(ConfidenceReasons.ChartedDepthFromS101, result.Confidence);
        Assert.Equal(["102TEST"], result.Sources.BathymetryDatasets);
        Assert.Equal(["101TEST"], result.Sources.FallbackDatasets);
    }

    // Regression for #888: a ragged S-102 edge (holes every 60–80 m, as on
    // the UKHO Solent cells near Fawley) gave a dozen fallback stretches of a
    // few metres each, every one its own finding.
    [Fact]
    public void A_ragged_s102_edge_reads_as_one_fallback_stretch_without_slivers()
    {
        // Between 0,02° and 0,04° E, S-102 has a ~22 m hole every ~111 m;
        // past that, one isolated ~11 m hole at 0,07° E.
        static bool Hole(double lon) =>
            (lon is >= 0.02 and < 0.04 && (lon - 0.02) % 0.001 < 0.0002)
            || lon is >= 0.07 and < 0.0701;
        var result = Check(
            (_, lon) => Hole(lon) ? FakeDepth.DepthArea(14) : FakeDepth.Bathymetry(16),
            leg: Fixture.Leg(draft: 10, required: 2),
            gridSpacing: 10);

        var fallback = Assert.Single(result.LowConfidence);
        Assert.Equal(PassageStretchKind.ChartedDepthFromS101, fallback.Kind);
        var metersPerDegree = PassageGeodesy.MetersPerDegreeLatitude;
        Assert.InRange(fallback.StartDistanceMeters, 0.02 * metersPerDegree - 30, 0.02 * metersPerDegree + 5);
        Assert.InRange(fallback.EndDistanceMeters, 0.0392 * metersPerDegree - 5, 0.04 * metersPerDegree + 30);

        // The samples still carry the fallback where it is used, sliver included.
        Assert.Contains(result.Samples, s => s.DistanceMeters > 0.06 * metersPerDegree && s.IsLowConfidence);
    }

    // No-data slivers explain an IncompleteData verdict, so they are kept.
    [Fact]
    public void A_short_hole_in_charted_depth_is_still_a_gap()
    {
        var result = Check(
            (_, lon) => lon is >= 0.05 and < 0.0501 ? null : FakeDepth.Bathymetry(16),
            leg: Fixture.Leg(draft: 10, required: 2),
            gridSpacing: 10);

        var gap = Assert.Single(result.Gaps);
        Assert.Equal(PassageStretchKind.NoChartedDepth, gap.Kind);
        Assert.True(gap.LengthMeters < 50);
        Assert.Equal(PassageVerdict.IncompleteData, result.Verdict);
    }

    [Fact]
    public void A_low_confidence_sample_below_the_setting_is_below()
    {
        var result = Check((_, _) => FakeDepth.DepthArea(5), leg: Fixture.Leg(draft: 10));

        Assert.All(result.Samples, s => Assert.Equal(UkcState.BelowSetting, s.State));
        Assert.Equal(PassageVerdict.BelowSetting, result.Verdict);
    }

    [Fact]
    public void Arrival_past_the_s104_end_makes_the_tail_no_data_and_gives_the_neutral_verdict()
    {
        // The water level ends 30 minutes after departure; the passage takes about an hour.
        var end = Fixture.Departure.UtcDateTime.AddMinutes(30);
        var water = new FakeWater((_, _) => [new FakeSeries(Fixture.Departure.UtcDateTime.AddHours(-1), end, _ => 0)]);

        // Also shoal near the start, to show the neutral verdict outranks "below".
        var result = Check((_, lon) => FakeDepth.Bathymetry(lon < 0.01 ? 9 : 15), water, Fixture.Leg(draft: 10, required: 1));

        Assert.Equal(PassageVerdict.WaterLevelEndsBeforeArrival, result.Verdict);
        var tail = Assert.Single(result.Gaps);
        Assert.Equal(PassageStretchKind.WaterLevelAfterEnd, tail.Kind);
        Assert.Equal(result.Samples.Count - 1, tail.LastSample);
        Assert.InRange((tail.StartTime - Fixture.Departure).TotalMinutes, 29, 31.5);
        foreach (var s in result.Samples)
        {
            if (s.Time.UtcDateTime > end)
            {
                Assert.Equal(WaterLevelStatus.AfterEnd, s.WaterLevel.Status);
                Assert.Equal(UkcState.NoData, s.State);
                Assert.Null(s.ClearanceMeters);
            }
        }

        Assert.True(result.Legs[0].HasNoWaterLevel);
    }

    [Fact]
    public void Departure_before_the_s104_start_is_no_data()
    {
        var water = new FakeWater((_, _) => [new FakeSeries(Fixture.Departure.UtcDateTime.AddHours(2), Fixture.Departure.UtcDateTime.AddHours(9), _ => 0)]);

        var result = Check((_, _) => FakeDepth.Bathymetry(15), water);

        Assert.All(result.Samples, s => Assert.Equal(WaterLevelStatus.BeforeStart, s.WaterLevel.Status));
        Assert.Equal(PassageStretchKind.WaterLevelBeforeStart, Assert.Single(result.Gaps).Kind);
        Assert.Equal(PassageVerdict.IncompleteData, result.Verdict);
        Assert.Null(result.Minimum);
    }

    [Fact]
    public void A_datum_mismatch_is_reported_not_mixed()
    {
        var result = Check((_, _) => FakeDepth.Bathymetry(15, datum: Fixture.Mllw), FakeWater.Constant(1.5, datum: Fixture.Lat));

        Assert.All(result.Samples, s =>
        {
            Assert.Equal(WaterLevelStatus.DatumMismatch, s.WaterLevel.Status);
            Assert.Equal(DatumAgreement.Different, s.WaterLevel.Datum);
            Assert.Equal(1.5, s.WaterLevel.HeightMeters!.Value, 6);
            Assert.Null(s.ClearanceMeters);
            Assert.Equal(UkcState.NoData, s.State);
        });
        Assert.Equal(PassageStretchKind.DatumMismatch, Assert.Single(result.Gaps).Kind);
        Assert.Equal(PassageVerdict.IncompleteData, result.Verdict);
        Assert.Equal([Fixture.Mllw], result.Sources.DepthDatums);
        Assert.Equal([Fixture.Lat], result.Sources.WaterLevelDatums);
    }

    [Fact]
    public void A_series_on_the_matching_datum_is_preferred_over_a_mismatched_one()
    {
        var start = Fixture.Departure.UtcDateTime.AddHours(-1);
        var end = Fixture.Departure.UtcDateTime.AddHours(3);
        var water = new FakeWater((_, _) =>
        [
            new FakeSeries(start, end, _ => 9, Fixture.Mllw, "mllw"),
            new FakeSeries(start, end, _ => 1, Fixture.Lat, "lat"),
        ]);

        var result = Check((_, _) => FakeDepth.Bathymetry(15, datum: Fixture.Lat), water);

        Assert.All(result.Samples, s =>
        {
            Assert.Equal(WaterLevelStatus.Applied, s.WaterLevel.Status);
            Assert.Equal("lat", s.WaterLevel.Source!.DatasetId);
            Assert.Equal(15 + 1 - 10, s.ClearanceMeters!.Value, 6);
        });
    }

    [Theory]
    [InlineData(null, Fixture.Lat)]
    [InlineData(Fixture.Lat, null)]
    [InlineData(4326, Fixture.Lat)] // not an S-100 vertical datum code
    public void An_undeclared_datum_applies_the_water_level_but_is_low_confidence(int? depthDatum, int? waterDatum)
    {
        var result = Check((_, _) => FakeDepth.Bathymetry(15, depthDatum), FakeWater.Constant(1, waterDatum));

        Assert.All(result.Samples, s =>
        {
            Assert.Equal(WaterLevelStatus.Applied, s.WaterLevel.Status);
            Assert.Equal(DatumAgreement.Unknown, s.WaterLevel.Datum);
            Assert.Equal(6, s.ClearanceMeters!.Value, 6);
            Assert.Equal(UkcState.MeetsSettings, s.State);
            Assert.Equal(ConfidenceReasons.DatumUnconfirmed, s.Confidence);
        });
        Assert.Equal(PassageStretchKind.DatumUnconfirmed, Assert.Single(result.LowConfidence).Kind);
        Assert.Equal(PassageVerdict.MeetsSettings, result.Verdict);
        Assert.Equal(ConfidenceReasons.DatumUnconfirmed, result.Confidence);
        Assert.Equal(ConfidenceReasons.DatumUnconfirmed, result.Minimum!.Confidence);
    }

    [Theory]
    [InlineData(10.3, UkcState.Tight, PassageVerdict.MeetsSettingsTight)]
    [InlineData(9.3, UkcState.BelowSetting, PassageVerdict.BelowSetting)]
    [InlineData(12, UkcState.MeetsSettings, PassageVerdict.MeetsSettings)]
    public void Lower_confidence_keeps_the_clearance_band(double depth, UkcState expected, PassageVerdict verdict)
    {
        // Draft 10 m, 1 m water level, 1 m required: clearance = depth − 9.
        // No depth datum, so every sample is DatumUnconfirmed (#939).
        var result = Check(
            (_, _) => FakeDepth.Bathymetry(depth, datum: null),
            FakeWater.Constant(1, Fixture.Lat),
            leg: Fixture.Leg(draft: 10, required: 1));

        Assert.All(result.Samples, s =>
        {
            Assert.Equal(expected, s.State);
            Assert.Equal(ConfidenceReasons.DatumUnconfirmed, s.Confidence);
        });
        Assert.Equal(verdict, result.Verdict);
        Assert.Equal(expected, result.Legs[0].WorstState);
        Assert.Equal(ConfidenceReasons.DatumUnconfirmed, result.Legs[0].Confidence);
    }

    [Fact]
    public void A_no_data_sample_on_s101_depth_stays_no_data_and_carries_its_confidence()
    {
        var result = Check((_, _) => FakeDepth.DepthArea(14), new FakeWater((_, _) => []));

        Assert.All(result.Samples, s =>
        {
            Assert.Equal(UkcState.NoData, s.State);
            Assert.Equal(ConfidenceReasons.ChartedDepthFromS101, s.Confidence);
        });
        Assert.Equal(PassageVerdict.IncompleteData, result.Verdict);
    }

    [Fact]
    public void NoData_masking_and_partial_coverage_become_gaps()
    {
        // 0,02–0,03° E: masked S-104 cell. 0,05–0,06° E: no charted depth.
        // East of 0,08° E: outside the S-104 extent.
        var start = Fixture.Departure.UtcDateTime.AddHours(-1);
        var end = Fixture.Departure.UtcDateTime.AddHours(3);
        var water = new FakeWater((_, lon) => lon > 0.08
            ? []
            : [new FakeSeries(start, end, _ => lon is >= 0.02 and < 0.03 ? null : 0.5)]);

        var result = Check((_, lon) => lon is >= 0.05 and < 0.06 ? null : FakeDepth.Bathymetry(15), water);

        var kinds = result.Gaps.Select(g => g.Kind).ToList();
        Assert.Equal(
            [PassageStretchKind.WaterLevelNoData, PassageStretchKind.NoChartedDepth, PassageStretchKind.NoWaterLevelCoverage],
            kinds);
        Assert.All(result.Gaps, g => Assert.True(g.LengthMeters > 0));
        Assert.Equal(PassageVerdict.IncompleteData, result.Verdict);

        var gap = result.Gaps.Single(g => g.Kind == PassageStretchKind.NoChartedDepth);
        Assert.InRange(gap.StartDistanceMeters, 0.05 * PassageGeodesy.MetersPerDegreeLatitude - 60, 0.05 * PassageGeodesy.MetersPerDegreeLatitude + 60);
        Assert.All(result.Samples.Where(s => s.Depth is null), s => Assert.Equal(UkcState.NoData, s.State));
    }

    [Fact]
    public void Without_a_speed_every_sample_uses_the_departure_time()
    {
        var result = Check((_, _) => FakeDepth.Bathymetry(15), leg: Fixture.Leg(speed: null));

        Assert.Null(result.Arrival);
        Assert.All(result.Samples, s =>
        {
            Assert.False(s.HasEta);
            Assert.Equal(Fixture.Departure, s.Time);
        });
        Assert.Equal(0, result.MinutesBelow);
    }

    [Fact]
    public void Depths_are_reused_for_new_times_and_vessel_values()
    {
        var depth = new FakeDepth((_, _) => FakeDepth.Bathymetry(13));
        var water = FakeWater.Constant(0);
        var check = new ClearanceCheck();
        Clearance(depth, water, Fixture.Plan(), check: check, sources: Sources(depth, water, out var sources));
        var depthCalls = depth.Calls;
        var waterCalls = water.Calls;

        var deep = Clearance(depth, water, Fixture.Plan(Fixture.Leg(draft: 8)), check: check, sources: sources);
        var shallow = Clearance(depth, water, Fixture.Plan(Fixture.Leg(draft: 12), Fixture.Departure.AddHours(1)), check: check, sources: sources);

        Assert.Equal(PassageVerdict.MeetsSettings, deep.Verdict);
        Assert.Equal(PassageVerdict.BelowSetting, shallow.Verdict);
        Assert.Equal(depthCalls, depth.Calls);
        Assert.Equal(waterCalls, water.Calls);
    }

    [Fact]
    public void Other_sources_are_sampled_again()
    {
        var depth = new FakeDepth((_, _) => FakeDepth.Bathymetry(13));
        var water = FakeWater.Constant(0);
        var check = new ClearanceCheck();
        Clearance(depth, water, Fixture.Plan(), check: check);
        var calls = depth.Calls;

        // A new bundle over the same data is new loaded data, so nothing is reused.
        Clearance(depth, water, Fixture.Plan(), check: check);

        Assert.Equal(2 * calls, depth.Calls);
    }

    [Fact]
    public void Resampling_reuses_unchanged_legs()
    {
        var depth = new FakeDepth((_, _) => FakeDepth.Bathymetry(13));
        var water = FakeWater.Constant(0);
        var check = new ClearanceCheck();
        var a = new GeoPosition(0, 0);
        var b = new GeoPosition(0, 0.05);
        var c = new GeoPosition(0, 0.1);
        var first = Clearance(depth, water, new PassagePlan([Fixture.Leg([a, b], 10), Fixture.Leg([b, c], 10)], Fixture.Departure), check: check, sources: Sources(depth, water, out var sources));
        var calls = depth.Calls;

        var moved = new GeoPosition(0.001, 0.1);
        var second = Clearance(depth, water, new PassagePlan([Fixture.Leg([a, b], 10), Fixture.Leg([b, moved], 10)], Fixture.Departure), check: check, sources: sources);

        // Only the second leg was walked again: about half as many samples.
        var resampled = depth.Calls - calls;
        Assert.InRange(resampled, calls / 2 - 5, calls / 2 + 5);
        Assert.Equal(first.Samples.Count(x => x.LegIndex == 0), second.Samples.Count(x => x.LegIndex == 0));
        Assert.NotEqual(first.Samples[^1].Position, second.Samples[^1].Position);
    }

    [Fact]
    public void A_moved_waypoint_gives_the_same_result_as_a_fresh_check()
    {
        var depth = new FakeDepth((lat, _) => FakeDepth.Bathymetry(lat > 0.005 ? 11 : 14));
        var water = FakeWater.Constant(0);
        var check = new ClearanceCheck();
        Sources(depth, water, out var sources);
        Clearance(depth, water, Fixture.Plan(), check: check, sources: sources);
        var other = Fixture.Plan(Fixture.Leg([Fixture.Start, new GeoPosition(0.01, 0.1)], 10, speed: 6, required: 2));

        var reused = Clearance(depth, water, other, check: check, sources: sources);
        var fresh = Clearance(depth, water, other);

        Assert.Equal(fresh.Samples.Select(x => (x.Position, x.ClearanceMeters)), reused.Samples.Select(x => (x.Position, x.ClearanceMeters)));
        Assert.Equal(fresh.Verdict, reused.Verdict);
    }

    [Fact]
    public void A_leg_without_a_draft_skips_the_check()
    {
        var noDraft = Fixture.Leg([Fixture.Start, Fixture.End], draft: null, speed: 6);

        var outcome = Checks.Clearance(new FakeDepth((_, _) => FakeDepth.Bathymetry(13)), FakeWater.Constant(0), new PassagePlan([noDraft], Fixture.Departure), Fixture.Settings());

        Assert.Equal(RouteCheckState.Skipped, outcome.State);
        Assert.Equal(RouteCheckReasons.DraftMissing, outcome.Reason);
        Assert.Empty(outcome.Findings);
        Assert.Null(outcome.Detail);
    }

    [Fact]
    public void Without_depth_or_water_level_the_check_is_skipped()
    {
        var depth = new FakeDepth((_, _) => FakeDepth.Bathymetry(13));
        var noDepth = Checks.Run(new ClearanceCheck(), Fixture.Plan(), Fixture.Settings(), new RouteCheckSources { WaterLevel = FakeWater.Constant(0) });
        var noWater = Checks.Run(new ClearanceCheck(), Fixture.Plan(), Fixture.Settings(), new RouteCheckSources { Depth = depth });

        Assert.Equal((RouteCheckState.Skipped, RouteCheckReasons.DepthMissing), (noDepth.State, noDepth.Reason));
        Assert.Equal((RouteCheckState.Skipped, RouteCheckReasons.WaterLevelMissing), (noWater.State, noWater.Reason));
        Assert.Equal(0, depth.Calls);
    }

    [Fact]
    public void Without_a_departure_the_plan_leaves_at_the_check_time()
    {
        var at = Fixture.Departure.AddHours(3);

        var result = Clearance(new FakeDepth((_, _) => FakeDepth.Bathymetry(13)), FakeWater.Constant(0), new PassagePlan([Fixture.Leg()]), Fixture.Settings(at));

        Assert.Equal(at, result.Departure);
        Assert.Equal(at, result.Samples[0].Time);
    }

    [Fact]
    public void Cancellation_stops_the_check()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            Checks.Clearance(new FakeDepth((_, _) => FakeDepth.Bathymetry(13), 1), FakeWater.Constant(0), Fixture.Plan(), Fixture.Settings(), cancellationToken: cts.Token));
    }

    [Fact]
    public void The_departure_sweep_reuses_depths_and_finds_the_window()
    {
        // The water rises 1 m per hour from 0 m at the fixture departure.
        var origin = Fixture.Departure.UtcDateTime;
        var water = new FakeWater((_, _) => [new FakeSeries(origin.AddHours(-6), origin.AddHours(8), t => (t - origin).TotalHours)]);
        var depth = new FakeDepth((_, _) => FakeDepth.Bathymetry(12));
        var legs = new[] { Fixture.Leg(draft: 12, required: 2) };
        var check = new ClearanceCheck();
        Sources(depth, water, out var sources);
        Clearance(depth, water, new PassagePlan(legs, Fixture.Departure), check: check, sources: sources);
        var calls = (depth.Calls, water.Calls);
        var window = DepartureRange.Between(Fixture.Departure.AddHours(-2), Fixture.Departure.AddHours(6), TimeSpan.FromMinutes(30));

        var sweep = Checks.Run(check, new PassagePlan(legs, Fixture.Departure), Fixture.Settings(window: window), sources).Detail!.Departures!;

        Assert.Equal(calls, (depth.Calls, water.Calls));
        Assert.Equal(17, sweep.Options.Count);

        // Clearance at a sample is (time since origin in hours); it must be ≥ 2 m everywhere, so the
        // departure (the earliest sample) needs ≥ +2 h. Arrival after +8 h runs past the data.
        var meeting = Assert.Single(sweep.Windows());
        Assert.Equal(Fixture.Departure.AddHours(2), meeting.First);
        Assert.Equal(Fixture.Departure.AddHours(6), meeting.Last);
        Assert.Equal(PassageVerdict.BelowSetting, sweep.Options[0].Verdict);
        Assert.Equal(Fixture.Departure.AddHours(2), sweep.NearestMeeting(Fixture.Departure)!.Departure);

        // The sweep agrees with a full evaluation.
        var full = Clearance(depth, water, new PassagePlan(legs, Fixture.Departure.AddHours(1)), check: check, sources: sources);
        var option = sweep.Options.Single(o => o.Departure == Fixture.Departure.AddHours(1));
        Assert.Equal(full.Verdict, option.Verdict);
        Assert.Equal(full.Minimum!.ClearanceMeters, option.MinimumClearanceMeters!.Value, 9);
        Assert.Equal(full.MinutesBelow, option.MinutesBelow, 9);
    }

    [Fact]
    public void The_departure_sweep_is_kept_while_only_the_departure_changes()
    {
        var origin = Fixture.Departure.UtcDateTime;
        var water = new FakeWater((_, _) => [new FakeSeries(origin.AddHours(-6), origin.AddHours(8), t => (t - origin).TotalHours)]);
        var depth = new FakeDepth((_, _) => FakeDepth.Bathymetry(12));
        var legs = new[] { Fixture.Leg(draft: 12, required: 2) };
        var check = new ClearanceCheck();
        Sources(depth, water, out var sources);
        var settings = Fixture.Settings(window: DepartureRange.WaterLevelData(TimeSpan.FromMinutes(30)));

        var first = Checks.Run(check, new PassagePlan(legs, Fixture.Departure), settings, sources).Detail!;
        var later = Checks.Run(check, new PassagePlan(legs, Fixture.Departure.AddHours(1)), settings, sources).Detail!;
        var deeper = Checks.Run(check, new PassagePlan([Fixture.Leg(draft: 13, required: 2)], Fixture.Departure), settings, sources).Detail!;

        Assert.Same(first.Departures, later.Departures);
        Assert.NotEqual(first.Clearance.Departure, later.Clearance.Departure);
        Assert.NotSame(first.Departures, deeper.Departures);
    }

    [Fact]
    public void Departures_that_meet_with_lower_confidence_still_meet_and_carry_the_caveat()
    {
        // As above, but the S-102 declares no datum (#939).
        var origin = Fixture.Departure.UtcDateTime;
        var water = new FakeWater((_, _) => [new FakeSeries(origin.AddHours(-6), origin.AddHours(8), t => (t - origin).TotalHours)]);
        var legs = new[] { Fixture.Leg(draft: 12, required: 2) };

        var sweep = Sweep(
            new FakeDepth((_, _) => FakeDepth.Bathymetry(12, datum: null)),
            water,
            legs,
            DepartureRange.Between(Fixture.Departure.AddHours(-2), Fixture.Departure.AddHours(6), TimeSpan.FromMinutes(30)))!;

        var window = Assert.Single(sweep.Windows());
        Assert.Equal(Fixture.Departure.AddHours(2), window.First);
        Assert.All(sweep.Options.Where(o => o.MeetsSettings), o => Assert.Equal(ConfidenceReasons.DatumUnconfirmed, o.Confidence));
        Assert.Equal(ConfidenceReasons.DatumUnconfirmed, sweep.MeetingConfidence());
    }

    [Fact]
    public void The_water_level_range_sweep_starts_at_a_whole_step_and_hatches_late_arrivals()
    {
        var origin = Fixture.Departure.UtcDateTime; // 06:00
        var start = origin.AddMinutes(2);
        var end = origin.AddHours(4);
        var water = new FakeWater((_, _) => [new FakeSeries(start, end, _ => 3)]);
        var legs = new[] { Fixture.Leg(draft: 12, required: 2) };

        var sweep = Sweep(new FakeDepth((_, _) => FakeDepth.Bathymetry(12)), water, legs, DepartureRange.WaterLevelData(TimeSpan.FromMinutes(5)))!;

        Assert.Equal(TimeSpan.FromMinutes(5), sweep.Step);
        Assert.Equal(Fixture.Departure.AddMinutes(5), sweep.Options[0].Departure);
        Assert.Equal(Fixture.Departure.AddHours(4), sweep.Options[^1].Departure);
        Assert.Equal(48, sweep.Options.Count);

        // The passage takes about an hour; departures arriving after 10:00 are the hatched tail.
        var firstLate = sweep.Options.First(o => o.Arrival > new DateTimeOffset(end));
        Assert.Equal((firstLate.Departure, Fixture.Departure.AddHours(4)), sweep.ArrivalAfterDataEnds());
        Assert.All(sweep.Options.Where(o => o.Departure >= firstLate.Departure), o => Assert.False(o.MeetsSettings));
        var window = Assert.Single(sweep.Windows());
        Assert.Equal(Fixture.Departure.AddMinutes(5), window.First);
        Assert.Equal(firstLate.Departure.AddMinutes(-5), window.Last);
    }

    [Fact]
    public void A_long_water_level_range_widens_the_sweep_step()
    {
        var origin = Fixture.Departure.UtcDateTime;
        var water = new FakeWater((_, _) => [new FakeSeries(origin.AddMinutes(2), origin.AddHours(4), _ => 3)]);
        var legs = new[] { Fixture.Leg(draft: 12, required: 2) };

        var sweep = Sweep(new FakeDepth((_, _) => FakeDepth.Bathymetry(12)), water, legs, DepartureRange.WaterLevelData(TimeSpan.FromMinutes(5), maxDepartures: 10))!;

        Assert.Equal(TimeSpan.FromMinutes(30), sweep.Step);
        Assert.Equal(Fixture.Departure.AddMinutes(30), sweep.Options[0].Departure);
        Assert.InRange(sweep.Options.Count, 2, 10);
    }

    [Fact]
    public void Without_water_level_coverage_there_is_no_range_to_sweep()
    {
        var legs = new[] { Fixture.Leg() };

        var sweep = Sweep(new FakeDepth((_, _) => FakeDepth.Bathymetry(12)), new FakeWater((_, _) => []), legs, DepartureRange.WaterLevelData(TimeSpan.FromMinutes(5)));

        Assert.Null(sweep);
        Assert.Throws<ArgumentOutOfRangeException>(() => DepartureRange.WaterLevelData(TimeSpan.Zero));
    }

    // One bundle over the sources, so the check may reuse what it computed for it.
    private static RouteCheckSources Sources(IChartedDepthSource depth, IWaterLevelSource water, out RouteCheckSources sources) =>
        sources = new RouteCheckSources { Depth = depth, WaterLevel = water };
}
