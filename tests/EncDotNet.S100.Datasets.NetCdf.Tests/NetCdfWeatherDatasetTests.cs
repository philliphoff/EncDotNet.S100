using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Quantities;
using static EncDotNet.S100.TestSupport.NetCdfFixtures;

namespace EncDotNet.S100.Datasets.NetCdf.Tests;

/// <summary>The CF interpretation: NetCDF-4 and NetCDF-3 files read into weather grid sets.</summary>
public sealed class NetCdfWeatherDatasetTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("netcdf-cf-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathFor(string name) => Path.Combine(_dir, name);

    [Fact]
    public void A_Copernicus_style_wave_file_reads_as_wave_height_direction_and_period()
    {
        using var dataset = NetCdfWeatherDataset.Open(WriteWaveNetCdf4(PathFor("waves.nc")));
        var set = dataset.GridSet;

        Assert.Equal(NetCdfFormat.NetCdf4, dataset.Format);
        Assert.Equal(["wave.hs", "wave.dir", "wave.period"], set.Parameters.Select(p => p.Id));
        Assert.Equal(["VHM0", "VMDR", "VTPK"], set.Parameters.Select(p => p.SourceName));
        Assert.All(set.Parameters, p => Assert.Equal(WeatherLevel.Surface, p.Level));
        Assert.Equal(ChartGrid.Times, dataset.Times);
        Assert.Equal(new WeatherGrid(20, 20, -32.31, 61.99, 0.01, 0.01), Round(set.Grid));
        Assert.Equal("MFWAM", set.Origin.Model);
        Assert.Equal("Synthetic test centre", set.Origin.Centre);
        Assert.Equal(Run, dataset.ReferenceTime);
        Assert.Equal("CF-1.6", dataset.Conventions);
        Assert.Empty(dataset.IgnoredVariables);
    }

    [Fact]
    public void Packing_and_missing_values_are_applied()
    {
        using var dataset = NetCdfWeatherDataset.Open(WriteWaveNetCdf4(PathFor("waves.nc")));
        var set = dataset.GridSet;
        var height = set.FindParameter(WeatherParameters.WaveHeight)!;

        var step = set.ReadStep(height, 2, Ct);

        var grid = ChartGrid;
        Assert.Equal(WaveHeight(4, 7, 2), step[4 * grid.Columns + 7], 3);
        Assert.True(float.IsNaN(step[^1]), "The _FillValue node is missing.");
        Assert.Equal(WavePeriod(0, 9, 0), set.ReadStep(set.FindParameter(WeatherParameters.WavePeriod)!, 0, Ct)[9], 4);
    }

    [Fact]
    public void North_to_south_latitudes_are_flipped()
    {
        using var dataset = NetCdfWeatherDataset.Open(WriteWaveNetCdf4(PathFor("waves.nc"), northToSouth: true));
        var set = dataset.GridSet;

        Assert.True(set.Layout.RowsFlipped);
        Assert.Equal(-32.31, set.Grid.OriginLatitude, 5);
        var step = set.ReadStep(set.FindParameter(WeatherParameters.WaveHeight)!, 0, Ct);
        Assert.Equal(WaveHeight(2, 3, 0), step[2 * ChartGrid.Columns + 3], 3);
        Assert.True(float.IsNaN(step[^1]));
    }

    [Fact]
    public void Steps_are_read_only_when_decoded()
    {
        var path = WriteWaveNetCdf4(PathFor("waves.nc"));
        using var dataset = NetCdfWeatherDataset.Open(path);
        var height = dataset.GridSet.FindParameter(WeatherParameters.WaveHeight)!;

        // Every step decodes on demand, and again after the first time.
        var first = dataset.GridSet.ReadStep(height, 1, Ct);
        var again = dataset.GridSet.ReadStep(height, 1, Ct);

        Assert.Equal(first, again);
        Assert.Equal(3, height.Steps.Count);
    }

    [Fact]
    public void A_10_m_wind_file_reads_as_wind_components_at_10_m()
    {
        using var dataset = NetCdfWeatherDataset.Open(WriteWindNetCdf4(PathFor("wind.nc")));
        var set = dataset.GridSet;

        Assert.Equal(["wind.u", "wind.v"], set.Parameters.Select(p => p.Id));
        Assert.All(set.Parameters, p => Assert.Equal(WeatherLevel.AboveGround(Length.FromMetres(10)), p.Level));
        Assert.Equal("Synthetic wind model", set.Origin.Model);
        // No reference time: the run time is the first valid time.
        Assert.Null(dataset.ReferenceTime);
        Assert.Equal(Run, set.Origin.RunTime);

        var grid = ChartWindGrid;
        var u = set.ReadStep(set.FindParameter(WeatherParameters.WindU)!, 1, Ct);
        var v = set.ReadStep(set.FindParameter(WeatherParameters.WindV)!, 1, Ct);
        int i = 2 * grid.Columns + 3;
        var (knots, from) = Wind(2, 3, 1, grid.Columns);
        Assert.Equal(knots, Speed.FromMetresPerSecond(Math.Sqrt(u[i] * u[i] + v[i] * v[i])).TotalKnots, 3);
        Assert.Equal(from, WindComponents.GetFromDirection(Speed.FromMetresPerSecond(u[i]), Speed.FromMetresPerSecond(v[i])).TotalDegrees, 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_ERDDAP_style_NetCDF3_file_reads_with_0_to_360_longitudes_and_a_depth_dimension(bool offset64)
    {
        using var dataset = NetCdfWeatherDataset.Open(WriteWaveNetCdf3(PathFor("ww3.nc"), offset64: offset64));
        var set = dataset.GridSet;

        Assert.Equal(offset64 ? NetCdfFormat.Offset64 : NetCdfFormat.Classic, dataset.Format);
        Assert.Equal(["wave.hs", "wave.dir", "netcdf:Tper"], set.Parameters.Select(p => p.Id));
        Assert.False(set.FindParameter("netcdf:Tper")!.IsKnown);
        Assert.Equal("s", set.FindParameter("netcdf:Tper")!.Units);
        Assert.Equal("WW3", set.Origin.Model);
        Assert.Equal(ChartGrid.Times, dataset.Times);
        Assert.Equal(61.99, set.Grid.OriginLongitude, 4);
        Assert.Equal(-32.31, set.Grid.OriginLatitude, 4);

        var step = set.ReadStep(set.FindParameter(WeatherParameters.WaveHeight)!, 1, Ct);
        Assert.Equal(WaveHeight(5, 6, 1), step[5 * ChartGrid.Columns + 6], 4);
        Assert.True(float.IsNaN(step[^1]));
        Assert.Equal(WaveDirection(0, 0, 2), set.ReadStep(set.FindParameter(WeatherParameters.WaveDirection)!, 2, Ct)[0], 4);
    }

    [Fact]
    public void Wave_height_in_feet_is_converted_to_metres()
    {
        var model = WaveFile(new Grid(10, 20, 1, 3, 4, Steps: 1), northToSouth: false, packedHeight: false);
        model.Variables[3].Attributes["units"] = "ft";
        var path = PathFor("feet.nc");
        WriteNetCdf4(path, model);

        using var dataset = NetCdfWeatherDataset.Open(path);
        var step = dataset.GridSet.ReadStep(dataset.GridSet.FindParameter(WeatherParameters.WaveHeight)!, 0, Ct);

        Assert.Equal(WaveHeight(1, 1, 0) * 0.3048, step[5], 4);
    }

    [Fact]
    public void Unconvertible_units_make_an_unknown_parameter()
    {
        var model = WaveFile(new Grid(10, 20, 1, 3, 4, Steps: 1), northToSouth: false, packedHeight: false);
        model.Variables[4].Attributes["units"] = "radians";
        var path = PathFor("radians.nc");
        WriteNetCdf4(path, model);

        using var dataset = NetCdfWeatherDataset.Open(path);

        Assert.Equal(["wave.hs", "netcdf:VMDR", "wave.period"], dataset.GridSet.Parameters.Select(p => p.Id));
        Assert.Contains(dataset.IgnoredVariables, v => v.Name == "VMDR" && v.Reason.Contains("radians"));
    }

    [Fact]
    public void Two_variables_for_one_parameter_keep_the_preferred_standard_name()
    {
        // A mean period alongside the peak period: the table prefers the peak.
        var model = WaveFile(new Grid(10, 20, 1, 3, 4, Steps: 1), northToSouth: false, packedHeight: false);
        var peak = model.Variables[5];
        model.Variables.Insert(5, peak with
        {
            Name = "VTM10",
            Attributes = new() { ["units"] = "s", ["standard_name"] = "sea_surface_wave_mean_period" },
        });
        var path = PathFor("periods.nc");
        WriteNetCdf4(path, model);

        using var dataset = NetCdfWeatherDataset.Open(path);

        Assert.Equal("VTPK", dataset.GridSet.FindParameter(WeatherParameters.WavePeriod)!.SourceName);
        Assert.NotNull(dataset.GridSet.FindParameter("netcdf:VTM10"));
    }

    [Fact]
    public void A_file_without_CF_coordinates_fails_listing_its_variables()
    {
        var model = WaveFile(new Grid(10, 20, 1, 3, 4, Steps: 1), northToSouth: false, packedHeight: false);
        foreach (var name in (string[])["latitude", "longitude"])
        {
            var variable = model.Variables.Single(v => v.Name == name);
            variable.Attributes.Clear();
        }

        var path = PathFor("no-cf.nc");
        WriteNetCdf3(path, model);

        var error = Assert.Throws<CfConventionsException>(() => NetCdfWeatherDataset.Open(path));

        Assert.Contains("no latitude coordinate", error.Message);
        Assert.Contains("Variables found: time(time) [time], latitude(latitude), longitude(longitude), VHM0(time, latitude, longitude) [sea_surface_wave_significant_height]", error.Message);
    }

    [Fact]
    public void A_file_with_no_known_standard_name_fails_listing_its_variables()
    {
        var model = WaveFile(new Grid(10, 20, 1, 3, 4, Steps: 1), northToSouth: false, packedHeight: false);
        foreach (var variable in model.Variables.Where(v => v.Name.StartsWith('V')))
            variable.Attributes.Remove("standard_name");
        var path = PathFor("unknown.nc");
        WriteNetCdf4(path, model);

        var error = Assert.Throws<CfConventionsException>(() => NetCdfWeatherDataset.Open(path));

        Assert.Contains("No variable on the grid has a CF standard_name the weather parameter table knows", error.Message);
        Assert.Contains("sea_surface_wave_significant_height", error.Message);
        Assert.Contains("VHM0(time, latitude, longitude)", error.Message);
    }

    [Fact]
    public void Two_dimensional_latitude_and_longitude_fail_naming_the_curvilinear_grid()
    {
        var model = new FixtureFile
        {
            Dimensions = [new("time", 1), new("y", 2), new("x", 3)],
            GlobalAttributes = new() { ["Conventions"] = "CF-1.8" },
            Variables =
            [
                new("time", FixtureType.Double, ["time"], [0], new() { ["units"] = "hours since 2026-10-25", ["standard_name"] = "time" }),
                new("lat", FixtureType.Float, ["y", "x"], [1, 1, 1, 2, 2, 2], new() { ["units"] = "degrees_north", ["standard_name"] = "latitude" }),
                new("lon", FixtureType.Float, ["y", "x"], [1, 2, 3, 1, 2, 3], new() { ["units"] = "degrees_east", ["standard_name"] = "longitude" }),
                new("hs", FixtureType.Float, ["time", "y", "x"], [1, 1, 1, 1, 1, 1], new() { ["units"] = "m", ["standard_name"] = "sea_surface_wave_significant_height", ["coordinates"] = "lat lon" }),
            ],
        };
        var path = PathFor("curvilinear.nc");
        WriteNetCdf3(path, model);

        var error = Assert.Throws<UnsupportedWeatherGridException>(() => NetCdfWeatherDataset.Open(path));

        Assert.Contains("curvilinear", error.Message);
        Assert.Contains("lat(y, x)", error.Message);
    }

    [Fact]
    public void A_projected_grid_mapping_fails_naming_the_projection()
    {
        var model = WaveFile(new Grid(10, 20, 1, 3, 4, Steps: 1), northToSouth: false, packedHeight: false);
        model.Variables.Add(new("crs", FixtureType.Int, [], [0], new() { ["grid_mapping_name"] = "lambert_conformal_conic" }));
        model.Variables[3].Attributes["grid_mapping"] = "crs";
        var path = PathFor("lambert.nc");
        WriteNetCdf3(path, model);

        var error = Assert.Throws<UnsupportedWeatherGridException>(() => NetCdfWeatherDataset.Open(path));

        Assert.Contains("lambert_conformal_conic", error.Message);
    }

    [Fact]
    public void A_latitude_longitude_grid_mapping_is_accepted()
    {
        var model = WaveFile(new Grid(10, 20, 1, 3, 4, Steps: 1), northToSouth: false, packedHeight: false);
        model.Variables.Add(new("crs", FixtureType.Int, [], [0], new() { ["grid_mapping_name"] = "latitude_longitude" }));
        model.Variables[3].Attributes["grid_mapping"] = "crs";
        var path = PathFor("geographic.nc");
        WriteNetCdf3(path, model);

        using var dataset = NetCdfWeatherDataset.Open(path);

        Assert.NotNull(dataset.GridSet.FindParameter(WeatherParameters.WaveHeight));
    }

    [Fact]
    public void An_unsupported_calendar_fails()
    {
        var model = WaveFile(new Grid(10, 20, 1, 3, 4, Steps: 1), northToSouth: false, packedHeight: false);
        model.Variables[0].Attributes["calendar"] = "noleap";
        var path = PathFor("noleap.nc");
        WriteNetCdf3(path, model);

        Assert.Contains("'noleap' calendar", Assert.Throws<CfConventionsException>(() => NetCdfWeatherDataset.Open(path)).Message);
    }

    [Fact]
    public void An_irregular_latitude_axis_fails()
    {
        var model = WaveFile(new Grid(10, 20, 1, 4, 4, Steps: 1), northToSouth: false, packedHeight: false);
        model.Variables[1].Values[2] = 12.6;
        var path = PathFor("gaussian.nc");
        WriteNetCdf3(path, model);

        var error = Assert.Throws<UnsupportedWeatherGridException>(() => NetCdfWeatherDataset.Open(path));

        Assert.Contains("isn't evenly spaced", error.Message);
        Assert.Contains("'latitude'", error.Message);
    }

    [Fact]
    public void A_variable_with_several_levels_is_listed_and_left_out()
    {
        var model = WaveFile(new Grid(10, 20, 1, 3, 4, Steps: 1), northToSouth: false, packedHeight: false);
        model.Dimensions.Add(new("depth", 2));
        model.Variables.Add(new("temp", FixtureType.Float, ["time", "depth", "latitude", "longitude"], new double[24],
            new() { ["units"] = "K", ["standard_name"] = "sea_water_temperature" }));
        var path = PathFor("levels.nc");
        WriteNetCdf3(path, model);

        using var dataset = NetCdfWeatherDataset.Open(path);

        var ignored = Assert.Single(dataset.IgnoredVariables);
        Assert.Equal("temp", ignored.Name);
        Assert.Contains("2 levels along 'depth'", ignored.Reason);
    }

    [Fact]
    public void Axis_attributes_alone_identify_the_coordinates()
    {
        var model = new FixtureFile
        {
            Dimensions = [new("t", 2), new("y", 2), new("x", 3)],
            GlobalAttributes = new() { ["title"] = "axes only" },
            Variables =
            [
                new("t", FixtureType.Double, ["t"], [0, 6], new() { ["units"] = "hours since 2026-10-25", ["axis"] = "T" }),
                new("y", FixtureType.Float, ["y"], [50, 51], new() { ["axis"] = "Y" }),
                new("x", FixtureType.Float, ["x"], [-5, -4, -3], new() { ["axis"] = "X" }),
                new("swh", FixtureType.Float, ["t", "y", "x"], Enumerable.Range(0, 12).Select(i => (double)i).ToArray(),
                    new() { ["units"] = "m", ["standard_name"] = "sea_surface_wave_significant_height" }),
            ],
        };
        var path = PathFor("axes.nc");
        WriteNetCdf3(path, model);

        using var dataset = NetCdfWeatherDataset.Open(path);

        Assert.Equal("axes only", dataset.GridSet.Origin.Model);
        Assert.Equal([Run, Run.AddHours(6)], dataset.Times);
        Assert.Equal(11, dataset.GridSet.ReadStep(dataset.GridSet.Parameters[0], 1, Ct)[^1]);
    }

    [Fact]
    public void A_forecast_reference_time_variable_gives_the_run()
    {
        var model = WaveFile(new Grid(10, 20, 1, 3, 4, Steps: 1), northToSouth: false, packedHeight: false);
        model.GlobalAttributes.Remove("forecast_reference_time");
        model.Variables.Add(new("forecast_reference_time", FixtureType.Double, [], [-6],
            new() { ["units"] = "hours since 2026-10-25", ["standard_name"] = "forecast_reference_time" }));
        var path = PathFor("frt.nc");
        WriteNetCdf3(path, model);

        using var dataset = NetCdfWeatherDataset.Open(path);

        Assert.Equal(Run.AddHours(-6), dataset.ReferenceTime);
        Assert.Equal(Run.AddHours(-6), dataset.GridSet.Origin.RunTime);
    }

    private static WeatherGrid Round(WeatherGrid g) => g with
    {
        OriginLatitude = Math.Round(g.OriginLatitude, 5),
        OriginLongitude = Math.Round(g.OriginLongitude, 5),
        SpacingLatitudinal = Math.Round(g.SpacingLatitudinal, 6),
        SpacingLongitudinal = Math.Round(g.SpacingLongitudinal, 6),
    };
}
