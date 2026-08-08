using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Mutable;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Verifies the mutating catalog tools (open / close / close-all) drive the
/// shared <see cref="IMutableDatasetCatalog"/> (#560, option b).
/// </summary>
public class MutableCatalogToolsTests
{
    // ---- open_dataset ---------------------------------------------------

    [Fact]
    public async Task OpenDataset_LoadsAndReturnsEnrichedMetadata()
    {
        var catalog = new FakeMutableDatasetCatalog
        {
            NextLoad = [FakeMutableDatasetCatalog.MakeDataset("cell.000", "S-101")],
        };
        using var file = new TempFile();

        var value = AssertOk(await new OpenDatasetTool(catalog)
            .InvokeAsync(new OpenDatasetRequest(file.Path)));

        Assert.Equal("file", value.Kind);
        Assert.Equal(1, value.Count);
        var opened = Assert.Single(value.Datasets);
        Assert.Equal("cell.000", opened.Id);
        Assert.Equal("S-101", opened.Spec);
        Assert.Equal(10, opened.NorthLatitude);
        Assert.Single(catalog.Datasets);
    }

    [Fact]
    public async Task OpenDataset_EmptyLoad_IsDatasetLoadFailed()
    {
        var catalog = new FakeMutableDatasetCatalog { NextLoad = [] };
        using var file = new TempFile();

        Assert.IsType<DatasetLoadFailed>(
            AssertErr(await new OpenDatasetTool(catalog).InvokeAsync(new OpenDatasetRequest(file.Path))));
    }

    [Fact]
    public async Task OpenDataset_MissingPath_IsInvalidArgument()
    {
        var catalog = new FakeMutableDatasetCatalog();
        var missing = Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.000");

        var error = Assert.IsType<InvalidArgument>(
            AssertErr(await new OpenDatasetTool(catalog).InvokeAsync(new OpenDatasetRequest(missing))));
        Assert.Equal("path", error.Parameter);
        Assert.Equal(0, catalog.LoadCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OpenDataset_EmptyPath_IsInvalidArgument(string path)
    {
        var catalog = new FakeMutableDatasetCatalog();

        Assert.IsType<InvalidArgument>(
            AssertErr(await new OpenDatasetTool(catalog).InvokeAsync(new OpenDatasetRequest(path))));
    }

    // ---- close_dataset --------------------------------------------------

    [Fact]
    public async Task CloseDataset_RemovesKnownId()
    {
        var catalog = new FakeMutableDatasetCatalog
        {
            NextLoad = [FakeMutableDatasetCatalog.MakeDataset("cell.000", "S-101")],
        };
        using (var file = new TempFile())
        {
            await new OpenDatasetTool(catalog).InvokeAsync(new OpenDatasetRequest(file.Path));
        }

        var value = AssertOk(await new CloseDatasetTool(catalog)
            .InvokeAsync(new CloseDatasetRequest("cell.000")));

        Assert.True(value.Removed);
        Assert.Equal(1, value.Count);
        Assert.Equal("S-101", Assert.Single(value.RemovedDatasets).Spec);
        Assert.Empty(catalog.Datasets);
    }

    [Fact]
    public async Task CloseDataset_UnknownId_IsGracefulNonError()
    {
        var catalog = new FakeMutableDatasetCatalog();

        var value = AssertOk(await new CloseDatasetTool(catalog)
            .InvokeAsync(new CloseDatasetRequest("ghost")));

        Assert.False(value.Removed);
        Assert.Equal(0, value.Count);
        Assert.Empty(value.RemovedDatasets);
    }

    [Fact]
    public async Task CloseDataset_EmptyId_IsInvalidArgument()
    {
        var catalog = new FakeMutableDatasetCatalog();

        Assert.IsType<InvalidArgument>(
            AssertErr(await new CloseDatasetTool(catalog).InvokeAsync(new CloseDatasetRequest("  "))));
    }

    // ---- close_all_datasets --------------------------------------------

    [Fact]
    public async Task CloseAll_RemovesEverything()
    {
        var catalog = new FakeMutableDatasetCatalog
        {
            NextLoad =
            [
                FakeMutableDatasetCatalog.MakeDataset("a", "S-101"),
                FakeMutableDatasetCatalog.MakeDataset("b", "S-102"),
            ],
        };
        using (var file = new TempFile())
        {
            await new OpenDatasetTool(catalog).InvokeAsync(new OpenDatasetRequest(file.Path));
        }

        var value = AssertOk(await new CloseAllDatasetsTool(catalog).InvokeAsync());

        Assert.True(value.Removed);
        Assert.Equal(2, value.Count);
        Assert.Equal(2, value.RemovedDatasets.Count);
        Assert.Empty(catalog.Datasets);
    }

    [Fact]
    public async Task CloseAll_WhenEmpty_ReportsNothingRemoved()
    {
        var catalog = new FakeMutableDatasetCatalog();

        var value = AssertOk(await new CloseAllDatasetsTool(catalog).InvokeAsync());

        Assert.False(value.Removed);
        Assert.Equal(0, value.Count);
    }

    private static TValue AssertOk<TValue>(ToolResult<TValue> result)
    {
        Assert.True(result.TryGetValue(out var value), "expected a success result");
        return value!;
    }

    private static ToolError AssertErr<TValue>(ToolResult<TValue> result)
    {
        Assert.True(result.TryGetError(out var error), "expected an error result");
        return error!;
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"encdotnet-open-{Guid.NewGuid():N}.000");

        public TempFile() => File.WriteAllText(Path, "x");

        public void Dispose()
        {
            try { File.Delete(Path); } catch { /* best-effort */ }
        }
    }
}
