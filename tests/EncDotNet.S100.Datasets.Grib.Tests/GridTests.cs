using EncDotNet.S100.Datasets.Grib.Tests.Encoding;
using EncDotNet.S100.Datasets.Weather;

namespace EncDotNet.S100.Datasets.Grib.Tests;

public class GridTests
{
    public static TheoryData<byte, double, double, double, double, double, double, float[]> ScanningModes => new()
    {
        // mode, La1, Lo1, La2, Lo2, expected latitude and longitude increments, expected row-major values.
        // The six file values are 0 to 5 in file order; the grid is 3 columns × 2 rows.
        { 0x00, 50, 300, 49.5, 301, -0.5, 0.5, [0, 1, 2, 3, 4, 5] },
        { 0x40, 49.5, 300, 50, 301, 0.5, 0.5, [0, 1, 2, 3, 4, 5] },
        { 0x80, 50, 301, 49.5, 300, -0.5, -0.5, [0, 1, 2, 3, 4, 5] },
        { 0xC0, 49.5, 301, 50, 300, 0.5, -0.5, [0, 1, 2, 3, 4, 5] },
        { 0x20, 50, 300, 49.5, 301, -0.5, 0.5, [0, 2, 4, 1, 3, 5] },
        { 0x10, 50, 300, 49.5, 301, -0.5, 0.5, [0, 1, 2, 5, 4, 3] },
        { 0x30, 50, 300, 49.5, 301, -0.5, 0.5, [0, 3, 4, 1, 2, 5] },
        { 0x60, 49.5, 300, 50, 301, 0.5, 0.5, [0, 2, 4, 1, 3, 5] },
    };

    [Theory]
    [MemberData(nameof(ScanningModes))]
    public void Scanning_mode_sets_increment_signs_and_value_order(byte mode, double la1, double lo1, double la2, double lo2, double dLat, double dLon, float[] expected)
    {
        var grid = new TestGrid(3, 2, la1, lo1, la2, lo2, 0.5, 0.5, mode);
        using var grib = GribFile.Open(GribTestEncoder.Message(new TestField { Grid = grid, Values = [0, 1, 2, 3, 4, 5] }));
        var field = grib.Fields[0];

        Assert.Equal((GribScanningMode)mode, field.Grid.LatLon!.ScanningMode);
        Assert.Equal(new WeatherNativeGrid(2, 3, la1, lo1, dLat, dLon), field.ToWeatherNativeGrid());
        Assert.Equal(expected, grib.ReadValues(field, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Negative_coordinates_are_sign_and_magnitude()
    {
        var grid = new TestGrid(3, 2, -10.25, -5.5, -10.75, -4.5, 0.5, 0.5);
        using var grib = GribFile.Open(GribTestEncoder.Message(new TestField { Grid = grid, Values = [0, 1, 2, 3, 4, 5] }));
        var latLon = grib.Fields[0].Grid.LatLon!;

        Assert.Equal(-10.25, latLon.FirstLatitude, 1e-9);
        Assert.Equal(-5.5, latLon.FirstLongitude, 1e-9);
        Assert.Equal(-10.75, latLon.LastLatitude, 1e-9);
        Assert.Equal(-4.5, latLon.LastLongitude, 1e-9);
    }

    [Fact]
    public void Missing_increments_are_derived_from_the_corners()
    {
        // Five columns from 350°E across 0° to 10°E, and three rows from 20°N to 10°N.
        var grid = new TestGrid(5, 3, 20, 350, 10, 10, 0, 0, IncrementsGiven: false);
        using var grib = GribFile.Open(GribTestEncoder.Message(new TestField { Grid = grid, Values = new double[15] }));
        var latLon = grib.Fields[0].Grid.LatLon!;

        Assert.Equal(5, latLon.IIncrement, 1e-9);
        Assert.Equal(5, latLon.JIncrement, 1e-9);
    }

    [Fact]
    public void Staggered_scanning_is_unsupported()
    {
        var grid = TestGrid.Small() with { ScanningMode = 0x08 };
        using var grib = GribFile.Open(GribTestEncoder.Message(new TestField { Grid = grid, Values = new double[35] }));
        var field = grib.Fields[0];

        Assert.Contains("staggered", field.UnsupportedReason);
        Assert.Throws<UnsupportedGribException>(() => grib.ReadValues(field, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(30, "3.30 (Lambert conformal)")]
    [InlineData(20, "3.20 (polar stereographic)")]
    [InlineData(40, "3.40 (Gaussian latitude/longitude)")]
    [InlineData(1, "3.1 (rotated latitude/longitude)")]
    public void Other_grid_templates_are_listed_but_not_decoded(int template, string named)
    {
        var grid = TestGrid.Small() with { TemplateNumber = template };
        using var grib = GribFile.Open(GribTestEncoder.Message(new TestField { Grid = grid, Values = new double[35] }));
        var field = grib.Fields[0];

        Assert.Null(field.Grid.LatLon);
        Assert.Equal(template, field.Grid.TemplateNumber);
        Assert.Contains(named, field.UnsupportedReason);
        var ex = Assert.Throws<UnsupportedWeatherGridException>(() => field.ToWeatherNativeGrid());
        Assert.Contains(named, ex.Message);
        Assert.Contains(named, Assert.Throws<UnsupportedGribException>(() => grib.ReadValues(field, TestContext.Current.CancellationToken)).Message);
    }
}
