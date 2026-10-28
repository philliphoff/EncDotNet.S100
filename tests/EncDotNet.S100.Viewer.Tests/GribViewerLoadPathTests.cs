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
/// Issue #1074: the NOAA GRIB2 fixtures go through the viewer's load path with
/// no viewer change — the steps <c>DatasetLoaderService</c> takes (detect the
/// spec, check for a portrayal catalogue, build the processor through the
/// pipeline factory, register it with the Mapsui session), stepping through
/// time, picking, the Library's metadata probe, the layer stack badge and the
/// pick report. The viewer itself (a window, a click) isn't driven here.
/// </summary>
/// <remarks>
/// In the collection that swaps <see cref="ProductRegistry.Default"/>'s GRIB2
/// registration for a fake (<see cref="NonS100ProductViewTests"/>), so the two
/// never run at once.
/// </remarks>
[Collection(nameof(ProductRegistryDefaultCollection))]
public sealed class GribViewerLoadPathTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("grib-viewer-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task A_GRIB_filter_download_loads_draws_steps_and_picks_through_the_session()
    {
        // A NOMADS GRIB filter download is named after the filter script.
        var path = GribFixtures.CopyFixture(GribFixtures.WaveFilter, Path.Combine(_dir, "filter_gfswave.pl"));

        // DatasetLoaderService.ResolveSpecOrWarn: the file is recognised.
        var spec = DatasetPipelineFactory.DetectProductSpec(path);
        Assert.Equal("GRIB2", spec);

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
        var processor = Assert.IsType<GribDatasetProcessor>(factory.CreateProcessorWithFilesystemUpdates(path));

        // The Mapsui session registers and draws it: the wave fill and the wind barbs.
        using var map = new Map();
        using var s100 = map.AddS100(new S100MapsuiOptions { CrsTransformFactory = new ProjNetCrsTransformFactory() });
        var id = new MapDatasetId("filter_gfswave.pl");
        Assert.True(await s100.AddDatasetAsync(new MapDataset(id, "filter_gfswave.pl", processor.Metadata), processor, cancellationToken: Ct));

        Assert.Equal(
            [WaveHeightStyle.Key, WindBarbStyle.Key],
            s100.Session.GetLayerStackEntries().Select(e => e.Item.SourceFeatureType).Order());
        Assert.NotEmpty(map.Layers);

        // The timeline names the run from the processor, and stepping redraws.
        var timed = Assert.Single(s100.GetTimeSnapshot().Datasets);
        Assert.Equal("GFS-Wave 00:00Z", ForecastRunNames.Describe(timed));
        await s100.SetTimeAsync(GribFixtures.Run.AddHours(3), Ct);
        Assert.Equal(GribFixtures.Run.AddHours(3), processor.Sources.Single(s => s.Name == WeatherParameters.WaveGroup).SelectedTime);

        // A pick at the stepped time reads ecCodes' value.
        var (latitude, longitude) = GribFixtures.CropPoint(566);
        var pick = Assert.Single(await s100.Query.PickAsync(new GeographicPickQuery { Latitude = latitude, Longitude = longitude }, Ct));
        Assert.Equal("Weather model data, not for navigation.", pick.Info.Attributes[0].DisplayValue);
        Assert.True(pick.Info.Attributes[0].IsNavigationNotice);
        Assert.Equal(
            $"{GribFixtures.FilterAt566.Height[1]:0.0#} m",
            pick.Info.Attributes.Single(a => a.Code == WeatherParameters.WaveHeight).DisplayValue);
    }

    [Fact]
    public void The_library_metadata_probe_reads_a_GRIB2_file()
    {
        var path = GribFixtures.CopyFixture(GribFixtures.Wind10m, Path.Combine(_dir, "gfs.t00z.pgrb2.0p25"));
        var reader = new CachingDatasetMetadataReader(
            new DiskDatasetMetadataCache(Path.Combine(_dir, "cache"), 1_000_000));

        var metadata = reader.TryRead(path);

        Assert.NotNull(metadata);
        Assert.Equal("GRIB2", metadata.Spec.Name);
        Assert.Equal(new TimeCoverage(GribFixtures.Run, GribFixtures.Run.AddHours(6)), metadata.TimeCoverage);
        Assert.Equal(GribFixtures.CropWest - 0.125, metadata.Extent!.WestLongitude, 6);
    }

    [AvaloniaFact]
    public void The_layer_stack_badges_a_GRIB2_dataset()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        var waves = datasets.Add(GribFixtures.CopyFixture(GribFixtures.Waves, Path.Combine(_dir, "gfswave.grib2")), "GRIB2");

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

        vm.SetPick("WeatherGridPoint", "Weather grid point", "(15,5)", "gfswave.grib2", "GRIB2", []);

        Assert.Equal("Weather model data, not for navigation.", vm.NavigationNotice);
        Assert.Equal("SoundCharts portrayal: GRIB2 defines no symbology", vm.PortrayalNote);
    }
}
