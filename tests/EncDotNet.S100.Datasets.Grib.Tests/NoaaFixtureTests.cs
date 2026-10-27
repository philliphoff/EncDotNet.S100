using EncDotNet.S100.Datasets.Weather;

namespace EncDotNet.S100.Datasets.Grib.Tests;

/// <summary>
/// Decodes the NOAA GFS and GFS-Wave fixtures in tests/datasets/GRIB2 and
/// compares the values with what ecCodes 2.50.0 (the <c>eccodes</c> and
/// <c>eccodeslib</c> 2.50.0 Python wheels, <c>codes_get_values</c>) reports
/// for the same messages. The reference values were dumped by
/// tools/grib-fixtures/make_grib_fixtures.py; see the fixtures' README.
/// </summary>
/// <remarks>
/// Values are compared at five grid points per message (first, second,
/// middle, last and an inner point), plus the count of points present and
/// their minimum, maximum and mean. ecCodes reports bitmap-missing points as
/// its missing value (set to 9999); the decoder gives NaN.
/// </remarks>
public class NoaaFixtureTests
{
    // Point 566 is row 17, column 5 of the 33 × 33 crops.
    private static readonly int[] CropPoints = [0, 1, 544, 1088, 566];

    public static TheoryData<string, int, double[], int, double, double, double> Expected => new()
    {
        // GFS 1°, UGRD 10 m, f003, exactly as NCEP serves it (5.3, order 2).
        { "gfs-1p00-ugrd10m-f003.grib2", 0, [-1.92164306640625, -1.94164306640625, 2.05835693359375, -0.9416430664062501, 1.32835693359375], 65160, -22.861643066406252, 30.25835693359375, 0.002685969812289075 },

        // GFS 0.25° 10 m wind, cropped, repacked by ecCodes as 5.3 order 2.
        { "gfs-useast-wind10m.grib2", 0, [-0.7508106231689453, 0.3273143768310547, -9.844560623168945, -12.860185623168945, -8.516435623168945], 1089, -13.375810623168945, 1.3898143768310547, -7.782907156685933 },
        { "gfs-useast-wind10m.grib2", 1, [0.05584430694580078, -0.4441556930541992, -4.819155693054199, -7.272280693054199, -4.959780693054199], 1089, -8.2566556930542, 1.9620943069458008, -5.259740518582206 },
        { "gfs-useast-wind10m.grib2", 2, [0.031703948974609375, 0.5942039489746094, -9.40579605102539, -13.15579605102539, -9.53079605102539], 1089, -14.53079605102539, 2.2192039489746094, -7.922210192439532 },
        { "gfs-useast-wind10m.grib2", 3, [0.25222158432006836, -0.32590341567993164, -5.404028415679932, -5.544653415679932, -4.310278415679932], 1089, -7.950903415679932, 1.9709715843200684, -4.960100500161107 },
        { "gfs-useast-wind10m.grib2", 4, [0.21496295928955078, 0.5899629592895508, -9.41003704071045, -14.03503704071045, -8.91003704071045], 1089, -15.03503704071045, 3.277462959289551, -8.015179373125509 },
        { "gfs-useast-wind10m.grib2", 5, [-0.337404727935791, -0.618654727935791, -5.993654727935791, -6.306154727935791, -5.056154727935791], 1089, -7.431154727935791, 0.615720272064209, -5.114565540607967 },

        // GFS 0.25° f003 in each other template: gust 5.0, PRMSL 5.2, TMAX 2 m (4.8) 5.41, UGRD 10 m 5.3 order 1.
        { "gfs-useast-templates.grib2", 0, [0.30403141975402836, 0.6040314197540284, 11.504031419754028, 14.60403141975403, 11.70403141975403], 1089, 0.20403141975402833, 16.80403141975403, 10.113030501480383 },
        { "gfs-useast-templates.grib2", 1, [103448.609375, 103441.609375, 103015.984375, 102111.234375, 103013.359375], 1089, 102066.609375, 103448.609375, 102916.90150080349 },
        { "gfs-useast-templates.grib2", 2, [281.34439453125003, 281.58439453125, 292.63439453125, 297.43439453125, 292.61439453125], 1089, 281.34439453125003, 297.57439453125, 292.1173605551251 },
        { "gfs-useast-templates.grib2", 3, [0.031703948974609375, 0.5942039489746094, -9.40579605102539, -13.15579605102539, -9.53079605102539], 1089, -14.53079605102539, 2.2192039489746094, -7.922210192439532 },

        // GFS-Wave 0.25°, cropped, with the land bitmap: HTSGW 5.3 order 1, PERPW 5.2, DIRPW 5.0, at f000, f003, f006.
        { "gfswave-useast.grib2", 0, [double.NaN, double.NaN, 2.5700000002980232, 3.3981250002980232, 2.5231250002980232], 1025, 0.07000000029802322, 3.7028125002980232, 2.528772866151682 },
        { "gfswave-useast.grib2", 1, [double.NaN, double.NaN, 10.071249961853027, 7.571249961853027, 10.172812461853027], 1025, 5.539999961853027, 11.618124961853027, 9.848658498438393 },
        { "gfswave-useast.grib2", 2, [double.NaN, double.NaN, 50.76, 47.63, 48.26], 1025, 17.150000000000002, 114.21000000000001, 48.466107317073174 },
        { "gfswave-useast.grib2", 3, [double.NaN, double.NaN, 2.4996875002980232, 3.6168750002980232, 2.5387500002980232], 1025, 0.07000000029802322, 3.9371875002980232, 2.5838719515175352 },
        { "gfswave-useast.grib2", 4, [double.NaN, double.NaN, 9.878125190734863, 8.018750190734863, 10.018750190734863], 1025, 6.050000190734863, 11.682812690734863, 9.624817263905594 },
        { "gfswave-useast.grib2", 5, [double.NaN, double.NaN, 50.31, 49.6, 49.84], 1025, 24.78, 114.21000000000001, 52.62827317073171 },
        { "gfswave-useast.grib2", 6, [double.NaN, double.NaN, 2.4662499986588955, 3.9349999986588955, 2.6224999986588955], 1025, 0.05999999865889549, 4.2162499986588955, 2.639268291341822 },
        { "gfswave-useast.grib2", 7, [double.NaN, double.NaN, 10.320624828338623, 8.476874828338623, 9.500312328338623], 1025, 5.929999828338623, 11.929999828338623, 9.82316293809472 },
        { "gfswave-useast.grib2", 8, [double.NaN, double.NaN, 51.81, 47.08, 52.09], 1025, 27.28, 116.77, 54.02031219512195 },
    };

