using System.Diagnostics;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Scene;
using Mapsui.Layers;
using Mapsui.Rendering;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pins the tile half of the viewer's settled signal (map rendering slice 1):
/// a layer is settled once its latest live frame showed only its own target
/// tiles, at the latest view, with no visible tile work left.
/// </summary>
[Collection(ChartSessionOptionsCollection.Name)]
public sealed class TileSettleTests : IDisposable
{
    private const int Band = 10;
    private const int ViewportDip = 256;
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(60);
    private readonly bool _previousTracking;

    public TileSettleTests()
    {
        _previousTracking = S100VectorTileRenderer.SettleTrackingEnabled;
        S100VectorTileRenderer.SettleTrackingEnabled = true;
    }

    public void Dispose() => S100VectorTileRenderer.SettleTrackingEnabled = _previousTracking;

    [Fact]
    public void EvaluateSettle_ColdLayer_IsNotSettledUntilItsTilesShow()
    {
        var viewport = MakeViewport();
        var layer = BindLayer();

        RenderFrame(layer, viewport);
        var cold = MapsuiChartLayers.EvaluateSettle([layer]);
        Assert.False(cold.IsSettled);
        Assert.Equal(1, cold.LayersConsidered);
        Assert.True(cold.MissingTiles > 0);

        RenderUntilSettled(layer, viewport);
        var settled = MapsuiChartLayers.EvaluateSettle([layer]);
        Assert.True(settled.IsSettled, settled.ToString());
        Assert.Equal(0, settled.MissingTiles);
        Assert.True(settled.LatestFrameTimestamp > 0);
    }

    [Fact]
    public void EvaluateSettle_LayerNotYetPaintedAtNewView_IsNotSettled()
    {
        var viewport = MakeViewport();
        var settledLayer = BindLayer();
        RenderUntilSettled(settledLayer, viewport);

        // A second layer painted at another view: the latest frame is its, so
        // the first layer hasn't painted that view yet.
        var moved = new Mapsui.Viewport(
            viewport.CenterX + (viewport.Resolution * 64),
            viewport.CenterY,
            viewport.Resolution,
            rotation: 0,
            width: ViewportDip,
            height: ViewportDip);
        var other = BindLayer();
        RenderFrame(other, moved);

        var state = MapsuiChartLayers.EvaluateSettle([settledLayer, other]);
        Assert.False(state.IsSettled);
        Assert.Equal(1, state.LayersNotPainted);
    }

    [Fact]
    public void EvaluateSettle_ReplacementShowingPredecessorTiles_IsNotSettled()
    {
        var viewport = MakeViewport();
        var source = BindLayer();
        RenderUntilSettled(source, viewport);

        var replacement = new MemoryLayer { Name = "filtered" };
        Assert.True(S100VectorTileRenderer.TryBindFilteredScene(source, replacement, _ => true, replaces: source));

        // The first frame draws the replaced layer's tiles in its place.
        RenderFrame(replacement, viewport);
        Assert.False(MapsuiChartLayers.EvaluateSettle([replacement]).IsSettled);

        RenderUntilSettled(replacement, viewport);
        Assert.True(MapsuiChartLayers.EvaluateSettle([replacement]).IsSettled);
    }

    [Fact]
    public void EvaluateSettle_DisabledOrNeverDrawnLayers_AreIgnored()
    {
        var disabled = BindLayer();
        disabled.Enabled = false;
        var neverBound = new MemoryLayer { Name = "plain" };

        var state = MapsuiChartLayers.EvaluateSettle([disabled, neverBound]);

        Assert.Equal(0, state.LayersConsidered);
        Assert.True(state.IsSettled);
    }

    [Fact]
    public void GetTileCacheMemory_CountsResidentTiles()
    {
        var viewport = MakeViewport();
        var layer = BindLayer();
        RenderUntilSettled(layer, viewport);

        var memory = S100VectorTileRenderer.GetTileCacheMemory();

        Assert.True(memory.Tiles > 0);
        Assert.True(memory.RasterBytes > 0);
        Assert.Equal(memory.RasterBytes + memory.GpuBytes, memory.TotalBytes);
        GC.KeepAlive(layer);
    }

