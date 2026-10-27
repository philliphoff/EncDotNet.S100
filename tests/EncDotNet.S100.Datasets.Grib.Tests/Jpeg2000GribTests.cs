using EncDotNet.S100.Datasets.Grib.Tests.Encoding;
using EncDotNet.S100.Datasets.Weather;

namespace EncDotNet.S100.Datasets.Grib.Tests;

/// <summary>
/// Template 5.40 (JPEG 2000) through <see cref="GribFile"/>: GFS-Wave
/// messages exactly as NCEP serves them on AWS (codestreams written by
/// JasPer 2.0.25), and GFS and GFS-Wave crops repacked by ecCodes (which
/// encodes with OpenJPEG 2.5.2). The reference values are ecCodes 2.50.0's
/// (the <c>eccodes</c> and <c>eccodeslib</c> 2.50.0 Python wheels,
/// <c>codes_get_values</c>), printed by tools/grib-fixtures/make_grib_fixtures.py.
/// </summary>
/// <remarks>
/// Besides five points, the count present and the minimum, maximum and
/// mean, each case checks a checksum over every point, weighted by
/// <c>index % 13 + 1</c> so the order counts. Every message of the
/// 2026-10-25 00Z f003 West Coast, Atlantic, East Pacific, South Global and
/// global 0.25° files (91 in all) was also compared with ecCodes point by
/// point while developing the decoder; only these are committed.
/// </remarks>
public class Jpeg2000GribTests
{
    public static TheoryData<string, int, double[], int, double, double, double, double> Expected => new()
    {
        // GFS-Wave West Coast 0.16°, f003, as NCEP serves it: HTSGW, PERPW, DIRPW, with the land bitmap.
        { "gfswave-wcoast-f003-jpeg2000.grib2", 0, [double.NaN, double.NaN, 1.79, double.NaN, double.NaN], 11041, 0.34, 5.8100000000000005, 2.054481478126981, 158744.86 },
        { "gfswave-wcoast-f003-jpeg2000.grib2", 1, [double.NaN, double.NaN, 7.8500000000000005, double.NaN, double.NaN], 11041, 3.71, 15.68, 9.70824744135495, 750046.36 },
        { "gfswave-wcoast-f003-jpeg2000.grib2", 2, [double.NaN, double.NaN, 17.55, double.NaN, double.NaN], 11041, 0.03, 359.98, 261.25214926184225, 20213876.560000002 },

        // GFS-Wave Atlantic 0.16°, HTSGW f003, as NCEP serves it.
        { "gfswave-atlocn-htsgw-f003-jpeg2000.grib2", 0, [double.NaN, double.NaN, 2.23, double.NaN, double.NaN], 29419, 0.01, 4.4, 1.6145188483633026, 332673.46 },

        // Cropped and repacked by ecCodes: 10 m U lossless (a 33 × 33 image), PRMSL lossy (5/3 cut short
        // by rate control), wave height with the bitmap (a one-row image with no decomposition levels).
        { "gfs-useast-jpeg2000.grib2", 0, [0.0392041015625, 0.6092041015625, -9.3807958984375, -13.1307958984375, -9.5207958984375], 1089, -14.5307958984375, 2.1892041015625, -7.921998836913167, -60331.9651953125 },
        { "gfs-useast-jpeg2000.grib2", 1, [103439.734375, 103435.609375, 103018.609375, 102115.109375, 103021.359375], 1089, 102068.484375, 103439.734375, 102917.35225837925, 782990540.875 },
        { "gfs-useast-jpeg2000.grib2", 2, [double.NaN, double.NaN, 2.5, 3.62, 2.54], 1025, 0.07, 3.94, 2.5839804878048778, 18483.39 },

        // A constant field: 0 bits and an empty Section 7.
        { "gfs-useast-jpeg2000.grib2", 3, [5.5, 5.5, 5.5, 5.5, 5.5], 1089, 5.5, 5.5, 5.5, 41844.0 },
    };

    [Theory]
    [MemberData(nameof(Expected))]
    public void Values_match_ecCodes(string file, int fieldIndex, double[] points, int present, double min, double max, double mean, double weighted)
    {
        using var grib = GribFile.Open(TestData.Path(file));
        var field = grib.Fields[fieldIndex];
        Assert.True(field.IsSupported, field.UnsupportedReason);
        Assert.Equal(40, field.DataRepresentation.TemplateNumber);

        var values = grib.ReadValues(field, TestContext.Current.CancellationToken);

        Assert.Equal(field.Grid.NumberOfPoints, values.Length);
        int[] indices = [0, 1, values.Length / 2, values.Length - 1, 566];
        for (var k = 0; k < indices.Length; k++)
            AssertClose(points[k], values[indices[k]]);

        var valid = values.Where(v => !float.IsNaN(v)).Select(v => (double)v).ToArray();
        Assert.Equal(present, valid.Length);
        AssertClose(min, (float)valid.Min());
        AssertClose(max, (float)valid.Max());
        Assert.Equal(mean, valid.Average(), Math.Max(1e-6, Math.Abs(mean) * 1e-6));

        double sum = 0, scale = 0;
        for (var i = 0; i < values.Length; i++)
        {
            if (float.IsNaN(values[i]))
                continue;
            sum += values[i] * ((i % 13) + 1);
            scale += Math.Abs(values[i]) * ((i % 13) + 1);
        }

        Assert.Equal(weighted, sum, Math.Max(1e-6, scale * 2e-7));
    }