    [Theory]
    [MemberData(nameof(Expected))]
    public void Values_match_ecCodes(string file, int fieldIndex, double[] points, int present, double min, double max, double mean)
    {
        using var grib = GribFile.Open(TestData.Path(file));
        var field = grib.Fields[fieldIndex];
        var values = grib.ReadValues(field, TestContext.Current.CancellationToken);

        Assert.Equal(field.Grid.NumberOfPoints, values.Length);
        var indices = values.Length == 1089 ? CropPoints : [0, 1, values.Length / 2, values.Length - 1, 566];
        for (var k = 0; k < indices.Length; k++)
            AssertClose(points[k], values[indices[k]]);

        var valid = values.Where(v => !float.IsNaN(v)).Select(v => (double)v).ToArray();
        Assert.Equal(present, valid.Length);
        AssertClose(min, (float)valid.Min());
        AssertClose(max, (float)valid.Max());
        Assert.Equal(mean, valid.Average(), Math.Max(1e-6, Math.Abs(mean) * 1e-6));
    }

    [Fact]
    public void Ncep_message_is_indexed_as_ecCodes_describes_it()
    {
        using var grib = GribFile.Open(TestData.Path("gfs-1p00-ugrd10m-f003.grib2"));

        var field = Assert.Single(grib.Fields);
        Assert.Equal(78547, field.MessageLength);
        Assert.Equal(GribCentres.Ncep, field.Identification.Centre);
        Assert.Equal("NCEP", field.Identification.CentreName);
        Assert.Equal(new GribParameterCode(0, 2, 2), field.Parameter);
        Assert.Equal(3, field.DataRepresentation.TemplateNumber);
        Assert.Equal(2, field.DataRepresentation.SpatialDifferencingOrder);
        Assert.Equal(GribMissingValueManagement.None, field.DataRepresentation.MissingValueManagement);
        Assert.False(field.HasBitmap);
        Assert.Equal(new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc), field.ReferenceTime);
        Assert.Equal(new DateTime(2026, 10, 25, 3, 0, 0, DateTimeKind.Utc), field.ValidTime);
        Assert.Equal(new GribFixedSurface(103, 10), field.Product.FirstSurface);
        Assert.Equal(new WeatherNativeGrid(181, 360, 90, 0, -1, 1), field.ToWeatherNativeGrid());
        Assert.Equal(WeatherParameters.WindU, field.FindWeatherParameter()?.Id);
    }

    [Fact]
    public void Wind_fixture_has_three_forecast_hours_of_u_and_v()
    {
        using var grib = GribFile.Open(TestData.Path("gfs-useast-wind10m.grib2"));

        Assert.Equal(6, grib.Fields.Count);
        Assert.Equal(
            [WeatherParameters.WindU, WeatherParameters.WindV, WeatherParameters.WindU, WeatherParameters.WindV, WeatherParameters.WindU, WeatherParameters.WindV],
            grib.Fields.Select(f => f.FindWeatherParameter()?.Id));
        Assert.Equal([0, 0, 3, 3, 6, 6], grib.Fields.Select(f => (int)(f.ValidTime!.Value - f.ReferenceTime).TotalHours));
        Assert.Equal([0, 1, 2, 3, 4, 5], grib.Fields.Select(f => f.MessageIndex));
        Assert.All(grib.Fields, f => Assert.Equal(new WeatherNativeGrid(33, 33, 40, 284, -0.25, 0.25), f.ToWeatherNativeGrid()));
    }

    [Fact]
    public void Template_fixture_maps_each_parameter()
    {
        using var grib = GribFile.Open(TestData.Path("gfs-useast-templates.grib2"));

        Assert.Equal([0, 2, 41, 3], grib.Fields.Select(f => f.DataRepresentation.TemplateNumber));
        Assert.Equal(WeatherParameters.WindGust, grib.Fields[0].FindWeatherParameter()?.Id);
        Assert.Equal(WeatherParameters.PressureMsl, grib.Fields[1].FindWeatherParameter()?.Id);
        Assert.Null(grib.Fields[2].FindWeatherParameter());

        // TMAX 2 m, "0-3 hour max": template 4.8, valid at the end of the interval.
        var tmax = grib.Fields[2];
        Assert.Equal(8, tmax.Product.TemplateNumber);
        Assert.Equal(new GribTimeRange(GribTimeUnit.Hour, 0), tmax.Product.ForecastTime);
        var process = Assert.Single(tmax.Product.StatisticalProcesses);
        Assert.Equal(GribStatisticalProcess.Maximum, process.Type);
        Assert.Equal(new GribTimeRange(GribTimeUnit.Hour, 3), process.Length);
        Assert.Equal(new DateTime(2026, 10, 25, 3, 0, 0, DateTimeKind.Utc), tmax.ValidTime);
        Assert.Equal(1, grib.Fields[3].DataRepresentation.SpatialDifferencingOrder);
    }

    [Fact]
    public void Wave_fixture_maps_to_wave_parameters_on_the_surface()
    {
        using var grib = GribFile.Open(TestData.Path("gfswave-useast.grib2"));

        Assert.Equal(9, grib.Fields.Count);
        Assert.All(grib.Fields, f => Assert.True(f.HasBitmap));
        Assert.All(grib.Fields, f => Assert.Equal(WeatherLevel.Surface, f.ToWeatherLevel()));
        Assert.Equal(
            [WeatherParameters.WaveHeight, WeatherParameters.WavePeriod, WeatherParameters.WaveDirection],
            grib.Fields.Take(3).Select(f => f.FindWeatherParameter()?.Id));
    }

    [Fact]
    public void Fixtures_build_a_weather_grid_set_with_lazy_steps()
    {
        using var grib = GribFile.Open(TestData.Path("gfs-useast-wind10m.grib2"));
        var decodes = 0;
        var parameters = grib.Fields
            .GroupBy(f => f.FindWeatherParameter()!)
            .Select(g => new WeatherParameter(
                g.Key,
                g.First().ToWeatherLevel(),
                [.. g.Select(f =>
                {
                    var decoder = grib.CreateStepDecoder(f);
                    return new WeatherTimeStep(f.ValidTime!.Value, ct =>
                    {
                        decodes++;
                        return decoder(ct);
                    });
                })]))
            .ToList();
        var first = grib.Fields[0];
        var set = new WeatherGridSet(
            new WeatherOrigin("GFS", first.ReferenceTime, first.Identification.CentreName),
            first.ToWeatherNativeGrid(),
            parameters);

        Assert.Equal(0, decodes);
        Assert.Equal(32, set.Grid.OriginLatitude);
        Assert.Equal(-76, set.Grid.OriginLongitude);

        var u = set.FindParameter(WeatherParameters.WindU)!;
        var step = set.ReadStep(u, 0, TestContext.Current.CancellationToken);
        Assert.Equal(1, decodes);

        // The normalised grid's first point is the native last row's first point.
        var native = grib.ReadValues(grib.Fields[0], TestContext.Current.CancellationToken);
        Assert.Equal(native[32 * 33], step[0]);
    }

    private static void AssertClose(double expected, float actual)
    {
        if (double.IsNaN(expected))
        {
            Assert.True(float.IsNaN(actual), $"Expected NaN, got {actual}.");
            return;
        }

        // ecCodes computes in double; the decoder rounds to float.
        Assert.Equal((float)expected, actual, Math.Max(1e-6f, Math.Abs((float)expected) * 2e-7f));
    }
}
