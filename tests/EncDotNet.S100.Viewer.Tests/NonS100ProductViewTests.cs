using Avalonia.Automation;
using Avalonia.Controls;
using EncDotNet.S100.Core.Metadata;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The tests in this collection register products with the process-wide
/// <see cref="ProductRegistry.Default"/>, which the viewer reads, so they don't
/// run in parallel with anything else.
/// </summary>
[CollectionDefinition(nameof(ProductRegistryDefaultCollection), DisableParallelization = true)]
public sealed class ProductRegistryDefaultCollection;

/// <summary>
/// Issue #1061: a product that isn't S-100 is one registration. With a fake
/// GRIB2 product (<see cref="FakeWeatherProduct"/>) registered, and no viewer
/// change, the layer stack badges its row "Not an S-100 product", the pick
/// report leads with "Weather model data, not for navigation." (or the
/// product's own line), and the Library's metadata probe reads it.
/// </summary>
[Collection(nameof(ProductRegistryDefaultCollection))]
public sealed class NonS100ProductViewTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("non-s100-view-").FullName;

    public NonS100ProductViewTests() => ProductRegistry.Default.Register(FakeWeatherProduct.Registration());

    public void Dispose()
    {
        ProductRegistry.Default.Unregister(FakeWeatherProduct.Spec);
        Directory.Delete(_dir, recursive: true);
    }

    [AvaloniaFact]
    public void The_layer_stack_badges_a_non_S100_product()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        var cell = datasets.Add("/data/US5SEAFL.000", "S-57");
        var grib = datasets.Add(FakeWeatherProduct.Write(_dir), FakeWeatherProduct.Spec);

        using var host = ViewHost.Show(new DatasetsView { DataContext = datasets }, width: 420, height: 900);
        host.Click(host.Find<TabItem>("Datasets.DatasetsTab"));

        var badge = host.Find<Border>("Datasets.Row.NotS100Badge", Row(host, grib));
        Assert.Equal(Strings.Label_NotS100Product, AutomationProperties.GetName(badge));
        Assert.Equal("Not an S-100 product", Strings.Label_NotS100Product);
        // Only the GRIB2 row has one.
        Assert.Single(host.FindAll<Border>("Datasets.Row.NotS100Badge"));
        Assert.True(grib.IsNotS100);
        Assert.False(cell.IsNotS100);
    }

    [Fact]
    public void The_pick_report_leads_with_the_not_for_navigation_notice()
    {
        var vm = new PickReportViewModel();
        vm.SetPick("WindSpeed", "Wind speed", "1", "gfs.grib2", FakeWeatherProduct.Spec, []);

        Assert.True(vm.HasNavigationNotice);
        Assert.Equal("Weather model data, not for navigation.", vm.NavigationNotice);

        vm.SetPick("DepthArea", "Depth Area", "300", "test.000", "S-101", []);

        Assert.False(vm.HasNavigationNotice);
        Assert.Null(vm.NavigationNotice);
    }

    [Fact]
    public void The_pick_report_uses_the_products_own_notice()
    {
        ProductRegistry.Default.Register(FakeWeatherProduct.Registration(FakeWeatherProduct.Notice));
        var vm = new PickReportViewModel();

        vm.SetPick("WindSpeed", "Wind speed", "1", "gfs.grib2", FakeWeatherProduct.Spec, []);

        Assert.Equal(FakeWeatherProduct.Notice, vm.NavigationNotice);
    }

    [Fact]
    public void The_portrayal_note_comes_from_the_registration()
    {
        var vm = new PickReportViewModel();

        vm.SetPick("WindSpeed", "Wind speed", "1", "gfs.grib2", FakeWeatherProduct.Spec, []);

        Assert.Equal("SoundCharts portrayal: GRIB2 Ed 2 defines no symbology", vm.PortrayalNote);
    }

    [Fact]
    public void The_metadata_probe_reads_a_registered_product()
    {
        var path = FakeWeatherProduct.Write(_dir);
        var reader = new CachingDatasetMetadataReader(
            new DiskDatasetMetadataCache(Path.Combine(_dir, "cache"), 1_000_000));

        var metadata = reader.TryRead(path);

        Assert.NotNull(metadata);
        Assert.Equal(FakeWeatherProduct.SpecRef, metadata.Spec);
        Assert.Equal(FakeWeatherProduct.Bounds, metadata.Extent);
    }

    private static ListBoxItem Row(ViewHost host, DatasetEntry entry)
        => host.Find<ListBoxItem>(item => ReferenceEquals(item.DataContext, entry));
}
