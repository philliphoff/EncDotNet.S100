using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using Mapsui;
using static EncDotNet.S100.TestSupport.GribFixtures;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1072: a weather processor offers its off-by-default layers (the
/// pressure isobars, the wind speed fill) as sub-layers that start hidden. The
/// Mapsui session lists them in the layer stack but doesn't draw them until
/// their sub-layer state is switched on, and keeps that choice across
/// re-renders; the headless compositor leaves them out unless asked for.
/// </summary>
public sealed class WeatherOptionalLayerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("weather-optional-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task The_processor_offers_isobars_hidden_only_when_the_file_has_pressure()
    {
        var factory = new ProjNetCrsTransformFactory();
        using var withPressure = new GribDatasetProcessor(Write("wind-msl.grib2", Concat(ChartWind(), ChartPressure())), factory);
        using var windOnly = new GribDatasetProcessor(Write("wind.grib2", ChartWind()), factory);

        var portrayal = await withPressure.BuildCoveragePortrayalAsync(new RenderContext { TimeStep = Run }, Ct);
        var isobars = Assert.IsType<IsolineCoverageSubLayer>(Assert.Single(portrayal.SubLayers, s => s.LayerKey == PressureIsobarStyle.Key));
        Assert.False(isobars.IsVisibleByDefault);
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, isobars.Plane);
        Assert.Equal(90, isobars.WithinPlanePriority);
        Assert.Contains("1008", isobars.Isolines.Select(l => l.Label));
        Assert.Contains("1012", isobars.Isolines.Select(l => l.Label));

        // The wind speed fill, off by default too, comes out the same way.
        Assert.False(Assert.Single(portrayal.SubLayers, s => s.LayerKey == WindSpeedStyle.Key).IsVisibleByDefault);
        Assert.True(Assert.Single(portrayal.SubLayers, s => s.LayerKey == WindBarbStyle.Key).IsVisibleByDefault);

        var windPortrayal = await windOnly.BuildCoveragePortrayalAsync(new RenderContext { TimeStep = Run }, Ct);
        Assert.DoesNotContain(windPortrayal.SubLayers, s => s.LayerKey == PressureIsobarStyle.Key);
        Assert.DoesNotContain(PressureIsobarStyle.Key, windOnly.LayerKeys);
    }

    [Fact]
    public async Task Headless_rendering_leaves_the_isobars_out_unless_they_are_asked_for()
    {
        var factory = new ProjNetCrsTransformFactory();
        using var processor = new GribDatasetProcessor(Write("msl.grib2", ChartPressure()), factory);
        var portrayal = await processor.BuildCoveragePortrayalAsync(new RenderContext { TimeStep = Run }, Ct);
        var compositor = new HeadlessCompositor(factory);
        var viewport = Assert.IsType<IsolineCoverageSubLayer>(Assert.Single(portrayal.SubLayers)).Extent!.Value;
        var options = new HeadlessCompositeOptions
        {
            Viewport = PointGlyphHeadlessAdapter.FitViewport(viewport, 300, 300, padding: 0.1),
        };

        using var hidden = compositor.Render([HeadlessCompositeInput.ForCoverage(portrayal)], options);
        using var shown = compositor.Render(
            [HeadlessCompositeInput.ForCoverage(portrayal)],
            new HeadlessCompositeOptions { Viewport = options.Viewport, ShownLayerKeys = [PressureIsobarStyle.Key] });
        using var processorRender = await processor.RenderHeadlessAsync(300, 300, new RenderContext { TimeStep = Run }, cancellationToken: Ct);

        Assert.Equal(0, Ink(hidden));
        Assert.True(Ink(shown) > 100, $"Only {Ink(shown)} ink pixels with the isobars asked for.");
        // The processor's own headless path (s100 render) keeps them off.
        Assert.Equal(0, Ink(processorRender));
    }

    [Fact]
    public async Task The_session_lists_the_isobars_hidden_and_draws_them_once_switched_on()
    {
        var path = Write("wind-msl.grib2", Concat(ChartWind(), ChartPressure()));
        using var map = new Map();
        using var s100 = map.AddS100(new S100MapsuiOptions
        {
            CrsTransformFactory = new ProjNetCrsTransformFactory(),
            DatasetPipelineFactory = CreateFactory(),
        });
        var processor = Assert.IsType<GribDatasetProcessor>(CreateFactory().CreateProcessorWithFilesystemUpdates(path));
        var id = new MapDatasetId("wind-msl.grib2");
        Assert.True(await s100.AddDatasetAsync(new MapDataset(id, "wind-msl.grib2", processor.Metadata), processor, cancellationToken: Ct));

        // Listed in the layer stack, on its plane, but not drawn.
        var entry = Assert.Single(s100.Session.GetLayerStackEntries(), e => e.Item.SourceFeatureType == PressureIsobarStyle.Key);
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, entry.Plane);
        Assert.False(entry.Layer.Enabled);
        var state = s100.Session.GetDataset(id)!.Dataset;
        Assert.False(Assert.Single(state.SubLayers, s => s.Key == PressureIsobarStyle.Key).IsVisible);
        Assert.False(Assert.Single(state.SubLayers, s => s.Key == WindSpeedStyle.Key).IsVisible);
        Assert.True(Assert.Single(state.SubLayers, s => s.Key == WindBarbStyle.Key).IsVisible);

        // Switched on, as the layer stack's toggle does through the sub-layer state.
        s100.Session.SetDataset(new MapDataset(
            id,
            state.Name,
            state.Metadata,
            availableTimes: state.AvailableTimes,
            currentTime: state.CurrentTime,
            subLayers: state.SubLayers
                .Select(s => s.Key == PressureIsobarStyle.Key ? new MapDatasetSubLayer(s.Key, s.Name, isVisible: true, s.Opacity) : s)
                .ToArray()));
        Assert.True(IsobarLayer(s100).Enabled);

        // The choice survives a re-render at another time step.
        await s100.SetTimeAsync(Run.AddHours(6), Ct);
        Assert.True(IsobarLayer(s100).Enabled);
        Assert.True(Assert.Single(s100.Session.GetDataset(id)!.Dataset.SubLayers, s => s.Key == PressureIsobarStyle.Key).IsVisible);
        Assert.False(Assert.Single(s100.Session.GetDataset(id)!.Dataset.SubLayers, s => s.Key == WindSpeedStyle.Key).IsVisible);
    }

    [Fact]
    public async Task A_pressure_only_file_still_gets_a_hidden_isobar_sub_layer()
    {
        var path = Write("msl.grib2", ChartPressure());
        using var map = new Map();
        using var s100 = map.AddS100(new S100MapsuiOptions
        {
            CrsTransformFactory = new ProjNetCrsTransformFactory(),
            DatasetPipelineFactory = CreateFactory(),
        });
        var processor = Assert.IsType<GribDatasetProcessor>(CreateFactory().CreateProcessorWithFilesystemUpdates(path));
        var id = new MapDatasetId("msl.grib2");
        Assert.True(await s100.AddDatasetAsync(new MapDataset(id, "msl.grib2", processor.Metadata), processor, cancellationToken: Ct));

        // One layer, but it starts hidden, so it gets sub-layer state to switch on.
        var subLayer = Assert.Single(s100.Session.GetDataset(id)!.Dataset.SubLayers);
        Assert.Equal(PressureIsobarStyle.Key, subLayer.Key);
        Assert.False(subLayer.IsVisible);
        Assert.False(IsobarLayer(s100).Enabled);
    }

    private static Mapsui.Layers.ILayer IsobarLayer(IS100MapSession s100) =>
        Assert.Single(s100.Session.GetLayerStackEntries(), e => e.Item.SourceFeatureType == PressureIsobarStyle.Key).Layer;

    private string Write(string name, byte[] bytes) => TestSupport.GribFixtures.Write(Path.Combine(_dir, name), bytes);

    // Pixels that aren't the white background.
    private static int Ink(SkiaSharp.SKBitmap bitmap)
    {
        int ink = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.Red < 250 || p.Green < 250 || p.Blue < 250)
                    ink++;
            }
        }

        return ink;
    }

    private static DatasetPipelineFactory CreateFactory() =>
        new(
            new PortrayalCatalogueManager(),
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(),
            new DisplayPlaneAuthorityProvider());
}
