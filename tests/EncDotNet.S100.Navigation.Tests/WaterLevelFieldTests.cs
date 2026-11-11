using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation.Tests;

public sealed class WaterLevelFieldTests
{
    private static readonly DateTime T0 = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly GeoPosition Here = new(0, 0);

    /// <summary>A linear series: 1 m at T0 falling 0.5 m an hour, steps hourly over 6 h.</summary>
    private static FakeSeries Falling(int? datum = Fixture.Lat, string id = "104A", double? uncertainty = null) =>
        new(T0, T0.AddHours(6), t => 1.0 - (0.5 * (t - T0).TotalHours), datum, id)
        {
            Steps = Enumerable.Range(0, 7).Select(h => T0.AddHours(h)).ToList(),
            Uncertainty = uncertainty,
        };

    private static FakeWater Water(params IWaterLevelSeries[] series) => new((_, _) => series);

    [Fact]
    public void Snapshot_applies_negative_height()
    {
        // 1 − 0.5·3 = −0.5: water below chart datum makes the depth shallower.
        var field = new WaterLevelSnapshot(Water(Falling()), T0.AddHours(3));

        var sample = field.Sample(Here);

        Assert.NotNull(sample);
        Assert.Equal(-0.5, sample!.Value.HeightMeters, 6);
        Assert.Equal(10 - 0.5, 10 + sample.Value.HeightMeters, 6);
    }

    [Fact]
    public void Snapshot_carries_uncertainty_and_source()
    {
        var field = new WaterLevelSnapshot(Water(Falling(uncertainty: 0.3)), T0.AddHours(1));

        var sample = field.Sample(Here)!.Value;

        Assert.Equal(0.3, sample.UncertaintyMeters);
        Assert.Equal("104A", sample.Source.DatasetId);
    }

    [Theory]
    [InlineData(-1, WaterLevelStatus.BeforeStart)]
    [InlineData(7, WaterLevelStatus.AfterEnd)]
    public void Snapshot_does_not_adjust_outside_the_series(int hours, WaterLevelStatus expected)
    {
        var field = new WaterLevelSnapshot(Water(Falling()), T0.AddHours(hours));

        Assert.Null(field.Sample(Here));
        Assert.Equal(expected, field.Resolve(Here, Fixture.Lat).Status);
    }

    [Fact]
    public void Snapshot_reports_no_coverage()
    {
        var field = new WaterLevelSnapshot(Water(), T0);

        Assert.Equal(WaterLevelStatus.NoCoverage, field.Resolve(Here, Fixture.Lat).Status);
    }

    [Fact]
    public void Snapshot_reports_nodata()
    {
        var masked = new FakeSeries(T0, T0.AddHours(6), _ => null);
        var result = new WaterLevelSnapshot(Water(masked), T0.AddHours(1)).Resolve(Here, Fixture.Lat);

        Assert.Equal(WaterLevelStatus.NoData, result.Status);
        Assert.NotNull(result.Source);
        Assert.Null(result.Sample);
    }

    [Theory]
    [InlineData(Fixture.Lat, DatumAgreement.Same, WaterLevelStatus.Applied)]
    [InlineData(Fixture.Mllw, DatumAgreement.Different, WaterLevelStatus.DatumMismatch)]
    [InlineData(null, DatumAgreement.Unknown, WaterLevelStatus.Applied)]
    public void Snapshot_compares_the_depth_datum(int? depthDatum, DatumAgreement agreement, WaterLevelStatus status)
    {
        var result = new WaterLevelSnapshot(Water(Falling(Fixture.Lat)), T0.AddHours(1)).Resolve(Here, depthDatum);

        Assert.Equal(status, result.Status);
        Assert.Equal(agreement, result.Datum);
        Assert.NotNull(result.Sample);
    }

    [Fact]
    public void Snapshot_skips_a_differing_datum_when_another_series_covers()
    {
        var onMllw = Falling(Fixture.Mllw, "104MLLW");
        var onLat = new FakeSeries(T0, T0.AddHours(6), _ => 2.0, Fixture.Lat, "104LAT");

        var result = new WaterLevelSnapshot(Water(onMllw, onLat), T0.AddHours(1)).Resolve(Here, Fixture.Lat);

        Assert.Equal(WaterLevelStatus.Applied, result.Status);
        Assert.Equal("104LAT", result.Sample!.Value.Source.DatasetId);
    }

    [Fact]
    public void Snapshot_uses_a_later_series_when_the_preferred_one_does_not_cover_the_time()
    {
        var early = new FakeSeries(T0, T0.AddHours(1), _ => 1.0, id: "104EARLY");
        var late = new FakeSeries(T0, T0.AddHours(6), _ => 3.0, id: "104LATE");

        var sample = new WaterLevelSnapshot(Water(early, late), T0.AddHours(2)).Sample(Here);

        Assert.Equal("104LATE", sample!.Value.Source.DatasetId);
    }

