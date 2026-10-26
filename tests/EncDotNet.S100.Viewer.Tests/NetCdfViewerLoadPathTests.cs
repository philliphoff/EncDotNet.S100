using Avalonia.Automation;
using Avalonia.Controls;
using EncDotNet.S100.Core;
using EncDotNet.S100.Core.Metadata;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Mapsui;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Issue #1071: a NetCDF-CF file goes through the viewer's load path with no
/// viewer change — the steps <c>DatasetLoaderService</c> takes (detect the
/// spec, check for a portrayal catalogue, build the processor through the
/// pipeline factory, register it with the Mapsui session), the Library's
/// metadata probe, the layer stack badge, the pick report notice and the
/// timeline's run name. The viewer itself (a window, a click) isn't driven
/// here.
/// </summary>
public sealed class NetCdfViewerLoadPathTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("netcdf-viewer-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task A_NetCDF_wave_file_loads_draws_and_steps_through_the_session()
    {
        var path = NetCdfFixtures.WriteWaveNetCdf4(Path.Combine(_dir, "waves.nc"));

        // DatasetLoaderService.ResolveSpecOrWarn: the file is recognised.
        var spec = DatasetPipelineFactory.DetectProductSpec(path);
        Assert.Equal("NetCDF", spec);

        // HasRequiredCatalogueOrWarn: drawn in code, so no catalogue is needed.
        Assert.IsNotType<PortrayalBasis.CatalogueBasis>(ProductRegistry.Default.PortrayalOf(spec));

        // CreateProcessorAsync: the factory the viewer builds, with an empty
        // catalogue manager.
        var factory = new DatasetPipelineFactory(
            new PortrayalCatalogueManager(),
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(),
            new DisplayPlaneAuthorityProvider());
        var processor = Assert.IsType<NetCdfDatasetProcessor>(factory.CreateProcessorWithFilesystemUpdates(path));

        // The Mapsui session registers and draws it.
        using var map = new Map();
        using var s100 = map.AddS100(new S100MapsuiOptions { CrsTransformFactory = new ProjNetCrsTransformFactory() });
        var id = new MapDatasetId("waves.nc");
        Assert.True(await s100.AddDatasetAsync(new MapDataset(id, "waves.nc", processor.Metadata), processor, cancellationToken: Ct));

        var entry = Assert.Single(s100.Session.GetLayerStackEntries());
        Assert.Equal(WaveHeightStyle.Key, entry.Item.SourceFeatureType);
        Assert.NotEmpty(map.Layers);

        // The timeline names the run from the processor, and stepping redraws.
        var timed = Assert.Single(s100.GetTimeSnapshot().Datasets);
        Assert.Equal("MFWAM 00:00Z", ForecastRunNames.Describe(timed));
        await s100.SetTimeAsync(NetCdfFixtures.Run.AddHours(3), Ct);
        Assert.Equal(NetCdfFixtures.Run.AddHours(3), processor.Sources[0].SelectedTime);
    }

    [Fact]
    public void The_library_metadata_probe_reads_a_NetCDF_file()
    {
        var path = NetCdfFixtures.WriteWaveNetCdf3(Path.Combine(_dir, "ww3.nc"));
        var reader = new CachingDatasetMetadataReader(
            new DiskDatasetMetadataCache(Path.Combine(_dir, "cache"), 1_000_000));

        var metadata = reader.TryRead(path);

        Assert.NotNull(metadata);
        Assert.Equal("NetCDF", metadata.Spec.Name);
        Assert.Equal(new TimeCoverage(NetCdfFixtures.Run, NetCdfFixtures.Run.AddHours(6)), metadata.TimeCoverage);
        Assert.NotNull(metadata.Extent);
    }

    [AvaloniaFact]
    public void The_layer_stack_badges_a_NetCDF_dataset()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        var waves = datasets.Add(NetCdfFixtures.WriteWaveNetCdf4(Path.Combine(_dir, "waves.nc")), "NetCDF");

        using var host = ViewHost.Show(new DatasetsView { DataContext = datasets }, width: 420, height: 900);
        host.Click(host.Find<TabItem>("Datasets.DatasetsTab"));

        var badge = host.Find<Border>("Datasets.Row.NotS100Badge");
        Assert.Equal(Strings.Label_NotS100Product, AutomationProperties.GetName(badge));
        Assert.True(waves.IsNotS100);
    }

    [Fact]
    public void The_pick_report_leads_with_the_notice_and_names_the_portrayal()
    {
        var vm = new PickReportViewModel();

        vm.SetPick("WeatherGridPoint", "Weather grid point", "(4,7)", "waves.nc", "NetCDF", []);

        Assert.Equal("Weather model data, not for navigation.", vm.NavigationNotice);
        Assert.Equal("SoundCharts portrayal: NetCDF-CF defines no symbology", vm.PortrayalNote);
    }
}
