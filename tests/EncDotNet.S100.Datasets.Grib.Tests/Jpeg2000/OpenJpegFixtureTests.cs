using System.Buffers.Binary;
using System.Text.Json;
using EncDotNet.S100.Datasets.Grib.Internal.Jpeg2000;

namespace EncDotNet.S100.Datasets.Grib.Tests.Jpeg2000;

/// <summary>
/// Decodes the codestreams in tests/datasets/GRIB2/jpeg2000, written by
/// OpenJPEG (2.5.4 through Pillow and imagecodecs, 2.5.0 through glymur)
/// with tools/grib-fixtures/make_jpeg2000_fixtures.py, and compares the
/// samples with the expected ones the script wrote beside each.
/// </summary>
/// <remarks>
/// Lossless cases must match exactly: the expected samples are the image
/// that was encoded. Lossy cases (9/7, or 5/3 cut short by rate control)
/// expect what OpenJPEG decodes, within the case's tolerance of 1: both
/// decoders reconstruct at the midpoint, but the 9/7 transform runs in
/// single precision and the order of operations differs, so a sample near
/// a rounding boundary can land on either side.
/// </remarks>
public class OpenJpegFixtureTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "TestData", "jpeg2000");

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        using var manifest = Manifest();
        foreach (var c in manifest.RootElement.GetProperty("cases").EnumerateArray())
            data.Add(c.GetProperty("name").GetString()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Decodes_as_OpenJpeg_does(string name)
    {
        using var manifest = Manifest();
        var c = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(e => e.GetProperty("name").GetString() == name);
        var width = c.GetProperty("width").GetInt32();
        var height = c.GetProperty("height").GetInt32();
        var precision = c.GetProperty("precision").GetInt32();
        var signed = c.GetProperty("signed").GetBoolean();
        var tolerance = c.GetProperty("tolerance").GetInt32();

        var image = Jpeg2000Decoder.Decode(File.ReadAllBytes(Path.Combine(Folder, c.GetProperty("file").GetString()!)), TestContext.Current.CancellationToken);

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.Equal(precision, image.Precision);
        Assert.Equal(signed, image.Signed);
        var expected = ReadRaw(Path.Combine(Folder, name + ".raw"), precision, signed);
        Assert.Equal(expected.Length, image.Samples.Length);

        var worst = 0;
        var at = -1;
        for (var i = 0; i < expected.Length; i++)
        {
            var error = Math.Abs((long)image.Samples[i] - expected[i]);
            if (error > worst)
            {
                worst = (int)Math.Min(error, int.MaxValue);
                at = i;
            }
        }

        Assert.True(
            worst <= tolerance,
            $"Sample {at} (column {at % Math.Max(width, 1)}, row {at / Math.Max(width, 1)}) is off by {worst}; the tolerance is {tolerance}.");
    }

    [Fact]
    public void Every_progression_order_decodes_to_the_same_image()
    {
        var images = new[] { "lrcp", "rlcp", "rpcl", "pcrl", "cprl" }
            .Select(o => Jpeg2000Decoder.Decode(File.ReadAllBytes(Path.Combine(Folder, $"progression-{o}.j2k")), TestContext.Current.CancellationToken).Samples)
            .ToList();
        Assert.All(images, s => Assert.Equal(images[0], s));
    }

    private static JsonDocument Manifest()
    {
        using var stream = File.OpenRead(Path.Combine(Folder, "manifest.json"));
        return JsonDocument.Parse(stream);
    }

    private static long[] ReadRaw(string path, int precision, bool signed)
    {
        var bytes = File.ReadAllBytes(path);
        var size = precision <= 8 ? 1 : precision <= 16 ? 2 : 4;
        var values = new long[bytes.Length / size];
        for (var i = 0; i < values.Length; i++)
        {
            var span = bytes.AsSpan(i * size, size);
            values[i] = (size, signed) switch
            {
                (1, false) => span[0],
                (1, true) => (sbyte)span[0],
                (2, false) => BinaryPrimitives.ReadUInt16LittleEndian(span),
                (2, true) => BinaryPrimitives.ReadInt16LittleEndian(span),
                (_, false) => BinaryPrimitives.ReadUInt32LittleEndian(span),
                _ => BinaryPrimitives.ReadInt32LittleEndian(span),
            };
        }

        return values;
    }
}
