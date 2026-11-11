using EncDotNet.S100.Datasets.Grib.Tests.Encoding;

namespace EncDotNet.S100.Datasets.Grib.Tests;

public class FramingTests
{
    private static TestField Field(double offset, int forecastHour = 3, TestGrid? grid = null, TestBitmap bitmap = TestBitmap.None)
    {
        var g = grid ?? TestGrid.Small();
        var values = Enumerable.Range(0, g.Ni * g.Nj).Select(i => offset + i).ToArray();
        if (bitmap != TestBitmap.None)
            values[4] = values[9] = double.NaN;
        return new TestField { Grid = g, ForecastTime = forecastHour, Values = values, Bitmap = bitmap };
    }

    [Fact]
    public void Concatenated_messages_are_indexed_in_order_with_offsets()
    {
        var m0 = GribTestEncoder.Message(Field(0, 0));
        var m1 = GribTestEncoder.Message(Field(100, 3) with { Packing = new ComplexTestPacking(SpatialDifferencingOrder: 2) });
        var m2 = GribTestEncoder.Message(Field(200, 6) with { Packing = new PngTestPacking() });
        using var grib = GribFile.Open(GribTestEncoder.File(m0, m1, m2));

        Assert.Equal(3, grib.Fields.Count);
        Assert.Equal([0L, m0.Length, m0.Length + m1.Length], grib.Fields.Select(f => f.MessageOffset));
        Assert.Equal([(long)m0.Length, m1.Length, m2.Length], grib.Fields.Select(f => f.MessageLength));
        Assert.Equal([0, 1, 2], grib.Fields.Select(f => f.MessageIndex));
        Assert.Equal([0, 1, 2], grib.Fields.Select(f => f.Index));
        Assert.Equal([0, 3, 6], grib.Fields.Select(f => f.Product.ForecastTime!.Value.Value));
        Assert.Equal(200f, grib.ReadValues(grib.Fields[2], TestContext.Current.CancellationToken)[0]);
        Assert.Equal(134f, grib.ReadValues(grib.Fields[1], TestContext.Current.CancellationToken)[^1]);
    }

    [Fact]
    public void Bytes_between_messages_are_skipped()
    {
        var m = GribTestEncoder.Message(Field(0));
        byte[] junk = [.. "\r\r\nSOME WMO HEADER\r\r\n"u8, 0, 0, 0];
        using var grib = GribFile.Open(GribTestEncoder.File(junk, m, junk, m, junk));

        Assert.Equal(2, grib.Fields.Count);
        Assert.Equal(junk.Length, grib.Fields[0].MessageOffset);
        Assert.Equal((2 * junk.Length) + m.Length, grib.Fields[1].MessageOffset);
    }

    [Fact]
    public void Repeated_product_and_data_sections_give_one_field_each()
    {
        // Sections 4 to 7 repeated over one grid, then 3 to 7 with a new grid.
        var other = TestGrid.Small(ni: 4, nj: 3);
        var message = GribTestEncoder.Message([Field(0, 0), Field(50, 3), Field(500, 6, other)]);
        using var grib = GribFile.Open(message);

        Assert.Equal(3, grib.Fields.Count);
        Assert.All(grib.Fields, f => Assert.Equal(0, f.MessageIndex));
        Assert.Equal([0, 1, 2], grib.Fields.Select(f => f.FieldNumber));
        Assert.Equal([35L, 35, 12], grib.Fields.Select(f => f.Grid.NumberOfPoints));
        Assert.Same(grib.Fields[0].Grid, grib.Fields[1].Grid);
        Assert.Equal(50f, grib.ReadValues(grib.Fields[1], TestContext.Current.CancellationToken)[0]);
        Assert.Equal(511f, grib.ReadValues(grib.Fields[2], TestContext.Current.CancellationToken)[^1]);
    }

