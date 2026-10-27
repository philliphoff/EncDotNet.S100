using EncDotNet.S100.Datasets.Weather;
using static EncDotNet.S100.TestSupport.GribFixtures;

namespace EncDotNet.S100.Datasets.Grib.Tests;

/// <summary>
/// Issue #1074: GRIB2 files read into a <see cref="WeatherGridSet"/> by
/// <see cref="GribWeatherDataset"/> — grouping by parameter and level, step
/// order, the table's levels and codes (GFS-Wave's surface wind, NCEP swell
/// partitions, ECMWF codes), several reference times, unsupported and
/// unknown fields, and the 0 to 360 normalisation. Values are checked against
/// ecCodes' (see tests/datasets/GRIB2/README.md).
/// </summary>
public sealed class GribWeatherDatasetTests
{
    private const double Tolerance = 1e-4;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The index of a file point (rows north to south) in the normalised grid (rows south to north).
    private static int Normalised(int fileIndex, int rows = 33, int columns = 33) =>
        (rows - 1 - fileIndex / columns) * columns + fileIndex % columns;

    // -----------------------------------------------------------------
    // A GRIB filter download of GFS-Wave
    // -----------------------------------------------------------------

    [Fact]
    public void A_filter_download_groups_its_messages_by_parameter_and_level()
    {
        using var dataset = GribWeatherDataset.Open(TestData.Path(WaveFilter));

        Assert.Equal("GFS-Wave", dataset.Model);
        Assert.Equal("NCEP", dataset.GridSet.Origin.Centre);
        Assert.Equal(Run, dataset.ReferenceTime);
        Assert.Equal(Run, dataset.GridSet.Origin.RunTime);
        Assert.Equal([Run, Run.AddHours(3), Run.AddHours(6)], dataset.Times);
        Assert.Empty(dataset.IgnoredFields);

        // 12 parameters, each with three steps in time order.
        var parameters = dataset.GridSet.Parameters;
        Assert.Equal(12, parameters.Count);
        Assert.All(parameters, p => Assert.Equal([Run, Run.AddHours(3), Run.AddHours(6)], p.Steps.Select(s => s.ValidTime)));

        var known = parameters.Where(p => p.IsKnown).Select(p => $"{p.Id} {p.SourceName} @ {p.LevelDescription}").ToArray();
        Assert.Equal(
            [
                "wind.speed WIND @ surface",
                "wind.from_direction WDIR @ surface",
                "wind.u UGRD @ surface",
                "wind.v VGRD @ surface",
                "wave.hs HTSGW @ surface",
                "wave.period PERPW @ surface",
                "wave.dir DIRPW @ surface",
                "wave.wind_sea.hs WVHGT @ surface",
                "wave.swell.hs SWELL @ GRIB2 fixed surface type 241 = 1",
                "wave.wind_sea.dir WVDIR @ surface",
            ],
            known);
    }

    [Fact]
    public void The_values_match_ecCodes_with_the_rows_flipped()
    {
        using var dataset = GribWeatherDataset.Open(TestData.Path(WaveFilter));
        var set = dataset.GridSet;
        int node = Normalised(566);

        // 35.75°N, 74.75°W is the normalised node.
        Assert.Equal((CropSouth, CropWest), (set.Grid.OriginLatitude, set.Grid.OriginLongitude));
        var (latitude, longitude) = CropPoint(566);
        Assert.Equal(latitude, set.Grid.OriginLatitude + (node / 33) * set.Grid.SpacingLatitudinal, 6);
        Assert.Equal(longitude, set.Grid.OriginLongitude + (node % 33) * set.Grid.SpacingLongitudinal, 6);

        for (int step = 0; step < 3; step++)
        {
            Assert.Equal(FilterAt566.Height[step], Read(set, WeatherParameters.WaveHeight, step)[node], Tolerance);
            Assert.Equal(FilterAt566.Period[step], Read(set, WeatherParameters.WavePeriod, step)[node], Tolerance);
            Assert.Equal(FilterAt566.Direction[step], Read(set, WeatherParameters.WaveDirection, step)[node], Tolerance);
            Assert.Equal(FilterAt566.WindSpeed[step], Read(set, WeatherParameters.WindSpeed, step)[node], Tolerance);
            Assert.Equal(FilterAt566.WindDirection[step], Read(set, WeatherParameters.WindFromDirection, step)[node], Tolerance);
            Assert.Equal(FilterAt566.U[step], Read(set, WeatherParameters.WindU, step)[node], Tolerance);
            Assert.Equal(FilterAt566.WindSeaHeight[step], Read(set, WeatherParameters.WindSeaHeight, step)[node], Tolerance);
            Assert.Equal(FilterAt566.WindSeaDirection[step], Read(set, WeatherParameters.WindSeaDirection, step)[node], Tolerance);
        }

        // The land bitmap: file point 0 (40°N, 76°W) is missing.
        Assert.True(float.IsNaN(Read(set, WeatherParameters.WaveHeight, 0)[Normalised(0)]));
    }