    [Fact]
    public void Snapshot_basis_and_footprint_come_from_the_source()
    {
        var field = new WaterLevelSnapshot(Water(Falling()), T0.AddHours(1));

        Assert.Equal(WaterLevelBasisKind.AtTime, field.Basis.Kind);
        Assert.Equal(T0.AddHours(1), field.Basis.Start);
        Assert.Equal(field.Basis.Start, field.Basis.End);
        Assert.Empty(field.Footprint);
    }

    [Fact]
    public void Period_minimum_takes_the_lowest_step_inside_the_period()
    {
        // Steps 0.5 (1 h), 0 (2 h), −0.5 (3 h); the period 0:30–3:00 ends on a step.
        var field = new WaterLevelPeriodMinimum(Water(Falling(uncertainty: 0.2)), T0.AddMinutes(30), T0.AddHours(3));

        var sample = field.Sample(Here)!.Value;

        Assert.Equal(-0.5, sample.HeightMeters, 6);
        Assert.Equal(0.2, sample.UncertaintyMeters);
        Assert.Equal(WaterLevelBasisKind.LowestOverPeriod, field.Basis.Kind);
    }

    [Fact]
    public void Period_minimum_includes_the_interpolated_end()
    {
        // The last step inside is 2 h (0 m); the end at 2:30 interpolates to −0.25 m.
        var field = new WaterLevelPeriodMinimum(Water(Falling()), T0.AddMinutes(30), T0.AddMinutes(150));

        Assert.Equal(-0.25, field.Sample(Here)!.Value.HeightMeters, 6);
    }

    [Fact]
    public void Period_minimum_includes_the_interpolated_start()
    {
        // A rising series: lowest at the start, between steps.
        var rising = new FakeSeries(T0, T0.AddHours(6), t => -1.0 + (t - T0).TotalHours, id: "104R")
        {
            Steps = Enumerable.Range(0, 7).Select(h => T0.AddHours(h)).ToList(),
        };

        var field = new WaterLevelPeriodMinimum(Water(rising), T0.AddMinutes(30), T0.AddHours(3));

        Assert.Equal(-0.5, field.Sample(Here)!.Value.HeightMeters, 6);
    }

    [Fact]
    public void Period_minimum_needs_the_whole_period_covered()
    {
        var field = new WaterLevelPeriodMinimum(Water(Falling()), T0.AddHours(5), T0.AddHours(8));

        Assert.Equal(WaterLevelStatus.AfterEnd, field.Resolve(Here, Fixture.Lat).Status);
    }

    [Fact]
    public void Period_minimum_is_nodata_when_any_value_is_missing()
    {
        var gappy = new FakeSeries(T0, T0.AddHours(6), t => t == T0.AddHours(2) ? null : 1.0)
        {
            Steps = Enumerable.Range(0, 7).Select(h => T0.AddHours(h)).ToList(),
        };

        var field = new WaterLevelPeriodMinimum(Water(gappy), T0, T0.AddHours(4));

        Assert.Equal(WaterLevelStatus.NoData, field.Resolve(Here, Fixture.Lat).Status);
    }

    [Fact]
    public void Period_minimum_rejects_a_reversed_period() =>
        Assert.Throws<ArgumentException>(() => new WaterLevelPeriodMinimum(Water(), T0.AddHours(1), T0));

    [Fact]
    public void Snapshot_and_clearance_check_agree()
    {
        // One answer everywhere: the clearance check's water level at a sample
        // is the snapshot's at the sample's ETA.
        var water = Water(Falling(Fixture.Lat));
        var depth = new FakeDepth((_, _) => FakeDepth.Bathymetry(12, Fixture.Lat));
        var plan = new PassagePlan([Fixture.Leg()], new DateTimeOffset(T0.AddHours(2)));

        var result = Checks.Run(new ClearanceCheck(), plan, Fixture.Settings(), new RouteCheckSources { Depth = depth, WaterLevel = water })
            .Detail!.Clearance;

        Assert.NotEmpty(result.Samples);
        foreach (var sample in result.Samples)
        {
            var eta = sample.Time.UtcDateTime;
            var expected = new WaterLevelSnapshot(water, eta).Resolve(sample.Position, Fixture.Lat);
            Assert.Equal(expected.Status, sample.WaterLevel.Status);
            Assert.Equal(expected.Sample!.Value.HeightMeters, sample.WaterLevel.HeightMeters!.Value, 9);
        }
    }
}
