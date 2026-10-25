using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Coverage.Pyramid;

namespace EncDotNet.S100.Datasets.Weather.Tests;

public class WeatherCoverageSourceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WeatherCoverageSource Source(WeatherNativeGrid grid, params WeatherParameter[] parameters)
    {
        var set = new WeatherGridSet(TestGrids.Origin, grid, parameters);
        return new WeatherCoverageSource(set, "test", parameters);
    }

    [Fact]
    public void Metadata_describes_the_normalised_grid_with_NaN_no_data()
    {
        // North-to-south rows, 0–360 longitudes east of 180°.
        var grid = new WeatherNativeGrid(3, 4, 50, 200, -0.5, 0.5);
        var source = Source(grid, TestGrids.Parameter(WeatherParameters.WaveHeight, (0, new float[12])));

        var metadata = source.Metadata;

        Assert.Equal(49, metadata.GridMetadata.OriginLatitude);
        Assert.Equal(-160, metadata.GridMetadata.OriginLongitude, 9);
        Assert.Equal(0.5, metadata.GridMetadata.SpacingLatitudinal);
        Assert.Equal(3, metadata.GridMetadata.NumRows);
        Assert.Equal(4, metadata.GridMetadata.NumColumns);
        Assert.Equal("EPSG:4326", metadata.HorizontalCRS);
        Assert.True(float.IsNaN(metadata.NoDataValue));
        var field = Assert.Single(metadata.ValueFields);
        Assert.Equal("wave.hs", field.Name);
        Assert.Equal("metres", field.Units);
        Assert.True(float.IsNaN(field.FillValue));
        Assert.Equal(new EncDotNet.S100.Pipelines.BoundingBox(49, -160, 50.5, -158), metadata.NativeExtent);
    }

    [Fact]
    public void Interim_spec_is_the_default_SpecRef_unless_the_reader_passes_one()
    {
        var source = Source(TestGrids.Regular(2, 2), TestGrids.Parameter(WeatherParameters.WaveHeight, (0, new float[4])));

        Assert.Equal(default, source.Metadata.Spec);
        Assert.Null(source.Metadata.Spec.Name);
    }

    [Fact]
    public void Sample_returns_rows_running_north()
    {
        // Native rows at 2°N, 1°N, 0°N.
        var grid = new WeatherNativeGrid(3, 2, 2, 0, -1, 1);
        float[] native = [20, 21, 10, 11, 0, 1];
        var source = Source(grid, TestGrids.Parameter(WeatherParameters.WaveHeight, (0, native)));

        var sample = source.Sample(GridRegion.Full, Ct);

        Assert.Equal([0f, 1f, 10f, 11f, 20f, 21f], sample.GetValues("wave.hs"));
        Assert.Equal(new EncDotNet.S100.DataModel.GeoPosition(2, 1), sample.GetPosition(2, 1));
    }

    [Fact]
    public void Missing_values_sample_as_NaN()
    {
        var set = new WeatherGridSet(
            TestGrids.Origin,
            TestGrids.Regular(1, 3),
            [
                new WeatherParameter(
                    TestGrids.Definition(WeatherParameters.WaveHeight),
                    WeatherLevel.Surface,
                    [WeatherTimeStep.FromValues(TestGrids.Run, [1f, -999f, float.NaN])],
                    missingValues: [-999f]),
            ]);
        var source = new WeatherCoverageSource(set, "wave", set.Parameters);

        var values = source.Sample(GridRegion.Full, Ct).GetValues("wave.hs");

        Assert.Equal(1f, values[0]);
        Assert.True(float.IsNaN(values[1]));
        Assert.True(float.IsNaN(values[2]));
    }

    [Fact]
    public void Wind_speed_and_direction_are_derived_from_U_and_V()
    {
        // Cells: from the north at 5 m/s, from the west at 3 m/s, a missing U.
        var u = TestGrids.Parameter(WeatherParameters.WindU, (0, [0f, 3f, float.NaN]));
        var v = TestGrids.Parameter(WeatherParameters.WindV, (0, [-5f, 0f, 1f]));
        var source = Source(TestGrids.Regular(1, 3), u, v);

        var sample = source.Sample(GridRegion.Full, Ct);

        Assert.Equal(["wind.u", "wind.v"], sample.Values.Keys.Order());
        Assert.Equal(["wind.u", "wind.v", "wind.speed", "wind.from_direction"], source.FieldNames);
        Assert.Equal(["wind.u", "wind.v"], source.Metadata.ValueFields.Select(f => f.Name));
        var speed = sample.GetValues("wind.speed");
        var direction = sample.GetValues("wind.from_direction");
        Assert.Equal(5f, speed[0], 5);
        Assert.Equal(0f, direction[0], 4);
        Assert.Equal(3f, speed[1], 5);
        Assert.Equal(270f, direction[1], 4);
        Assert.True(float.IsNaN(speed[2]));
        Assert.True(float.IsNaN(direction[2]));
    }

    [Fact]
    public void Stored_wind_speed_and_direction_are_not_derived()
    {
        var u = TestGrids.Parameter(WeatherParameters.WindU, (0, [1f]));
        var v = TestGrids.Parameter(WeatherParameters.WindV, (0, [1f]));
        var speed = TestGrids.Parameter(WeatherParameters.WindSpeed, (0, [42f]));
        var direction = TestGrids.Parameter(WeatherParameters.WindFromDirection, (0, [7f]));
        var source = Source(TestGrids.Regular(1, 1), u, v, speed, direction);

        var sample = source.Sample(GridRegion.Full, Ct);

        Assert.Empty(source.DerivedFields);
        Assert.Equal(42f, sample.GetValues("wind.speed")[0]);
        Assert.Equal(7f, sample.GetValues("wind.from_direction")[0]);
    }

    [Fact]
    public void Only_the_missing_derived_field_is_added()
    {
        var u = TestGrids.Parameter(WeatherParameters.WindU, (0, [1f]));
        var v = TestGrids.Parameter(WeatherParameters.WindV, (0, [1f]));
        var speed = TestGrids.Parameter(WeatherParameters.WindSpeed, (0, [42f]));
        var source = Source(TestGrids.Regular(1, 1), u, v, speed);

        var derived = Assert.Single(source.DerivedFields);
        Assert.Equal(WeatherParameters.WindFromDirection, derived.Name);
        Assert.Equal(225f, source.Sample(GridRegion.Full, Ct).GetValues("wind.from_direction")[0], 4);
    }

    [Fact]
    public void Steps_are_decoded_only_when_sampled()
    {
        var counter = new DecodeCounter();
        var source = Source(
            TestGrids.Regular(2, 2),
            TestGrids.Counted(WeatherParameters.WindU, counter, 4, 0, 3, 6),
            TestGrids.Counted(WeatherParameters.WindV, counter, 4, 0, 3, 6));

        _ = source.Metadata;
        _ = source.AvailableTimes;
        source.SelectTime(TestGrids.Run.AddHours(3));
        Assert.Equal(0, counter.Total);

        var sample = source.Sample(GridRegion.Full, Ct);
        Assert.Equal(3f, sample.GetValues("wind.u")[0]);
        Assert.Equal([("wind.u", 3), ("wind.v", 3)], counter.Decodes);

        // A second sample, at a coarser level or with a stride, uses the cache.
        source.Sample(new GridRegion(null, null, null, null, 2, 2), Ct);
        Assert.Equal(2, counter.Total);
    }

    [Fact]
    public void Least_recently_used_steps_are_evicted()
    {
        var counter = new DecodeCounter();
        var set = new WeatherGridSet(
            TestGrids.Origin,
            TestGrids.Regular(1, 1),
            [TestGrids.Counted(WeatherParameters.WaveHeight, counter, 1, 0, 1, 2)]);
        var source = new WeatherCoverageSource(set, "wave", set.Parameters, cacheCapacity: 2);

        SampleAt(0);
        SampleAt(1);
        SampleAt(0);
        Assert.Equal(2, counter.Total);
        Assert.Equal([0, 1], source.CachedTimeIndexes);

        // Hour 2 evicts hour 1, the least recently used.
        SampleAt(2);
        Assert.Equal([2, 0], source.CachedTimeIndexes);
        SampleAt(0);
        Assert.Equal(3, counter.Total);
        SampleAt(1);
        Assert.Equal(2, counter.CountFor(1));
        Assert.Equal([1, 0], source.CachedTimeIndexes);

        void SampleAt(int hours)
        {
            source.SelectTime(TestGrids.Run.AddHours(hours));
            Assert.Equal(hours, source.Sample(GridRegion.Full, Ct).GetValues("wave.hs")[0]);
        }
    }

    [Fact]
    public void SelectTime_picks_the_exact_step_or_the_nearest()
    {
        var source = Source(
            TestGrids.Regular(1, 1),
            TestGrids.Parameter(WeatherParameters.WaveHeight, (6, [6f]), (0, [0f]), (3, [3f])));

        Assert.Equal([TestGrids.Run, TestGrids.Run.AddHours(3), TestGrids.Run.AddHours(6)], source.AvailableTimes);

        Assert.Equal(0f, ValueAt(TestGrids.Run.AddHours(-5)));
        Assert.Equal(3f, ValueAt(TestGrids.Run.AddHours(3)));
        Assert.Equal(3f, ValueAt(TestGrids.Run.AddHours(4)));
        Assert.Equal(6f, ValueAt(TestGrids.Run.AddHours(5)));
        // Halfway between 0 and 3 h: the earlier step.
        Assert.Equal(0f, ValueAt(TestGrids.Run.AddMinutes(90)));
        Assert.Equal(6f, ValueAt(TestGrids.Run.AddDays(2)));

        float ValueAt(DateTime time)
        {
            source.SelectTime(time);
            return source.Sample(GridRegion.Full, Ct).GetValues("wave.hs")[0];
        }
    }

    [Fact]
    public void A_parameter_without_a_step_at_the_selected_time_samples_as_NaN()
    {
        var source = Source(
            TestGrids.Regular(1, 1),
            TestGrids.Parameter(WeatherParameters.WaveHeight, (0, [1f]), (3, [2f])),
            TestGrids.Parameter(WeatherParameters.WaveDirection, (0, [90f])));

        source.SelectTime(TestGrids.Run.AddHours(3));
        var sample = source.Sample(GridRegion.Full, Ct);

        Assert.Equal(2f, sample.GetValues("wave.hs")[0]);
        Assert.True(float.IsNaN(sample.GetValues("wave.dir")[0]));
    }

    [Fact]
    public void Overview_levels_decimate_2x_and_4x()
    {
        var source = Source(
            new WeatherNativeGrid(9, 9, 10, 20, 0.5, 0.5),
            TestGrids.Parameter(WeatherParameters.WaveHeight, (0, TestGrids.Indexed(9, 9))));

        var levels = source.AvailableOverviewLevels;
        Assert.Equal(3, levels.Count);
        Assert.Equal(new CoverageOverviewLevel(0, 9, 9, 0.5, 0.5), levels[0]);
        Assert.Equal(new CoverageOverviewLevel(1, 5, 5, 1.0, 1.0), levels[1]);
        Assert.Equal(new CoverageOverviewLevel(2, 3, 3, 2.0, 2.0), levels[2]);

        source.SelectOverviewLevel(1);
        Assert.Equal(1, source.SelectedOverviewLevel);
        var metadata = source.Metadata.GridMetadata;
        Assert.Equal(5, metadata.NumRows);
        Assert.Equal(1.0, metadata.SpacingLatitudinal);
        Assert.Equal(10, metadata.OriginLatitude);
        Assert.Equal(20, metadata.OriginLongitude);
        var level1 = source.Sample(GridRegion.Full, Ct);
        Assert.Equal(5, level1.Metadata.NumRows);
        // Cell (1, 2) of level 1 is base cell (2, 4).
        Assert.Equal(204f, level1.GetField("wave.hs")[1, 2]);
        Assert.Equal(new EncDotNet.S100.DataModel.GeoPosition(11, 22), level1.GetPosition(1, 2));

        source.SelectOverviewLevel(2);
        var level2 = source.Sample(GridRegion.Full, Ct);
        Assert.Equal(3, level2.Metadata.NumColumns);
        Assert.Equal(808f, level2.GetField("wave.hs")[2, 2]);

        // A region is in the selected level's cells.
        var region = source.Sample(new GridRegion(1, 3, 2, 3, 1, 1), Ct);
        Assert.Equal([408f, 808f], region.GetValues("wave.hs"));
        Assert.Equal(12, region.Metadata.OriginLatitude);
        Assert.Equal(24, region.Metadata.OriginLongitude);

        Assert.Throws<ArgumentOutOfRangeException>(() => source.SelectOverviewLevel(3));
    }

    [Fact]
    public void Small_grids_offer_fewer_overview_levels()
    {
        var source = Source(TestGrids.Regular(2, 3), TestGrids.Parameter(WeatherParameters.WaveHeight, (0, new float[6])));

        Assert.Single(source.AvailableOverviewLevels);
        Assert.Throws<ArgumentOutOfRangeException>(() => source.SelectOverviewLevel(1));
    }

    [Fact]
    public void Stride_decimates_and_scales_the_sample_spacing()
    {
        var source = Source(TestGrids.Regular(5, 5), TestGrids.Parameter(WeatherParameters.WaveHeight, (0, TestGrids.Indexed(5, 5))));

        var sample = source.Sample(new GridRegion(1, 5, 0, 5, 2, 3), Ct);

        Assert.Equal(2, sample.Metadata.NumRows);
        Assert.Equal(2, sample.Metadata.NumColumns);
        Assert.Equal([100f, 103f, 300f, 303f], sample.GetValues("wave.hs"));
        Assert.Equal(1, sample.Metadata.OriginLatitude);
        Assert.Equal(2, sample.Metadata.SpacingLatitudinal);
        Assert.Equal(3, sample.Metadata.SpacingLongitudinal);
    }

    [Fact]
    public void Empty_region_samples_nothing_and_decodes_nothing()
    {
        var counter = new DecodeCounter();
        var source = Source(TestGrids.Regular(2, 2), TestGrids.Counted(WeatherParameters.WaveHeight, counter, 4, 0));

        var sample = source.Sample(new GridRegion(1, 1, 0, 2, 1, 1), Ct);

        Assert.Empty(sample.GetValues("wave.hs"));
        Assert.Equal(0, counter.Total);
    }

    [Fact]
    public void Cancellation_is_observed()
    {
        var source = Source(TestGrids.Regular(2, 2), TestGrids.Parameter(WeatherParameters.WaveHeight, (0, new float[4])));

        Assert.Throws<OperationCanceledException>(
            () => source.Sample(GridRegion.Full, new CancellationToken(canceled: true)));
    }

    [Fact]
    public void CreateForGroups_makes_one_source_per_group_and_skips_unknown_parameters()
    {
        float[] one = [1f];
        var parameters = new[]
        {
            TestGrids.Parameter(WeatherParameters.WindU, (0, one)),
            TestGrids.Parameter(WeatherParameters.WaveHeight, (0, one)),
            TestGrids.Parameter(WeatherParameters.WindV, (0, one)),
            TestGrids.Parameter(WeatherParameters.WavePeriod, (0, one)),
            TestGrids.Parameter(WeatherParameters.PressureMsl, (0, one)),
            new WeatherParameter("grib2:0,1,8", "kg m-2", WeatherLevel.Surface, [WeatherTimeStep.FromValues(TestGrids.Run, one)]),
        };
        var set = new WeatherGridSet(TestGrids.Origin, TestGrids.Regular(1, 1), parameters);

        var sources = WeatherCoverageSource.CreateForGroups(set);

        Assert.Equal(["wind", "wave", "pressure"], sources.Select(s => s.Name));
        Assert.Equal(["wind.u", "wind.v"], sources[0].Parameters.Select(p => p.Id));
        Assert.Equal(["wind.u", "wind.v", "wind.speed", "wind.from_direction"], sources[0].FieldNames);
        Assert.Equal("10 m above ground", sources[0].Metadata.VerticalDatum);
        Assert.Equal(["wave.hs", "wave.period"], sources[1].Parameters.Select(p => p.Id));
        Assert.Equal("pascals", sources[2].Metadata.ValueFields[0].Units);
    }

    [Fact]
    public void Constructor_rejects_bad_parameter_lists()
    {
        var inSet = TestGrids.Parameter(WeatherParameters.WaveHeight, (0, [1f]));
        var outside = TestGrids.Parameter(WeatherParameters.WaveDirection, (0, [1f]));
        var empty = TestGrids.Parameter(WeatherParameters.WavePeriod);
        var set = new WeatherGridSet(TestGrids.Origin, TestGrids.Regular(1, 1), [inSet, empty]);

        Assert.Throws<ArgumentException>(() => new WeatherCoverageSource(set, "wave", []));
        Assert.Throws<ArgumentException>(() => new WeatherCoverageSource(set, "wave", [outside]));
        Assert.Throws<ArgumentException>(() => new WeatherCoverageSource(set, "wave", [inSet, inSet]));
        Assert.Throws<ArgumentException>(() => new WeatherCoverageSource(set, "wave", [empty]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeatherCoverageSource(set, "wave", [inSet], cacheCapacity: 0));
    }

    [Fact]
    public void Grid_set_rejects_an_antimeridian_crossing_grid_and_duplicate_parameters()
    {
        var wave = TestGrids.Parameter(WeatherParameters.WaveHeight, (0, new float[51]));

        Assert.Throws<UnsupportedWeatherGridException>(
            () => new WeatherGridSet(TestGrids.Origin, new WeatherNativeGrid(1, 51, 0, 175, 1, 1), [wave]));
        Assert.Throws<ArgumentException>(
            () => new WeatherGridSet(TestGrids.Origin, new WeatherNativeGrid(1, 51, 0, 0, 1, 1), [wave, wave]));
    }
}
