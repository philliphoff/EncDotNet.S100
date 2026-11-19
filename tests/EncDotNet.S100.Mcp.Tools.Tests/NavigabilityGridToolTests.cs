using System.Buffers.Binary;
using System.Text;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// <c>get_navigability_grid</c> (#1558) over a synthetic S-101 cell: 10 m of
/// water in the west half, 1 m in the east half.
/// </summary>
public sealed class NavigabilityGridToolTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "s100-grid-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task The_summary_counts_navigable_and_shoal_water_and_finds_the_way_between()
    {
        var (result, png) = await Invoke(Request() with
        {
            Connect = [new GeoPoint(0.01, 0.005), new GeoPoint(0.04, 0.02)],
        });

        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e.Message : "");
        Assert.Null(png);
        Assert.Equal(2, value!.MinimumDepthMeters);
        Assert.True(value.Counts.Navigable > 0);
        Assert.True(value.Counts.Shoal > 0);
        Assert.Equal(0, value.Counts.Land);
        Assert.InRange(value.Counts.Navigable / (double)value.Counts.Shoal, 0.8, 1.25);
        Assert.Equal(10, value.ShoalestNavigable!.DepthMeters);
        Assert.Equal("depth_area", value.ShoalestNavigable.DepthKind);
        Assert.Equal(["101GRID"], value.Datasets);
        Assert.Equal(1, value.LargestNavigableArea!.AreaCount);
        Assert.True(value.Connection!.Connected);
        Assert.NotNull(value.Connection.Narrowest);
        Assert.Contains("Not for navigation", value.Caveat, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_npy_file_holds_depths_and_classes_row_by_row_from_the_south_west()
    {
        var (result, _) = await Invoke(Request());
        Assert.True(result.TryGetValue(out var value));
        var file = value!.File!;
        Assert.Equal("npy", file.Format);
        Assert.StartsWith(_directory, file.Path, StringComparison.Ordinal);

        var bytes = await File.ReadAllBytesAsync(file.Path, TestContext.Current.CancellationToken);
        Assert.Equal(file.Bytes, bytes.LongLength);
        Assert.Equal(0x93, bytes[0]);
        Assert.Equal("NUMPY", Encoding.ASCII.GetString(bytes, 1, 5));
        var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));
        Assert.Equal(0, (10 + headerLength) % 64);
        var header = Encoding.ASCII.GetString(bytes, 10, headerLength);
        Assert.Contains("('depth_dm', '<i2'), ('class', '|u1')", header, StringComparison.Ordinal);
        Assert.Contains($"'shape': ({value.Grid.Rows}, {value.Grid.Columns})", header, StringComparison.Ordinal);

        var data = bytes.AsSpan(10 + headerLength);
        Assert.Equal(value.Grid.Rows * value.Grid.Columns * 3, data.Length);

        // Row 0, column 0 is the south-west: 10 m and navigable. The last cell of row 0 is in the east: 1 m and shoal.
        Assert.Equal(100, BinaryPrimitives.ReadInt16LittleEndian(data));
        Assert.Equal((byte)NavigabilityCellClass.Navigable, data[2]);
        var east = (value.Grid.Columns - 1) * 3;
        Assert.Equal(10, BinaryPrimitives.ReadInt16LittleEndian(data[east..]));
        Assert.Equal((byte)NavigabilityCellClass.Shoal, data[east + 2]);
    }

    [Fact]
    public async Task The_raw_file_is_depths_then_classes_and_the_image_is_a_png()
    {
        var (result, png) = await Invoke(Request() with { Format = "raw", IncludeImage = true });
        Assert.True(result.TryGetValue(out var value));
        var count = value!.Grid.Rows * value.Grid.Columns;
        Assert.Equal(count * 3, value.File!.Bytes);
        var bytes = await File.ReadAllBytesAsync(value.File.Path, TestContext.Current.CancellationToken);
        Assert.Equal((byte)NavigabilityCellClass.Navigable, bytes[count * 2]);

        Assert.NotNull(png);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png![..4]);
        Assert.Equal(value.Grid.Columns, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(value.Grid.Rows, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
    }

    [Fact]
    public async Task Format_none_writes_no_file()
    {
        var (result, _) = await Invoke(Request() with { Format = "none" });
        Assert.True(result.TryGetValue(out var value));
        Assert.Null(value!.File);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task A_grid_over_the_cap_is_refused_with_a_cell_size_that_fits()
    {
        var (result, _) = await Invoke(new NavigabilityGridToolRequest(40, -130, 50, -120, CellMeters: 10, DraftMeters: 2));

        Assert.True(result.TryGetError(out var error));
        var tooLarge = Assert.IsType<GridTooLarge>(error);
        Assert.Equal("grid_too_large", tooLarge.Code);
        Assert.True(tooLarge.SuggestedCellMeters > 10);
        var (retry, _) = await Invoke(new NavigabilityGridToolRequest(40, -130, 50, -120, CellMeters: tooLarge.SuggestedCellMeters, DraftMeters: 2, Format: "none"));
        Assert.True(retry.TryGetValue(out _));
    }

    [Theory]
    [InlineData("draftMeters")]
    [InlineData("north")]
    [InlineData("format")]
    [InlineData("connect")]
    [InlineData("cellMeters")]
    public async Task Bad_arguments_are_refused(string parameter)
    {
        var request = parameter switch
        {
            "draftMeters" => Request() with { DraftMeters = null },
            "north" => Request() with { North = -1 },
            "format" => Request() with { Format = "tiff" },
            "connect" => Request() with { Connect = [new GeoPoint(0, 0)] },
            _ => Request() with { CellMeters = 0 },
        };

        var (result, _) = await Invoke(request);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal(parameter, Assert.IsType<InvalidArgument>(error).Parameter);
    }

    private static NavigabilityGridToolRequest Request() =>
        new(0.0, 0.0, 0.05, 0.05, CellMeters: 100, DraftMeters: 2);

    private Task<(ToolResult<NavigabilityGridResult> Result, byte[]? Png)> Invoke(NavigabilityGridToolRequest request)
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101(
            "101GRID",
            S101AreaSynth.Cell(
            [
                S101AreaSynth.DataCoverage(22_000, S101AreaSynth.Box(0, 0, 0.05, 0.05)),
                S101AreaSynth.DepthArea(10, S101AreaSynth.Box(0, 0, 0.05, 0.025)),
                S101AreaSynth.DepthArea(1, S101AreaSynth.Box(0, 0.025, 0.05, 0.05)),
            ]),
            LoadedDatasetFactory.Box(0, 0, 0.05, 0.05)));
        var tool = new NavigabilityGridTool(catalog, new RouteCheckSourceCache(new ProjNetCrsTransformFactory()), _directory);
        return tool.InvokeAsync(request, TestContext.Current.CancellationToken);
    }
}