    [Fact]
    public void Repeated_fields_can_reuse_the_previous_bitmap()
    {
        var message = GribTestEncoder.Message([
            Field(0, 0, bitmap: TestBitmap.Explicit),
            Field(10, 3, bitmap: TestBitmap.ReusePrevious) with { Packing = new ComplexTestPacking() },
        ]);
        using var grib = GribFile.Open(message);

        var second = grib.ReadValues(grib.Fields[1], TestContext.Current.CancellationToken);
        Assert.True(grib.Fields[1].HasBitmap);
        Assert.True(float.IsNaN(second[4]));
        Assert.True(float.IsNaN(second[9]));
        Assert.Equal(10f, second[0]);
        Assert.Equal(44f, second[^1]);
    }

    [Fact]
    public void Local_use_section_is_skipped()
    {
        var message = GribTestEncoder.Message([Field(7)], localUse: [1, 2, 3, 4, 5, 6, 7, 8, 9]);
        using var grib = GribFile.Open(message);

        Assert.Equal(7f, grib.ReadValues(Assert.Single(grib.Fields), TestContext.Current.CancellationToken)[0]);
    }

    [Fact]
    public void Stream_and_file_sources_read_the_same_values()
    {
        var bytes = GribTestEncoder.File(GribTestEncoder.Message(Field(0)), GribTestEncoder.Message(Field(1000)));
        var path = Path.Combine(Path.GetTempPath(), $"grib-{Guid.NewGuid():N}.grib2");
        File.WriteAllBytes(path, bytes);
        try
        {
            using var fromFile = GribFile.Open(path);
            using var fromStream = GribFile.Open(new MemoryStream(bytes));
            using var fromMemory = GribFile.Open(bytes);
            var ct = TestContext.Current.CancellationToken;
            Assert.Equal(fromMemory.ReadValues(fromMemory.Fields[1], ct), fromFile.ReadValues(fromFile.Fields[1], ct));
            Assert.Equal(fromMemory.ReadValues(fromMemory.Fields[1], ct), fromStream.ReadValues(fromStream.Fields[1], ct));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Fields_can_be_decoded_in_parallel()
    {
        var messages = Enumerable.Range(0, 16).Select(i => GribTestEncoder.Message(Field(i * 100, i) with { Packing = new ComplexTestPacking(SpatialDifferencingOrder: 1) })).ToArray();
        using var grib = GribFile.Open(new MemoryStream(GribTestEncoder.File(messages)));
        var ct = TestContext.Current.CancellationToken;

        var results = await Task.WhenAll(grib.Fields.Select(f => Task.Run(() => grib.ReadValues(f, ct), ct)));

        for (var i = 0; i < results.Length; i++)
            Assert.Equal(i * 100f, results[i][0]);
    }

    [Fact]
    public void Decoder_is_lazy_and_repeatable()
    {
        using var grib = GribFile.Open(GribTestEncoder.Message(Field(3)));
        var decoder = grib.CreateStepDecoder(grib.Fields[0]);
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(decoder(ct), decoder(ct));
        Assert.Equal(3f, decoder(ct)[0]);
    }

    [Fact]
    public void Decoding_after_dispose_throws()
    {
        var grib = GribFile.Open(GribTestEncoder.Message(Field(3)));
        var decoder = grib.CreateStepDecoder(grib.Fields[0]);
        grib.Dispose();

        Assert.Throws<ObjectDisposedException>(() => decoder(CancellationToken.None));
    }

    [Fact]
    public void Field_from_another_file_is_rejected()
    {
        using var a = GribFile.Open(GribTestEncoder.Message(Field(3)));
        using var b = GribFile.Open(GribTestEncoder.Message(Field(3)));

        Assert.Throws<ArgumentException>(() => a.ReadValues(b.Fields[0], TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Cancelled_decode_throws()
    {
        using var grib = GribFile.Open(GribTestEncoder.Message(Field(3)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => grib.ReadValues(grib.Fields[0], cts.Token));
    }
}