    [Fact]
    public void Summarise_SkipsLayersOutOfRangeAtTheLatestResolution()
    {
        var latest = new S100VectorTileRenderer.SettleStamp(10, 0, 0, 256, 256, Resolution: 50, Exact: true, MissingTiles: 0);
        var stale = latest with { Timestamp = 5, CenterX = 1000, Exact = false };
        S100VectorTileRenderer.SettleInput[] inputs =
        [
            new(Enabled: true, MinVisible: 0, MaxVisible: double.MaxValue, latest, PendingVisible: 0),
            // Out of range at resolution 50: Mapsui doesn't draw it, so its stale stamp doesn't count.
            new(Enabled: true, MinVisible: 0, MaxVisible: 10, stale, PendingVisible: 0),
        ];

        var state = S100VectorTileRenderer.Summarise(inputs, latest);

        Assert.Equal(1, state.LayersConsidered);
        Assert.True(state.IsSettled);
    }

    [Fact]
    public void Summarise_PendingVisibleWork_IsIncompleteEvenWhenTheFrameWasExact()
    {
        var latest = new S100VectorTileRenderer.SettleStamp(10, 0, 0, 256, 256, Resolution: 50, Exact: true, MissingTiles: 0);
        S100VectorTileRenderer.SettleInput[] inputs =
        [
            new(Enabled: true, MinVisible: 0, MaxVisible: double.MaxValue, latest, PendingVisible: 2),
        ];

        var state = S100VectorTileRenderer.Summarise(inputs, latest);

        Assert.False(state.IsSettled);
        Assert.Equal(1, state.LayersIncomplete);
        Assert.Equal(2, state.PendingVisibleTiles);
    }

    [Fact]
    public void Summarise_NoFrameYet_CountsEveryLayerAsNotPainted()
    {
        S100VectorTileRenderer.SettleInput[] inputs =
        [
            new(Enabled: true, MinVisible: 0, MaxVisible: double.MaxValue, Stamp: null, PendingVisible: 0),
        ];

        var state = S100VectorTileRenderer.Summarise(inputs, latest: null);

        Assert.False(state.IsSettled);
        Assert.Equal(1, state.LayersNotPainted);
        Assert.Equal(0, state.LatestFrameTimestamp);
    }

    private static void RenderFrame(ILayer layer, Mapsui.Viewport viewport)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ViewportDip, ViewportDip));
        surface.Canvas.Clear(SKColors.Transparent);
        MapsuiChartLayers.RenderTiles(surface.Canvas, viewport, layer, new RenderService());
    }

    private static void RenderUntilSettled(ILayer layer, Mapsui.Viewport viewport)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            RenderFrame(layer, viewport);
            if (MapsuiChartLayers.EvaluateSettle([layer]).IsSettled)
            {
                return;
            }

            if (sw.Elapsed > SettleTimeout)
            {
                Assert.Fail(
                    $"Tiles did not settle within {SettleTimeout.TotalSeconds:F0} s: "
                    + S100VectorTileRenderer.DescribeTileWorkForTest(layer));
            }

            Thread.Sleep(20);
        }
    }

    private static Mapsui.Viewport MakeViewport()
    {
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(new TileKey(Band, 512, 512));
        return new Mapsui.Viewport(
            (minX + maxX) * 0.5,
            (minY + maxY) * 0.5,
            TileGrid.ResolutionForBand(Band),
            rotation: 0,
            width: ViewportDip,
            height: ViewportDip);
    }

    /// <summary>A layer with a filled square across the view and no disk namespace.</summary>
    private static ILayer BindLayer()
    {
        var viewport = MakeViewport();
        var half = viewport.Resolution * ViewportDip * 0.45;
        var square = new AreaPaintOp
        {
            FeatureReference = "area",
            WorldShell =
            [
                (viewport.CenterX - half, viewport.CenterY - half),
                (viewport.CenterX + half, viewport.CenterY - half),
                (viewport.CenterX + half, viewport.CenterY + half),
                (viewport.CenterX - half, viewport.CenterY + half),
                (viewport.CenterX - half, viewport.CenterY - half),
            ],
            Fill = new RgbaColor(40, 120, 220, 255),
        };
        var layer = new MemoryLayer { Name = "cell" };
        S100VectorTileRenderer.BindScene(layer, new VectorScene(new List<PaintOp> { square }), productLayerSet: null, styleStateHash: null);
        return layer;
    }
}
