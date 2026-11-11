using System.Buffers.Binary;
using static EncDotNet.S100.TestSupport.NetCdfFixtures;

namespace EncDotNet.S100.Datasets.NetCdf.Tests;

/// <summary>The NetCDF-3 reader against files from the test-only classic-format writer.</summary>
public sealed class NetCdf3FileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("netcdf3-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(false, NetCdfFormat.Classic)]
    [InlineData(true, NetCdfFormat.Offset64)]
    public void Reads_the_header_of_classic_and_64_bit_offset_files(bool offset64, NetCdfFormat expected)
    {
        var path = WriteWaveNetCdf3(Path.Combine(_dir, "waves.nc"), offset64: offset64);

        using var file = NetCdfFile.Open(path);

        Assert.Equal(expected, file.Format);
        Assert.Equal(["time", "depth", "latitude", "longitude"], file.Dimensions.Select(d => d.Name));
        Assert.True(file.Dimensions[0].IsUnlimited);
        Assert.Equal(3, file.Dimensions[0].Length);
        Assert.Equal("COARDS, CF-1.6, ACDD-1.3", file.GetGlobalText("Conventions"));
        var thgt = file.FindVariable("Thgt")!;
        Assert.Equal(NetCdfDataType.Float, thgt.DataType);
        Assert.Equal([3L, 1, 20, 20], thgt.Shape);
        Assert.Equal("sea_surface_wave_significant_height", thgt.GetText("standard_name"));
        Assert.Equal(-9999.0, thgt.FindAttribute("_FillValue")!.FirstNumber);
        Assert.True(file.FindVariable("time")!.IsCoordinateVariable);
    }

    [Fact]
    public void Reads_record_variables_one_record_at_a_time()
    {
        var path = WriteWaveNetCdf3(Path.Combine(_dir, "waves.nc"));
        using var file = NetCdfFile.Open(path);
        var thgt = file.FindVariable("Thgt")!;
        var grid = ChartGrid;

        // Step 2, one file row (north to south: file row 0 is the northern row).
        var values = file.Read(thgt, [2, 0, 0, 0], [1, 1, 1, grid.Columns]);

        Assert.Equal(grid.Columns, values.Length);
        Assert.Equal(WaveHeight(grid.Rows - 1, 0, 2), values[0], 4);
        Assert.Equal(-9999, values[^1]); // the missing north-east node
        // Every value of the variable, record by record.
        var all = file.ReadAll(thgt);
        Assert.Equal(3 * grid.Rows * grid.Columns, all.Length);
        Assert.Equal(WaveHeight(0, 5, 1), all[(1 * grid.Rows + grid.Rows - 1) * grid.Columns + 5], 4);
        // A record coordinate: one value per record.
        Assert.Equal(grid.Times.Select(t => (t - DateTime.UnixEpoch).TotalSeconds), file.ReadAll(file.FindVariable("time")!));
    }

    [Fact]
    public void Reads_a_hyperslab_of_a_fixed_size_variable()
    {
        var path = WriteWaveNetCdf3(Path.Combine(_dir, "waves.nc"));
        using var file = NetCdfFile.Open(path);

        var lons = file.Read(file.FindVariable("longitude")!, [3], [2]);

        Assert.Equal([62.02, 62.03], lons.Select(l => Math.Round(l, 4)));
    }

    [Fact]
    public void A_single_record_variable_is_not_padded()
    {
        // Three shorts a record (6 bytes): with one record variable the
        // records follow each other with no padding.
        var model = new FixtureFile
        {
            Dimensions = [new("time", 4, Unlimited: true), new("x", 3)],
            GlobalAttributes = new() { ["title"] = "padding" },
            Variables =
            [
                new("v", FixtureType.Short, ["time", "x"], Enumerable.Range(0, 12).Select(i => (double)i).ToArray(), new()),
            ],
        };
        var path = Path.Combine(_dir, "single.nc");
        WriteNetCdf3(path, model);

        using var file = NetCdfFile.Open(path);

        Assert.Equal(Enumerable.Range(0, 12).Select(i => (double)i), file.ReadAll(file.FindVariable("v")!));
        Assert.Equal([7.0, 8.0], file.Read(file.FindVariable("v")!, [2, 1], [1, 2]));
    }

    [Fact]
    public void A_streaming_record_count_is_worked_out_from_the_file_length()
    {
        var path = WriteWaveNetCdf3(Path.Combine(_dir, "waves.nc"));
        var bytes = File.ReadAllBytes(path);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), 0xFFFFFFFF);
        File.WriteAllBytes(path, bytes);

        using var file = NetCdfFile.Open(path);

        Assert.Equal(3, file.Dimensions[0].Length);
    }

    [Fact]
    public void CDF5_and_other_files_are_rejected_with_a_reason()
    {
        var cdf5 = Path.Combine(_dir, "cdf5.nc");
        File.WriteAllBytes(cdf5, [(byte)'C', (byte)'D', (byte)'F', 5, 0, 0, 0, 0]);
        var text = Path.Combine(_dir, "text.nc");
        File.WriteAllText(text, "netcdf text { }");

        Assert.Contains("CDF-5", Assert.Throws<NetCdfFormatException>(() => NetCdfFile.Open(cdf5)).Message);
        Assert.Contains("isn't a NetCDF file", Assert.Throws<NetCdfFormatException>(() => NetCdfFile.Open(text)).Message);
    }

    [Fact]
    public void A_truncated_header_is_rejected()
    {
        var path = WriteWaveNetCdf3(Path.Combine(_dir, "waves.nc"));
        var bytes = File.ReadAllBytes(path)[..40];
        File.WriteAllBytes(path, bytes);

        Assert.Contains("truncated", Assert.Throws<NetCdfFormatException>(() => NetCdfFile.Open(path)).Message);
    }

    [Theory]
    [InlineData(new byte[] { (byte)'C', (byte)'D', (byte)'F', 1 }, NetCdfFormat.Classic)]
    [InlineData(new byte[] { (byte)'C', (byte)'D', (byte)'F', 2 }, NetCdfFormat.Offset64)]
    [InlineData(new byte[] { 0x89, (byte)'H', (byte)'D', (byte)'F', 0x0D, 0x0A, 0x1A, 0x0A }, NetCdfFormat.NetCdf4)]
    public void Detects_the_format_from_the_leading_bytes(byte[] leading, NetCdfFormat expected)
    {
        Assert.Equal(expected, NetCdfFile.DetectFormat(leading));
    }

    [Fact]
    public void CDF5_and_GRIB_are_not_detected()
    {
        Assert.Null(NetCdfFile.DetectFormat([(byte)'C', (byte)'D', (byte)'F', 5]));
        Assert.Null(NetCdfFile.DetectFormat("GRIB\0\0\0\u0002"u8));
    }
}
