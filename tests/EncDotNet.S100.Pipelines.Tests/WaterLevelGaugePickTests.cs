using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui.Manipulations;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The S-104 station gauge is drawn by a custom Mapsui style renderer; a click
/// on it must still resolve the station, as a click on the old ellipse did.
/// </summary>
public sealed class WaterLevelGaugePickTests
{
    [Fact]
    public async Task Click_on_a_gauge_resolves_its_station()
    {
        S100MapsuiRendering.Register();
        var result = await new MapsuiDatasetRenderer(new IdentityCrsTransformFactory())
            .RenderAsync(new GaugeProcessor(), cancellationToken: TestContext.Current.CancellationToken);
        var layer = Assert.Single(result.Layers);
        var viewport = new Mapsui.Viewport(0, 0, 10, 0, 256, 256);

        var onGauge = new MapRenderer().GetMapInfo(
            new ScreenPosition(128 + 7, 128), viewport, [layer], new RenderService(), margin: 0);
        var offGauge = new MapRenderer().GetMapInfo(
            new ScreenPosition(20, 20), viewport, [layer], new RenderService(), margin: 0);

        var record = Assert.Single(onGauge.MapInfoRecords);
        Assert.Equal("station:WL1", record.Feature[MapsuiDisplayListRenderer.FeatureRefKey]);
        Assert.Empty(offGauge.MapInfoRecords);
    }

    private sealed class GaugeProcessor : IDatasetProcessor, ICoveragePortrayalSource
    {
        public Core.SpecRef Spec => new("S-104", new Core.SpecVersion(2, 0, 0));

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;

        public Task<CoveragePortrayalResult> BuildCoveragePortrayalAsync(
            RenderContext? context = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CoveragePortrayalResult
            {
                SubLayers =
                [
                    new GlyphCoverageSubLayer
                    {
                        LayerKey = "s104.stations",
                        LayerName = "stations",
                        Plane = S98DisplayPlane.OtherChartOverlays,
                        Glyphs =
                        [
                            new PointGlyph
                            {
                                MercatorX = 0,
                                MercatorY = 0,
                                FeatureRefTag = "station:WL1",
                                Attributes = new Dictionary<string, object>(),
                                Symbol = PointGlyphSymbol.WaterLevelGauge,
                                Gauge = new WaterLevelGauge
                                {
                                    Trend = WaterLevelGaugeTrend.Rising,
                                    Label = "+1.42",
                                    Ink = new RgbaColor(15, 94, 115),
                                    Fill = new RgbaColor(255, 255, 255),
                                    Halo = new RgbaColor(255, 255, 255),
                                },
                            },
                        ],
                        Extent = new MercatorBounds(-1, -1, 1, 1),
                    },
                ],
                Spec = Spec,
                SourceDatasetId = "s104.h5",
                Info = "test",
            });
    }
}
