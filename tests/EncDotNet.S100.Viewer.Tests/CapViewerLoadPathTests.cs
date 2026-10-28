using Avalonia.Automation;
using Avalonia.Controls;
using EncDotNet.S100.Core;
using EncDotNet.S100.Core.Metadata;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
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
/// Issue #1069: the CAP fixtures go through the viewer's load path with no
/// viewer change — the steps <c>DatasetLoaderService</c> takes (detect the
/// spec, check for a portrayal catalogue, build the processor through the
/// pipeline factory, register it with the Mapsui session), drawing only what
/// is in force as the view time moves, picking, the Library's metadata probe,
/// the layer stack badge and the pick report's notice. CAP alerts aren't
/// listed with the S-124 navigational warnings.
/// </summary>
[Collection(nameof(ProductRegistryDefaultCollection))]
public sealed class CapViewerLoadPathTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("cap-viewer-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task An_NWS_alert_feed_loads_and_draws_what_is_in_force_at_the_view_time()
    {
        var path = CapFixtures.CopyFixture(CapFixtures.GeoJson, Path.Combine(_dir, "active"));

        // DatasetLoaderService.ResolveSpecOrWarn: recognised with no extension.
        var spec = DatasetPipelineFactory.DetectProductSpec(path);
        Assert.Equal("CAP", spec);

        // HasRequiredCatalogueOrWarn: drawn in code, so no catalogue is needed.
        Assert.IsNotType<PortrayalBasis.CatalogueBasis>(ProductRegistry.Default.PortrayalOf(spec));

        var factory = new DatasetPipelineFactory(
            new PortrayalCatalogueManager(),
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(),
            new DisplayPlaneAuthorityProvider());
        var processor = Assert.IsType<CapDatasetProcessor>(factory.CreateProcessorWithFilesystemUpdates(path));
        Assert.Empty(((IDatasetProcessor)processor).GetNavigationalWarnings());

        using var map = new Map();
        using var s100 = map.AddS100(new S100MapsuiOptions { CrsTransformFactory = new ProjNetCrsTransformFactory() });
        var id = new MapDatasetId("active");
        Assert.True(await s100.AddDatasetAsync(new MapDataset(id, "active", processor.Metadata), processor, cancellationToken: Ct));
        Assert.Equal(S98DisplayPlane.CautionsAndWarnings, Assert.Single(s100.Session.GetLayerStackEntries()).Item.Plane);

        // The Special Marine Warning, in force from 15:12Z to 16:15Z.
        await s100.SetTimeAsync(new DateTime(2026, 8, 28, 15, 30, 0, DateTimeKind.Utc), Ct);
        var pick = Assert.Single(await s100.Query.PickAsync(new GeographicPickQuery { Latitude = 39.1, Longitude = -75.1 }, Ct));
        Assert.Equal(CapDatasetProcessor.NotForNavigationNotice, pick.Info.Attributes[0].DisplayValue);
        Assert.True(pick.Info.Attributes[0].IsNavigationNotice);
        Assert.Equal("Severe", pick.Info.Attributes.Single(a => a.Code == "severity").DisplayValue);

        // An hour later it has ended and isn't drawn or picked.
        await s100.SetTimeAsync(new DateTime(2026, 8, 28, 16, 30, 0, DateTimeKind.Utc), Ct);
        Assert.Empty(await s100.Query.PickAsync(new GeographicPickQuery { Latitude = 39.1, Longitude = -75.1 }, Ct));
    }

    [Fact]
    public void The_library_metadata_probe_reads_a_CAP_file()
    {
        var path = CapFixtures.CopyFixture(CapFixtures.GaleWarning, Path.Combine(_dir, "gale.xml"));
        var reader = new CachingDatasetMetadataReader(
            new DiskDatasetMetadataCache(Path.Combine(_dir, "cache"), 1_000_000));

        var metadata = reader.TryRead(path);

        Assert.NotNull(metadata);
        Assert.Equal("CAP", metadata.Spec.Name);
        Assert.Equal(new TimeCoverage(CapFixtures.GaleOnset.UtcDateTime, CapFixtures.GaleEnds.UtcDateTime), metadata.TimeCoverage);
        Assert.InRange(metadata.Extent!.SouthLatitude, 36.5, 36.7);
    }

    [AvaloniaFact]
    public void The_layer_stack_badges_a_CAP_dataset()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        var alerts = datasets.Add(CapFixtures.CopyFixture(CapFixtures.Atom, Path.Combine(_dir, "alerts.atom")), "CAP");

        using var host = ViewHost.Show(new DatasetsView { DataContext = datasets }, width: 420, height: 900);
        host.Click(host.Find<TabItem>("Datasets.DatasetsTab"));

        var badge = host.Find<Border>("Datasets.Row.NotS100Badge");
        Assert.Equal(Strings.Label_NotS100Product, AutomationProperties.GetName(badge));
        Assert.True(alerts.IsNotS100);
    }

    [Fact]
    public void The_pick_report_leads_with_the_CAP_notice_and_names_the_portrayal()
    {
        var vm = new PickReportViewModel();

        vm.SetPick(CapDatasetProcessor.FeatureType, "Gale Warning", "alert1.info1.area1", "gale.xml", "CAP", []);

        Assert.Equal(CapDatasetProcessor.NotForNavigationNotice, vm.NavigationNotice);
        Assert.Equal("SoundCharts portrayal: CAP 1.2 defines no symbology", vm.PortrayalNote);
    }
}
