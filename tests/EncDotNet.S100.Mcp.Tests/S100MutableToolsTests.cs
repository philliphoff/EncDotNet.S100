using System.Net;
using System.Text.Json;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Mcp.MutableTools;
using EncDotNet.S100.Mcp.Tools.Mutable;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tests;

public class S100MutableToolsTests
{
    [Fact]
    public void Create_WithNoCapabilities_ReturnsEmpty()
    {
        Assert.Empty(S100MutableTools.Create(presentation: null));
    }

    [Fact]
    public void Create_WithPresentation_AddsTheThreePresentationTools()
    {
        var tools = S100MutableTools.Create(PresentationAccessor(new FakeController()));

        Assert.Equal(
            new[] { "set_palette", "set_display_category", "set_display_mode" },
            tools.Select(t => t.ProtocolTool.Name).ToArray());
    }

    [Fact]
    public void Create_AddsTimeStepToolWhenTimeAccessorSupplied()
    {
        var tools = S100MutableTools.Create(
            PresentationAccessor(new FakeController()),
            time: new StaticCapabilityAccessor<ITimeController>(new FakeTime()));

        Assert.Equal(
            new[] { "set_palette", "set_display_category", "set_display_mode", "set_time_step" },
            tools.Select(t => t.ProtocolTool.Name).ToArray());
    }

    [Fact]
    public void Create_WithOnlyTime_AddsOnlyTimeStep()
    {
        var tools = S100MutableTools.Create(
            time: new StaticCapabilityAccessor<ITimeController>(new FakeTime()));

        Assert.Equal(new[] { "set_time_step" }, tools.Select(t => t.ProtocolTool.Name).ToArray());
    }

    [Fact]
    public void Create_WithCatalog_AddsOpenCloseTools()
    {
        var tools = S100MutableTools.Create(catalog: new FakeMutableCatalog());

        Assert.Equal(
            new[] { "open_dataset", "close_dataset", "close_all_datasets" },
            tools.Select(t => t.ProtocolTool.Name).ToArray());
    }

    [Fact]
    public void Create_AddsViewportToolWhenViewportAccessorSupplied()
    {
        var tools = S100MutableTools.Create(
            viewport: new StaticCapabilityAccessor<IViewportController>(new FakeViewport()));

        Assert.Equal(new[] { "set_viewport" }, tools.Select(t => t.ProtocolTool.Name).ToArray());
    }

