using EncDotNet.S100.Core;
using EncDotNet.S100.Core.Scheduling;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Engine;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The chart engine's host-neutral render entry point (map rendering slice 10,
/// #1279): datasets portray in parallel on the work scheduler, tile blocks are
/// written in plan order, and a device scale renders a HiDPI image of the same
/// view.
/// </summary>
public sealed class ChartRendererTests : IDisposable
{
    private readonly WorkScheduler _scheduler = new(new WorkSchedulerOptions { WorkerCount = 4 });

    public void Dispose() => _scheduler.Dispose();

    [Fact]
    public async Task Portrayals_run_in_parallel_and_return_in_request_order()
    {
        // Each portrayal waits until all three have started, so they only
        // finish if the scheduler runs them at the same time.
        using var allStarted = new CountdownEvent(3);
        var requests = new[] { "a", "b", "c" }
            .Select(id => new ChartPortrayalRequest(new BarrierProcessor(id, allStarted), new RenderContext()))
            .ToArray();

        var inputs = await ChartRenderer
            .PortrayAsync(requests, _scheduler, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b", "c"], inputs.Select(i => i.Vector!.SourceDatasetId));
    }

    [Fact]
    public async Task Portrayal_of_an_unsupported_processor_is_refused()
    {
        var request = new ChartPortrayalRequest(new PlainProcessor(), new RenderContext());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => ChartRenderer.PortrayAsync([request], _scheduler, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Tile_blocks_are_written_in_plan_order()
    {
        var composition = NewCompositor().Prepare(
            [Cell("cell.000", Box(-10, 10, -10, 10))],
            new ChartCompositeOptions { HonorScaleVisibility = true, EnableSeamWrap = false, KeepAreaSymbolsInView = false });
        var renderer = new ChartTileRenderer(composition);
        var blocks = ChartTileRenderer.PlanBlocks(6, [.. Enumerable.Range(28, 8)], 28, 35, blockSize: 2);

        var written = new List<TileBlock>();
        await ChartRenderer.RenderTilesAsync(
            renderer,
            blocks,
            tiles => new TileBlock(tiles[0].Zoom, tiles[0].X, tiles[0].Y, 0, 0),
            first =>
            {
                written.Add(first);
                return ValueTask.CompletedTask;
            },
            new ChartTileJobOptions { MaxInFlight = 3 },
            _scheduler,
            TestContext.Current.CancellationToken);

        Assert.Equal(blocks.Select(b => (b.X, b.Y)), written.Select(b => (b.X, b.Y)));
    }

    [Fact]
    public async Task Tile_rendering_stops_when_cancelled()
    {
        var composition = NewCompositor().Prepare([Cell("cell.000", Box(-10, 10, -10, 10))]);
        var renderer = new ChartTileRenderer(composition);
        var blocks = ChartTileRenderer.PlanBlocks(6, [.. Enumerable.Range(28, 8)], 28, 35, blockSize: 1);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int writes = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ChartRenderer.RenderTilesAsync(
            renderer,
            blocks,
            tiles => tiles.Count,
            _ =>
            {
                if (++writes == 2)
                    cancel.Cancel();
                return ValueTask.CompletedTask;
            },
            new ChartTileJobOptions { MaxInFlight = 2 },
            _scheduler,
            cancel.Token));

        Assert.Equal(2, writes);
    }

    [Fact]
    public void Device_scale_renders_the_same_view_with_more_pixels()
    {
        var viewport = new Viewport
        {
            MinLongitude = -2,
            MaxLongitude = 2,
            MinLatitude = -2,
            MaxLatitude = 2,
            WidthPixels = 200,
            HeightPixels = 100,
            ScaleDenominator = 5_000,
        };
        var composition = NewCompositor().Prepare([Cell("cell.000", Box(-1, 1, -1, 1))]);

        using var standard = composition.Render(viewport);
        using var hiDpi = composition.Render(viewport, deviceScale: 2f);

        Assert.Equal((200, 100), (standard.Width, standard.Height));
        Assert.Equal((400, 200), (hiDpi.Width, hiDpi.Height));
        // The fill and the background land at the same view positions.
        Assert.Equal(standard.GetPixel(100, 50), hiDpi.GetPixel(200, 100));
        Assert.Equal(standard.GetPixel(10, 10), hiDpi.GetPixel(20, 20));
        Assert.NotEqual(standard.GetPixel(100, 50), standard.GetPixel(10, 10));
    }

    [Fact]
    public void Device_scale_applies_to_a_rotated_view()
    {
        var viewport = new Viewport
        {
            MinLongitude = -2,
            MaxLongitude = 2,
            MinLatitude = -2,
            MaxLatitude = 2,
            WidthPixels = 100,
            HeightPixels = 100,
            ScaleDenominator = 5_000,
            RotationDegrees = 30,
        };
        var composition = NewCompositor().Prepare([Cell("cell.000", Box(-1, 1, -1, 1))]);

        using var standard = composition.Render(viewport);
        using var hiDpi = composition.Render(viewport, deviceScale: 2f);

        Assert.Equal((200, 200), (hiDpi.Width, hiDpi.Height));
        Assert.Equal(standard.GetPixel(50, 50), hiDpi.GetPixel(100, 100));
        Assert.Equal(standard.GetPixel(3, 3), hiDpi.GetPixel(6, 6));
    }

    private static ChartCompositor NewCompositor() =>
        new(new ProjNetCrsTransformFactory(), new InteroperabilityAuthority(ProductRegistry.Default.DisplayPlanes));

    private static (double Lon, double Lat)[] Box(double west, double east, double south, double north) =>
        [(west, south), (east, south), (east, north), (west, north)];

    private static ChartCompositeInput Cell(string datasetId, (double Lon, double Lat)[] fill) =>
        ChartCompositeInput.ForVector(Portrayal(datasetId, fill));

    private static VectorPortrayalResult Portrayal(string datasetId, (double Lon, double Lat)[] fill)
    {
        const string token = "FILL";
        const string featureRef = "1";
        var ring = fill.Select(p => new GeoPosition(p.Lat, p.Lon)).Append(new GeoPosition(fill[0].Lat, fill[0].Lon)).ToArray();
        return new VectorPortrayalResult
        {
            SubLayers =
            [
                new VectorSubLayer
                {
                    LayerKey = datasetId + ".area",
                    LayerName = datasetId,
                    Instructions = [new AreaInstruction { FeatureReference = featureRef, FillColor = token }],
                    Plane = S98DisplayPlane.BaseChartUnder,
                    SourceFeatureType = "area",
                },
            ],
            Palette = new ColorPalette("test", new Dictionary<string, string> { [token] = "#0000FF" }),
            GeometryProvider = new SurfaceProvider(featureRef, ring),
            Product = "S-101",
            Spec = new SpecRef("S-101", default),
            SourceDatasetId = datasetId,
            Info = "test",
        };
    }

    private sealed class SurfaceProvider(string featureRef, GeoPosition[] shell) : IFeatureGeometryProvider
    {
        private readonly FeatureGeometry _geometry = new() { Type = GeometryType.Surface, Coordinates = shell };

        public FeatureGeometry? GetGeometry(string featureReference) =>
            featureReference == featureRef ? _geometry : null;
    }

    private sealed class BarrierProcessor(string id, CountdownEvent allStarted) : IDatasetProcessor, IVectorPortrayalSource
    {
        public SpecRef Spec => new("S-101", default);

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;

        public Task<VectorPortrayalResult> BuildVectorPortrayalAsync(RenderContext? context, CancellationToken cancellationToken)
        {
            allStarted.Signal();
            allStarted.Wait(TimeSpan.FromSeconds(20), cancellationToken);
            Assert.True(allStarted.IsSet, "The portrayals did not run at the same time.");
            return Task.FromResult(Portrayal(id, Box(-1, 1, -1, 1)));
        }
    }

    private sealed class PlainProcessor : IDatasetProcessor
    {
        public SpecRef Spec => new("S-999", default);

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
    }
}
