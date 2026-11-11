using EncDotNet.S100.Datasets.Grib.Tests.Encoding;

namespace EncDotNet.S100.Datasets.Grib.Tests;

/// <summary>
/// Byte-exact round trips through the test encoder for each supported data
/// template: encode, decode, re-encode the decoded values with the same
/// packing and reference value, and compare the bytes. The decoded values
/// are also checked against the inputs, within the packing's precision.
/// </summary>
public class RoundTripTests
{
    public static TheoryData<string, TestPacking, TestBitmap> Packings => new()
    {
        // 5.0, simple packing.
        { "5.0 integers", new SimpleTestPacking(), TestBitmap.None },
        { "5.0 D=2", new SimpleTestPacking(DecimalScale: 2), TestBitmap.None },
        { "5.0 D=-1", new SimpleTestPacking(DecimalScale: -1), TestBitmap.None },
        { "5.0 E=-3", new SimpleTestPacking(BinaryScale: -3), TestBitmap.None },
        { "5.0 E=2", new SimpleTestPacking(BinaryScale: 2), TestBitmap.None },
        { "5.0 fixed 13 bits", new SimpleTestPacking(DecimalScale: 1, Bits: 13), TestBitmap.None },
        { "5.0 32 bits", new SimpleTestPacking(Bits: 32), TestBitmap.None },
        { "5.0 bitmap", new SimpleTestPacking(DecimalScale: 2), TestBitmap.Explicit },

        // 5.2, complex packing.
        { "5.2 groups of 1", new ComplexTestPacking(GroupSize: 1), TestBitmap.None },
        { "5.2 groups of 7", new ComplexTestPacking(DecimalScale: 2, GroupSize: 7), TestBitmap.None },
        { "5.2 one group", new ComplexTestPacking(DecimalScale: 1, GroupSize: 1000), TestBitmap.None },
        { "5.2 E=-2", new ComplexTestPacking(BinaryScale: -2, GroupSize: 5), TestBitmap.None },
        { "5.2 bitmap", new ComplexTestPacking(DecimalScale: 2, GroupSize: 6), TestBitmap.Explicit },
        { "5.2 missing 1", new ComplexTestPacking(DecimalScale: 2, GroupSize: 6, MissingValueManagement: 1), TestBitmap.None },
        { "5.2 missing 2", new ComplexTestPacking(DecimalScale: 2, GroupSize: 4, MissingValueManagement: 2), TestBitmap.None },

        // 5.3, complex packing with spatial differencing.
        { "5.3 order 1", new ComplexTestPacking(DecimalScale: 2, GroupSize: 7, SpatialDifferencingOrder: 1), TestBitmap.None },
        { "5.3 order 2", new ComplexTestPacking(DecimalScale: 2, GroupSize: 7, SpatialDifferencingOrder: 2), TestBitmap.None },
        { "5.3 order 2 one group", new ComplexTestPacking(DecimalScale: 3, GroupSize: 1000, SpatialDifferencingOrder: 2), TestBitmap.None },
        { "5.3 order 1 bitmap", new ComplexTestPacking(DecimalScale: 2, GroupSize: 5, SpatialDifferencingOrder: 1), TestBitmap.Explicit },
        { "5.3 order 2 bitmap", new ComplexTestPacking(DecimalScale: 2, GroupSize: 5, SpatialDifferencingOrder: 2), TestBitmap.Explicit },
        { "5.3 order 1 missing 1", new ComplexTestPacking(DecimalScale: 2, GroupSize: 6, MissingValueManagement: 1, SpatialDifferencingOrder: 1), TestBitmap.None },
        { "5.3 order 2 missing 1", new ComplexTestPacking(DecimalScale: 2, GroupSize: 6, MissingValueManagement: 1, SpatialDifferencingOrder: 2), TestBitmap.None },
        { "5.3 order 1 missing 2", new ComplexTestPacking(DecimalScale: 2, GroupSize: 4, MissingValueManagement: 2, SpatialDifferencingOrder: 1), TestBitmap.None },
        { "5.3 order 2 missing 2", new ComplexTestPacking(DecimalScale: 2, GroupSize: 4, MissingValueManagement: 2, SpatialDifferencingOrder: 2), TestBitmap.None },

        // 5.41, PNG.
        { "5.41 8 bits", new PngTestPacking(Depth: 8), TestBitmap.None },
        { "5.41 16 bits", new PngTestPacking(DecimalScale: 2, Depth: 16), TestBitmap.None },
        { "5.41 24 bits", new PngTestPacking(DecimalScale: 4, Depth: 24), TestBitmap.None },
        { "5.41 32 bits", new PngTestPacking(DecimalScale: 4, Depth: 32), TestBitmap.None },
        { "5.41 4 bits", new PngTestPacking(BinaryScale: 4, Depth: 4), TestBitmap.None },
        { "5.41 2 bits", new PngTestPacking(BinaryScale: 5, Depth: 2), TestBitmap.None },
        { "5.41 1 bit", new PngTestPacking(BinaryScale: 6, Depth: 1), TestBitmap.None },
        { "5.41 bitmap", new PngTestPacking(DecimalScale: 2, Depth: 16), TestBitmap.Explicit },
    };

