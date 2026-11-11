using PureHDF;
using PureHDF.Filters;
using static EncDotNet.S100.TestSupport.NetCdfFixtures;

namespace EncDotNet.S100.Datasets.NetCdf.Tests;

/// <summary>
/// The tests in this collection change PureHDF's process-wide filter
/// registrations, so they don't run in parallel with anything else.
/// </summary>
[CollectionDefinition(nameof(PureHdfFilterCollection), DisableParallelization = true)]
public sealed class PureHdfFilterCollection;

/// <summary>The NetCDF-4 reader (on PureHDF) against files from the PureHDF fixture writer.</summary>
[Collection(nameof(PureHdfFilterCollection))]
public sealed class NetCdf4FileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("netcdf4-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Reads_dimensions_from_dimension_scales_and_DIMENSION_LIST()
    {
        var path = WriteWaveNetCdf4(Path.Combine(_dir, "waves.nc"));

        using var file = NetCdfFile.Open(path);

        Assert.Equal(NetCdfFormat.NetCdf4, file.Format);
        Assert.Equal(["time", "latitude", "longitude"], file.Dimensions.Select(d => d.Name));
        var vhm0 = file.FindVariable("VHM0")!;
        Assert.Equal(NetCdfDataType.Short, vhm0.DataType);
        Assert.Equal(["time", "latitude", "longitude"], vhm0.Dimensions.Select(d => d.Name));
        Assert.Equal(0.001, vhm0.FindAttribute("scale_factor")!.FirstNumber);
        Assert.Equal(-32767, vhm0.FindAttribute("_FillValue")!.FirstNumber);
        Assert.Equal("CF-1.6", file.GetGlobalText("Conventions"));
        // The NetCDF-4 bookkeeping attributes aren't user attributes.
        Assert.Null(vhm0.FindAttribute("DIMENSION_LIST"));
        Assert.Null(file.FindVariable("latitude")!.FindAttribute("CLASS"));
    }

    [Fact]
    public void Reads_a_deflated_and_shuffled_time_slice()
    {
        var path = WriteWaveNetCdf4(Path.Combine(_dir, "waves.nc"));
        using var file = NetCdfFile.Open(path);
        var grid = ChartGrid;

        var slice = file.Read(file.FindVariable("VHM0")!, [1, 0, 0], [1, grid.Rows, grid.Columns]);

        Assert.Equal(grid.Rows * grid.Columns, slice.Length);
        Assert.Equal(Math.Round(WaveHeight(3, 4, 1) / 0.001), slice[3 * grid.Columns + 4]);
        Assert.Equal(-32767, slice[^1]);
    }

    [Fact]
    public void Without_DIMENSION_LIST_dimensions_are_matched_by_length()
    {
        // 20 × 20 is ambiguous; a 4 × 6 grid isn't.
        var path = WriteWaveNetCdf4(Path.Combine(_dir, "waves.nc"), new Grid(10, 20, 1, 4, 6), dimensionList: false);

        using var file = NetCdfFile.Open(path);

        Assert.Equal(["time", "latitude", "longitude"], file.FindVariable("VMDR")!.Dimensions.Select(d => d.Name));
    }

    [Fact]
    public void An_unsupported_filter_is_named()
    {
        var path = Path.Combine(_dir, "zstd.nc");
        H5Filter.Register(new PassThroughZstd());
        try
        {
            WriteNetCdf4(path, WaveFile(ChartGrid, northToSouth: false, packedHeight: false), filters: [PassThroughZstd.Id]);
        }
        finally
        {
            H5Filter.ResetRegistrations();
        }

        using var file = NetCdfFile.Open(path);
        var error = Assert.Throws<NetCdfFormatException>(() => file.ReadAll(file.FindVariable("VHM0")!));

        Assert.Contains("Zstandard (id 32015)", error.Message);
        Assert.Contains("only deflate and shuffle", error.Message);
    }

    [Fact]
    public void A_file_with_variables_only_in_groups_says_so()
    {
        var path = Path.Combine(_dir, "groups.nc");
        new H5File { ["forecast"] = new H5Group { ["VHM0"] = new float[] { 1, 2 } } }.Write(path);

        var error = Assert.Throws<NetCdfFormatException>(() => NetCdfFile.Open(path));

        Assert.Contains("groups (forecast)", error.Message);
    }

    [Fact]
    public void Reads_attributes_of_every_numeric_type()
    {
        var path = Path.Combine(_dir, "attributes.nc");
        new H5File
        {
            ["x"] = new H5Dataset(new double[] { 1, 2 })
            {
                Attributes = new()
                {
                    ["f"] = 1.5f,
                    ["d"] = new[] { 2.5, 3.5 },
                    ["s"] = (short)-4,
                    ["u"] = (ushort)5,
                    ["b"] = (sbyte)-6,
                    ["l"] = 7L,
                    ["text"] = "hello",
                },
            },
        }.Write(path);

        using var file = NetCdfFile.Open(path);
        var x = file.FindVariable("x")!;

        Assert.Equal(1.5, x.FindAttribute("f")!.FirstNumber);
        Assert.Equal([2.5, 3.5], x.FindAttribute("d")!.Numbers);
        Assert.Equal(-4, x.FindAttribute("s")!.FirstNumber);
        Assert.Equal(5, x.FindAttribute("u")!.FirstNumber);
        Assert.Equal(-6, x.FindAttribute("b")!.FirstNumber);
        Assert.Equal(7, x.FindAttribute("l")!.FirstNumber);
        Assert.Equal("hello", x.GetText("text"));
        // A plain HDF5 dataset gets an anonymous dimension.
        Assert.Equal("phony_dim_0", x.Dimensions[0].Name);
    }

    // Writes a "zstd" filter that leaves the bytes as they are, so the
    // reader (with default registrations) meets an unknown filter id.
    private sealed class PassThroughZstd : IH5Filter
    {
        public const ushort Id = 32015;

        public ushort FilterId => Id;

        public string Name => "zstd";

        public Memory<byte> Filter(FilterInfo info) => info.Buffer;

        public uint[] GetParameters(uint[] chunkDimensions, uint typeSize, Dictionary<string, object>? options) => [];
    }
}
