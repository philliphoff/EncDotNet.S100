using EncDotNet.S100.Datasets.Grib.Tests.Encoding;

namespace EncDotNet.S100.Datasets.Grib.Tests;

public class ErrorTests
{
    private static TestField Field() => new() { Values = [.. Enumerable.Range(0, 35).Select(i => (double)i)] };

    [Fact]
    public void Grib_edition_1_fails_naming_the_edition()
    {
        // A GRIB1 indicator section: "GRIB", a 3-octet length, edition 1.
        byte[] grib1 = [.. "GRIB"u8, 0, 0, 40, 1, .. new byte[28], .. "7777"u8];

        var ex = Assert.Throws<UnsupportedGribException>(() => GribFile.Open(grib1));
        Assert.Contains("GRIB edition 1", ex.Message);
        Assert.Contains("only GRIB edition 2", ex.Message);
    }

    [Fact]
    public void Other_editions_fail_naming_the_edition()
    {
        var message = GribTestEncoder.Message(Field());
        message[7] = 3;

        var ex = Assert.Throws<UnsupportedGribException>(() => GribFile.Open(message));
        Assert.Contains("GRIB edition 3", ex.Message);
    }

    [Theory]
    [InlineData(42, "5.42 (CCSDS/AEC)")]
    [InlineData(4, "5.4 (IEEE floating point)")]
    [InlineData(61, "5.61 (simple packing with logarithmic preprocessing)")]
    [InlineData(200, "5.200 (run length)")]
    public void Unsupported_data_templates_are_listed_and_fail_on_decode(int template, string named)
    {
        // R, E, D, bits, type and a few more octets, as most templates start.
        byte[] body = [0x41, 0x20, 0, 0, 0, 0, 0, 0, 12, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        var field = Field() with { Packing = new RawTestPacking(template, body, [1, 2, 3, 4], 35) };
        using var grib = GribFile.Open(GribTestEncoder.Message(field));
        var indexed = Assert.Single(grib.Fields);

        Assert.Equal(template, indexed.DataRepresentation.TemplateNumber);
        Assert.False(indexed.IsSupported);
        Assert.Contains(named, indexed.UnsupportedReason);
        Assert.Contains("only templates 5.0, 5.2, 5.3, 5.40 and 5.41", indexed.UnsupportedReason);
        var ex = Assert.Throws<UnsupportedGribException>(() => grib.ReadValues(indexed, TestContext.Current.CancellationToken));
        Assert.Contains(named, ex.Message);
    }

    [Fact]
    public void Unsupported_field_doesnt_stop_the_others_decoding()
    {
        var ccsds = Field() with { Packing = new RawTestPacking(42, new byte[18], [], 35) };
        using var grib = GribFile.Open(GribTestEncoder.File(GribTestEncoder.Message(ccsds), GribTestEncoder.Message(Field())));

        Assert.False(grib.Fields[0].IsSupported);
        Assert.Equal(34f, grib.ReadValues(grib.Fields[1], TestContext.Current.CancellationToken)[^1]);
    }

    [Fact]
    public void Missing_value_management_3_is_unsupported()
    {
        var message = GribTestEncoder.Message(Field() with { Packing = new ComplexTestPacking() });

        // Section 5's octet 23 (missing value management) of the only field.
        var section5 = FindSection(message, 5);
        message[section5 + 22] = 3;
        using var grib = GribFile.Open(message);

        Assert.Contains("missing value management 3", grib.Fields[0].UnsupportedReason);
    }

    [Fact]
    public void Spatial_differencing_order_3_is_unsupported()
    {
        var message = GribTestEncoder.Message(Field() with { Packing = new ComplexTestPacking(SpatialDifferencingOrder: 1) });
        message[FindSection(message, 5) + 47] = 3;
        using var grib = GribFile.Open(message);

        Assert.Contains("order 3", grib.Fields[0].UnsupportedReason);
    }

    [Fact]
    public void Predetermined_bitmap_is_unsupported()
    {
        var message = GribTestEncoder.Message(Field());
        message[FindSection(message, 6) + 5] = 7;
        using var grib = GribFile.Open(message);

        Assert.Contains("predetermined bitmap 7", grib.Fields[0].UnsupportedReason);
    }

    [Fact]
    public void Truncated_file_fails()
    {
        var message = GribTestEncoder.Message(Field());

        var ex = Assert.Throws<GribFormatException>(() => GribFile.Open(message.AsMemory(0, message.Length - 10)));
        Assert.Contains("truncated", ex.Message);
    }

    [Fact]
    public void Missing_end_section_fails()
    {
        var message = GribTestEncoder.Message(Field());
        message[^1] = (byte)'X';

        Assert.Throws<GribFormatException>(() => GribFile.Open(message));
    }

    [Fact]
    public void File_without_grib_fails()
    {
        var ex = Assert.Throws<GribFormatException>(() => GribFile.Open("not a grib file at all"u8.ToArray()));
        Assert.Contains("no GRIB message", ex.Message);
    }

    [Fact]
    public void Bitmap_that_disagrees_with_the_packed_count_fails_on_decode()
    {
        var values = Field().Values.ToArray();
        values[3] = double.NaN;
        var message = GribTestEncoder.Message(Field() with { Values = values, Bitmap = TestBitmap.Explicit });

        // Mark the missing point present in the bitmap.
        var bitmap = FindSection(message, 6) + 6;
        message[bitmap] |= 0x10;
        using var grib = GribFile.Open(message);

        Assert.Throws<GribFormatException>(() => grib.ReadValues(grib.Fields[0], TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Non_seekable_stream_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => GribFile.Open(new NonSeekableStream()));
    }

    private static int FindSection(byte[] message, int number)
    {
        var position = 16;
        while (true)
        {
            var length = (message[position] << 24) | (message[position + 1] << 16) | (message[position + 2] << 8) | message[position + 3];
            if (message[position + 4] == number)
                return position;
            position += length;
        }
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