    [Fact]
    public async Task SetViewport_RoundTripsOverTheWireAndMutatesTheController()
    {
        var host = new FakeViewport();
        var catalog = McpTestHelpers.NewCatalog();

        await using var server = new S100McpServer(catalog, new S100McpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            AdditionalTools = S100MutableTools.Create(
                viewport: new StaticCapabilityAccessor<IViewportController>(host)),
        });
        await server.StartAsync();
        await using var client = await McpTestClient.ConnectAsync(server);

        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, t => t.Name == "set_viewport");

        var result = await client.CallToolAsync(
            "set_viewport",
            new Dictionary<string, object?>
            {
                ["centerLongitude"] = -1.25,
                ["centerLatitude"] = 50.5,
                ["scaleDenominator"] = 50000,
            });

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(GetText(result)).RootElement;
        Assert.Equal("center", json.GetProperty("mode").GetString());
        Assert.Equal(50000, json.GetProperty("scaleDenominator").GetDouble());
        Assert.NotNull(host.Current);
        Assert.Equal(-1.25, host.Current!.CenterLongitude);
    }

    [Fact]
    public async Task RenderToImage_RoundTripsAsImageBlockPlusMetadata()
    {
        var png = new byte[] { 9, 8, 7, 6, 5 };
        var catalog = McpTestHelpers.NewCatalog();

        await using var server = new S100McpServer(catalog, new S100McpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            AdditionalTools = S100MutableTools.Create(
                renderer: new StaticCapabilityAccessor<IImageRenderer>(new FakeRenderer(png))),
        });
        await server.StartAsync();
        await using var client = await McpTestClient.ConnectAsync(server);

        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, t => t.Name == "render_to_image");

        var result = await client.CallToolAsync(
            "render_to_image",
            new Dictionary<string, object?> { ["width"] = 320, ["height"] = 240 });

        Assert.False(result.IsError);
        var image = Assert.IsType<ModelContextProtocol.Protocol.ImageContentBlock>(result.Content[0]);
        Assert.Equal("image/png", image.MimeType);
        // The wire carries the image as base64 text; decode it back to the raw PNG bytes.
        var decoded = Convert.FromBase64String(
            System.Text.Encoding.ASCII.GetString(image.Data.Span));
        Assert.Equal(png, decoded);

        var meta = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(result.Content[1]);
        var json = JsonDocument.Parse(meta.Text).RootElement;
        Assert.Equal(320, json.GetProperty("width").GetInt32());
        Assert.Equal(240, json.GetProperty("height").GetInt32());
        Assert.Equal(png.Length, json.GetProperty("byteLength").GetInt32());
    }

    [Fact]
    public async Task SetPalette_RoundTripsOverTheWireAndMutatesTheController()
    {
        var host = new FakeController();
        var catalog = McpTestHelpers.NewCatalog();

        await using var server = new S100McpServer(catalog, new S100McpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            AdditionalTools = S100MutableTools.Create(PresentationAccessor(host)),
        });
        await server.StartAsync();
        await using var client = await McpTestClient.ConnectAsync(server);

        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, t => t.Name == "set_palette");

        var result = await client.CallToolAsync(
            "set_palette",
            new Dictionary<string, object?> { ["palette"] = "Night" });

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(GetText(result)).RootElement;
        Assert.Equal("Night", json.GetProperty("palette").GetString());
        Assert.Equal("Day", json.GetProperty("previous").GetString());
        Assert.Equal(PaletteType.Night, host.Current.Palette);
    }

    [Fact]
    public async Task SetPalette_InvalidValue_ReturnsStructuredError()
    {
        var host = new FakeController();
        var catalog = McpTestHelpers.NewCatalog();

        await using var server = new S100McpServer(catalog, new S100McpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            AdditionalTools = S100MutableTools.Create(PresentationAccessor(host)),
        });
        await server.StartAsync();
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync(
            "set_palette",
            new Dictionary<string, object?> { ["palette"] = "Purple" });

        Assert.True(result.IsError);
        var json = JsonDocument.Parse(GetText(result)).RootElement;
        Assert.Equal("invalid_argument", json.GetProperty("code").GetString());
        Assert.Equal(PaletteType.Day, host.Current.Palette); // unchanged
    }

    private static string GetText(ModelContextProtocol.Protocol.CallToolResult result)
    {
        var block = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(
            result.Content[0]);
        return block.Text;
    }

    private static ICapabilityAccessor<IPresentationController> PresentationAccessor(
        IPresentationController controller)
        => new StaticCapabilityAccessor<IPresentationController>(controller);

    private sealed class FakeController : IPresentationController
    {
        public MapPresentationState Current { get; private set; } = MapPresentationState.Default;

        public Task SetPresentationAsync(
            MapPresentationState presentation, CancellationToken cancellationToken = default)
        {
            Current = presentation;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTime : ITimeController
    {
        public DateTime? Current => null;

        public IReadOnlyList<DateTime> AvailableSteps { get; } = Array.Empty<DateTime>();

        public Task SetTimeAsync(DateTime time, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeViewport : IViewportController
    {
        public MapViewport? Current { get; private set; }

        public void Set(MapViewport viewport) => Current = viewport;

        public void SetToBounds(EncDotNet.S100.ExchangeSets.BoundingBox bounds)
            => Current = new MapViewport(
                (bounds.WestBoundLongitude + bounds.EastBoundLongitude) / 2.0,
                (bounds.SouthBoundLatitude + bounds.NorthBoundLatitude) / 2.0,
                1.0);
    }

    private sealed class FakeRenderer(byte[] png) : IImageRenderer
    {
        public Task<byte[]?> RenderToPngAsync(
            int widthPx, int heightPx, double pixelDensity, CancellationToken cancellationToken = default)
            => Task.FromResult<byte[]?>(png);
    }

    private sealed class FakeMutableCatalog : IMutableDatasetCatalog
    {
        public IReadOnlyList<EncDotNet.S100.Datasets.Pipelines.Catalog.LoadedDataset> Datasets { get; } = [];

        public event EventHandler<EncDotNet.S100.Datasets.Pipelines.Catalog.DatasetCatalogChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<DatasetLoadOutcome> LoadAsync(
            string path, string? specHint = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new DatasetLoadOutcome(
                path, DatasetSourceKind.File, [], TimedOut: false));

        public bool Remove(EncDotNet.S100.Datasets.Pipelines.Catalog.DatasetId id) => false;

        public int RemoveAll() => 0;
    }
}