    [Fact]
    public void The_swell_height_is_the_first_partition()
    {
        using var dataset = GribWeatherDataset.Open(TestData.Path(WaveFilter));
        var set = dataset.GridSet;

        Assert.Equal(FilterSwellAt9.First, Read(set, WeatherParameters.SwellHeight, 1)[Normalised(9)], Tolerance);

        // The other partitions are listed, not used.
        var others = set.Parameters.Where(p => p.Id == "grib2:10,0,8").ToArray();
        Assert.Equal(["GRIB2 fixed surface type 241 = 2", "GRIB2 fixed surface type 241 = 3"], others.Select(p => p.LevelDescription));
        Assert.Equal(
            "'wave.swell.hs' is read at GRIB2 fixed surface type 241 = 1, the first partition the file has.",
            dataset.UnusedReasons[others[0]]);
        var second = set.ReadStep(others[0], 1, Ct)[Normalised(9)];
        Assert.Equal(FilterSwellAt9.Second, second, Tolerance);
    }

    [Fact]
    public void The_order_of_the_messages_doesnt_matter()
    {
        // The f006 messages, then f003, then f000: steps are ordered by valid time.
        var bytes = File.ReadAllBytes(TestData.Path(WaveFilter));
        using var original = GribFile.Open(bytes);
        var reversed = Concat(original.Fields
            .OrderByDescending(f => f.ValidTime)
            .Select(f => bytes.AsSpan((int)f.MessageOffset, (int)f.MessageLength).ToArray()));

        using var dataset = GribWeatherDataset.Read(GribFile.Open(reversed), "filter_gfswave.pl");

        Assert.Equal([Run, Run.AddHours(3), Run.AddHours(6)], dataset.Times);
        var height = dataset.GridSet.FindParameter(WeatherParameters.WaveHeight)!;
        Assert.Equal([Run, Run.AddHours(3), Run.AddHours(6)], height.Steps.Select(s => s.ValidTime));
        Assert.Equal(FilterAt566.Height[2], dataset.GridSet.ReadStep(height, 2, Ct)[Normalised(566)], Tolerance);
    }

    [Fact]
    public void A_field_is_decoded_only_when_its_step_is_read()
    {
        // The 03Z message's bitmap is corrupted (it marks four points present
        // for three packed values): the file still opens and the 00Z step
        // reads, and only reading the 03Z step fails.
        var grid = new Grid(10, 10, 1, 2, 2);
        var good = Encode(new Message { Discipline = 10, Number = 3, Process = 11, Grid = grid, Values = [1, double.NaN, 3, 4] });
        var bad = Encode(new Message { Discipline = 10, Number = 3, Process = 11, ForecastHours = 3, Grid = grid, Values = [1, double.NaN, 3, 4] });
        int bitmap = bad.AsSpan().IndexOf((byte[])[0, 0, 0, 7, 6, 0]) + 6;
        bad[bitmap] = 0xF0;

        using var dataset = GribWeatherDataset.Read(GribFile.Open(Concat(good, bad)));
        var height = dataset.GridSet.FindParameter(WeatherParameters.WaveHeight)!;

        Assert.Equal(2, height.Steps.Count);
        Assert.True(float.IsNaN(dataset.GridSet.ReadStep(height, 0, Ct)[3]));
        Assert.Throws<GribFormatException>(() => dataset.GridSet.ReadStep(height, 1, Ct));
    }

    // -----------------------------------------------------------------
    // Levels: GFS-Wave's surface wind and the 10 m wind
    // -----------------------------------------------------------------