    [Theory]
    [MemberData(nameof(Packings))]
    public void Round_trip_is_byte_exact(string name, TestPacking packing, TestBitmap bitmap)
    {
        _ = name;
        var grid = TestGrid.Small(ni: 9, nj: 6);
        var values = SampleValues(grid.Ni * grid.Nj, packing, bitmap);
        var field = new TestField { Grid = grid, Packing = packing, Bitmap = bitmap, Values = values };

        var bytes = GribTestEncoder.Message(field);
        using var grib = GribFile.Open(bytes);
        var decodedField = Assert.Single(grib.Fields);
        var decoded = grib.ReadValues(decodedField, TestContext.Current.CancellationToken);

        // Within the packing's precision, and missing where the input was.
        var step = Math.Pow(2, packing.BinaryScale) * Math.Pow(10, -packing.DecimalScale);
        for (var i = 0; i < values.Length; i++)
        {
            if (double.IsNaN(values[i]) || double.IsNegativeInfinity(values[i]))
                Assert.True(float.IsNaN(decoded[i]), $"Point {i} should be missing.");
            else
                Assert.Equal(values[i], decoded[i], (step / 2) + (Math.Abs(values[i]) * 1e-6));
        }

        // Re-encode what was decoded, with the decoded reference value.
        var again = decoded.Select((v, i) => float.IsNaN(v) ? values[i] : v).ToArray();
        var reencoded = GribTestEncoder.Message(field with
        {
            Values = again,
            Packing = packing with { Reference = decodedField.DataRepresentation.ReferenceValue },
        });
        Assert.Equal(bytes, reencoded);
    }

    [Fact]
    public void Constant_field_with_zero_bits_decodes_to_the_reference_value()
    {
        foreach (TestPacking packing in new TestPacking[] { new SimpleTestPacking(DecimalScale: 1), new PngTestPacking(DecimalScale: 1, Depth: 0) })
        {
            var field = new TestField { Packing = packing, Values = [.. Enumerable.Repeat(12.5, 35)] };
            using var grib = GribFile.Open(GribTestEncoder.Message(field));
            var values = grib.ReadValues(grib.Fields[0], TestContext.Current.CancellationToken);
            Assert.Equal(0, grib.Fields[0].DataRepresentation.BitsPerValue);
            Assert.All(values, v => Assert.Equal(12.5f, v));
        }
    }

    [Fact]
    public void Complex_packing_reports_its_descriptors()
    {
        var field = new TestField
        {
            Packing = new ComplexTestPacking(DecimalScale: 2, GroupSize: 4, MissingValueManagement: 2, SpatialDifferencingOrder: 2),
            Values = SampleValues(35, new ComplexTestPacking(MissingValueManagement: 2), TestBitmap.None),
        };
        using var grib = GribFile.Open(GribTestEncoder.Message(field));
        var drs = grib.Fields[0].DataRepresentation;

        Assert.Equal(3, drs.TemplateNumber);
        Assert.Equal(2, drs.SpatialDifferencingOrder);
        Assert.Equal(GribMissingValueManagement.PrimaryAndSecondary, drs.MissingValueManagement);
        Assert.Equal(9, drs.NumberOfGroups);
        Assert.Equal(35, drs.NumberOfPackedValues);
    }

    /// <summary>
    /// A smooth field with some sharp jumps (so complex packing gets groups
    /// of different widths) and, where the packing allows, missing points:
    /// a run of NaN that fills whole groups, scattered NaN, and for missing
    /// value management 2 some secondary missing values.
    /// </summary>
    private static double[] SampleValues(int count, TestPacking packing, TestBitmap bitmap)
    {
        var values = new double[count];
        for (var i = 0; i < count; i++)
        {
            var smooth = 5 + (3 * Math.Sin(i * 0.37)) + (i * 0.05);
            values[i] = i % 11 == 0 ? smooth + 20 : smooth;
        }

        switch (packing)
        {
            case SimpleTestPacking { Bits: 32 }:
                // Multiples of 256 below 2^32, which a float holds exactly.
                for (var i = 0; i < count; i++)
                    values[i] = Math.Floor(i * 80_000_000.0 % 4_294_967_040.0 / 256) * 256;
                values[1] = 4_294_967_040.0;
                break;
            case PngTestPacking { Depth: <= 4 } png:
                var levels = (1 << png.Depth) - 1;
                for (var i = 0; i < count; i++)
                    values[i] = (i * 7 % (levels + 1)) * Math.Pow(2, png.BinaryScale);
                break;
            case PngTestPacking { Depth: 8 }:
                for (var i = 0; i < count; i++)
                    values[i] = i * 13 % 256;
                break;
        }

        var mvm = packing is ComplexTestPacking c ? c.MissingValueManagement : 0;
        if (bitmap == TestBitmap.Explicit || mvm > 0)
        {
            foreach (var i in new[] { 2, 3, 17, 30, 31, 32, 33, 34, 35, count - 1 })
            {
                if (i < count)
                    values[i] = double.NaN;
            }

            if (mvm == 2)
            {
                foreach (var i in new[] { 8, 9, 10, 11, 21 })
                {
                    if (i < count)
                        values[i] = double.NegativeInfinity;
                }
            }
        }

        return values;
    }
}