    [Fact]
    public void Ncep_messages_carry_the_template_5_40_settings()
    {
        using var grib = GribFile.Open(TestData.Path("gfswave-wcoast-f003-jpeg2000.grib2"));

        Assert.Equal(3, grib.Fields.Count);
        Assert.All(grib.Fields, f => Assert.True(f.HasBitmap));
        Assert.Equal([10, 11, 16], grib.Fields.Select(f => f.DataRepresentation.BitsPerValue));
        Assert.All(grib.Fields, f => Assert.Equal(0, f.DataRepresentation.CompressionType));
        Assert.All(grib.Fields, f => Assert.Null(f.DataRepresentation.TargetCompressionRatio));
        Assert.Equal(
            [WeatherParameters.WaveHeight, WeatherParameters.WavePeriod, WeatherParameters.WaveDirection],
            grib.Fields.Select(f => f.FindWeatherParameter()?.Id));
        Assert.Equal(new WeatherNativeGrid(151, 241, 50, 210, -1 / 6.0, 1 / 6.0), grib.Fields[0].ToWeatherNativeGrid(), new NativeGridComparer());
    }

    [Fact]
    public void Lossy_message_reports_its_target_ratio()
    {
        using var grib = GribFile.Open(TestData.Path("gfs-useast-jpeg2000.grib2"));

        Assert.Equal(1, grib.Fields[1].DataRepresentation.CompressionType);
        Assert.Equal(10, grib.Fields[1].DataRepresentation.TargetCompressionRatio);
        Assert.Equal(0, grib.Fields[3].DataRepresentation.BitsPerValue);
        Assert.Equal(0, grib.Fields[3].DataLength);
    }

    [Fact]
    public void Codestream_in_a_test_message_decodes_with_the_scaling()
    {
        // An OpenJPEG codestream of 37 × 19 12-bit samples, wrapped as template 5.40
        // with R = 1.5, E = -1, D = 1: Y = (1.5 + X / 2) / 10.
        var codestream = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "jpeg2000", "depth-12.j2k"));
        var raw = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "jpeg2000", "depth-12.raw"));
        var field = new TestField
        {
            Grid = TestGrid.Small(37, 19),
            Values = new double[37 * 19],
            Packing = new RawTestPacking(40, Template540(1.5f, -1, 1, 12, compression: 0), codestream, 37 * 19),
        };
        using var grib = GribFile.Open(GribTestEncoder.Message(field));

        var values = grib.ReadValues(grib.Fields[0], TestContext.Current.CancellationToken);

        for (var i = 0; i < values.Length; i++)
        {
            var x = BitConverter.ToUInt16(raw, 2 * i);
            Assert.Equal((float)((1.5 + (x * 0.5)) / 10), values[i]);
        }
    }

    [Fact]
    public void Zero_bits_is_a_constant_field_of_the_unscaled_reference_value()
    {
        // As ecCodes reads it: D is left over from the field before it was constant.
        var field = new TestField
        {
            Values = new double[35],
            Packing = new RawTestPacking(40, Template540(273.25f, -2, 1, 0, compression: 0), [], 35),
        };
        using var grib = GribFile.Open(GribTestEncoder.Message(field));

        Assert.All(grib.ReadValues(grib.Fields[0], TestContext.Current.CancellationToken), v => Assert.Equal(273.25f, v));
    }

    [Fact]
    public void Too_few_samples_fail_naming_both_counts()
    {
        var codestream = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "jpeg2000", "size-3x5.j2k"));
        var field = new TestField
        {
            Values = new double[35],
            Packing = new RawTestPacking(40, Template540(0, 0, 0, 8, compression: 0), codestream, 35),
        };
        using var grib = GribFile.Open(GribTestEncoder.Message(field));

        var ex = Assert.Throws<GribFormatException>(() => grib.ReadValues(grib.Fields[0], TestContext.Current.CancellationToken));
        Assert.Contains("3 × 5 samples, fewer than the 35 values", ex.Message);
    }

    [Fact]
    public void Corrupt_codestream_fails_as_a_format_error()
    {
        var field = new TestField
        {
            Values = new double[35],
            Packing = new RawTestPacking(40, Template540(0, 0, 0, 8, compression: 0), [1, 2, 3, 4], 35),
        };
        using var grib = GribFile.Open(GribTestEncoder.Message(field));

        var ex = Assert.Throws<GribFormatException>(() => grib.ReadValues(grib.Fields[0], TestContext.Current.CancellationToken));
        Assert.Contains("isn't a JPEG 2000 codestream", ex.Message);
    }

    // Template 5.40 from octet 12: R, E and D (sign and magnitude), bits, type, compression, ratio.
    private static byte[] Template540(float reference, int binaryScale, int decimalScale, int bits, byte compression)
    {
        var r = BitConverter.GetBytes(reference);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(r);
        return [.. r, .. SignMagnitude(binaryScale), .. SignMagnitude(decimalScale), (byte)bits, 0, compression, 255];
    }

    private static byte[] SignMagnitude(int v) => [(byte)((v < 0 ? 0x80 : 0) | (Math.Abs(v) >> 8)), (byte)(Math.Abs(v) & 0xFF)];

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

    private sealed class NativeGridComparer : IEqualityComparer<WeatherNativeGrid>
    {
        public bool Equals(WeatherNativeGrid? x, WeatherNativeGrid? y) =>
            x is not null && y is not null && x.Rows == y.Rows && x.Columns == y.Columns
            && Math.Abs(x.FirstLatitude - y.FirstLatitude) < 1e-6 && Math.Abs(x.FirstLongitude - y.FirstLongitude) < 1e-6
            && Math.Abs(x.LatitudeIncrement - y.LatitudeIncrement) < 1e-5 && Math.Abs(x.LongitudeIncrement - y.LongitudeIncrement) < 1e-5;

        public int GetHashCode(WeatherNativeGrid obj) => 0;
    }
}