    [Fact]
    public void With_a_10_m_wind_in_the_file_the_surface_wind_is_listed_not_used()
    {
        // GFS 10 m wind appended to the GFS-Wave download.
        var bytes = Concat(File.ReadAllBytes(TestData.Path(WaveFilter)), File.ReadAllBytes(TestData.Path(Wind10m)));

        using var dataset = GribWeatherDataset.Read(GribFile.Open(bytes));

        Assert.Equal("GFS-Wave + GFS", dataset.Model);
        var u = dataset.GridSet.FindParameter(WeatherParameters.WindU)!;
        Assert.Equal("10 m above ground", u.LevelDescription);
        Assert.Null(dataset.GridSet.FindParameter(WeatherParameters.WindSpeed));
        var surfaceSpeed = dataset.GridSet.Parameters.Single(p => p.Id == "grib2:0,2,1");
        Assert.Equal("surface", surfaceSpeed.LevelDescription);
        Assert.Equal("'wind.speed' is read at 10 m above ground, the level the table uses.", dataset.UnusedReasons[surfaceSpeed]);
    }

    // -----------------------------------------------------------------
    // Unsupported and unknown fields
    // -----------------------------------------------------------------

    [Fact]
    public void A_JPEG_2000_file_reads_as_NCEP_serves_it()
    {
        using var dataset = GribWeatherDataset.Open(TestData.Path(EncDotNet.S100.TestSupport.GribFixtures.Jpeg2000));

        Assert.Empty(dataset.IgnoredFields);
        Assert.Equal([Run.AddHours(3)], dataset.Times);
        var values = Read(dataset.GridSet, WeatherParameters.WaveHeight, 0);
        Assert.Contains(values, float.IsNaN);
        Assert.Contains(values, v => v > 0);
        Assert.NotNull(dataset.GridSet.FindParameter(WeatherParameters.WavePeriod));
        Assert.NotNull(dataset.GridSet.FindParameter(WeatherParameters.WaveDirection));
    }

    [Fact]
    public void A_CCSDS_message_is_listed_with_its_reason_and_not_used()
    {
        using var dataset = GribWeatherDataset.Read(GribFile.Open(Concat(EcmwfWave(), CcsdsMessage())), "wave-fc.grib2");

        var ignored = Assert.Single(dataset.IgnoredFields);
        Assert.Contains("5.42 (CCSDS", ignored.Reason);
        Assert.Equal("HTSGW (10,0,3), mean sea level, valid 2026-10-25 03:00Z", ignored.Name);
    }

    [Fact]
    public void A_file_with_nothing_decodable_fails_naming_the_template()
    {
        var error = Assert.Throws<UnsupportedGribException>(() => GribWeatherDataset.Read(GribFile.Open(CcsdsMessage())));

        Assert.StartsWith("The file's only field can't be decoded: Data representation template 5.42 (CCSDS", error.Message);
    }

    [Fact]
    public void Unknown_parameters_are_kept_but_not_known()
    {
        using var dataset = GribWeatherDataset.Open(TestData.Path(Templates));

        var tmax = dataset.GridSet.Parameters.Single(p => p.Id == "grib2:0,0,4");
        Assert.False(tmax.IsKnown);
        Assert.Equal("TMAX", tmax.SourceName);
        Assert.Equal("The weather parameter table doesn't have this parameter.", dataset.UnusedReasons[tmax]);
        Assert.NotNull(dataset.GridSet.FindParameter(WeatherParameters.WindGust));
        Assert.NotNull(dataset.GridSet.FindParameter(WeatherParameters.PressureMsl));
        Assert.Equal([Run.AddHours(3)], dataset.Times);
    }

    [Fact]
    public void A_field_on_another_grid_is_listed()
    {
        var other = Encode(new Message { Discipline = 10, Number = 3, Process = 11, Grid = new Grid(10, 10, 1, 2, 2), Values = [1, 2, 3, 4] });
        var bytes = Concat(File.ReadAllBytes(TestData.Path(Waves)), other);

        using var dataset = GribWeatherDataset.Read(GribFile.Open(bytes));

        var ignored = Assert.Single(dataset.IgnoredFields);
        Assert.StartsWith("It's on another grid (2 × 2 points from 10°, 10° by -1°, 1°) than the dataset's (33 × 33 points from 40°, 284°", ignored.Reason);
    }

    [Fact]
    public void A_repeated_field_is_listed()
    {
        var bytes = File.ReadAllBytes(TestData.Path(Wind10m));

        using var dataset = GribWeatherDataset.Read(GribFile.Open(Concat(bytes, bytes)));

        Assert.Equal(6, dataset.IgnoredFields.Count);
        Assert.Equal("Field #0 already gives this parameter at this level and valid time.", dataset.IgnoredFields[0].Reason);
        Assert.Equal(6, dataset.IgnoredFields[0].Field.Index);
    }

    // -----------------------------------------------------------------
    // Several reference times
    // -----------------------------------------------------------------

