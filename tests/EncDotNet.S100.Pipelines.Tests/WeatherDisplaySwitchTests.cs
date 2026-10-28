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
/// Issue #1072: a weather processor offers every style that applies and draws
/// those its display settings switch on
/// (<see cref="EcdisDisplaySettings.LayerVisibility"/>), else each style's
/// default: isobars, the wave height fill and the barbs on; the wind speed
/// fill and wave arrows off. A layer switched off isn't built or listed.
/// </summary>
public sealed class WeatherDisplaySwitchTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("weather-switch-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task By_default_isobars_are_drawn_where_the_file_has_pressure()
    {
        var factory = new ProjNetCrsTransformFactory();
        using var withPressure = new GribDatasetProcessor(Write("wind-msl.grib2", Concat(ChartWind(), ChartPressure())), factory);
        using var windOnly = new GribDatasetProcessor(Write("wind.grib2", ChartWind()), factory);

        var portrayal = await withPressure.BuildCoveragePortrayalAsync(new RenderContext { TimeStep = Run }, Ct);
        var windPortrayal = await windOnly.BuildCoveragePortrayalAsync(new RenderContext { TimeStep = Run }, Ct);

        Assert.Equal([WindBarbStyle.Key, PressureIsobarStyle.Key], portrayal.SubLayers.Select(s => s.LayerKey));
        var isobars = Assert.IsType<IsolineCoverageSubLayer>(portrayal.SubLayers[1]);
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, isobars.Plane);
        Assert.Equal(90, isobars.WithinPlanePriority);
        Assert.Contains("1008", isobars.Isolines.Select(l => l.Label));
        Assert.Contains("1012", isobars.Isolines.Select(l => l.Label));
        // Offered: the wind speed fill too, though not drawn by default.
        Assert.Equal([WindSpeedStyle.Key, WindBarbStyle.Key, PressureIsobarStyle.Key], withPressure.LayerKeys);

        Assert.Equal([WindBarbStyle.Key], windPortrayal.SubLayers.Select(s => s.LayerKey));
    }

    [Fact]
    public async Task The_switches_add_and_remove_layers_and_change_the_content_scope()
    {
        using var processor = new GribDatasetProcessor(
            Write("wind-msl.grib2", Concat(ChartWind(), ChartPressure())), new ProjNetCrsTransformFactory());
        var display = new EcdisDisplaySettings
        {
            LayerVisibility = new Dictionary<string, bool>
            {
                [PressureIsobarStyle.Key] = false,
                [WindSpeedStyle.Key] = true,
                [WaveDirectionStyle.Key] = true, // no wave direction in the file: nothing to draw
            },
        };

        var byDefault = await processor.BuildCoveragePortrayalAsync(new RenderContext { TimeStep = Run }, Ct);
        var switched = await processor.BuildCoveragePortrayalAsync(new RenderContext { TimeStep = Run, EcdisDisplay = display }, Ct);

        Assert.Equal([WindSpeedStyle.Key, WindBarbStyle.Key], switched.SubLayers.Select(s => s.LayerKey));
        Assert.NotEqual(byDefault.ContentScope, switched.ContentScope);
    }

    [Fact]
    public async Task The_session_draws_and_lists_what_the_presentation_switches_on()
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

        Assert.Equal([PressureIsobarStyle.Key, WindBarbStyle.Key], Listed(s100));

        // The map-wide switch, as the viewer's Display controls set it: every dataset re-renders.
        var presentation = MapPresentationState.Default.WithEcdisDisplay(new EcdisDisplaySettings
        {
            LayerVisibility = new Dictionary<string, bool> { [PressureIsobarStyle.Key] = false, [WindSpeedStyle.Key] = true },
        });
        await s100.SetPresentationAsync(presentation, Ct);

        Assert.Equal([WindBarbStyle.Key, WindSpeedStyle.Key], Listed(s100));
        Assert.All(s100.Session.GetLayerStackEntries(), e => Assert.True(e.Layer.Enabled));
    }

    private static string[] Listed(IS100MapSession s100) =>
        s100.Session.GetLayerStackEntries().Select(e => e.Item.SourceFeatureType!).Order(StringComparer.Ordinal).ToArray();

    private string Write(string name, byte[] bytes) => TestSupport.GribFixtures.Write(Path.Combine(_dir, name), bytes);

    private static DatasetPipelineFactory CreateFactory() =>
        new(
            new PortrayalCatalogueManager(),
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(),
            new DisplayPlaneAuthorityProvider());
}
