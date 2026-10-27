using EncDotNet.S100.Datasets.Grib.Internal.Jpeg2000;

namespace EncDotNet.S100.Datasets.Grib.Tests.Jpeg2000;

/// <summary>
/// The features the decoder rejects, and malformed codestreams: OpenJPEG
/// fixtures with a marker inserted or a field changed.
/// </summary>
public class CodestreamErrorTests
{
    // size-37x19.j2k: SOC at 0, SIZ at 2 (Rsiz at 6, Csiz at 40, Ssiz at 42), COD after SIZ.
    private static byte[] Fixture(string name = "size-37x19.j2k") =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "jpeg2000", name));

    [Theory]
    [InlineData(new byte[] { 0xFF, 0x5E, 0, 5, 0, 0, 3 }, "region of interest (RGN marker)")]
    [InlineData(new byte[] { 0xFF, 0x5F, 0, 9, 0, 0, 1, 1, 0, 0 }, "progression order changes (POC marker)")]
    [InlineData(new byte[] { 0xFF, 0x60, 0, 4, 0, 0 }, "packed packet headers (PPM marker)")]
    [InlineData(new byte[] { 0xFF, 0x50, 0, 8, 0x00, 0x02, 0, 0, 0, 0 }, "HTJ2K")]
    public void Unsupported_main_header_markers_fail_naming_the_feature(byte[] segment, string named)
    {
        var data = InsertAfterSiz(Fixture(), segment);

        var ex = Assert.Throws<UnsupportedGribException>(() => Jpeg2000Decoder.Decode(data, TestContext.Current.CancellationToken));
        Assert.Contains(named, ex.Message);
    }

    [Fact]
    public void Packed_headers_in_a_tile_part_fail_naming_the_feature()
    {
        var data = Fixture();
        var sot = IndexOf(data, 0xFF, 0x90);
        byte[] ppt = [0xFF, 0x61, 0, 3, 0];
        var patched = data[..(sot + 12)].Concat(ppt).Concat(data[(sot + 12)..]).ToArray();
        var psot = (patched[sot + 6] << 24) | (patched[sot + 7] << 16) | (patched[sot + 8] << 8) | patched[sot + 9];
        if (psot != 0)
        {
            psot += ppt.Length;
            patched[sot + 6] = (byte)(psot >> 24);
            patched[sot + 7] = (byte)(psot >> 16);
            patched[sot + 8] = (byte)(psot >> 8);
            patched[sot + 9] = (byte)psot;
        }

        var ex = Assert.Throws<UnsupportedGribException>(() => Jpeg2000Decoder.Decode(patched, TestContext.Current.CancellationToken));
        Assert.Contains("packed packet headers (PPT marker)", ex.Message);
    }

    [Theory]
    [InlineData(0x80, 0x00, "Part 2 extensions (Rsiz 0x8000)")]
    [InlineData(0x40, 0x00, "HTJ2K")]
    public void Part_2_and_HTJ2K_profiles_fail(byte high, byte low, string named)
    {
        var data = Fixture();
        data[6] = high;
        data[7] = low;

        var ex = Assert.Throws<UnsupportedGribException>(() => Jpeg2000Decoder.Decode(data, TestContext.Current.CancellationToken));
        Assert.Contains(named, ex.Message);
    }

    [Fact]
    public void Several_components_fail_naming_the_count()
    {
        // SOC, then a SIZ for three 8-bit components.
        byte[] siz = [0xFF, 0x51, 0, 47, 0, 0, 0, 0, 0, 8, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3,
            7, 1, 1, 7, 1, 1, 7, 1, 1];
        byte[] data = [0xFF, 0x4F, .. siz, 0xFF, 0xD9];

        var ex = Assert.Throws<UnsupportedGribException>(() => Jpeg2000Decoder.Decode(data, TestContext.Current.CancellationToken));
        Assert.Contains("3 components", ex.Message);
        Assert.Contains("single-component", ex.Message);
    }

    [Fact]
    public void Samples_wider_than_31_bits_fail()
    {
        var data = Fixture();
        data[42] = 31;

        var ex = Assert.Throws<UnsupportedGribException>(() => Jpeg2000Decoder.Decode(data, TestContext.Current.CancellationToken));
        Assert.Contains("32 bits", ex.Message);
    }

    [Theory]
    [InlineData(12, 0x40, "HT code-blocks")]
    [InlineData(13, 2, "wavelet transformation 2")]
    public void Part_2_and_HTJ2K_coding_styles_fail(int offset, byte value, string named)
    {
        var data = Fixture();
        data[IndexOf(data, 0xFF, 0x52) + offset] = value;

        var ex = Assert.Throws<UnsupportedGribException>(() => Jpeg2000Decoder.Decode(data, TestContext.Current.CancellationToken));
        Assert.Contains(named, ex.Message);
    }

    [Fact]
    public void Not_a_codestream_fails_as_a_format_error()
    {
        var ex = Assert.Throws<GribFormatException>(() => Jpeg2000Decoder.Decode([0x12, 0x34, 0x56, 0x78], TestContext.Current.CancellationToken));
        Assert.Contains("SOC marker is missing", ex.Message);
    }

    [Theory]
    [InlineData(0.9)]
    [InlineData(0.5)]
    [InlineData(0.2)]
    public void Truncated_codestream_fails_as_a_format_error(double keep)
    {
        var data = Fixture("levels-3.j2k");
        var cut = data[..(int)(data.Length * keep)];

        Assert.Throws<GribFormatException>(() => Jpeg2000Decoder.Decode(cut, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Missing_tiles_fail_naming_the_count()
    {
        // tiles-16x16.j2k has 4 × 3 tiles; drop all but the first tile-part.
        var data = Fixture("tiles-16x16.j2k");
        var sot = IndexOf(data, 0xFF, 0x90);
        var psot = (data[sot + 6] << 24) | (data[sot + 7] << 16) | (data[sot + 8] << 8) | data[sot + 9];
        byte[] cut = [.. data[..(sot + psot)], 0xFF, 0xD9];

        var ex = Assert.Throws<GribFormatException>(() => Jpeg2000Decoder.Decode(cut, TestContext.Current.CancellationToken));
        Assert.Contains("tile-parts for 1 of its 12 tiles", ex.Message);
    }

    [Fact]
    public void Decoding_can_be_cancelled()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => Jpeg2000Decoder.Decode(Fixture(), source.Token));
    }

    private static byte[] InsertAfterSiz(byte[] data, byte[] segment)
    {
        var sizEnd = 4 + ((data[4] << 8) | data[5]);
        return [.. data[..sizEnd], .. segment, .. data[sizEnd..]];
    }

    private static int IndexOf(byte[] data, byte first, byte second)
    {
        for (var i = 0; i + 1 < data.Length; i++)
        {
            if (data[i] == first && data[i + 1] == second)
                return i;
        }

        throw new InvalidOperationException("Marker not found.");
    }
}