    [Fact]
    public void Appended_runs_keep_the_latest_run()
    {
        var grid = new Grid(10, 10, 1, 2, 2);
        byte[] Height(DateTime run, int hours, double value) =>
            Encode(new Message { Discipline = 10, Number = 3, Process = 11, ReferenceTime = run, ForecastHours = hours, Grid = grid, Values = [value, value, value, value] });
        var older = Run.AddHours(-6);
        var bytes = Concat(Height(older, 6, 1), Height(older, 9, 2), Height(Run, 0, 3), Height(Run, 3, 4));

        using var dataset = GribWeatherDataset.Read(GribFile.Open(bytes));

        Assert.Equal(Run, dataset.ReferenceTime);
        Assert.Equal([Run, Run.AddHours(3)], dataset.Times);
        Assert.Equal(3f, dataset.GridSet.ReadStep(dataset.GridSet.Parameters[0], 0, Ct)[0], 3);
        Assert.Equal(2, dataset.IgnoredFields.Count);
        Assert.Equal("It's from an older model run (2026-10-24 18Z); the file's latest run, 2026-10-25 00Z, is used.", dataset.IgnoredFields[0].Reason);
    }

    [Fact]
    public void Appended_analyses_are_an_analysis_series()
    {
        var grid = new Grid(10, 10, 1, 2, 2);
        byte[] Analysis(DateTime run, double value) =>
            Encode(new Message { Discipline = 10, Number = 3, Process = 11, ReferenceTime = run, Grid = grid, Values = [value, value, value, value] });
        var bytes = Concat(Analysis(Run.AddHours(6), 2), Analysis(Run, 1));

        using var dataset = GribWeatherDataset.Read(GribFile.Open(bytes));

        Assert.Null(dataset.ReferenceTime);
        Assert.Equal(Run, dataset.GridSet.Origin.RunTime);
        Assert.Equal([Run, Run.AddHours(6)], dataset.Times);
        Assert.Empty(dataset.IgnoredFields);
        Assert.Equal(2f, dataset.GridSet.ReadStep(dataset.GridSet.Parameters[0], 1, Ct)[0], 3);
    }

    // -----------------------------------------------------------------
    // Longitudes
    // -----------------------------------------------------------------

    [Fact]
    public void A_global_0_to_360_grid_is_normalised_across_the_prime_meridian()
    {
        using var dataset = GribWeatherDataset.Read(GribFile.Open(GlobalFile()));
        var set = dataset.GridSet;

        Assert.Equal((-90.0, -180.0), (set.Grid.OriginLatitude, set.Grid.OriginLongitude));
        Assert.Equal((91, 180), (set.Grid.Rows, set.Grid.Columns));
        Assert.Equal(90, set.Layout.ColumnRotation);
        var values = Read(set, WeatherParameters.WaveHeight, 1);

        // Either side of the prime meridian at 40°N: 2°W is the file's 358°E, 2°E its 2°E.
        int row = (40 + 90) / 2;
        Assert.Equal(GlobalValue(40, 358) + 1, values[row * 180 + (-2 + 180) / 2], 3);
        Assert.Equal(GlobalValue(40, 0) + 1, values[row * 180 + (0 + 180) / 2], 3);
        Assert.Equal(GlobalValue(40, 2) + 1, values[row * 180 + (2 + 180) / 2], 3);
        // The western edge is the file's 180°E, the eastern edge its 178°E.
        Assert.Equal(GlobalValue(40, 180) + 1, values[row * 180], 3);
        Assert.Equal(GlobalValue(40, 178) + 1, values[row * 180 + 179], 3);
        // The south pole row is the file's last.
        Assert.Equal(GlobalValue(-90, 0) + 1, values[90], 3);
    }

    [Fact]
    public void A_regional_grid_from_350_to_10_east_becomes_minus_10_to_10()
    {
        using var dataset = GribWeatherDataset.Read(GribFile.Open(PrimeMeridianFile()));
        var set = dataset.GridSet;

        Assert.Equal((50.0, -10.0), (set.Grid.OriginLatitude, set.Grid.OriginLongitude));
        Assert.Equal(0, set.Layout.ColumnRotation);
        var values = Read(set, WeatherParameters.WaveHeight, 0);
        Assert.Equal(GlobalValue(50, 350), values[0], 3);
        Assert.Equal(GlobalValue(50, 0), values[10], 3);
        Assert.Equal(GlobalValue(60, 10), values[10 * 21 + 20], 3);
    }

    // -----------------------------------------------------------------
    // ECMWF
    // -----------------------------------------------------------------

