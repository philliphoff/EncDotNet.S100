using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;
using Mapsui;
using Mapsui.Layers;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class GetViewerStateToolTests
{
    private sealed class FakeMapHost : IMapHost
    {
        public MapViewportWgs84? Viewport { get; set; }

        public void AddLayer(ILayer layer) { }
        public void RemoveLayer(ILayer layer) { }
        public void AddOverlayLayer(ILayer layer) { }
        public void RemoveOverlayLayer(ILayer layer) { }
        public void ReorderDatasetLayers(IReadOnlyList<ILayer> orderedDatasetLayers) { }
        public void ZoomToExtent(MRect extent) { }
        public void SetViewportToExtent(MRect mercatorExtent) { }
        public void SetViewportToCenterAndResolution(MPoint mercatorCenter, double resolution) { }
        public void CenterOn(double latitudeWgs84, double longitudeWgs84, long durationMs = 300) { }
        public (double Latitude, double Longitude)? TryGetViewportCenterWgs84() => null;
        public MapViewportWgs84? TryGetViewportWgs84() => Viewport;
        public Task<byte[]?> RenderCurrentViewToPngAsync(int widthPx, int heightPx, double pixelDensity, CancellationToken ct = default)
            => Task.FromResult<byte[]?>(null);
    }

    private sealed class FakeMapHostAccessor : IMapHostAccessor
    {
        public IMapHost? Current { get; set; }
    }

    private sealed class FakeRenderStateController : IRenderStateController
    {
        public PaletteType CurrentPalette { get; set; } = PaletteType.Day;
        public EcdisDisplayCategory CurrentDisplayCategory { get; set; } = EcdisDisplayCategory.Standard;
        public Task SetPaletteAsync(PaletteType palette, CancellationToken ct = default) { CurrentPalette = palette; return Task.CompletedTask; }
        public Task SetDisplayCategoryAsync(EcdisDisplayCategory category, CancellationToken ct = default) { CurrentDisplayCategory = category; return Task.CompletedTask; }
    }

    private sealed class FakeRenderStateAccessor : IRenderStateControllerAccessor
    {
        public IRenderStateController? Current { get; set; }
    }

    private sealed class FakeOwnShip : IOwnShipPositionProvider, IOwnShipHelmState
    {
        public OwnShipPosition? Current { get; set; }
        public event EventHandler<OwnShipPosition>? Updated;
        public bool IsHeld { get; set; }
        public double TurnRateDegPerSec { get; set; }
        public double CommandedSpeedMs { get; set; }
        public void Raise() => Updated?.Invoke(this, Current!);
    }

    [Fact]
    public async Task Reads_viewport_from_map_host()
    {
        var host = new FakeMapHost { Viewport = new MapViewportWgs84(50.0, -4.0, 51.0, -3.0, 50.5, -3.5, 11.0) };
        var tool = new GetViewerStateTool(mapHostAccessor: new FakeMapHostAccessor { Current = host });

        var result = await tool.InvokeAsync();

        Assert.True(result.TryGetValue(out var value));
        Assert.NotNull(value!.Viewport);
        Assert.Equal(50.0, value.Viewport!.South, 6);
        Assert.Equal(-3.0, value.Viewport.East, 6);
        Assert.Equal(11.0, value.Viewport.Zoom, 6);
    }

    [Fact]
    public async Task Viewport_is_null_when_map_not_ready()
    {
        var tool = new GetViewerStateTool(mapHostAccessor: new FakeMapHostAccessor { Current = null });

        var result = await tool.InvokeAsync();

        Assert.True(result.TryGetValue(out var value));
        Assert.Null(value!.Viewport);
    }

    [Fact]
    public async Task Reads_palette_and_display_category()
    {
        var controller = new FakeRenderStateController
        {
            CurrentPalette = PaletteType.Night,
            CurrentDisplayCategory = EcdisDisplayCategory.DisplayBase,
        };
        var tool = new GetViewerStateTool(renderStateAccessor: new FakeRenderStateAccessor { Current = controller });

        var result = await tool.InvokeAsync();

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("Night", value!.Palette);
        Assert.Equal("DisplayBase", value.DisplayCategory);
    }

    [Fact]
    public async Task Time_section_is_null_when_no_time_aware_dataset()
    {
        var tool = new GetViewerStateTool(globalTime: new GlobalTimeService());

        var result = await tool.InvokeAsync();

        Assert.True(result.TryGetValue(out var value));
        Assert.Null(value!.Time);
    }

    [Fact]
    public async Task Reads_own_ship_state()
    {
        var ownShip = new FakeOwnShip
        {
            Current = new OwnShipPosition(48.5, -5.0, 90.0, 5.0, DateTimeOffset.UnixEpoch, HeadingDeg: 95.0),
            IsHeld = true,
            CommandedSpeedMs = 6.0,
        };
        var tool = new GetViewerStateTool(ownShipPosition: ownShip, ownShipHelmState: ownShip);

        var result = await tool.InvokeAsync();

        Assert.True(result.TryGetValue(out var value));
        Assert.NotNull(value!.OwnShip);
        Assert.Equal(48.5, value.OwnShip!.Lat, 6);
        Assert.Equal(90.0, value.OwnShip.Cog);
        Assert.Equal(95.0, value.OwnShip.Heading);
        Assert.True(value.OwnShip.IsHeld);
        Assert.Equal(6.0, value.OwnShip.CommandedSpeedMs);
    }

    [Fact]
    public async Task Own_ship_section_is_null_without_a_fix()
    {
        var ownShip = new FakeOwnShip { Current = null };
        var tool = new GetViewerStateTool(ownShipPosition: ownShip, ownShipHelmState: ownShip);

        var result = await tool.InvokeAsync();

        Assert.True(result.TryGetValue(out var value));
        Assert.Null(value!.OwnShip);
    }

    [Fact]
    public async Task No_accessors_yields_empty_but_successful_snapshot()
    {
        var tool = new GetViewerStateTool();

        var result = await tool.InvokeAsync();

        Assert.True(result.TryGetValue(out var value));
        Assert.Null(value!.Viewport);
        Assert.Null(value.Palette);
        Assert.Null(value.DisplayCategory);
        Assert.Null(value.Time);
        Assert.Null(value.OwnShip);
        Assert.Equal(0, value.DatasetCount);
    }
}