    [Fact]
    public void ECMWF_codes_map_to_the_table()
    {
        using var dataset = GribWeatherDataset.Read(GribFile.Open(EcmwfOper()), "20261025000000-3h-oper-fc.grib2");
        var set = dataset.GridSet;

        Assert.Equal("ECMWF IFS", dataset.Model);
        Assert.Equal("ECMWF", set.Origin.Centre);
        // msl is (0,3,0) at mean sea level.
        var msl = set.FindParameter(WeatherParameters.PressureMsl)!;
        Assert.Equal("PRES", msl.SourceName);
        Assert.Equal(101_000 + 10 * 20 + 300, Read(set, WeatherParameters.PressureMsl, 0)[0], 1);
        // 10fg is a 3-hour maximum: valid at the end of the interval, like the instantaneous fields.
        var gust = set.FindParameter(WeatherParameters.WindGust)!;
        Assert.Equal([Run.AddHours(3), Run.AddHours(6)], gust.Steps.Select(s => s.ValidTime));
        Assert.Equal([Run.AddHours(3), Run.AddHours(6)], dataset.Times);
        Assert.Equal(12 + 0.2 * 4 + 6, Read(set, WeatherParameters.WindGust, 1)[4], 3);
        Assert.Equal("10 m above ground", set.FindParameter(WeatherParameters.WindU)!.LevelDescription);
        Assert.Empty(dataset.UnusedReasons);
    }

    [Fact]
    public void ECMWF_wave_codes_prefer_the_peak_period()
    {
        using var dataset = GribWeatherDataset.Read(GribFile.Open(EcmwfWave()), "20261025000000-3h-wave-fc.grib2");
        var set = dataset.GridSet;

        Assert.Equal("ECMWF WAM", dataset.Model);
        Assert.Equal(11f, Read(set, WeatherParameters.WavePeriod, 0)[0], 3);
        Assert.Equal(280f, Read(set, WeatherParameters.WaveDirection, 0)[0], 3);
        Assert.Equal("mean sea level", set.FindParameter(WeatherParameters.WaveHeight)!.LevelDescription);
        var mwp = set.Parameters.Single(p => p.Id == "grib2:10,0,15");
        Assert.Equal("'wave.period' is read from 10,0,34, which the table prefers.", dataset.UnusedReasons[mwp]);
    }

    [Fact]
    public void Without_a_file_name_ECMWF_is_named_by_its_centre()
    {
        using var dataset = GribWeatherDataset.Read(GribFile.Open(EcmwfWave()));

        Assert.Equal("ECMWF", dataset.Model);
    }

    [Theory]
    [InlineData("gfs.t00z.pgrb2.0p25.f003", "GFS")]
    [InlineData("filter_gfs_0p25.pl", "GFS")]
    [InlineData("gfswave.t00z.global.0p25.f003.grib2", "GFS-Wave")]
    [InlineData("filter_gfswave.pl", "GFS-Wave")]
    [InlineData("20261025000000-0h-oper-fc.grib2", "ECMWF IFS")]
    [InlineData("20261025060000-0h-scda-fc.grib2", "ECMWF IFS")]
    [InlineData("20261025000000-3h-wave-fc.grib2", "ECMWF WAM")]
    [InlineData("20261025000000-0h-aifs-single-fc.grib2", "ECMWF AIFS")]
    [InlineData("waves.grib2", null)]
    [InlineData("gfsx.grib2", null)]
    public void The_file_name_names_the_model(string fileName, string? expected)
    {
        Assert.Equal(expected, GribModelNames.FromFileName(fileName, GribCentres.Ecmwf));
    }

    [Fact]
    public void NCEPs_generating_process_names_the_model_whatever_the_file_name()
    {
        using var gfs = GribWeatherDataset.Read(GribFile.Open(File.ReadAllBytes(TestData.Path(Wind10m))), "gfswave.grib2");

        // f000 is the GFS analysis (process 81), f003 and f006 the forecast (96).
        Assert.Equal("GFS", gfs.Model);
    }

    [Fact]
    public void Disposing_closes_the_file()
    {
        var dataset = GribWeatherDataset.Open(TestData.Path(WaveFilter));
        var height = dataset.GridSet.FindParameter(WeatherParameters.WaveHeight)!;

        dataset.Dispose();

        Assert.Throws<ObjectDisposedException>(() => dataset.GridSet.ReadStep(height, 0, Ct));
    }

    private static float[] Read(WeatherGridSet set, string id, int step) =>
        set.ReadStep(set.FindParameter(id) ?? throw new InvalidOperationException($"No {id}."), step, Ct);
}
